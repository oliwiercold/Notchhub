using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotchHub.UI;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Controls;

public partial class ControlsView : UserControl, ITabView
{
    public ControlsView()
    {
        InitializeComponent();
        DataContext = new ControlsViewModel();
        // SheetContent is not guaranteed to inherit the DataContext — set it explicitly.
        SheetRoot.DataContext = DataContext;
    }

    public ControlsViewModel ViewModel => (ControlsViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();

    private void OnVolumeDragStarted(object sender, RoutedEventArgs e) => ViewModel.IsDraggingVolume = true;
    private void OnVolumeDragCompleted(object sender, RoutedEventArgs e) => ViewModel.IsDraggingVolume = false;
    private void OnBrightnessDragStarted(object sender, RoutedEventArgs e) => ViewModel.IsDraggingBrightness = true;
    private void OnBrightnessDragCompleted(object sender, RoutedEventArgs e) => ViewModel.IsDraggingBrightness = false;
    private void OnSeekCompleted(object? sender, double fraction) => ViewModel.Seek(fraction);

    /// <summary>Spring "pop" of the Spotify heart: squash, then overshoot back to 1.</summary>
    private void OnLikeClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.ShowLike) return;
        LikeScale.Set(ScaleTransform.ScaleXProperty, 0.72);
        LikeScale.Set(ScaleTransform.ScaleYProperty, 0.72);
        LikeScale.Animate(ScaleTransform.ScaleXProperty, 1.0, MotionToken.Pop);
        LikeScale.Animate(ScaleTransform.ScaleYProperty, 1.0, MotionToken.Pop);
    }
}
