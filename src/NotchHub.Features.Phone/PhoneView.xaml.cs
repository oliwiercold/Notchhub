using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Phone;

public partial class PhoneView : UserControl, ITabView, IDisposable
{
    private readonly ViewLifetime _life;

    public PhoneView()
    {
        InitializeComponent();
        DataContext = new PhoneViewModel();
        _life = new ViewLifetime(this, ViewModel.OnShown, ViewModel.OnHidden, ViewModel.Release);
    }

    public PhoneViewModel ViewModel => (PhoneViewModel)DataContext;

    public void OnShown() => _life.Show();
    public void OnHidden() => _life.Hide();

    /// <summary>Called by the shell when it throws a long-hidden view away; a new view is created when the tab opens again.</summary>
    public void Dispose() => _life.Dispose();
}
