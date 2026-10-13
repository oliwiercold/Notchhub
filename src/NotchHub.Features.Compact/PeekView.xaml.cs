using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Compact;

public partial class PeekView : UserControl, ITabView, IDisposable
{
    private readonly ViewLifetime _life;

    public PeekView()
    {
        InitializeComponent();
        DataContext = new PeekViewModel();
        _life = new ViewLifetime(this, ViewModel.OnShown, ViewModel.OnHidden, ViewModel.Release);
    }

    public PeekViewModel ViewModel => (PeekViewModel)DataContext;

    public void OnShown() => _life.Show();
    public void OnHidden() => _life.Hide();

    /// <summary>Called by the shell when it throws the view away; a new view is created when needed again.</summary>
    public void Dispose() => _life.Dispose();

    private void OnSeekCompleted(object? sender, double fraction) => ViewModel.Seek(fraction);
}
