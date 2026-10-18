using MachineMonitor.Core.Models;

namespace MachineMonitor.Core.Detection;

/// <summary>
/// Verbindet beide Verfahren in der laufenden Überwachung:
/// der z-Wert reagiert sofort, SR-CNN bewertet den jüngsten Verlauf jedes Sensors.
/// Ein Alarm entsteht, wenn eines der Verfahren anschlägt; Wiederholungen werden kurz unterdrückt.
/// SR-CNN ist optional (null), etwa für Tests oder sehr schwache Hardware.
/// </summary>
public sealed class AnomalyMonitor
{
    private readonly RollingZScoreDetector _zScore;
    private readonly SrCnnDetector? _srCnn;
    private readonly int _historySize;
    private readonly TimeSpan _alarmCooldown;
    private readonly Dictionary<(string, SensorType), List<double>> _history = new();
    private readonly Dictionary<(string, SensorType), DateTimeOffset> _lastAlarm = new();
    private readonly object _lock = new();

    public AnomalyMonitor(
        RollingZScoreDetector zScore,
        SrCnnDetector? srCnn,
        int historySize = 64,
        TimeSpan? alarmCooldown = null)
    {
        _zScore = zScore;
        _srCnn = srCnn;
        _historySize = historySize;
        _alarmCooldown = alarmCooldown ?? TimeSpan.FromSeconds(10);
    }

    public IReadOnlyList<AnomalyResult> Process(SensorReading reading, out Alarm? alarm)
    {
        alarm = null;
        var results = new List<AnomalyResult> { _zScore.Evaluate(reading) };

        List<double> snapshot;
        lock (_lock)
        {
            var key = (reading.MachineId, reading.Sensor);
            if (!_history.TryGetValue(key, out var history))
            {
                history = new List<double>(_historySize + 1);
                _history[key] = history;
            }

            history.Add(reading.Value);
            if (history.Count > _historySize)
            {
                history.RemoveAt(0);
            }
            snapshot = history.ToList();
        }

        if (_srCnn is not null && snapshot.Count >= SrCnnDetector.MinimumPoints)
        {
            var (isAnomaly, score) = _srCnn.EvaluateLatest(snapshot);
            results.Add(new AnomalyResult(reading, isAnomaly, score, SrCnnDetector.Name));
        }

        var trigger = results.FirstOrDefault(r => r.IsAnomaly);
        if (trigger is not null && ShouldRaise(reading))
        {
            alarm = new Alarm(
                reading.MachineId,
                reading.Sensor,
                reading.Value,
                trigger.Score,
                trigger.Detector,
                BuildMessage(reading, trigger),
                reading.Timestamp);
        }

        return results;
    }

    private bool ShouldRaise(SensorReading reading)
    {
        lock (_lock)
        {
            var key = (reading.MachineId, reading.Sensor);
            if (_lastAlarm.TryGetValue(key, out var last) && reading.Timestamp - last < _alarmCooldown)
            {
                return false;
            }

            _lastAlarm[key] = reading.Timestamp;
            return true;
        }
    }

    private static string BuildMessage(SensorReading reading, AnomalyResult trigger) => reading.Sensor switch
    {
        SensorType.Vibration => $"Ungewöhnliche Vibration an {reading.MachineId} ({reading.Value} mm/s). Lager und Unwucht prüfen.",
        SensorType.Temperature => $"Temperatur auffällig an {reading.MachineId} ({reading.Value} °C). Kühlung prüfen.",
        SensorType.MotorCurrent => $"Motorstrom auffällig an {reading.MachineId} ({reading.Value} A). Last und Antrieb prüfen.",
        _ => $"Auffälliger Messwert an {reading.MachineId} erkannt durch {trigger.Detector}."
    };
}
