using System.Collections.Concurrent;

namespace HighwaySpeed.Processor.Analytics;

/// <summary>The running-average state for one vehicle, promoted when it qualifies for the global board.</summary>
internal readonly record struct AverageRecord(string NumberPlate, float AverageSpeedKmh, int CamerasPassed);

/// <summary>
/// Tracks each vehicle's cumulative average speed across the cameras it has
/// passed so far, and applies the "must have passed at least 3 cameras" gate.
///
/// Thread safety: a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
/// plate. The per-vehicle state is an immutable struct that is swapped
/// atomically inside <see cref="ConcurrentDictionary{TKey,TValue}.AddOrUpdate{TArg}"/>,
/// so there is no lock and no read-modify-write race even if two readings for the
/// same vehicle are processed concurrently.
/// </summary>
internal sealed class VehicleAverageTracker
{
    /// <summary>A vehicle only counts toward the global board once it has passed this many cameras.</summary>
    public const int QualifyingCameraCount = 3;

    private readonly ConcurrentDictionary<string, Progress> _progressByPlate = new(StringComparer.Ordinal);

    /// <summary>Immutable per-vehicle accumulator: enough to compute the average without storing every speed.</summary>
    private readonly record struct Progress(int CamerasPassed, double SpeedSum)
    {
        public float Average => (float)(SpeedSum / CamerasPassed);
    }

    /// <summary>
    /// Folds a reading into the vehicle's average. Returns the updated average
    /// record if the vehicle now qualifies for the global board, otherwise null.
    /// </summary>
    public AverageRecord? Record(in TelemetryReading reading)
    {
        Progress updated = _progressByPlate.AddOrUpdate(
            reading.NumberPlate,
            addValueFactory: static (_, speed) => new Progress(CamerasPassed: 1, SpeedSum: speed),
            updateValueFactory: static (_, current, speed) =>
                new Progress(current.CamerasPassed + 1, current.SpeedSum + speed),
            factoryArgument: reading.SpeedKmh);

        if (updated.CamerasPassed < QualifyingCameraCount)
            return null;

        return new AverageRecord(reading.NumberPlate, updated.Average, updated.CamerasPassed);
    }

    /// <summary>Drops all tracking data for a vehicle. Returns true if something was removed.</summary>
    public bool TryPurge(string numberPlate) => _progressByPlate.TryRemove(numberPlate, out _);

    /// <summary>Whether the tracker still holds state for the given plate. Used by tests.</summary>
    public bool IsTracking(string numberPlate) => _progressByPlate.ContainsKey(numberPlate);

    /// <summary>How many vehicles are currently being tracked. Used for diagnostics and tests.</summary>
    public int TrackedVehicleCount => _progressByPlate.Count;
}
