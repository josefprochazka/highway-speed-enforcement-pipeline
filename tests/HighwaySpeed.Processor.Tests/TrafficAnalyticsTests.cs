using HighwaySpeed.Contracts;
using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Tests;

/// <summary>
/// End-to-end tests for the analytics rules: the 3-camera gate, "live active
/// traffic only" on the global board, and the purge rule on camera 10.
/// </summary>
public class TrafficAnalyticsTests
{
    private static readonly DateTime AnyTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TrafficAnalytics CreateAnalytics() => new(TimeProvider.System);

    private static TelemetryReading Reading(string plate, float speed, int camera)
        => new(plate, AnyTime, speed, camera);

    /// <summary>Puts ten fast vehicles on every camera board so nothing slow can score.</summary>
    private static void FloodEveryCameraBoardWithFastTraffic(TrafficAnalytics analytics)
    {
        for (int camera = 1; camera <= TrafficAnalytics.CameraCount; camera++)
            for (int i = 0; i < CameraSpeedBoard.Capacity; i++)
                analytics.Record(Reading($"FILLER-{camera:D2}-{i:D2}", speed: 200, camera));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void Camera_id_outside_one_to_ten_is_rejected(int cameraId)
    {
        var analytics = CreateAnalytics();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => analytics.Record(Reading("CAR-1", 130, cameraId)));
    }

    [Fact]
    public void A_single_fast_first_camera_reading_never_reaches_the_global_board()
    {
        var analytics = CreateAnalytics();

        analytics.Record(Reading("SPIKE-1", speed: 300, camera: 1));

        Assert.Empty(analytics.BuildSnapshot().GlobalLeaderboard.Entries);
    }

    [Fact]
    public void A_vehicle_reaches_the_global_board_once_it_clears_the_third_camera()
    {
        var analytics = CreateAnalytics();

        analytics.Record(Reading("CRUISER-1", 160, camera: 1));
        analytics.Record(Reading("CRUISER-1", 160, camera: 2));
        Assert.Empty(analytics.BuildSnapshot().GlobalLeaderboard.Entries);

        analytics.Record(Reading("CRUISER-1", 160, camera: 3));

        GlobalLeaderboardEntry entry = Assert.Single(analytics.BuildSnapshot().GlobalLeaderboard.Entries);
        Assert.Equal("CRUISER-1", entry.NumberPlate);
        Assert.Equal(160f, entry.AverageSpeedKmh);
        Assert.Equal(3, entry.CamerasPassed);
    }

    [Fact]
    public void A_vehicle_leaves_the_global_board_after_passing_the_final_camera()
    {
        var analytics = CreateAnalytics();

        for (int camera = 1; camera <= 9; camera++)
            analytics.Record(Reading("RUNNER-1", 180, camera));

        Assert.Contains(analytics.BuildSnapshot().GlobalLeaderboard.Entries, e => e.NumberPlate == "RUNNER-1");

        analytics.Record(Reading("RUNNER-1", 180, camera: 10));

        Assert.DoesNotContain(analytics.BuildSnapshot().GlobalLeaderboard.Entries, e => e.NumberPlate == "RUNNER-1");
    }

    [Fact]
    public void A_vehicle_that_scores_on_no_board_is_purged_after_the_final_camera()
    {
        var analytics = CreateAnalytics();
        FloodEveryCameraBoardWithFastTraffic(analytics);

        for (int camera = 1; camera <= 9; camera++)
            analytics.Record(Reading("SLOWPOKE-1", speed: 80, camera));

        Assert.True(analytics.IsTrackingAverage("SLOWPOKE-1"), "should be tracked while still on the road");

        analytics.Record(Reading("SLOWPOKE-1", speed: 80, camera: 10));

        Assert.False(analytics.IsTrackingAverage("SLOWPOKE-1"), "should be purged: it scored nowhere");
    }

    [Fact]
    public void A_vehicle_that_holds_a_camera_record_keeps_its_data_after_the_final_camera()
    {
        var analytics = CreateAnalytics();
        FloodEveryCameraBoardWithFastTraffic(analytics);

        // Blazes through camera 5 fast enough to top its board, ordinary elsewhere.
        for (int camera = 1; camera <= 9; camera++)
        {
            float speed = camera == 5 ? 320 : 100;
            analytics.Record(Reading("RECORD-HOLDER", speed, camera));
        }

        analytics.Record(Reading("RECORD-HOLDER", speed: 100, camera: 10));

        Assert.True(analytics.IsTrackingAverage("RECORD-HOLDER"), "kept: still holds the camera 5 record");

        CameraLeaderboard cameraFive = analytics.BuildSnapshot().CameraLeaderboards.Single(c => c.CameraId == 5);
        Assert.Equal("RECORD-HOLDER", cameraFive.Entries[0].NumberPlate);
    }

    [Fact]
    public void Snapshot_always_has_ten_camera_leaderboards_and_a_fresh_timestamp()
    {
        var analytics = CreateAnalytics();
        DateTime before = DateTime.UtcNow;

        TrafficSnapshot snapshot = analytics.BuildSnapshot();

        Assert.Equal(10, snapshot.CameraLeaderboards.Count);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraLeaderboards.Select(c => c.CameraId));
        Assert.InRange(snapshot.GeneratedAtUtc, before, DateTime.UtcNow.AddSeconds(1));
    }
}
