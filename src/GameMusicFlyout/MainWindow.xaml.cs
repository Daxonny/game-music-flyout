using System.Windows;
using System.Windows.Input;

namespace GameMusicFlyout;

public partial class MainWindow : Window
{
    private const double ScreenMargin = 16;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Step 1 default: bottom-right of the primary work area. Step 4 replaces this with a persisted position.
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - ScreenMargin;
        Top = workArea.Bottom - ActualHeight - ScreenMargin;
    }

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Buttons and the seek bar handle their own clicks, so this only fires on empty card areas.
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        // Step 1 has no tray icon to bring the window back, so hiding exits for now. Step 4 changes this to Hide().
        Application.Current.Shutdown();
    }
}
