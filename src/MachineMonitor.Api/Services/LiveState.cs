using System.Collections.Concurrent;
using MachineMonitor.Core.Models;

namespace MachineMonitor.Api.Services;

/// <summary>
/// Aktueller Zustand aller Maschinen im Arbeitsspeicher für das Live-Dashboard.
/// </summary>
public sealed class LiveState
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<SensorType, SensorReading>> _latest = new();
    private readonly ConcurrentDictionary<string, int> _alarmCounts = new();

    public void Update(SensorReading reading)
    {
        var sensors = _latest.GetOrAdd(reading.MachineId, _ => new ConcurrentDictionary<SensorType, SensorReading>());
        sensors[reading.Sensor] = reading;
    }

    public void CountAlarm(string machineId) => _alarmCounts.AddOrUpdate(machineId, 1, (_, count) => count + 1);

    public IReadOnlyList<MachineStatus> Snapshot() =>
        _latest
            .OrderBy(m => m.Key)
            .Select(m => new MachineStatus(
                m.Key,
                m.Value.Values.OrderBy(r => r.Sensor).ToList(),
                _alarmCounts.GetValueOrDefault(m.Key)))
            .ToList();
}

public sealed record MachineStatus(string MachineId, IReadOnlyList<SensorReading> Latest, int AlarmCount);
