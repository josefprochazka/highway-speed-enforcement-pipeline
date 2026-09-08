using HighwaySpeed.Generator;
using HighwaySpeed.Generator.Publishing;
using HighwaySpeed.Generator.Simulation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// Allow gRPC over plain-text HTTP/2 (no TLS). Fine for a local prototype talking
// to a container on the same machine; a real deployment would use HTTPS.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<GeneratorOptions>()
    .Bind(builder.Configuration.GetSection(GeneratorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// One shared clock and RNG so a run can be made reproducible via RandomSeed.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider =>
{
    GeneratorOptions options = serviceProvider.GetRequiredService<IOptions<GeneratorOptions>>().Value;
    return options.RandomSeed is int seed ? new Random(seed) : new Random();
});

builder.Services.AddSingleton<NumberPlateFactory>();
builder.Services.AddSingleton(serviceProvider =>
{
    GeneratorOptions options = serviceProvider.GetRequiredService<IOptions<GeneratorOptions>>().Value;
    return new SpeedModel(options.BaselineSpeedKmh, serviceProvider.GetRequiredService<Random>());
});
builder.Services.AddSingleton<VehicleRegistry>();

// The publisher is both a hosted service (its background pump) and the sink the
// simulator writes to, so it is registered once and surfaced under both roles.
builder.Services.AddSingleton<GrpcTelemetryPublisher>();
builder.Services.AddSingleton<ITelemetryPublisher>(sp => sp.GetRequiredService<GrpcTelemetryPublisher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GrpcTelemetryPublisher>());

// Registered after the publisher so it starts second and stops first: on shutdown
// the simulator stops producing before the publisher drains and closes the stream.
builder.Services.AddHostedService<TrafficSimulator>();

IHost host = builder.Build();
await host.RunAsync();
