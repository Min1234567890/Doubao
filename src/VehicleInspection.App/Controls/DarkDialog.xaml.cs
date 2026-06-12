using System.Windows;

namespace VehicleInspection.App.Controls;

public partial class DarkDialog : Window
{
    public bool Result { get; private set; }

    public DarkDialog(string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    /// <summary>
    /// Shows a dark-themed confirmation dialog. Returns true if confirmed (Yes), false if cancelled (No).
    /// </summary>
    public static bool Show(Window owner, string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
        var dialog = new DarkDialog(title, message, confirmText, cancelText)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
