using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using HighwaySpeed.Client.Infrastructure;
using HighwaySpeed.Client.Services;
using HighwaySpeed.Contracts;

namespace HighwaySpeed.Client.ViewModels;

/// <summary>
/// Backs the dashboard window. Owns a background polling loop that fetches a
/// snapshot every <c>PollIntervalSeconds</c> and pushes the results into the
/// bound collections. The only thing that touches the UI thread is
/// <see cref="ApplySnapshot"/>, marshalled there via the captured dispatcher.
/// </summary>
public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly TrafficApiClient _apiClient;
    private readonly TimeSpan _pollInterval;
    private readonly Dispatcher _dispatcher;

    private CancellationTokenSource? _cancellation;
    private Task? _pollingLoop;
    private TrafficSnapshot? _latestSnapshot;

    public DashboardViewModel(TrafficApiClient apiClient, ClientOptions options)
    {
        _apiClient = apiClient;
        _pollInterval = TimeSpan.FromSeconds(options.PollIntervalSeconds);

        // Constructed on the UI thread, so this captures the UI dispatcher.
        _dispatcher = Dispatcher.CurrentDispatcher;

        RefreshNowCommand = new RelayCommand(() => _ = FetchOnceAsync(_cancellation?.Token ?? CancellationToken.None));
    }

    /// <summary>Global Top 10 by highest average speed.</summary>
    public ObservableCollection<GlobalLeaderboardEntry> GlobalLeaderboard { get; } = new();

    /// <summary>Cameras the user can pick from (1..10).</summary>
    public ObservableCollection<int> Cameras { get; } = new(Enumerable.Range(1, 10));

    /// <summary>Top 10 fastest vehicles for <see cref="SelectedCamera"/>.</summary>
    public ObservableCollection<CameraLeaderboardEntry> SelectedCameraLeaderboard { get; } = new();

    public ICommand RefreshNowCommand { get; }

    private int _selectedCamera = 1;
    public int SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (SetProperty(ref _selectedCamera, value))
                ShowSelectedCameraFromLatestSnapshot();
        }
    }

    private string _connectionStatus = "Starting…";
    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    private DateTime? _lastUpdatedUtc;
    public DateTime? LastUpdatedUtc
    {
        get => _lastUpdatedUtc;
        private set => SetProperty(ref _lastUpdatedUtc, value);
    }

    /// <summary>Starts the background polling loop. Call once, from the UI thread.</summary>
    public void Start()
    {
        _cancellation = new CancellationTokenSource();
        _pollingLoop = Task.Run(() => PollLoopAsync(_cancellation.Token));
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        // Fetch straight away, then keep a steady 3-second cadence regardless of
        // how long any single fetch takes.
        using var timer = new PeriodicTimer(_pollInterval);
        do
        {
            await FetchOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        while (!cancellationToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task FetchOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            TrafficSnapshot? snapshot = await _apiClient.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
                return;

            _dispatcher.Invoke(() => ApplySnapshot(snapshot));
        }
        catch (OperationCanceledException)
        {
            // Window closing - ignore.
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() => ConnectionStatus = $"Offline – {ex.Message}");
        }
    }

    private void ApplySnapshot(TrafficSnapshot snapshot)
    {
        _latestSnapshot = snapshot;

        GlobalLeaderboard.Clear();
        foreach (GlobalLeaderboardEntry entry in snapshot.GlobalLeaderboard.Entries)
            GlobalLeaderboard.Add(entry);

        ShowSelectedCameraFromLatestSnapshot();

        LastUpdatedUtc = snapshot.GeneratedAtUtc;
        ConnectionStatus = "Connected";
    }

    private void ShowSelectedCameraFromLatestSnapshot()
    {
        SelectedCameraLeaderboard.Clear();

        CameraLeaderboard? board = _latestSnapshot?.CameraLeaderboards
            .FirstOrDefault(camera => camera.CameraId == SelectedCamera);

        if (board is null)
            return;

        foreach (CameraLeaderboardEntry entry in board.Entries)
            SelectedCameraLeaderboard.Add(entry);
    }

    public void Dispose()
    {
        _cancellation?.Cancel();

        try
        {
            _pollingLoop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Loop was cancelled while awaiting - expected during shutdown.
        }

        _cancellation?.Dispose();
    }
}
