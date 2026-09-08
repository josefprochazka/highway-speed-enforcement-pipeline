using HighwaySpeed.Processor.Analytics;
using HighwaySpeed.Processor.Ingestion;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Two protocols, two ports (configurable via the "HighwaySpeed" section):
//   5080  REST/HTTP-1.1  - the WPF client polls GET /api/traffic-snapshot here
//   5081  gRPC/HTTP-2    - the generator streams telemetry here
// Keeping them on separate ports means we don't need TLS just to let HTTP/2 and
// HTTP/1.1 share one socket. In Docker both ports are published to the host.
int restPort = builder.Configuration.GetValue("HighwaySpeed:RestPort", 5080);
int grpcPort = builder.Configuration.GetValue("HighwaySpeed:GrpcPort", 5081);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(restPort, listen => listen.Protocols = HttpProtocols.Http1);
    kestrel.ListenAnyIP(grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
});

// One clock for the whole process; makes the snapshot timestamp testable.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<ITrafficAnalytics, TrafficAnalytics>();
builder.Services.AddSingleton<TelemetryIngestPipeline>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelemetryIngestPipeline>());

builder.Services.AddGrpc();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Interactive API reference at /scalar/v1 (served off the OpenAPI document).
app.MapOpenApi();
app.MapScalarApiReference();

app.MapGrpcService<TelemetryIngestService>();

app.MapGet("/api/traffic-snapshot", (ITrafficAnalytics analytics) => analytics.BuildSnapshot())
    .WithName("GetTrafficSnapshot")
    .WithSummary("Current per-camera and global speed leaderboards, plus the UTC time the snapshot was taken.");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.Run();

// Exposed so the test project can host the app in-memory with WebApplicationFactory.
public partial class Program;
