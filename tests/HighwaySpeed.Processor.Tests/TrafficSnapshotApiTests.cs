using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HighwaySpeed.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HighwaySpeed.Processor.Tests;

/// <summary>
/// Boots the whole processor in-memory (Kestrel replaced by a test server) and
/// checks the REST contract the WPF client depends on.
/// </summary>
public class TrafficSnapshotApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplicationFactory<Program> _factory;

    public TrafficSnapshotApiTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Traffic_snapshot_has_ten_camera_leaderboards_a_global_board_and_a_utc_timestamp()
    {
        HttpClient client = _factory.CreateClient();

        TrafficSnapshot? snapshot = await client.GetFromJsonAsync<TrafficSnapshot>(
            "/api/traffic-snapshot", JsonOptions);

        Assert.NotNull(snapshot);
        Assert.Equal(10, snapshot!.CameraLeaderboards.Count);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraLeaderboards.Select(c => c.CameraId));
        Assert.NotNull(snapshot.GlobalLeaderboard);
        Assert.InRange(
            snapshot.GeneratedAtUtc,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddMinutes(5));
    }
}
