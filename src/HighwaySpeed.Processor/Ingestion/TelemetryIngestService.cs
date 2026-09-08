using Grpc.Core;
using HighwaySpeed.Contracts.Grpc;
using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Ingestion;

/// <summary>
/// gRPC endpoint the generator streams telemetry into. It does as little as
/// possible: read a message, map it to the internal <see cref="TelemetryReading"/>,
/// hand it to the ingestion pipeline, repeat until the client closes the stream.
/// </summary>
internal sealed class TelemetryIngestService : TelemetryIngest.TelemetryIngestBase
{
    private readonly TelemetryIngestPipeline _pipeline;
    private readonly ILogger<TelemetryIngestService> _logger;

    public TelemetryIngestService(TelemetryIngestPipeline pipeline, ILogger<TelemetryIngestService> logger)
    {
        _pipeline = pipeline;
        _logger = logger;
    }

    public override async Task<PublishSummary> PublishTelemetry(
        IAsyncStreamReader<TelemetryMessage> requestStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Telemetry stream opened by {Peer}.", context.Peer);
        long received = 0;

        await foreach (TelemetryMessage message in requestStream.ReadAllAsync(context.CancellationToken))
        {
            await _pipeline.EnqueueAsync(ToReading(message), context.CancellationToken);
            received++;
        }

        _logger.LogInformation("Telemetry stream from {Peer} closed after {Count} messages.", context.Peer, received);
        return new PublishSummary { MessagesReceived = received };
    }

    private static TelemetryReading ToReading(TelemetryMessage message) => new(
        NumberPlate: message.NumberPlate,
        CapturedAtUtc: new DateTime(message.TimestampTicks, DateTimeKind.Utc),
        SpeedKmh: message.CurrentSpeed,
        CameraId: message.CameraId);
}
