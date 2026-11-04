using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.Messaging;

/// <summary>
/// One long-lived TCP connection to RabbitMQ per service (connections are expensive;
/// channels are the cheap, per-task things you open on top of it).
/// </summary>
public sealed class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    IHostEnvironment environment,
    ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsConnected => _connection?.IsOpen == true;

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password,
                // Shows up in the RabbitMQ management UI under "Connections".
                ClientProvidedName = environment.ApplicationName,
                // If the broker restarts, reconnect and re-create channels, queues and consumers.
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
            };

            // Keep trying until the broker is reachable: starting before RabbitMQ is normal.
            var delay = TimeSpan.FromSeconds(1);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _connection = await factory.CreateConnectionAsync(ct);
                    logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", settings.HostName, settings.Port);
                    return _connection;
                }
                catch (BrokerUnreachableException ex)
                {
                    logger.LogWarning("RabbitMQ not reachable (attempt {Attempt}): {Error}. Retrying in {Delay}s",
                        attempt, ex.InnerException?.Message ?? ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }
}
