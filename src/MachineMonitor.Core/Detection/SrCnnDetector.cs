using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.TimeSeries;

namespace MachineMonitor.Core.Detection;

/// <summary>
/// KI-gestützte Anomalieerkennung mit ML.NET (Spectral Residual + CNN, "SR-CNN").
/// Das Verfahren stammt aus der Zeitreihenanalyse und benötigt keine gelabelten Trainingsdaten.
/// </summary>
public sealed class SrCnnDetector
{
    public const string Name = "ML.NET SR-CNN";
    public const int MinimumPoints = 12;

    private readonly MLContext _ml = new(seed: 1);
    private readonly double _threshold;
    private readonly double _sensitivity;
    private readonly object _lock = new();

    public SrCnnDetector(double threshold = 0.35, double sensitivity = 90)
    {
        _threshold = threshold;
        _sensitivity = sensitivity;
    }

    /// <summary>
    /// Bewertet eine komplette Zeitreihe und liefert für jeden Punkt, ob er auffällig ist, samt Score.
    /// </summary>
    public IReadOnlyList<(bool IsAnomaly, double Score)> Detect(IReadOnlyList<double> series)
    {
        if (series.Count < MinimumPoints)
        {
            return series.Select(_ => (false, 0d)).ToList();
        }

        // MLContext ist nicht für parallele Nutzung gedacht.
        lock (_lock)
        {
            var data = _ml.Data.LoadFromEnumerable(series.Select(v => new SeriesPoint { Value = v }));

            var output = _ml.AnomalyDetection.DetectEntireAnomalyBySrCnn(
                data,
                outputColumnName: nameof(SrCnnOutput.Prediction),
                inputColumnName: nameof(SeriesPoint.Value),
                threshold: _threshold,
                batchSize: -1,
                sensitivity: _sensitivity,
                detectMode: SrCnnDetectMode.AnomalyOnly);

            return _ml.Data
                .CreateEnumerable<SrCnnOutput>(output, reuseRowObject: false)
                .Select(p => (p.Prediction[0] > 0, Math.Round(p.Prediction[1], 3)))
                .ToList();
        }
    }

    /// <summary>Prüft nur den letzten Punkt einer Reihe, z. B. für die laufende Überwachung.</summary>
    public (bool IsAnomaly, double Score) EvaluateLatest(IReadOnlyList<double> series)
    {
        var results = Detect(series);
        return results.Count == 0 ? (false, 0) : results[^1];
    }

    /// <summary>Eingabezeile für ML.NET (öffentlich, da ML.NET per Reflection darauf zugreift).</summary>
    public sealed class SeriesPoint
    {
        public double Value { get; set; }
    }

    /// <summary>Ausgabezeile von ML.NET.</summary>
    public sealed class SrCnnOutput
    {
        // Im Modus AnomalyOnly: [IstAnomalie, Score, Magnitude]
        [VectorType(3)]
        public double[] Prediction { get; set; } = Array.Empty<double>();
    }
}
