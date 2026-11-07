using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using PatternDigitizerPro.Core.Licensing;

namespace PatternDigitizerPro.UI.Views;

public partial class LicenseIssuerWindow : Window
{
    public LicenseIssuerWindow()
    {
        InitializeComponent();
        ExpiryPicker.SelectedDate = DateTime.Today.AddDays(14);
        KeyPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "H-Photo License Authority", "hphoto-license-private.pem");
        OutputPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "H-PHOTO-trial.hphoto-license");
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PEM private key (*.pem)|*.pem|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) KeyPathBox.Text = dialog.FileName;
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "H-PHOTO license (*.hphoto-license)|*.hphoto-license", FileName = Path.GetFileName(OutputPathBox.Text), InitialDirectory = Path.GetDirectoryName(OutputPathBox.Text) };
        if (dialog.ShowDialog(this) == true) OutputPathBox.Text = dialog.FileName;
    }

    private void CreateLicense_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CustomerBox.Text) || !System.Text.RegularExpressions.Regex.IsMatch(MachineIdBox.Text.Trim(), "^[A-Fa-f0-9]{24}$") || ExpiryPicker.SelectedDate is null)
        {
            MessageBox.Show(this, "Enter customer name, a 24-character hexadecimal Machine ID, and expiry date.", "H-PHOTO Key Issuer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(KeyPathBox.Text));
            DateTimeOffset expires = new(ExpiryPicker.SelectedDate.Value.Year, ExpiryPicker.SelectedDate.Value.Month, ExpiryPicker.SelectedDate.Value.Day, 23, 59, 59, TimeSpan.Zero);
            var payload = new HPhotoLicensePayload(Guid.NewGuid().ToString(), CustomerBox.Text.Trim(), MachineIdBox.Text.Trim().ToUpperInvariant(), DateTimeOffset.UtcNow, expires);
            string signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(LicenseService.Canonical(payload)), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPathBox.Text)!);
            File.WriteAllText(OutputPathBox.Text, JsonSerializer.Serialize(new HPhotoLicenseEnvelope(payload, signature), new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            MessageBox.Show(this, $"License created:\n{OutputPathBox.Text}\n\nValid until {expires:yyyy-MM-dd} UTC.", "H-PHOTO Key Issuer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "H-PHOTO Key Issuer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
