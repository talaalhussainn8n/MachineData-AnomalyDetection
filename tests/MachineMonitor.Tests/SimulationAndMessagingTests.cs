using MachineMonitor.Core.Messaging;
using MachineMonitor.Core.Models;
using MachineMonitor.Core.Simulation;

namespace MachineMonitor.Tests;

public class SimulationAndMessagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Simulator_liefert_alle_Sensoren_pro_Takt()
    {
        var readings = new MachineSimulator("presse-01").Next(Now);

        Assert.Equal(3, readings.Count);
        Assert.Equal(
            new[] { SensorType.Temperature, SensorType.Vibration, SensorType.MotorCurrent },
            readings.Select(r => r.Sensor));
    }

    [Fact]
    public void Simulator_ist_mit_gleichem_Seed_reproduzierbar()
    {
        var a = new MachineSimulator("presse-01", seed: 3);
        var b = new MachineSimulator("presse-01", seed: 3);

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(a.Next(Now), b.Next(Now));
        }
    }

    [Fact]
    public void Eingestreute_Stoerungen_heben_Vibration_deutlich_an()
    {
        var simulator = new MachineSimulator("presse-01", seed: 11, anomalyProbability: 0.05);
        var vibration = Enumerable.Range(0, 500)
            .Select(_ => simulator.Next(Now).Single(r => r.Sensor == SensorType.Vibration).Value)
            .ToList();

        Assert.NotEmpty(simulator.InjectedAnomalies);
        foreach (var tick in simulator.InjectedAnomalies)
        {
            Assert.True(vibration[tick] > 3.0, $"Takt {tick}: {vibration[tick]}");
        }
    }

    [Fact]
    public void Ueberhitzung_steigert_Temperatur()
    {
        var simulator = new MachineSimulator("presse-01", seed: 5, anomalyProbability: 0);
        var before = simulator.Next(Now).Single(r => r.Sensor == SensorType.Temperature).Value;

        simulator.StartOverheating();
        var temperatures = Enumerable.Range(0, 40)
            .Select(_ => simulator.Next(Now).Single(r => r.Sensor == SensorType.Temperature).Value)
            .ToList();

        Assert.True(temperatures[^1] > before + 8);
    }

    [Fact]
    public void Messwert_uebersteht_Serialisierung()
    {
        var reading = new SensorReading("presse-02", SensorType.MotorCurrent, 12.345, Now);

        var json = ReadingSerializer.Serialize(reading);

        Assert.Contains("\"sensor\":\"MotorCurrent\"", json);
        Assert.Equal(reading, ReadingSerializer.Deserialize(json));
    }

    [Fact]
    public void Ungueltiges_Json_wird_ignoriert()
    {
        Assert.Null(ReadingSerializer.Deserialize("{kein json"));
    }

    [Fact]
    public void Topic_folgt_dem_Schema()
    {
        var reading = new SensorReading("presse-03", SensorType.Temperature, 60, Now);
        Assert.Equal("factory/presse-03/temperature", ReadingSerializer.Topic(reading));
    }
}
