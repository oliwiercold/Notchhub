using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Power;

public partial class PowerView : UserControl, ITabView
{
    public PowerView()
    {
        InitializeComponent();
        DataContext = new PowerViewModel();
    }

    public PowerViewModel ViewModel => (PowerViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();
}
