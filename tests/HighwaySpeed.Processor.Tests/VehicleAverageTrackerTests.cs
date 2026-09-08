using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Tests;

public class VehicleAverageTrackerTests
{
    private static readonly DateTime AnyTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TelemetryReading Reading(string plate, float speed, int camera)
        => new(plate, AnyTime, speed, camera);

    [Fact]
    public void Vehicle_does_not_qualify_before_the_third_camera()
    {
        var tracker = new VehicleAverageTracker();

        Assert.Null(tracker.Record(Reading("CAR-1", 200, camera: 1)));
        Assert.Null(tracker.Record(Reading("CAR-1", 200, camera: 2)));
    }

    [Fact]
    public void Vehicle_qualifies_on_the_third_camera_with_the_running_average()
    {
        var tracker = new VehicleAverageTracker();

        tracker.Record(Reading("CAR-1", 120, camera: 1));
        tracker.Record(Reading("CAR-1", 130, camera: 2));
        AverageRecord? qualified = tracker.Record(Reading("CAR-1", 140, camera: 3));

        Assert.NotNull(qualified);
        Assert.Equal("CAR-1", qualified!.Value.NumberPlate);
        Assert.Equal(3, qualified.Value.CamerasPassed);
        Assert.Equal(130.0, qualified.Value.AverageSpeedKmh, precision: 3); // (120+130+140)/3
    }

    [Fact]
    public void Average_keeps_accumulating_after_qualification()
    {
        var tracker = new VehicleAverageTracker();

        for (int camera = 1; camera <= 4; camera++)
            tracker.Record(Reading("CAR-1", 100, camera));

        AverageRecord? fifth = tracker.Record(Reading("CAR-1", 150, camera: 5));

        Assert.NotNull(fifth);
        Assert.Equal(5, fifth!.Value.CamerasPassed);
        Assert.Equal(110.0, fifth.Value.AverageSpeedKmh, precision: 3); // (100*4 + 150) / 5
    }

    [Fact]
    public void Purge_removes_all_tracking_state()
    {
        var tracker = new VehicleAverageTracker();
        tracker.Record(Reading("CAR-1", 100, camera: 1));

        Assert.True(tracker.IsTracking("CAR-1"));
        Assert.True(tracker.TryPurge("CAR-1"));
        Assert.False(tracker.IsTracking("CAR-1"));
        Assert.False(tracker.TryPurge("CAR-1"));
    }
}
