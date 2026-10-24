using MachineMonitor.Core.Detection;
using MachineMonitor.Core.Models;
using Xunit;

namespace MachineMonitor.Tests;

public class DetectionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static SensorReading Reading(double value, int second = 0, SensorType sensor = SensorType.Vibration) =>
        new("presse-01", sensor, value, Start.AddSeconds(second));

    /// <summary>Gleichmäßige Schwingung mit kleinem, reproduzierbarem Rauschen.</summary>
    private static List<double> NormalSeries(int count, int seed = 7)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count)
            .Select(i => 2.0 + Math.Sin(i / 10.0) * 0.2 + (random.NextDouble() - 0.5) * 0.05)
            .ToList();
    }

    [Fact]
    public void ZScore_meldet_in_der_Aufwaermphase_nichts()
    {
        var detector = new RollingZScoreDetector(windowSize: 30, warmUp: 10);
        for (var i = 0; i < 10; i++)
        {
            Assert.False(detector.Evaluate(Reading(i == 5 ? 100 : 2.0, i)).IsAnomaly);
        }
    }

    [Fact]
    public void ZScore_erkennt_Ausreisser_nach_Aufwaermphase()
    {
        var detector = new RollingZScoreDetector(windowSize: 60, warmUp: 20, threshold: 4);
        var series = NormalSeries(40);
        for (var i = 0; i < series.Count; i++)
        {
            detector.Evaluate(Reading(series[i], i));
        }

        var result = detector.Evaluate(Reading(4.5, 41));

        Assert.True(result.IsAnomaly);
        Assert.True(result.Score > 4);
    }

    [Fact]
    public void ZScore_trennt_Maschinen_und_Sensoren()
    {
        var detector = new RollingZScoreDetector(windowSize: 30, warmUp: 10);
        foreach (var value in NormalSeries(20))
        {
            detector.Evaluate(Reading(value, sensor: SensorType.Vibration));
        }

        // Temperatur hat ein eigenes Fenster und ist noch in der Aufwärmphase.
        Assert.False(detector.Evaluate(Reading(65, sensor: SensorType.Temperature)).IsAnomaly);
    }

    [Fact]
    public void SrCnn_findet_eingestreute_Spitze()
    {
        var series = NormalSeries(200);
        series[150] += 8;

        var results = new SrCnnDetector().Detect(series);

        Assert.Equal(series.Count, results.Count);
        Assert.True(results[150].IsAnomaly);
        var highest = results.Select((r, i) => (r.Score, i)).MaxBy(x => x.Score).i;
        Assert.Equal(150, highest);
    }

    [Fact]
    public void SrCnn_braucht_Mindestanzahl_an_Punkten()
    {
        var results = new SrCnnDetector().Detect(new[] { 1.0, 2.0, 100.0 });
        Assert.All(results, r => Assert.False(r.IsAnomaly));
    }

    [Fact]
    public void Monitor_loest_bei_Spitze_Alarm_aus()
    {
        var monitor = new AnomalyMonitor(new RollingZScoreDetector(), new SrCnnDetector(), alarmCooldown: TimeSpan.Zero);
        var series = NormalSeries(40);
        for (var i = 0; i < series.Count; i++)
        {
            monitor.Process(Reading(series[i], i), out _);
        }

        monitor.Process(Reading(6.0, 41), out var alarm);

        Assert.NotNull(alarm);
        Assert.Equal("presse-01", alarm!.MachineId);
        Assert.Equal(SensorType.Vibration, alarm.Sensor);
        Assert.Contains("Vibration", alarm.Message);
    }

    [Fact]
    public void Monitor_unterdrueckt_Wiederholungsalarme()
    {
        var monitor = new AnomalyMonitor(
            new RollingZScoreDetector(windowSize: 30, warmUp: 10, threshold: 4),
            srCnn: null,
            alarmCooldown: TimeSpan.FromSeconds(30));

        var series = NormalSeries(20);
        for (var i = 0; i < series.Count; i++)
        {
            monitor.Process(Reading(series[i], i), out _);
        }

        monitor.Process(Reading(9.0, 25), out var first);
        monitor.Process(Reading(9.5, 26), out var second);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
