namespace HighwaySpeed.Generator.Simulation;

/// <summary>
/// Mutable state for one vehicle currently inside the monitored stretch.
/// Lives only in <see cref="VehicleRegistry"/>.
/// </summary>
internal sealed class Vehicle
{
    public required string NumberPlate { get; init; }

    /// <summary>The driver's personal cruising speed, km/h (set once at spawn).</summary>
    public required float CruisingSpeedKmh { get; init; }

    /// <summary>
    /// How many cameras this vehicle has already cleared. 0 means it has just
    /// entered and has not reached camera 1 yet.
    /// </summary>
    public int CamerasCleared { get; set; }

    /// <summary>Speed recorded at the previous camera; seeds the next sample.</summary>
    public float LastSpeedKmh { get; set; }
}
