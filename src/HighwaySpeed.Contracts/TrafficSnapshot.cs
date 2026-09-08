namespace HighwaySpeed.Contracts;

/// <summary>
/// The payload returned by <c>GET /api/traffic-snapshot</c>.
///
/// This is a plain data-transfer object: an immutable, self-contained picture of
/// the analytics state at one instant. The processor builds a fresh instance for
/// every request by copying its internal leaderboards, so the client can never
/// observe a half-updated leaderboard and never holds a reference to live state.
/// </summary>
public sealed record TrafficSnapshot
{
    /// <summary>When this snapshot was assembled (UTC).</summary>
    public required DateTime GeneratedAtUtc { get; init; }

    /// <summary>
    /// One leaderboard per speed camera, ordered by <see cref="CameraLeaderboard.CameraId"/>
    /// (1..10). Each holds up to ten entries, fastest first.
    /// </summary>
    public required IReadOnlyList<CameraLeaderboard> CameraLeaderboards { get; init; }

    /// <summary>
    /// The single system-wide leaderboard of highest average speed among vehicles
    /// still on the monitored stretch.
    /// </summary>
    public required GlobalLeaderboard GlobalLeaderboard { get; init; }
}

/// <summary>Top-ten fastest vehicles recorded by one camera.</summary>
public sealed record CameraLeaderboard
{
    public required int CameraId { get; init; }

    /// <summary>Up to ten entries, ordered fastest first, ties broken by plate.</summary>
    public required IReadOnlyList<CameraLeaderboardEntry> Entries { get; init; }
}

/// <summary>One row on a <see cref="CameraLeaderboard"/>.</summary>
public sealed record CameraLeaderboardEntry
{
    /// <summary>1-based position on the leaderboard, for display convenience.</summary>
    public required int Rank { get; init; }

    public required string NumberPlate { get; init; }

    /// <summary>Instantaneous speed at the camera, km/h.</summary>
    public required float SpeedKmh { get; init; }

    /// <summary>Time the vehicle passed the camera (UTC).</summary>
    public required DateTime CapturedAtUtc { get; init; }
}

/// <summary>The system-wide "who is driving fastest on average right now" board.</summary>
public sealed record GlobalLeaderboard
{
    /// <summary>Up to ten entries, highest average first, ties broken by plate.</summary>
    public required IReadOnlyList<GlobalLeaderboardEntry> Entries { get; init; }
}

/// <summary>One row on the <see cref="GlobalLeaderboard"/>.</summary>
public sealed record GlobalLeaderboardEntry
{
    public required int Rank { get; init; }

    public required string NumberPlate { get; init; }

    /// <summary>Cumulative average of every camera speed recorded so far, km/h.</summary>
    public required float AverageSpeedKmh { get; init; }

    /// <summary>How many cameras the vehicle has passed (always &gt;= 3 to qualify).</summary>
    public required int CamerasPassed { get; init; }
}
