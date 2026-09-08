using System.Threading.Channels;
using Grpc.Net.Client;
using HighwaySpeed.Contracts.Grpc;
using HighwaySpeed.Generator.Simulation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HighwaySpeed.Generator.Publishing;

/// <summary>
/// Streams telemetry to the processor over a single long-lived gRPC
/// client-streaming call.
///
/// Design:
///   * <see cref="PublishAsync"/> only drops the message into a bounded in-memory
///     outbox, so the simulation loop never waits on the network.
///   * A background pump drains the outbox into the gRPC stream. If the stream
///     breaks it reconnects with capped exponential back-off and keeps going.
///   * The outbox is bounded and drops the oldest messages when full: telemetry
///     is lossy by nature and shedding load is better than an unbounded buffer.
/// </summary>
public sealed class GrpcTelemetryPublisher : ITelemetryPublisher, IHostedService
{
    private readonly GeneratorOptions _options;
    private readonly ILogger<GrpcTelemetryPublisher> _logger;
    private readonly Channel<TelemetryMessage> _outbox;

    private CancellationTokenSource? _stopping;
    private Task? _pump;

    public GrpcTelemetryPublisher(IOptions<GeneratorOptions> options, ILogger<GrpcTelemetryPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        _outbox = Channel.CreateBounded<TelemetryMessage>(new BoundedChannelOptions(_options.OutboxCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
    }

    public ValueTask PublishAsync(CameraCapture capture, CancellationToken cancellationToken)
    {
        return _outbox.Writer.WriteAsync(ToMessage(capture), cancellationToken);
    }

    /// <summary>Maps the generator's domain type onto the wire contract.</summary>
    internal static TelemetryMessage ToMessage(CameraCapture capture) => new()
    {
        NumberPlate = capture.NumberPlate,
        TimestampTicks = capture.CapturedAtUtc.Ticks,
        CurrentSpeed = capture.SpeedKmh,
        CameraId = capture.CameraId,
    };

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        _pump = Task.Run(() => PumpAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _outbox.Writer.TryComplete();
        if (_stopping is not null)
            await _stopping.CancelAsync();

        if (_pump is not null)
        {
            try
            {
                await _pump.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Host is shutting down; nothing more to do.
            }
        }

        _stopping?.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        var maxBackoff = TimeSpan.FromSeconds(15);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await StreamOutboxAsync(cancellationToken);
                return; // outbox completed normally -> graceful shutdown
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Telemetry stream to {Address} dropped; reconnecting in {Seconds:0.#}s.",
                    _options.ProcessorGrpcAddress, backoff.TotalSeconds);

                try
                {
                    await Task.Delay(backoff, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, maxBackoff.TotalSeconds));
            }
        }
    }

    private async Task StreamOutboxAsync(CancellationToken cancellationToken)
    {
        using GrpcChannel channel = GrpcChannel.ForAddress(_options.ProcessorGrpcAddress);
        var client = new TelemetryIngest.TelemetryIngestClient(channel);

        using var call = client.PublishTelemetry(cancellationToken: cancellationToken);
        _logger.LogInformation("Streaming telemetry to {Address}.", _options.ProcessorGrpcAddress);

        await foreach (TelemetryMessage message in _outbox.Reader.ReadAllAsync(cancellationToken))
        {
            await call.RequestStream.WriteAsync(message, cancellationToken);
        }

        await call.RequestStream.CompleteAsync();
        PublishSummary summary = await call;
        _logger.LogInformation("Processor acknowledged {Count} messages.", summary.MessagesReceived);
    }
}
