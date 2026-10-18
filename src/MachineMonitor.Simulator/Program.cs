using MachineMonitor.Core.Messaging;
using MachineMonitor.Core.Simulation;

// Simuliert mehrere Maschinen und sendet ihre Messwerte per MQTT.
// Konfiguration über Umgebungsvariablen: MQTT_HOST, MQTT_PORT, MACHINES, INTERVAL_MS
var host = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "localhost";
var port = int.TryParse(Environment.GetEnvironmentVariable("MQTT_PORT"), out var p) ? p : 1883;
var machineCount = int.TryParse(Environment.GetEnvironmentVariable("MACHINES"), out var m) ? m : 3;
var interval = TimeSpan.FromMilliseconds(int.TryParse(Environment.GetEnvironmentVariable("INTERVAL_MS"), out var i) ? i : 1000);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var simulators = Enumerable.Range(1, machineCount)
    .Select(n => new MachineSimulator($"presse-{n:D2}", seed: n))
    .ToList();

await using var gateway = new MqttGateway(host, port);
await gateway.ConnectAsync("machine-simulator", cts.Token);
Console.WriteLine($"Verbunden mit MQTT-Broker {host}:{port}. Simuliere {machineCount} Maschinen.");

var random = new Random();
var tick = 0;
try
{
    while (!cts.IsCancellationRequested)
    {
        foreach (var simulator in simulators)
        {
            // Gelegentlich eine Überhitzung starten, damit auch schleichende Fehler auftreten.
            if (tick > 60 && random.NextDouble() < 0.003)
            {
                simulator.StartOverheating();
                Console.WriteLine($"[{simulator.MachineId}] Überhitzung simuliert.");
            }

            foreach (var reading in simulator.Next(DateTimeOffset.UtcNow))
            {
                await gateway.PublishAsync(reading, cts.Token);
            }
        }

        tick++;
        await Task.Delay(interval, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Beenden mit Strg+C.
}

Console.WriteLine("Simulator beendet.");
