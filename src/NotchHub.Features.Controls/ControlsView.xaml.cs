using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NotchHub.UI;
using NotchHub.UI.Animation;

namespace NotchHub.Features.Controls;

public partial class ControlsView : UserControl, ITabView, IDisposable
{
    private readonly ViewLifetime _life;

    public ControlsView()
    {
        InitializeComponent();
        DataContext = new ControlsViewModel();
        // SheetContent is not guaranteed to inherit the DataContext — set it explicitly.
        SheetRoot.DataContext = DataContext;
        _life = new ViewLifetime(this, ViewModel.OnShown, ViewModel.OnHidden, ViewModel.Release);
    }

    public ControlsViewModel ViewModel => (ControlsViewModel)DataContext;

    public void OnShown() => _life.Show();
    public void OnHidden() => _life.Hide();

    /// <summary>Called by the shell when it throws a long-hidden view away; a new view is created when the tab opens again.</summary>
    public void Dispose() => _life.Dispose();

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
