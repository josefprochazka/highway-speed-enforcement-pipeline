using HighwaySpeed.Contracts;

namespace HighwaySpeed.Processor.Analytics;

/// <summary>
/// The bounded "Top 10 fastest vehicles" board for one camera, ranked by the
/// instantaneous speed recorded as each vehicle passed.
///
/// Thread safety: every board has its own lock. Ten boards therefore never
/// contend with each other, and each critical section is a handful of
/// comparisons on a ten-element list, so it is held for microseconds. A plain
/// sorted <see cref="List{T}"/> is intentionally chosen over anything fancier -
/// at ten items the insert-sort-trim cost is negligible and the code stays
/// obvious. If sustained throughput were orders of magnitude higher, a bounded
/// min-heap (evict the current slowest in O(log n)) would be the next step.
/// </summary>
internal sealed class CameraSpeedBoard
{
    public const int Capacity = 10;

    private readonly int _cameraId;
    private readonly object _gate = new();
    private readonly List<CameraRecord> _entries = new(Capacity + 1);

    public CameraSpeedBoard(int cameraId) => _cameraId = cameraId;

    public int CameraId => _cameraId;

    /// <summary>Offers a reading to the board; it is kept only if it makes the top ten.</summary>
    public void Register(in TelemetryReading reading)
    {
        var candidate = new CameraRecord(reading.NumberPlate, reading.SpeedKmh, reading.CapturedAtUtc);

        lock (_gate)
        {
            // A vehicle passes any given camera exactly once, so there is never an
            // existing row for this plate to replace.
            _entries.Add(candidate);
            _entries.Sort(CameraRecord.FastestFirst);

            if (_entries.Count > Capacity)
                _entries.RemoveAt(_entries.Count - 1);
        }
    }

    /// <summary>Whether the given plate currently holds a place on this board.</summary>
    public bool Contains(string numberPlate)
    {
        lock (_gate)
        {
            foreach (CameraRecord entry in _entries)
            {
                if (string.Equals(entry.NumberPlate, numberPlate, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Takes an immutable copy of the board for the REST snapshot.</summary>
    public CameraLeaderboard ToDto()
    {
        lock (_gate)
        {
            var rows = new List<CameraLeaderboardEntry>(_entries.Count);

            for (int i = 0; i < _entries.Count; i++)
            {
                CameraRecord entry = _entries[i];
                rows.Add(new CameraLeaderboardEntry
                {
                    Rank = i + 1,
                    NumberPlate = entry.NumberPlate,
                    SpeedKmh = entry.SpeedKmh,
                    CapturedAtUtc = entry.CapturedAtUtc,
                });
            }

            return new CameraLeaderboard { CameraId = _cameraId, Entries = rows };
        }
    }
}
