using HighwaySpeed.Contracts;
using HighwaySpeed.Processor.Analytics;

namespace HighwaySpeed.Processor.Tests;

public class GlobalAverageBoardTests
{
    [Fact]
    public void Entries_are_ordered_by_average_speed_descending()
    {
        var board = new GlobalAverageBoard();
        board.AddOrUpdate(new AverageRecord("A", AverageSpeedKmh: 150, CamerasPassed: 5));
        board.AddOrUpdate(new AverageRecord("B", AverageSpeedKmh: 170, CamerasPassed: 5));
        board.AddOrUpdate(new AverageRecord("C", AverageSpeedKmh: 160, CamerasPassed: 5));

        GlobalLeaderboard dto = board.ToDto();

        Assert.Equal(new[] { "B", "C", "A" }, dto.Entries.Select(e => e.NumberPlate));
        Assert.Equal(new[] { 1, 2, 3 }, dto.Entries.Select(e => e.Rank));
    }

    [Fact]
    public void An_entry_is_updated_in_place_for_the_same_plate()
    {
        var board = new GlobalAverageBoard();
        board.AddOrUpdate(new AverageRecord("A", 120, 3));
        board.AddOrUpdate(new AverageRecord("A", 190, 6)); // same vehicle, later

        GlobalLeaderboard dto = board.ToDto();

        GlobalLeaderboardEntry only = Assert.Single(dto.Entries);
        Assert.Equal(190f, only.AverageSpeedKmh);
        Assert.Equal(6, only.CamerasPassed);
    }

    [Fact]
    public void Ties_break_alphabetically_by_plate()
    {
        var board = new GlobalAverageBoard();
        board.AddOrUpdate(new AverageRecord("ZZ", 150, 4));
        board.AddOrUpdate(new AverageRecord("AA", 150, 4));

        GlobalLeaderboard dto = board.ToDto();

        Assert.Equal(new[] { "AA", "ZZ" }, dto.Entries.Select(e => e.NumberPlate));
    }

    [Fact]
    public void Snapshot_is_capped_at_ten_even_though_more_are_tracked()
    {
        var board = new GlobalAverageBoard();
        for (int i = 0; i < 30; i++)
            board.AddOrUpdate(new AverageRecord($"CAR-{i:D2}", 100 + i, 4));

        GlobalLeaderboard dto = board.ToDto();

        Assert.Equal(10, dto.Entries.Count);
        Assert.Equal(129f, dto.Entries[0].AverageSpeedKmh);
    }

    [Fact]
    public void Removed_vehicles_disappear_from_the_snapshot()
    {
        var board = new GlobalAverageBoard();
        board.AddOrUpdate(new AverageRecord("A", 150, 4));
        board.AddOrUpdate(new AverageRecord("B", 140, 4));

        board.Remove("A");

        Assert.False(board.Contains("A"));
        Assert.Equal(new[] { "B" }, board.ToDto().Entries.Select(e => e.NumberPlate));
    }
}
