using MachineMonitor.Core.Messaging;
using MachineMonitor.Core.Simulation;

namespace MachineMonitor.Api.Services;

public sealed class MonitorOptions
{
    /// <summary>MQTT-Broker. Leer lassen, um ohne Broker mit der eingebauten Simulation zu arbeiten.</summary>
    public string? MqttHost { get; set; }
    public int MqttPort { get; set; } = 1883;

    /// <summary>Eingebaute Simulation, wenn kein MQTT-Broker konfiguriert ist.</summary>
    public bool SimulationEnabled { get; set; } = true;
    public int SimulatedMachines { get; set; } = 3;
    public int SimulationIntervalMs { get; set; } = 1000;
}

/// <summary>
/// Empfängt Messwerte über MQTT (Topic factory/+/+) und legt sie in den Verarbeitungspuffer.
/// Baut die Verbindung bei Ausfall automatisch neu auf.
/// </summary>
public sealed class MqttIngestionService : BackgroundService
{
    private readonly MonitorOptions _options;
    private readonly ReadingChannel _channel;
    private readonly ILogger<MqttIngestionService> _logger;

    public MqttIngestionService(MonitorOptions options, ReadingChannel channel, ILogger<MqttIngestionService> logger)
    {
        _options = options;
        _channel = channel;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.MqttHost))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var gateway = new MqttGateway(_options.MqttHost!, _options.MqttPort);
                await gateway.ConnectAsync($"machine-monitor-{Environment.MachineName}", stoppingToken);
                await gateway.SubscribeAsync(r => _channel.WriteAsync(r, stoppingToken).AsTask(), stoppingToken);
                _logger.LogInformation("MQTT verbunden mit {Host}:{Port}", _options.MqttHost, _options.MqttPort);

                while (gateway.IsConnected && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }

                _logger.LogWarning("MQTT-Verbindung verloren, neuer Versuch.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT nicht erreichbar, neuer Versuch in 5 Sekunden.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}

/// <summary>
/// Eingebaute Maschinensimulation, damit die Anwendung ohne Broker und Hardware vorgeführt werden kann.
/// </summary>
public sealed class SimulationService : BackgroundService
{
    private readonly MonitorOptions _options;
    private readonly ReadingChannel _channel;
    private readonly List<MachineSimulator> _simulators;

    public SimulationService(MonitorOptions options, ReadingChannel channel)
    {
        _options = options;
        _channel = channel;
        _simulators = Enumerable.Range(1, Math.Max(1, options.SimulatedMachines))
            .Select(n => new MachineSimulator($"presse-{n:D2}", seed: n))
            .ToList();
    }

    public bool IsActive => _options.SimulationEnabled && string.IsNullOrWhiteSpace(_options.MqttHost);

    public bool TriggerOverheating(string machineId)
    {
        var simulator = _simulators.FirstOrDefault(s => s.MachineId == machineId);
        simulator?.StartOverheating();
        return simulator is not null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsActive)
        {
            return;
        }

        var interval = TimeSpan.FromMilliseconds(_options.SimulationIntervalMs);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var simulator in _simulators)
                {
                    foreach (var reading in simulator.Next(DateTimeOffset.UtcNow))
                    {
                        await _channel.WriteAsync(reading, stoppingToken);
                    }
                }

                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Regulärer Shutdown.
        }
    }
}
