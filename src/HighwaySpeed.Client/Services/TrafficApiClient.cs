using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using HighwaySpeed.Contracts;

namespace HighwaySpeed.Client.Services;

/// <summary>
/// Thin wrapper over <see cref="HttpClient"/> for the one endpoint this client
/// needs. Keeping it separate makes the view-model trivial to test with a fake.
/// </summary>
public sealed class TrafficApiClient
{
    // Match ASP.NET Core's default camelCase JSON.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public TrafficApiClient(HttpClient httpClient) => _httpClient = httpClient;

    /// <summary>Fetches the current leaderboard snapshot, or throws if the service is unreachable.</summary>
    public async Task<TrafficSnapshot?> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return await _httpClient
            .GetFromJsonAsync<TrafficSnapshot>("/api/traffic-snapshot", JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
