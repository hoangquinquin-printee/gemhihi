using System.Windows;

namespace PatternDigitizerPro.UI.Views;

public partial class ExportInvestronicaDbWindow : Window
{
    public ExportInvestronicaDbWindow()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Investronica SQL Database export is not implemented in Phase 1 (UI Replication only).",
            "Pattern Digitizer Pro — Phase 1 Status",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
