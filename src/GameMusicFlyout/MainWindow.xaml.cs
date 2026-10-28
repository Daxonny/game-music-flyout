using System.Windows;
using System.Windows.Controls;
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

        // Registered after OnSeekPressed so the seek begins before the drag does.
        SeekSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(StartThumbDrag), handledEventsToo: true);
        VolumeSlider.AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(StartThumbDrag), handledEventsToo: true);

        // Ctrl+wheel (scaling) is handled first at window level, so this only sees plain wheel turns.
        VolumePanel.PreviewMouseWheel += OnVolumeMouseWheel;
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

    /// <summary>
    /// IsMoveToPointEnabled jumps the thumb to a click on the track but doesn't start a drag, so a second
    /// press was needed to drag. Once the slider has moved the thumb under the cursor, hand the same press
    /// to the thumb so it keeps following the mouse until release.
    /// </summary>
    private static void StartThumbDrag(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.Template.FindName("PART_Track", slider) is not Track { Thumb: { } thumb }
            || thumb.IsDragging
            || (e.OriginalSource is DependencyObject source && thumb.IsAncestorOf(source)))
        {
            return; // Pressed on the thumb itself: it starts its own drag.
        }

        slider.UpdateLayout(); // Put the thumb at the new value before it records the drag origin.
        thumb.RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left)
        {
            RoutedEvent = MouseLeftButtonDownEvent,
            Source = thumb,
        });
    }

    private void OnVolumeMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ViewModel?.ChangeVolumeBy(Math.Sign(e.Delta));
        e.Handled = true;
    }

    private void OnSeekPressed(object sender, MouseButtonEventArgs e) => ViewModel?.BeginSeek();

    // One seek per gesture: release commits the final value; DragCompleted also covers a drag
    // that ends by losing mouse capture. CommitSeekAsync ignores the second call.
    private async void OnSeekReleased(object sender, MouseButtonEventArgs e) => await CommitSeekAsync();

    private async void OnSeekDragCompleted(object sender, DragCompletedEventArgs e) => await CommitSeekAsync();

    private Task CommitSeekAsync() => ViewModel?.CommitSeekAsync() ?? Task.CompletedTask;

    // User preference: ✕ exits the app; the global shortcut is for hiding.
    private void OnCloseClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
