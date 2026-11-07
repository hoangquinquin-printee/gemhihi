using System.Windows;

namespace PatternDigitizerPro.UI.Views;

public partial class RegisterProductWindow : Window
{
    public RegisterProductWindow()
    {
        InitializeComponent();
    }

    private void RegisterBtn_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Registration dialog replicated for 1:1 UI parity (Phase 1).",
            "Pattern Digitizer Pro — Phase 1",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
