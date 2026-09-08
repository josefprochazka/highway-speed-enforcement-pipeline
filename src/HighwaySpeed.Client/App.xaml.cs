using System.IO;
using System.Net.Http;
using System.Windows;
using HighwaySpeed.Client.Services;
using HighwaySpeed.Client.ViewModels;
using Microsoft.Extensions.Configuration;

namespace HighwaySpeed.Client;

/// <summary>
/// Application entry point. Reads configuration, builds the small object graph by
/// hand (the app has three collaborators - a full DI container would be overkill),
/// shows the dashboard, and tears the polling loop down on exit.
/// </summary>
public partial class App : Application
{
    private DashboardViewModel? _dashboard;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        ClientOptions options = configuration.GetSection(ClientOptions.SectionName).Get<ClientOptions>()
                                ?? new ClientOptions();

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl),
            Timeout = TimeSpan.FromSeconds(10),
        };

        var apiClient = new TrafficApiClient(httpClient);
        _dashboard = new DashboardViewModel(apiClient, options);

        var window = new MainWindow { DataContext = _dashboard };
        _dashboard.Start();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dashboard?.Dispose();
        base.OnExit(e);
    }
}
