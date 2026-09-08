namespace HighwaySpeed.Client;

/// <summary>Client configuration, bound from the "Api" section of appsettings.json.</summary>
public sealed class ClientOptions
{
    public const string SectionName = "Api";

    /// <summary>Base address of the processor's REST endpoint.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>How often the dashboard polls the API. The spec fixes this at 3 seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 3;
}
