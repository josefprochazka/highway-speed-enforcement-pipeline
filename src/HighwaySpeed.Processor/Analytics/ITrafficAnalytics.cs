using HighwaySpeed.Contracts;

namespace HighwaySpeed.Processor.Analytics;

/// <summary>
/// The analytics engine: it folds each incoming reading into the per-camera and
/// global leaderboards, and produces an immutable snapshot on demand for the
/// REST API.
/// </summary>
internal interface ITrafficAnalytics
{
    /// <summary>Processes a single camera capture. Safe to call from many threads at once.</summary>
    void Record(in TelemetryReading reading);

    /// <summary>Builds a consistent, immutable picture of all leaderboards right now.</summary>
    TrafficSnapshot BuildSnapshot();

    /// <summary>Total readings processed since start-up. Diagnostics only.</summary>
    long ReadingsProcessed { get; }
}
