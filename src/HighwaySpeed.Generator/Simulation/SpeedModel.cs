namespace HighwaySpeed.Generator.Simulation;

/// <summary>
/// Produces speed values that vary naturally around the highway speed limit.
///
/// Two ideas:
///   * every driver gets their own "cruising speed" drawn once, around the limit,
///     so the population has a realistic spread of law-abiding drivers and speeders;
///   * between cameras the speed does a random walk that is gently pulled back
///     toward that cruising speed, so values wander but never drift off to nonsense.
/// </summary>
public sealed class SpeedModel
{
    private readonly float _speedLimitKmh;
    private readonly Random _random;

    // How strongly the speed is pulled back toward the driver's cruising speed
    // each step. 0 = pure random walk, 1 = snap straight back.
    private const double ReversionStrength = 0.25;

    public SpeedModel(float speedLimitKmh, Random random)
    {
        _speedLimitKmh = speedLimitKmh;
        _random = random;
    }

    /// <summary>Draws a cruising speed for a newly spawned vehicle.</summary>
    public float NewCruisingSpeed()
    {
        double cruising = _speedLimitKmh + Gaussian(mean: 0, stdDev: 12);
        return (float)Math.Clamp(cruising, 110, 175);
    }

    /// <summary>Next instantaneous speed given the previous one and the driver's cruising speed.</summary>
    public float NextSpeed(float previousKmh, float cruisingSpeedKmh)
    {
        double reversion = (cruisingSpeedKmh - previousKmh) * ReversionStrength;
        double next = previousKmh + reversion + Gaussian(mean: 0, stdDev: 4);
        return (float)Math.Clamp(next, 80, 220);
    }

    /// <summary>Standard normal sample via the Box–Muller transform.</summary>
    private double Gaussian(double mean, double stdDev)
    {
        double u1 = 1.0 - _random.NextDouble();
        double u2 = 1.0 - _random.NextDouble();
        double standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * standardNormal;
    }
}
