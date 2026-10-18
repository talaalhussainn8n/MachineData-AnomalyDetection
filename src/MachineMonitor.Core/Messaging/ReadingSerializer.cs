using System.Text.Json;
using System.Text.Json.Serialization;
using MachineMonitor.Core.Models;

namespace MachineMonitor.Core.Messaging;

/// <summary>
/// JSON-Format der Messwerte auf dem MQTT-Bus und Aufbau der Topics.
/// Topic-Schema: factory/{machineId}/{sensor}
/// </summary>
public static class ReadingSerializer
{
    public const string TopicRoot = "factory";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Topic(SensorReading reading) =>
        $"{TopicRoot}/{reading.MachineId}/{reading.Sensor.ToString().ToLowerInvariant()}";

    public static string SubscriptionFilter => $"{TopicRoot}/+/+";

    public static string Serialize(SensorReading reading) => JsonSerializer.Serialize(reading, Options);

    public static SensorReading? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SensorReading>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
