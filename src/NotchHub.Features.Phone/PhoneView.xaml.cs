using System.Windows.Controls;
using NotchHub.UI;

namespace NotchHub.Features.Phone;

public partial class PhoneView : UserControl, ITabView
{
    public PhoneView()
    {
        InitializeComponent();
        DataContext = new PhoneViewModel();
    }

    public PhoneViewModel ViewModel => (PhoneViewModel)DataContext;

    public void OnShown() => ViewModel.OnShown();
    public void OnHidden() => ViewModel.OnHidden();
}
