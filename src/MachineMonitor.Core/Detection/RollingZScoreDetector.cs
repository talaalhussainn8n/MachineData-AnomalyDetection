using MachineMonitor.Core.Models;

namespace MachineMonitor.Core.Detection;

/// <summary>
/// Statistische Echtzeit-Erkennung: Abweichung eines Messwerts vom gleitenden Mittelwert
/// in Standardabweichungen (z-Wert). Auffällige Werte fließen nicht in das Fenster ein,
/// damit eine Störung die Referenz nicht verfälscht.
/// </summary>
public sealed class RollingZScoreDetector
{
    private readonly int _windowSize;
    private readonly int _warmUp;
    private readonly double _threshold;
    private readonly Dictionary<(string Machine, SensorType Sensor), Queue<double>> _windows = new();
    private readonly object _lock = new();

    public RollingZScoreDetector(int windowSize = 60, int warmUp = 20, double threshold = 4.0)
    {
        if (warmUp > windowSize)
        {
            throw new ArgumentException("Die Aufwärmphase darf nicht größer als das Fenster sein.");
        }

        _windowSize = windowSize;
        _warmUp = warmUp;
        _threshold = threshold;
    }

    public const string Name = "Z-Score";

    public AnomalyResult Evaluate(SensorReading reading)
    {
        lock (_lock)
        {
            var key = (reading.MachineId, reading.Sensor);
            if (!_windows.TryGetValue(key, out var window))
            {
                window = new Queue<double>(_windowSize);
                _windows[key] = window;
            }

            if (window.Count < _warmUp)
            {
                Push(window, reading.Value);
                return new AnomalyResult(reading, false, 0, Name);
            }

            var mean = window.Average();
            var std = Math.Sqrt(window.Sum(v => (v - mean) * (v - mean)) / window.Count);
            var z = std < 1e-9 ? 0 : Math.Abs(reading.Value - mean) / std;
            var isAnomaly = z > _threshold;

            if (!isAnomaly)
            {
                Push(window, reading.Value);
            }

            return new AnomalyResult(reading, isAnomaly, Math.Round(z, 2), Name);
        }
    }

    private void Push(Queue<double> window, double value)
    {
        window.Enqueue(value);
        while (window.Count > _windowSize)
        {
            window.Dequeue();
        }
    }
}
