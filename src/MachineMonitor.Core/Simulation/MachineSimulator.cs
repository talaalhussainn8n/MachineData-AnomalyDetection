using MachineMonitor.Core.Models;

namespace MachineMonitor.Core.Simulation;

/// <summary>
/// Simuliert eine Produktionsmaschine mit realistischem Rauschen, langsamer Erwärmung
/// und gezielt eingestreuten Störungen (Spitzen und Drift), damit die Erkennung geprüft werden kann.
/// </summary>
public sealed class MachineSimulator
{
    private static readonly IReadOnlyDictionary<SensorType, (double Baseline, double Noise)> Profiles =
        new Dictionary<SensorType, (double, double)>
        {
            [SensorType.Temperature] = (65.0, 0.4),   // Grad Celsius
            [SensorType.Vibration] = (2.0, 0.08),     // mm/s
            [SensorType.MotorCurrent] = (12.0, 0.25)  // Ampere
        };

    private readonly string _machineId;
    private readonly Random _random;
    private readonly double _anomalyProbability;
    private int _tick;
    private int _driftRemaining;

    public MachineSimulator(string machineId, int seed = 42, double anomalyProbability = 0.02)
    {
        _machineId = machineId;
        _random = new Random(seed);
        _anomalyProbability = anomalyProbability;
    }

    public string MachineId => _machineId;

    /// <summary>Indizes der Takte, in denen bewusst eine Störung erzeugt wurde.</summary>
    public List<int> InjectedAnomalies { get; } = new();

    /// <summary>Erzeugt die Messwerte aller Sensoren für den nächsten Takt.</summary>
    public IReadOnlyList<SensorReading> Next(DateTimeOffset timestamp)
    {
        var readings = new List<SensorReading>(Profiles.Count);
        var injectSpike = _random.NextDouble() < _anomalyProbability;
        if (injectSpike)
        {
            InjectedAnomalies.Add(_tick);
        }

        foreach (var (sensor, (baseline, noise)) in Profiles)
        {
            // Leichte Schwingung wie ein Lastzyklus der Maschine.
            var cycle = Math.Sin(_tick / 30.0) * noise * 2;
            var value = baseline + cycle + Gaussian() * noise;

            if (injectSpike && sensor == SensorType.Vibration)
            {
                value += noise * 25; // z. B. Lagerschaden: starker Ausschlag
            }

            if (_driftRemaining > 0 && sensor == SensorType.Temperature)
            {
                value += (40 - _driftRemaining) * 0.3; // Überhitzung baut sich auf
            }

            readings.Add(new SensorReading(_machineId, sensor, Math.Round(value, 3), timestamp));
        }

        if (_driftRemaining > 0)
        {
            _driftRemaining--;
        }

        _tick++;
        return readings;
    }

    /// <summary>Startet eine langsame Überhitzung über die nächsten 40 Takte.</summary>
    public void StartOverheating() => _driftRemaining = 40;

    /// <summary>Box-Muller-Transformation für normalverteiltes Rauschen.</summary>
    private double Gaussian()
    {
        var u1 = 1.0 - _random.NextDouble();
        var u2 = 1.0 - _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}
