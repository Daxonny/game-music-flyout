using System.Windows;
using GameMusicFlyout.ViewModels;

namespace GameMusicFlyout;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var viewModel = new FlyoutViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
    }
}
