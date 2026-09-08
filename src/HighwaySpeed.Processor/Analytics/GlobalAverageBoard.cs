using HighwaySpeed.Contracts;

namespace HighwaySpeed.Processor.Analytics;

/// <summary>
/// The single system-wide leaderboard of highest average speed among vehicles
/// that are still on the monitored stretch ("live active traffic only").
///
/// Unlike a camera board, an entry here is keyed by plate and is updated in place
/// every time the vehicle passes another camera and its running average moves.
///
/// Design choice: internally we keep <em>every</em> currently-qualifying active
/// vehicle, not just ten. The REST snapshot exposes the top ten. Keeping the full
/// live set is a few hundred small structs at most and it means a vehicle that
/// briefly drops out of the visible top ten and then speeds up is ranked
/// correctly again with no "re-admission" bookkeeping. Making the board strictly
/// bounded instead would be a one-line change in <see cref="AddOrUpdate"/>.
///
/// Thread safety: a single lock guarding a dictionary. Writes are O(1); building
/// the snapshot is an O(n log n) sort over that small live set.
/// </summary>
internal sealed class GlobalAverageBoard
{
    public const int SnapshotSize = 10;

    private readonly object _gate = new();
    private readonly Dictionary<string, AverageRecord> _entriesByPlate = new(StringComparer.Ordinal);

    /// <summary>Inserts or refreshes the vehicle's entry.</summary>
    public void AddOrUpdate(in AverageRecord record)
    {
        lock (_gate)
        {
            _entriesByPlate[record.NumberPlate] = record;
        }
    }

    /// <summary>Removes the vehicle (e.g. once it has left the sector).</summary>
    public void Remove(string numberPlate)
    {
        lock (_gate)
        {
            _entriesByPlate.Remove(numberPlate);
        }
    }

    /// <summary>Whether the vehicle currently has an entry (i.e. it "scored" on this board).</summary>
    public bool Contains(string numberPlate)
    {
        lock (_gate)
        {
            return _entriesByPlate.ContainsKey(numberPlate);
        }
    }

    /// <summary>Takes an immutable top-ten copy for the REST snapshot.</summary>
    public GlobalLeaderboard ToDto()
    {
        lock (_gate)
        {
            List<GlobalLeaderboardEntry> rows = _entriesByPlate.Values
                .OrderByDescending(entry => entry.AverageSpeedKmh)
                .ThenBy(entry => entry.NumberPlate, StringComparer.Ordinal)
                .Take(SnapshotSize)
                .Select((entry, index) => new GlobalLeaderboardEntry
                {
                    Rank = index + 1,
                    NumberPlate = entry.NumberPlate,
                    AverageSpeedKmh = entry.AverageSpeedKmh,
                    CamerasPassed = entry.CamerasPassed,
                })
                .ToList();

            return new GlobalLeaderboard { Entries = rows };
        }
    }
}
