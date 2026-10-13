using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Stats;

public partial class StatsView : UserControl, ITabView
{
    public StatsView()
    {
        InitializeComponent();
        DataContext = new StatsViewModel();
    }

    public StatsViewModel ViewModel => (StatsViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();
}
