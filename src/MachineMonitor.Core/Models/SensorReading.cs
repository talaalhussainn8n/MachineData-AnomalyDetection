namespace MachineMonitor.Core.Models;

/// <summary>
/// Gemessene Größen einer Produktionsmaschine.
/// </summary>
public enum SensorType
{
    Temperature,
    Vibration,
    MotorCurrent
}

/// <summary>
/// Ein einzelner Messwert, wie er über MQTT übertragen und gespeichert wird.
/// </summary>
public sealed record SensorReading(
    string MachineId,
    SensorType Sensor,
    double Value,
    DateTimeOffset Timestamp);

/// <summary>
/// Ergebnis der Anomalieerkennung für einen Messwert.
/// </summary>
public sealed record AnomalyResult(
    SensorReading Reading,
    bool IsAnomaly,
    double Score,
    string Detector);

/// <summary>
/// Ein Alarm, der für die Instandhaltung angezeigt wird.
/// </summary>
public sealed record Alarm(
    string MachineId,
    SensorType Sensor,
    double Value,
    double Score,
    string Detector,
    string Message,
    DateTimeOffset Timestamp);
