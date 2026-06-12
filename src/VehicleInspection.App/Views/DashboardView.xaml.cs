using System.Windows.Controls;
using System.Windows.Input;
using VehicleInspection.App.Controls;
using VehicleInspection.App.ViewModels;

namespace VehicleInspection.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private async void VlprImage_LicensePlateUpdated(object sender, LicensePlateUpdatedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            await vm.UpdateLicensePlateAsync(e.OldPlate, e.NewPlate);
        }
    }

    private void NotesTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SaveNotesAsync(sender);
            e.Handled = true;
        }
    }

    private void NotesTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = SaveNotesAsync(sender);
    }

    private async System.Threading.Tasks.Task SaveNotesAsync(object sender)
    {
        if (sender is not TextBox tb) return;
        if (DataContext is DashboardViewModel vm)
        {
            await vm.UpdateNotesAsync(tb.Text);
        }
    }
}
