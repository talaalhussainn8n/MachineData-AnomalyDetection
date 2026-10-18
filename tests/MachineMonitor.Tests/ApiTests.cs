using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MachineMonitor.Tests;

/// <summary>
/// End-to-End-Test: Messwerte per HTTP einliefern, Verarbeitung abwarten, Alarm über die API abrufen.
/// Läuft mit einer temporären SQLite-Datenbank und ohne eingebaute Simulation.
/// </summary>
public class ApiTests : IClassFixture<ApiTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"monitor-test-{Guid.NewGuid():N}.db");

        public Factory()
        {
            // Program.cs liest die Konfiguration vor Build(); Umgebungsvariablen greifen dort zuverlässig.
            Environment.SetEnvironmentVariable("Monitor__SimulationEnabled", "false");
            Environment.SetEnvironmentVariable("Monitor__MqttHost", "");
            Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
            Environment.SetEnvironmentVariable("ConnectionStrings__Monitor", $"Data Source={_dbPath}");
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Monitor:SimulationEnabled", "false");
            builder.UseSetting("Monitor:MqttHost", "");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("ConnectionStrings:Monitor", $"Data Source={_dbPath}");
        }
    }

    private readonly HttpClient _client;

    public ApiTests(Factory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Spitze_ueber_HTTP_erzeugt_Alarm()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var random = new Random(1);

        for (var i = 0; i < 30; i++)
        {
            var value = 2.0 + Math.Sin(i / 10.0) * 0.2 + (random.NextDouble() - 0.5) * 0.05;
            await Post("test-01", value, start.AddSeconds(i));
        }
        await Post("test-01", 7.5, start.AddSeconds(31));

        var alarms = await WaitForAlarms();

        Assert.Contains(alarms.EnumerateArray(), a => a.GetProperty("machineId").GetString() == "test-01");
    }

    [Fact]
    public async Task Ungueltiger_Messwert_wird_abgelehnt()
    {
        var response = await _client.PostAsJsonAsync("/api/readings",
            new { machineId = "", sensor = "Vibration", value = 1.0, timestamp = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simulation_ist_im_Test_deaktiviert()
    {
        var response = await _client.PostAsync("/api/machines/presse-01/simulate-overheating", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task Post(string machineId, double value, DateTimeOffset timestamp)
    {
        var response = await _client.PostAsJsonAsync("/api/readings",
            new { machineId, sensor = "Vibration", value, timestamp });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<JsonElement> WaitForAlarms()
    {
        // Die Verarbeitung läuft asynchron im Hintergrund; kurz abwarten.
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var alarms = await _client.GetFromJsonAsync<JsonElement>("/api/alarms");
            if (alarms.GetArrayLength() > 0)
            {
                return alarms;
            }
            await Task.Delay(200);
        }

        throw new TimeoutException("Es wurde kein Alarm gespeichert.");
    }
}
