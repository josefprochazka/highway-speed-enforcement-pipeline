using System.Threading.Channels;
using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Ingestion;

/// <summary>
/// Decouples "receiving a message off the gRPC stream" from "updating the
/// leaderboards", and lets several worker threads process readings concurrently.
///
///   gRPC stream(s) ──► bounded Channel ──► N worker tasks ──► ITrafficAnalytics
///
/// The channel is bounded and configured to <c>Wait</c> when full, so if the
/// analytics ever fall behind the back-pressure propagates all the way to the
/// gRPC client instead of the process eating memory. The analytics engine is
/// thread-safe, which is what makes fanning out to several workers legal.
/// </summary>
internal sealed class TelemetryIngestPipeline : BackgroundService
{
    private const int Capacity = 100_000;

    private readonly ITrafficAnalytics _analytics;
    private readonly ILogger<TelemetryIngestPipeline> _logger;
    private readonly Channel<TelemetryReading> _channel;
    private readonly int _workerCount;

    public TelemetryIngestPipeline(ITrafficAnalytics analytics, ILogger<TelemetryIngestPipeline> logger)
    {
        _analytics = analytics;
        _logger = logger;
        _workerCount = Math.Max(2, Environment.ProcessorCount);

        _channel = Channel.CreateBounded<TelemetryReading>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <summary>Called by the gRPC service for every message it reads off a stream.</summary>
    public ValueTask EnqueueAsync(TelemetryReading reading, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(reading, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Ingestion pipeline starting with {Workers} workers.", _workerCount);

        IEnumerable<Task> workers = Enumerable
            .Range(0, _workerCount)
            .Select(_ => Task.Run(() => ConsumeAsync(stoppingToken), CancellationToken.None));

        await Task.WhenAll(workers);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TelemetryReading reading in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                _analytics.Record(reading);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion worker failed.");
            throw;
        }
    }
}
