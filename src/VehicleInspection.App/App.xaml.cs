using System.Windows;

namespace VehicleInspection.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        base.OnStartup(e);
    }
}
