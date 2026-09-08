namespace HighwaySpeed.Generator.Simulation;

/// <summary>
/// Hands out unique, plausible-looking number plates.
///
/// The plate is a deterministic function of an incrementing serial number, laid
/// out as <c>D LL - DDDD</c> (one digit 1-9, two letters, a 1000-9999 number),
/// e.g. <c>2AB-1041</c>. Because the mapping serial → plate is a bijection, every
/// plate is unique by construction and we never have to remember which ones were
/// already issued. The components are ordered so that the digit, then the
/// letters, then the number all change as the serial advances, which keeps
/// consecutive plates visibly different. Capacity is 9 × 26 × 26 × 9000 ≈ 54.7
/// million plates, far more than any demo run needs.
/// </summary>
public sealed class NumberPlateFactory
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    // Only ever touched from the single simulation loop, so a plain field is enough.
    private long _nextSerial;

    public string Next()
    {
        long serial = _nextSerial++;

        int digit = (int)(serial % 9) + 1;
        serial /= 9;

        char second = Letters[(int)(serial % 26)];
        serial /= 26;

        char first = Letters[(int)(serial % 26)];
        serial /= 26;

        int number = (int)(serial % 9_000) + 1_000;

        return $"{digit}{first}{second}-{number}";
    }
}
