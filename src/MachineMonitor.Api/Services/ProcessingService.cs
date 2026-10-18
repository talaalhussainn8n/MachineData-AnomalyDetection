using MachineMonitor.Api.Data;
using MachineMonitor.Core.Detection;
using MachineMonitor.Core.Models;

namespace MachineMonitor.Api.Services;

/// <summary>
/// Liest Messwerte aus dem Puffer, bewertet sie und speichert Messwerte und Alarme.
/// Schreibt gesammelt, um die Datenbank nicht mit Einzelzugriffen zu belasten.
/// </summary>
public sealed class ProcessingService : BackgroundService
{
    private const int BatchSize = 50;

    private readonly ReadingChannel _channel;
    private readonly AnomalyMonitor _monitor;
    private readonly LiveState _state;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ProcessingService> _logger;

    public ProcessingService(
        ReadingChannel channel,
        AnomalyMonitor monitor,
        LiveState state,
        IServiceScopeFactory scopes,
        ILogger<ProcessingService> logger)
    {
        _channel = channel;
        _monitor = monitor;
        _state = state;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var readings = new List<ReadingEntity>(BatchSize);
        var alarms = new List<AlarmEntity>();

        try
        {
            await foreach (var reading in _channel.ReadAllAsync(stoppingToken))
            {
                var results = _monitor.Process(reading, out var alarm);
                var anomaly = results.FirstOrDefault(r => r.IsAnomaly);

                _state.Update(reading);
                readings.Add(new ReadingEntity
                {
                    MachineId = reading.MachineId,
                    Sensor = reading.Sensor.ToString(),
                    Value = reading.Value,
                    IsAnomaly = anomaly is not null,
                    AnomalyScore = anomaly?.Score ?? 0,
                    TimestampUtc = reading.Timestamp.UtcDateTime
                });

                if (alarm is not null)
                {
                    _state.CountAlarm(alarm.MachineId);
                    alarms.Add(ToEntity(alarm));
                    _logger.LogWarning("Alarm: {Message}", alarm.Message);
                }

                // Sofort schreiben, wenn der Puffer leer ist oder genug gesammelt wurde.
                if (readings.Count >= BatchSize || !_channel.HasPending)
                {
                    await SaveAsync(readings, alarms, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Regulärer Shutdown.
        }
    }

    private async Task SaveAsync(List<ReadingEntity> readings, List<AlarmEntity> alarms, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MonitorDbContext>();
            db.Readings.AddRange(readings);
            db.Alarms.AddRange(alarms);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Speichern von {Count} Messwerten fehlgeschlagen.", readings.Count);
        }
        finally
        {
            readings.Clear();
            alarms.Clear();
        }
    }

    private static AlarmEntity ToEntity(Alarm alarm) => new()
    {
        MachineId = alarm.MachineId,
        Sensor = alarm.Sensor.ToString(),
        Value = alarm.Value,
        Score = alarm.Score,
        Detector = alarm.Detector,
        Message = alarm.Message,
        TimestampUtc = alarm.Timestamp.UtcDateTime
    };
}
