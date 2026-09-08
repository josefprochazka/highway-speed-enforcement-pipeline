using HighwaySpeed.Generator.Publishing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HighwaySpeed.Generator.Simulation;

/// <summary>
/// Drives the simulation: once per tick it runs the three registry steps in order
/// and forwards the resulting captures to the publisher.
/// </summary>
public sealed class TrafficSimulator : BackgroundService
{
    private readonly VehicleRegistry _registry;
    private readonly ITelemetryPublisher _publisher;
    private readonly GeneratorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TrafficSimulator> _logger;

    public TrafficSimulator(
        VehicleRegistry registry,
        ITelemetryPublisher publisher,
        IOptions<GeneratorOptions> options,
        TimeProvider timeProvider,
        ILogger<TrafficSimulator> logger)
    {
        _registry = registry;
        _publisher = publisher;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Simulation starting: {Vehicles} vehicles every {Interval}, streaming to {Address}.",
            _options.VehiclesPerTick, _options.TickInterval, _options.ProcessorGrpcAddress);

        using var timer = new PeriodicTimer(_options.TickInterval, _timeProvider);
        long tick = 0;

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            tick++;
            DateTime capturedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

            // The three steps, in the order the spec mandates.
            _registry.SpawnNewVehicles(_options.VehiclesPerTick);
            IReadOnlyList<CameraCapture> captures = _registry.AdvanceAndCapture(capturedAtUtc);
            int departed = _registry.RemoveDepartedVehicles();

            foreach (CameraCapture capture in captures)
                await _publisher.PublishAsync(capture, stoppingToken);

            if (tick % 5 == 0)
            {
                _logger.LogInformation(
                    "tick {Tick}: active={Active} emitted={Emitted} departed={Departed}",
                    tick, _registry.ActiveCount, captures.Count, departed);
            }
        }
    }
}
