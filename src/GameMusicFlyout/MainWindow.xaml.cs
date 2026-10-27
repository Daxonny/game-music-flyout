using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GameMusicFlyout.ViewModels;

namespace GameMusicFlyout;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The slider marks these handled itself (IsMoveToPointEnabled), so listen to handled events too.
        SeekSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnSeekPressed), handledEventsToo: true);
        SeekSlider.AddHandler(PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnSeekReleased), handledEventsToo: true);
    }

    private FlyoutViewModel? ViewModel => DataContext as FlyoutViewModel;

    /// <summary>Scales the whole card proportionally; the window resizes to fit.</summary>
    public void ApplyScale(double scale) => CardRoot.LayoutTransform = new ScaleTransform(scale, scale);

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Buttons and the seek bar handle their own clicks, so this only fires on empty card areas.
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnSeekPressed(object sender, MouseButtonEventArgs e) => ViewModel?.BeginSeek();

    // One seek per gesture: release commits the final value; DragCompleted also covers a drag
    // that ends by losing mouse capture. CommitSeekAsync ignores the second call.
    private async void OnSeekReleased(object sender, MouseButtonEventArgs e) => await CommitSeekAsync();

    private async void OnSeekDragCompleted(object sender, DragCompletedEventArgs e) => await CommitSeekAsync();

    private Task CommitSeekAsync() => ViewModel?.CommitSeekAsync() ?? Task.CompletedTask;

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();
}
