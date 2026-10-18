using System.Threading.Channels;
using MachineMonitor.Core.Models;

namespace MachineMonitor.Api.Services;

/// <summary>
/// Puffer zwischen den Datenquellen (MQTT, HTTP, Simulation) und der Verarbeitung.
/// Entkoppelt Empfang und Auswertung, damit kurze Lastspitzen keine Messwerte verlieren.
/// </summary>
public sealed class ReadingChannel
{
    private readonly Channel<SensorReading> _channel = Channel.CreateBounded<SensorReading>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ValueTask WriteAsync(SensorReading reading, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(reading, cancellationToken);

    /// <summary>True, wenn noch unverarbeitete Messwerte im Puffer liegen.</summary>
    public bool HasPending => _channel.Reader.Count > 0;

    public IAsyncEnumerable<SensorReading> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
