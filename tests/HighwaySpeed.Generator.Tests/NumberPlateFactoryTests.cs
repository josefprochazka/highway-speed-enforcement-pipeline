using System.Text.RegularExpressions;
using HighwaySpeed.Generator.Simulation;

namespace HighwaySpeed.Generator.Tests;

public class NumberPlateFactoryTests
{
    [Fact]
    public void Plates_are_unique_across_a_large_run()
    {
        var factory = new NumberPlateFactory();
        var seen = new HashSet<string>();

        for (int i = 0; i < 200_000; i++)
            Assert.True(seen.Add(factory.Next()), "produced a duplicate number plate");
    }

    [Fact]
    public void Plates_follow_the_expected_format()
    {
        var factory = new NumberPlateFactory();
        var pattern = new Regex("^[1-9][A-Z]{2}-[0-9]{4}$");

        for (int i = 0; i < 5_000; i++)
        {
            string plate = factory.Next();
            Assert.Matches(pattern, plate);
        }
    }
}
