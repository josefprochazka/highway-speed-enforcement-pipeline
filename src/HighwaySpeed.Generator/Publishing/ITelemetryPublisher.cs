using HighwaySpeed.Generator.Simulation;

namespace HighwaySpeed.Generator.Publishing;

/// <summary>
/// Accepts camera captures from the simulation loop and is responsible for
/// getting them to the processor. Implementations are expected to return quickly
/// (hand the capture to a buffer) rather than block the loop on the network.
/// </summary>
public interface ITelemetryPublisher
{
    ValueTask PublishAsync(CameraCapture capture, CancellationToken cancellationToken);
}
