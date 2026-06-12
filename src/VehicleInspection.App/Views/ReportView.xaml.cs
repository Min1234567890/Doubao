using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VehicleInspection.App.Localization;
using VehicleInspection.Application.Models;
using VehicleInspection.App.ViewModels;

namespace VehicleInspection.App.Views;

public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
        Loc.LanguageChanged += (_, _) => RefreshColumnHeaders();
    }

    private void RefreshColumnHeaders()
    {
        foreach (var col in SearchDataGrid.Columns)
        {
            var key = col.Header switch
            {
                "Scan Time" or "وقت المسح" or "Masa Imbasan" or "เวลาสแกน" => "ScanTime",
                "Plate" or "اللوحة" or "Plat" or "ป้าย" => "Plate",
                "Status" or "الحالة" or "สถานะ" => "Status",
                "Lane" or "المسار" or "Lorong" or "ช่องทาง" => "Lane",
                "Operator" or "المشغل" or "ผู้ปฏิบัติงาน" => "Operator",
                "FOD" or "أجسام غريبة" or "FOD" => "FodHeader",
                "System" or "النظام" or "Sistem" or "ระบบ" => "SystemHeader",
                _ => null
            };
            if (key is not null)
                col.Header = Loc.Get(key);
        }
    }

    private async void PlateTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await TrySavePlateAsync(sender);
            e.Handled = true;
        }
    }

    private async void PlateTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        await TrySavePlateAsync(sender);
    }

    private async System.Threading.Tasks.Task TrySavePlateAsync(object sender)
    {
        if (sender is not TextBox tb || tb.DataContext is not InspectionRecord record)
            return;

        if (DataContext is not ReportViewModel vm)
            return;

        var oldPlate = record.LicensePlate;
        var newPlate = tb.Text.Trim();

        if (string.IsNullOrWhiteSpace(newPlate))
            return;

        if (string.Equals(oldPlate, newPlate, StringComparison.OrdinalIgnoreCase))
            return;

        await vm.UpdateLicensePlateAsync(record, oldPlate, newPlate);
    }

    private void NotesTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = SaveNotesAsync(sender);
            e.Handled = true;
        }
    }

    private void NotesTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _ = SaveNotesAsync(sender);
    }

    private async System.Threading.Tasks.Task SaveNotesAsync(object sender)
    {
        if (sender is not TextBox tb || tb.DataContext is not InspectionRecord record)
            return;
        if (DataContext is not ReportViewModel vm)
            return;

        var newNotes = tb.Text;
        if (newNotes == record.Notes)
            return;

        await vm.UpdateNotesAsync(record, newNotes);
    }

    private async void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.DataContext is not InspectionRecord record)
            return;

        if (DataContext is not ReportViewModel vm)
            return;

        if (cb.Tag is not InspectionStatus original)
        {
            // First time — store the original and don't save
            cb.Tag = record.Status;
            return;
        }

        if (cb.SelectedItem is not InspectionStatus newStatus)
            return;

        if (original == newStatus)
            return;

        cb.Tag = newStatus;
        await vm.UpdateInspectionStatusAsync(record, original, newStatus);
    }

}
