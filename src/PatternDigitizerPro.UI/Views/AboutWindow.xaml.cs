using System.Windows;

namespace PatternDigitizerPro.UI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
