using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Power;

public partial class PowerView : UserControl, ITabView, IDisposable
{
    private readonly ViewLifetime _life;

    public PowerView()
    {
        InitializeComponent();
        DataContext = new PowerViewModel();
        _life = new ViewLifetime(this, ViewModel.OnShown, ViewModel.OnHidden, ViewModel.Release);
    }

    public PowerViewModel ViewModel => (PowerViewModel)DataContext;

    public void OnShown() => _life.Show();
    public void OnHidden() => _life.Hide();

    /// <summary>Called by the shell when it throws a long-hidden view away; a new view is created when the tab opens again.</summary>
    public void Dispose() => _life.Dispose();
}
