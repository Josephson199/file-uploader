using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Threading.Channels;
using FileUploader.Data;

namespace FileUploader.ApiService;

internal static class DefaultJsonOptions
{
    internal static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

internal class EventStream
{
    private readonly Channel<JobEvent> _channel =
        Channel.CreateUnbounded<JobEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public void Publish(JobEvent message)
    {
        _channel.Writer.TryWrite(message);
    }

    public IAsyncEnumerable<JobEvent> Subscribe(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}

internal class JobUpdateListener : BackgroundService
{
    private readonly ILogger<JobUpdateListener> _logger;
    private readonly EventStream _stream;
    private readonly string _connectionString;

    public JobUpdateListener(
        ILogger<JobUpdateListener> logger,
        EventStream stream,
        IConfiguration config)
    {
        _logger = logger;
        _stream = stream;
        _connectionString = config.GetConnectionString("postgresdb")
            ?? throw new InvalidOperationException("Missing Postgres connection string");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenLoop(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobUpdateListener crashed, restarting in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ListenLoop(CancellationToken stoppingToken)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(stoppingToken);

        _logger.LogInformation("JobUpdateListener connected to Postgres");

        conn.Notification += (_, e) =>
        {
            try
            {
                _logger.LogInformation("Received job update: {Payload}", e.Payload);

                var jobEvent = JsonSerializer.Deserialize<JobEvent>(e.Payload, DefaultJsonOptions.JsonSerializerOptions)!;

                // Push to SSE stream
                _stream.Publish(jobEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process NOTIFY payload");
            }
        };

        // Subscribe to the channel
        await using (var cmd = new NpgsqlCommand("LISTEN job_updates;", conn))
        {
            await cmd.ExecuteNonQueryAsync(stoppingToken);
        }

        // Block and wait for notifications
        while (!stoppingToken.IsCancellationRequested)
        {
            await conn.WaitAsync(stoppingToken);
        }

        _logger.LogInformation("JobUpdateListener stopping");
    }
}
