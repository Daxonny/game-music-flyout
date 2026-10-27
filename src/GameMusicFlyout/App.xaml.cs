using System.Windows;
using GameMusicFlyout.Services;
using GameMusicFlyout.ViewModels;

namespace GameMusicFlyout;

public partial class App : Application
{
    private GsmTCMediaSessionService? _mediaService;
    private FlyoutViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mediaService = new GsmTCMediaSessionService();
        _viewModel = new FlyoutViewModel(_mediaService, Dispatcher);
        var window = new MainWindow { DataContext = _viewModel };
        window.Show();

        // Show the window first; connecting to media sessions is async and never throws.
        await _mediaService.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _mediaService?.Dispose();
        base.OnExit(e);
    }
}
