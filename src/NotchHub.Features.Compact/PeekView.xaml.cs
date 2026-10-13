using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Compact;

public partial class PeekView : UserControl, ITabView
{
    public PeekView()
    {
        InitializeComponent();
        DataContext = new PeekViewModel();
    }

    public PeekViewModel ViewModel => (PeekViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();

    private void OnSeekCompleted(object? sender, double fraction) => ViewModel.Seek(fraction);
}
