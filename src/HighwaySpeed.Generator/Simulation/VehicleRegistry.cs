namespace HighwaySpeed.Generator.Simulation;

/// <summary>
/// The predictable state machine at the heart of the generator. It owns the set
/// of vehicles currently inside the monitored stretch and exposes the three steps
/// the spec requires, to be run in this exact order once per tick:
///
///   1. <see cref="SpawnNewVehicles"/>        – 100 new vehicles enter (counter = 0)
///   2. <see cref="AdvanceAndCapture"/>        – every vehicle moves to its next camera and emits a capture
///   3. <see cref="RemoveDepartedVehicles"/>   – vehicles past camera 10 leave the sector
///
/// The class is single-threaded by design: only the simulation loop touches it,
/// which keeps the logic trivial to read and to test.
/// </summary>
public sealed class VehicleRegistry
{
    /// <summary>Number of cameras on the stretch.</summary>
    public const int CameraCount = 10;

    private readonly List<Vehicle> _activeVehicles = new();
    private readonly NumberPlateFactory _plateFactory;
    private readonly SpeedModel _speedModel;

    public VehicleRegistry(NumberPlateFactory plateFactory, SpeedModel speedModel)
    {
        _plateFactory = plateFactory;
        _speedModel = speedModel;
    }

    /// <summary>How many vehicles are currently in the sector.</summary>
    public int ActiveCount => _activeVehicles.Count;

    /// <summary>Step 1 – add <paramref name="count"/> brand-new vehicles at camera counter 0.</summary>
    public void SpawnNewVehicles(int count)
    {
        for (int i = 0; i < count; i++)
        {
            float cruising = _speedModel.NewCruisingSpeed();
            _activeVehicles.Add(new Vehicle
            {
                NumberPlate = _plateFactory.Next(),
                CruisingSpeedKmh = cruising,
                CamerasCleared = 0,
                LastSpeedKmh = cruising,
            });
        }
    }

    /// <summary>
    /// Step 2 – advance every active vehicle by one camera and produce a capture
    /// for it. A vehicle whose counter has just moved past camera 10 is on its way
    /// out (step 3 will drop it) and does NOT emit: the spec fixes each vehicle at
    /// exactly ten messages, one per camera.
    /// </summary>
    public IReadOnlyList<CameraCapture> AdvanceAndCapture(DateTime capturedAtUtc)
    {
        var captures = new List<CameraCapture>(_activeVehicles.Count);

        foreach (Vehicle vehicle in _activeVehicles)
        {
            vehicle.CamerasCleared++;

            if (vehicle.CamerasCleared > CameraCount)
                continue;

            vehicle.LastSpeedKmh = _speedModel.NextSpeed(vehicle.LastSpeedKmh, vehicle.CruisingSpeedKmh);

            captures.Add(new CameraCapture(
                NumberPlate: vehicle.NumberPlate,
                CameraId: vehicle.CamerasCleared,
                SpeedKmh: vehicle.LastSpeedKmh,
                CapturedAtUtc: capturedAtUtc));
        }

        return captures;
    }

    /// <summary>Step 3 – drop every vehicle whose counter has exceeded 10. Returns how many left.</summary>
    public int RemoveDepartedVehicles()
    {
        return _activeVehicles.RemoveAll(vehicle => vehicle.CamerasCleared > CameraCount);
    }
}
