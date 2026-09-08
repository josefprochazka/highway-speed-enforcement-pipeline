using HighwaySpeed.Generator.Publishing;
using HighwaySpeed.Generator.Simulation;

namespace HighwaySpeed.Generator.Tests;

public class TelemetryMessageMappingTests
{
    [Fact]
    public void Capture_maps_onto_the_wire_contract_field_for_field()
    {
        var capturedAt = new DateTime(2026, 9, 8, 12, 30, 45, DateTimeKind.Utc);
        var capture = new CameraCapture("2AB-0417", CameraId: 7, SpeedKmh: 148.6f, CapturedAtUtc: capturedAt);

        var message = GrpcTelemetryPublisher.ToMessage(capture);

        Assert.Equal("2AB-0417", message.NumberPlate);
        Assert.Equal(capturedAt.Ticks, message.TimestampTicks);
        Assert.Equal(148.6f, message.CurrentSpeed);
        Assert.Equal(7, message.CameraId);
    }
}
