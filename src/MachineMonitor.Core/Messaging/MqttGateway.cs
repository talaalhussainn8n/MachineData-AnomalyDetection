using MachineMonitor.Core.Models;
using MQTTnet;
using MQTTnet.Client;

namespace MachineMonitor.Core.Messaging;

/// <summary>
/// Dünne Schicht über MQTTnet zum Senden und Empfangen von Messwerten.
/// </summary>
public sealed class MqttGateway : IAsyncDisposable
{
    private readonly MqttFactory _factory = new();
    private readonly IMqttClient _client;
    private readonly string _host;
    private readonly int _port;

    public MqttGateway(string host, int port = 1883)
    {
        _host = host;
        _port = port;
        _client = _factory.CreateMqttClient();
    }

    public bool IsConnected => _client.IsConnected;

    public async Task ConnectAsync(string clientId, CancellationToken cancellationToken)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_host, _port)
            .WithClientId(clientId)
            .WithCleanSession()
            .Build();

        await _client.ConnectAsync(options, cancellationToken);
    }

    public Task PublishAsync(SensorReading reading, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(ReadingSerializer.Topic(reading))
            .WithPayload(ReadingSerializer.Serialize(reading))
            .Build();

        return _client.PublishAsync(message, cancellationToken);
    }

    public async Task SubscribeAsync(Func<SensorReading, Task> onReading, CancellationToken cancellationToken)
    {
        _client.ApplicationMessageReceivedAsync += async e =>
        {
            var reading = ReadingSerializer.Deserialize(e.ApplicationMessage.ConvertPayloadToString());
            if (reading is not null)
            {
                await onReading(reading);
            }
        };

        var subscribe = _factory.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(ReadingSerializer.SubscriptionFilter))
            .Build();

        await _client.SubscribeAsync(subscribe, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }
        _client.Dispose();
    }
}
