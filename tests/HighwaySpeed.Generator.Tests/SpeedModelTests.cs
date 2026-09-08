using HighwaySpeed.Generator.Simulation;

namespace HighwaySpeed.Generator.Tests;

public class SpeedModelTests
{
    [Fact]
    public void New_cruising_speed_stays_within_a_realistic_band()
    {
        var model = new SpeedModel(speedLimitKmh: 130, new Random(1));

        for (int i = 0; i < 10_000; i++)
        {
            float cruising = model.NewCruisingSpeed();
            Assert.InRange(cruising, 110f, 175f);
        }
    }

    [Fact]
    public void Next_speed_is_clamped_and_wanders_around_the_cruising_speed()
    {
        var model = new SpeedModel(speedLimitKmh: 130, new Random(2));
        float speed = 130f;
        double sum = 0;
        const int samples = 20_000;

        for (int i = 0; i < samples; i++)
        {
            speed = model.NextSpeed(speed, cruisingSpeedKmh: 140f);
            Assert.InRange(speed, 80f, 220f);
            sum += speed;
        }

        // Mean-reversion should keep the long-run average near the cruising speed.
        double mean = sum / samples;
        Assert.InRange(mean, 130.0, 150.0);
    }

    [Fact]
    public void Same_seed_produces_the_same_sequence()
    {
        var a = new SpeedModel(130, new Random(42));
        var b = new SpeedModel(130, new Random(42));

        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NewCruisingSpeed(), b.NewCruisingSpeed());
    }
}
