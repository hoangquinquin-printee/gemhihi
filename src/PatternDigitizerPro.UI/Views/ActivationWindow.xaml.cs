using System.Windows;
using PatternDigitizerPro.Core.Licensing;

namespace PatternDigitizerPro.UI.Views;

public partial class ActivationWindow : Window
{
    public ActivationWindow()
    {
        InitializeComponent();
        MachineIdBox.Text = LicenseService.MachineId;
    }
    private void CopyMachineId_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(MachineIdBox.Text);
    private void Activate_Click(object sender, RoutedEventArgs e)
    {
        LicenseStatus status = LicenseService.Activate(LicenseBox.Text);
        if (!status.IsValid) { MessageBox.Show(this, status.Message, "gemhihi Activation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        DialogResult = true;
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
