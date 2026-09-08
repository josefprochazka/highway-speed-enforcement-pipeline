using System.ComponentModel.DataAnnotations;

namespace HighwaySpeed.Generator;

/// <summary>
/// Configuration for the telemetry generator, bound from the "Generator" section
/// of appsettings.json (or overridden by environment variables such as
/// <c>Generator__ProcessorGrpcAddress</c> when running in Docker).
/// </summary>
public sealed class GeneratorOptions
{
    public const string SectionName = "Generator";

    /// <summary>Address of the processor's gRPC (HTTP/2) endpoint.</summary>
    [Required]
    public string ProcessorGrpcAddress { get; set; } = "http://localhost:5081";

    /// <summary>How many brand-new vehicles enter the sector every tick. The spec fixes this at 100.</summary>
    [Range(1, 100_000)]
    public int VehiclesPerTick { get; set; } = 100;

    /// <summary>Length of one simulation tick. The spec fixes this at one second.</summary>
    [Range(1, 3600)]
    public int TickIntervalSeconds { get; set; } = 1;

    /// <summary>Highway speed limit the driver population fluctuates around, km/h.</summary>
    [Range(1, 400)]
    public float BaselineSpeedKmh { get; set; } = 130f;

    /// <summary>
    /// Upper bound on the in-memory outbox between the simulation loop and the
    /// gRPC sender. If the network stalls, the oldest telemetry is dropped rather
    /// than letting the buffer grow without limit.
    /// </summary>
    [Range(1_000, 10_000_000)]
    public int OutboxCapacity { get; set; } = 50_000;

    /// <summary>Optional fixed seed to make a run reproducible. Null = non-deterministic.</summary>
    public int? RandomSeed { get; set; }

    public TimeSpan TickInterval => TimeSpan.FromSeconds(TickIntervalSeconds);
}
