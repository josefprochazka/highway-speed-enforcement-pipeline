using HighwaySpeed.Generator.Simulation;

namespace HighwaySpeed.Generator.Tests;

/// <summary>
/// Exercises the spec's per-second state machine: spawn 100, advance every
/// vehicle by one camera and emit, then drop anything past camera 10.
/// </summary>
public class VehicleRegistryTests
{
    private const int VehiclesPerTick = 100;
    private static readonly DateTime AnyTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static VehicleRegistry CreateRegistry(int seed = 123)
        => new(new NumberPlateFactory(), new SpeedModel(speedLimitKmh: 130, new Random(seed)));

    private static IReadOnlyList<CameraCapture> RunTick(VehicleRegistry registry)
    {
        registry.SpawnNewVehicles(VehiclesPerTick);
        IReadOnlyList<CameraCapture> captures = registry.AdvanceAndCapture(AnyTime);
        registry.RemoveDepartedVehicles();
        return captures;
    }

    [Fact]
    public void First_tick_emits_one_message_per_new_vehicle_all_at_camera_one()
    {
        var registry = CreateRegistry();

        IReadOnlyList<CameraCapture> captures = RunTick(registry);

        Assert.Equal(VehiclesPerTick, captures.Count);
        Assert.Equal(VehiclesPerTick, registry.ActiveCount);
        Assert.All(captures, capture => Assert.Equal(1, capture.CameraId));
    }

    [Fact]
    public void Registry_reaches_a_steady_state_of_ten_cohorts()
    {
        var registry = CreateRegistry();

        for (int tick = 1; tick <= 10; tick++)
        {
            IReadOnlyList<CameraCapture> captures = RunTick(registry);
            Assert.Equal(VehiclesPerTick * tick, captures.Count);
            Assert.Equal(VehiclesPerTick * tick, registry.ActiveCount);
        }

        // From now on: 100 enter, 100 leave, 1000 stay, 1000 messages per tick.
        for (int tick = 11; tick <= 20; tick++)
        {
            IReadOnlyList<CameraCapture> captures = RunTick(registry);
            Assert.Equal(VehiclesPerTick * 10, captures.Count);
            Assert.Equal(VehiclesPerTick * 10, registry.ActiveCount);
        }
    }

    [Fact]
    public void Each_vehicle_is_captured_exactly_once_per_camera_in_order_then_leaves()
    {
        var registry = CreateRegistry();
        var camerasByPlate = new Dictionary<string, List<int>>();

        // The 100 plates that entered on tick 1.
        IReadOnlyList<CameraCapture> firstTick = RunTick(registry);
        var firstCohort = firstTick.Select(c => c.NumberPlate).ToHashSet();
        Record(firstTick);

        // Run well past the point the first cohort has left the sector.
        for (int tick = 2; tick <= 25; tick++)
            Record(RunTick(registry));

        foreach (string plate in firstCohort)
        {
            Assert.True(camerasByPlate.TryGetValue(plate, out List<int>? cameras));
            Assert.Equal(Enumerable.Range(1, 10), cameras!);
        }

        void Record(IReadOnlyList<CameraCapture> captures)
        {
            foreach (CameraCapture capture in captures)
            {
                if (!firstCohort.Contains(capture.NumberPlate))
                    continue;

                if (!camerasByPlate.TryGetValue(capture.NumberPlate, out List<int>? cameras))
                    camerasByPlate[capture.NumberPlate] = cameras = new List<int>();

                cameras.Add(capture.CameraId);
            }
        }
    }

    [Fact]
    public void No_message_is_ever_emitted_for_a_camera_outside_one_to_ten()
    {
        var registry = CreateRegistry();

        for (int tick = 1; tick <= 30; tick++)
        {
            foreach (CameraCapture capture in RunTick(registry))
                Assert.InRange(capture.CameraId, 1, 10);
        }
    }

    [Fact]
    public void Same_seed_produces_an_identical_run()
    {
        var a = CreateRegistry(seed: 7);
        var b = CreateRegistry(seed: 7);

        for (int tick = 1; tick <= 12; tick++)
        {
            IReadOnlyList<CameraCapture> fromA = RunTick(a);
            IReadOnlyList<CameraCapture> fromB = RunTick(b);

            Assert.Equal(fromA.Count, fromB.Count);
            for (int i = 0; i < fromA.Count; i++)
            {
                Assert.Equal(fromA[i].NumberPlate, fromB[i].NumberPlate);
                Assert.Equal(fromA[i].CameraId, fromB[i].CameraId);
                Assert.Equal(fromA[i].SpeedKmh, fromB[i].SpeedKmh);
            }
        }
    }
}
