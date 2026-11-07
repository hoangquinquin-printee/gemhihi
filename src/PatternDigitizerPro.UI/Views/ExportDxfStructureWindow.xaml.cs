using System.Windows;

namespace PatternDigitizerPro.UI.Views;

public partial class ExportDxfStructureWindow : Window
{
    public ExportDxfStructureWindow()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
