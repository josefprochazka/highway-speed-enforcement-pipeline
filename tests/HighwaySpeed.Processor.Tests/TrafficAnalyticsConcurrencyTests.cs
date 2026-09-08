using HighwaySpeed.Contracts;
using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Tests;

/// <summary>
/// Hammers the analytics engine from many threads at once - the scenario the
/// spec calls out ("hundreds of concurrent messages") - and checks that every
/// invariant still holds and nothing throws.
/// </summary>
public class TrafficAnalyticsConcurrencyTests
{
    private static readonly DateTime AnyTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Concurrent_ingestion_keeps_every_board_consistent()
    {
        var analytics = new TrafficAnalytics(TimeProvider.System);

        const int threads = 16;
        const int readingsPerThread = 20_000;

        Parallel.For(0, threads, thread =>
        {
            var random = new Random(thread);
            for (int i = 0; i < readingsPerThread; i++)
            {
                string plate = $"T{thread:D2}-{random.Next(0, 500):D3}";
                int camera = random.Next(1, 11);
                float speed = 90 + random.Next(0, 130);
                analytics.Record(new TelemetryReading(plate, AnyTime, speed, camera));
            }
        });

        Assert.Equal((long)threads * readingsPerThread, analytics.ReadingsProcessed);

        TrafficSnapshot snapshot = analytics.BuildSnapshot();

        // Every camera board: at most 10 entries, still correctly ordered.
        Assert.Equal(10, snapshot.CameraLeaderboards.Count);
        foreach (CameraLeaderboard board in snapshot.CameraLeaderboards)
        {
            Assert.True(board.Entries.Count <= 10);
            AssertOrderedFastestFirst(board.Entries.Select(e => (e.SpeedKmh, e.NumberPlate)));
        }

        // Global board: at most 10, ordered by average desc, and only qualified vehicles.
        Assert.True(snapshot.GlobalLeaderboard.Entries.Count <= 10);
        Assert.All(snapshot.GlobalLeaderboard.Entries, e => Assert.True(e.CamerasPassed >= 3));
        AssertOrderedFastestFirst(snapshot.GlobalLeaderboard.Entries.Select(e => (e.AverageSpeedKmh, e.NumberPlate)));
    }

    [Fact]
    public void The_true_top_ten_survives_a_concurrent_stampede_on_one_camera()
    {
        var analytics = new TrafficAnalytics(TimeProvider.System);

        // Ten unmistakably fast vehicles that must end up on camera 4's board...
        var winners = Enumerable.Range(0, 10).Select(i => $"WINNER-{i:D2}").ToArray();

        Parallel.Invoke(
            () =>
            {
                foreach (string plate in winners)
                    analytics.Record(new TelemetryReading(plate, AnyTime, SpeedKmh: 260, CameraId: 4));
            },
            () =>
            {
                for (int i = 0; i < 50_000; i++)
                    analytics.Record(new TelemetryReading($"SLOW-{i:D5}", AnyTime, SpeedKmh: 120, CameraId: 4));
            });

        CameraLeaderboard cameraFour = analytics.BuildSnapshot().CameraLeaderboards.Single(c => c.CameraId == 4);

        Assert.Equal(10, cameraFour.Entries.Count);
        Assert.Equal(winners.OrderBy(p => p, StringComparer.Ordinal),
                     cameraFour.Entries.Select(e => e.NumberPlate).OrderBy(p => p, StringComparer.Ordinal));
    }

    private static void AssertOrderedFastestFirst(IEnumerable<(float Speed, string Plate)> entries)
    {
        (float Speed, string Plate)? previous = null;
        foreach ((float Speed, string Plate) current in entries)
        {
            if (previous is { } p)
            {
                bool ordered = p.Speed > current.Speed
                               || (p.Speed == current.Speed && string.CompareOrdinal(p.Plate, current.Plate) <= 0);
                Assert.True(ordered, $"out of order: {p} then {current}");
            }

            previous = current;
        }
    }
}
