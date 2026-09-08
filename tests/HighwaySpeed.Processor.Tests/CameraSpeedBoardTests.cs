using HighwaySpeed.Contracts;
using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Tests;

public class CameraSpeedBoardTests
{
    private static readonly DateTime AnyTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TelemetryReading Reading(string plate, float speed, int camera = 1)
        => new(plate, AnyTime, speed, camera);

    [Fact]
    public void Board_keeps_only_the_ten_fastest()
    {
        var board = new CameraSpeedBoard(cameraId: 3);

        for (int i = 0; i < 25; i++)
            board.Register(Reading($"CAR-{i:D3}", speed: 100 + i, camera: 3));

        CameraLeaderboard dto = board.ToDto();

        Assert.Equal(10, dto.Entries.Count);
        // Fastest were 124 .. 115.
        Assert.Equal(124f, dto.Entries[0].SpeedKmh);
        Assert.Equal(115f, dto.Entries[9].SpeedKmh);
    }

    [Fact]
    public void Entries_are_ordered_fastest_first_with_sequential_ranks()
    {
        var board = new CameraSpeedBoard(cameraId: 1);

        board.Register(Reading("A", 130));
        board.Register(Reading("B", 150));
        board.Register(Reading("C", 140));

        CameraLeaderboard dto = board.ToDto();

        Assert.Equal(new[] { "B", "C", "A" }, dto.Entries.Select(e => e.NumberPlate));
        Assert.Equal(new[] { 1, 2, 3 }, dto.Entries.Select(e => e.Rank));
    }

    [Fact]
    public void Exact_speed_ties_are_broken_alphabetically_by_plate()
    {
        var board = new CameraSpeedBoard(cameraId: 1);

        board.Register(Reading("ZZZ-001", 160));
        board.Register(Reading("AAA-001", 160));
        board.Register(Reading("MMM-001", 160));

        CameraLeaderboard dto = board.ToDto();

        Assert.Equal(new[] { "AAA-001", "MMM-001", "ZZZ-001" }, dto.Entries.Select(e => e.NumberPlate));
    }

    [Fact]
    public void Contains_reports_membership_of_the_current_top_ten()
    {
        var board = new CameraSpeedBoard(cameraId: 1);
        board.Register(Reading("FAST-1", 200));

        for (int i = 0; i < 12; i++)
            board.Register(Reading($"SLOW-{i:D2}", 90 + i));

        Assert.True(board.Contains("FAST-1"));
        Assert.False(board.Contains("SLOW-00")); // pushed off the bottom
        Assert.False(board.Contains("NEVER-SEEN"));
    }
}
