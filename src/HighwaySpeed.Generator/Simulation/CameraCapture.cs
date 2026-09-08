namespace HighwaySpeed.Generator.Simulation;

/// <summary>
/// A single "vehicle seen by a camera" event produced by the simulation.
///
/// This is the generator's own domain type. It is deliberately separate from the
/// generated gRPC <c>TelemetryMessage</c> so the simulation logic can be unit
/// tested without any Protobuf or networking dependency; the publisher is the one
/// place that maps between the two.
/// </summary>
/// <param name="NumberPlate">Unique vehicle plate, e.g. "2AB-0417".</param>
/// <param name="CameraId">Camera that captured the vehicle, 1..10 in travel order.</param>
/// <param name="SpeedKmh">Instantaneous speed at that camera, km/h.</param>
/// <param name="CapturedAtUtc">Capture time (UTC).</param>
public sealed record CameraCapture(string NumberPlate, int CameraId, float SpeedKmh, DateTime CapturedAtUtc);
