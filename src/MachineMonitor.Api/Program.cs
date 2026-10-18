using System.Text.Json.Serialization;
using MachineMonitor.Api.Data;
using MachineMonitor.Api.Services;
using MachineMonitor.Core.Detection;
using MachineMonitor.Core.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection("Monitor").Get<MonitorOptions>() ?? new MonitorOptions();
builder.Services.AddSingleton(options);

// Datenbank: SQLite für die lokale Entwicklung, MS SQL Server im Betrieb (siehe docker-compose.yml).
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Monitor") ?? "Data Source=machinemonitor.db";
builder.Services.AddDbContext<MonitorDbContext>(db =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        db.UseSqlServer(connectionString);
    }
    else
    {
        db.UseSqlite(connectionString);
    }
});

builder.Services.AddSingleton(new RollingZScoreDetector(windowSize: 60, warmUp: 20, threshold: 4.0));
builder.Services.AddSingleton(new SrCnnDetector(threshold: 0.35, sensitivity: 90));
builder.Services.AddSingleton(sp => new AnomalyMonitor(
    sp.GetRequiredService<RollingZScoreDetector>(),
    sp.GetRequiredService<SrCnnDetector>()));

builder.Services.AddSingleton<ReadingChannel>();
builder.Services.AddSingleton<LiveState>();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationService>());
builder.Services.AddHostedService<MqttIngestionService>();
builder.Services.AddHostedService<ProcessingService>();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<MonitorDbContext>().Database.EnsureCreated();
}

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

// Aktueller Zustand aller Maschinen (aus dem Arbeitsspeicher, für das Live-Dashboard).
api.MapGet("/machines", (LiveState state) => Results.Ok(state.Snapshot()));

// Messwertverlauf eines Sensors aus der Datenbank.
api.MapGet("/machines/{machineId}/readings", async (
    string machineId, SensorType sensor, int? minutes, MonitorDbContext db, CancellationToken ct) =>
{
    var since = DateTime.UtcNow.AddMinutes(-(minutes ?? 5));
    var sensorName = sensor.ToString();

    var readings = await db.Readings
        .Where(r => r.MachineId == machineId && r.Sensor == sensorName && r.TimestampUtc >= since)
        .OrderBy(r => r.TimestampUtc)
        .Select(r => new { r.Value, r.IsAnomaly, r.AnomalyScore, r.TimestampUtc })
        .ToListAsync(ct);

    return Results.Ok(readings);
});

// Messwerte per HTTP einliefern, z. B. von Geräten ohne MQTT.
api.MapPost("/readings", async (SensorReading reading, ReadingChannel channel, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(reading.MachineId))
    {
        return Results.BadRequest(new { error = "MachineId fehlt." });
    }

    await channel.WriteAsync(reading, ct);
    return Results.Accepted();
});

api.MapGet("/alarms", async (int? take, MonitorDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Alarms
        .OrderByDescending(a => a.TimestampUtc)
        .Take(Math.Clamp(take ?? 50, 1, 500))
        .ToListAsync(ct)));

api.MapPost("/alarms/{id:long}/acknowledge", async (long id, MonitorDbContext db, CancellationToken ct) =>
{
    var alarm = await db.Alarms.FindAsync(new object[] { id }, ct);
    if (alarm is null)
    {
        return Results.NotFound();
    }

    alarm.Acknowledged = true;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

// Für Vorführungen: an einer simulierten Maschine eine Überhitzung auslösen.
api.MapPost("/machines/{machineId}/simulate-overheating", (string machineId, SimulationService simulation) =>
{
    if (!simulation.IsActive)
    {
        return Results.BadRequest(new { error = "Die eingebaute Simulation ist nicht aktiv." });
    }

    return simulation.TriggerOverheating(machineId) ? Results.Accepted() : Results.NotFound();
});

app.Run();

// Für Integrationstests mit WebApplicationFactory.
public partial class Program;
