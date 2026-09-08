using HighwaySpeed.Contracts;

namespace HighwaySpeed.Processor.Analytics;

/// <summary>
/// Default <see cref="ITrafficAnalytics"/>. Registered as a singleton and shared
/// by every ingestion worker.
///
/// The three collaborators it coordinates are each independently thread-safe, so
/// this class holds no lock of its own. <see cref="BuildSnapshot"/> copies each
/// board in turn: every board is internally consistent, though two boards may be
/// microseconds apart. A single globally-atomic snapshot across all eleven boards
/// would need a coarser reader/writer scheme; for a live dashboard the per-board
/// guarantee is enough.
/// </summary>
internal sealed class TrafficAnalytics : ITrafficAnalytics
{
    public const int CameraCount = 10;

    private readonly TimeProvider _timeProvider;
    private readonly CameraSpeedBoard[] _cameraBoards;
    private readonly GlobalAverageBoard _globalBoard = new();
    private readonly VehicleAverageTracker _averageTracker = new();

    private long _readingsProcessed;

    public TrafficAnalytics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        _cameraBoards = new CameraSpeedBoard[CameraCount];
        for (int i = 0; i < CameraCount; i++)
            _cameraBoards[i] = new CameraSpeedBoard(cameraId: i + 1);
    }

    public long ReadingsProcessed => Interlocked.Read(ref _readingsProcessed);

    public void Record(in TelemetryReading reading)
    {
        if (reading.CameraId is < 1 or > CameraCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reading), reading.CameraId, $"CameraId must be between 1 and {CameraCount}.");
        }

        Interlocked.Increment(ref _readingsProcessed);

        // 1. The instantaneous speed goes onto that camera's Top 10.
        _cameraBoards[reading.CameraId - 1].Register(reading);

        // 2. Fold the speed into the vehicle's running average; if it now clears
        //    the 3-camera gate, (re)publish it to the global board.
        AverageRecord? qualified = _averageTracker.Record(reading);
        if (qualified is { } record)
            _globalBoard.AddOrUpdate(record);

        // 3. Once a vehicle passes the final camera it is no longer live active
        //    traffic: it drops off the global board. Its running-average state is
        //    purged too, unless it still holds a place on a camera board - i.e.
        //    it "scored" and we keep that enforcement record.
        if (reading.CameraId == CameraCount)
        {
            _globalBoard.Remove(reading.NumberPlate);

            if (!AppearsOnAnyCameraBoard(reading.NumberPlate))
                _averageTracker.TryPurge(reading.NumberPlate);
        }
    }

    public TrafficSnapshot BuildSnapshot()
    {
        var cameraLeaderboards = new List<CameraLeaderboard>(CameraCount);
        foreach (CameraSpeedBoard board in _cameraBoards)
            cameraLeaderboards.Add(board.ToDto());

        return new TrafficSnapshot
        {
            GeneratedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            CameraLeaderboards = cameraLeaderboards,
            GlobalLeaderboard = _globalBoard.ToDto(),
        };
    }

    // --- diagnostics / test-only surface ---

    /// <summary>Whether the running-average tracker still holds state for this plate.</summary>
    internal bool IsTrackingAverage(string numberPlate) => _averageTracker.IsTracking(numberPlate);

    /// <summary>How many vehicles currently have running-average state.</summary>
    internal int TrackedVehicleCount => _averageTracker.TrackedVehicleCount;

    private bool AppearsOnAnyCameraBoard(string numberPlate)
    {
        foreach (CameraSpeedBoard board in _cameraBoards)
        {
            if (board.Contains(numberPlate))
                return true;
        }

        return false;
    }
}
