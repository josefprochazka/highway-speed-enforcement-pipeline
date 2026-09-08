namespace HighwaySpeed.Processor.Analytics;

/// <summary>
/// The processor's internal representation of one camera capture, mapped from the
/// gRPC <c>TelemetryMessage</c> at the edge of the system. Everything downstream
/// works with this type and never touches Protobuf.
/// </summary>
/// <param name="NumberPlate">Unique vehicle plate.</param>
/// <param name="CapturedAtUtc">When the vehicle passed the camera (UTC).</param>
/// <param name="SpeedKmh">Instantaneous speed at the camera, km/h.</param>
/// <param name="CameraId">Which camera, 1..10.</param>
internal readonly record struct TelemetryReading(
    string NumberPlate,
    DateTime CapturedAtUtc,
    float SpeedKmh,
    int CameraId);
