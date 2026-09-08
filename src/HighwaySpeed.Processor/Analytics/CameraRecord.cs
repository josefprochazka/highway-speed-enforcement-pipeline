namespace HighwaySpeed.Processor.Analytics;

/// <summary>One entry on a single camera's "fastest vehicles" board.</summary>
internal readonly record struct CameraRecord(string NumberPlate, float SpeedKmh, DateTime CapturedAtUtc)
{
    /// <summary>
    /// Orders entries fastest first. An exact speed tie is broken by ordinal plate
    /// order, so every board (and every process) ranks an identical set of
    /// readings the same way.
    /// </summary>
    public static readonly IComparer<CameraRecord> FastestFirst = Comparer<CameraRecord>.Create(static (left, right) =>
    {
        int bySpeed = right.SpeedKmh.CompareTo(left.SpeedKmh);
        return bySpeed != 0
            ? bySpeed
            : string.CompareOrdinal(left.NumberPlate, right.NumberPlate);
    });
}
