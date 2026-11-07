using System.Globalization;
using System.Windows;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.UI.ViewModels;

namespace PatternDigitizerPro.UI.Views;

public partial class SettingsWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;

    public SettingsWindow(int initialTabIndex = 0, MainWindowViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        SettingsTabControl.SelectedIndex = Math.Clamp(initialTabIndex, 0, 4);

        if (_viewModel is not null)
        {
            CalibrationProfile cal = _viewModel.ActiveCalibration;
            RestrictRoiCheck.IsChecked = _viewModel.RestrictDetectionToManualBox;
            if (cal.Mode == CalibrationMode.ManualRectangleModeB)
            {
                UseGridCalibrationCheck.IsChecked = false;
                CalibByDimRadio.IsChecked = true;
                GridHorizSizeBox.Text = cal.ActiveAreaWidthCm.ToString("0.##", CultureInfo.InvariantCulture);
                GridVertSizeBox.Text = cal.ActiveAreaHeightCm.ToString("0.##", CultureInfo.InvariantCulture);
            }
            else if (cal.Mode == CalibrationMode.ResolutionDpi)
            {
                UseGridCalibrationCheck.IsChecked = false;
                CalibByResRadio.IsChecked = true;
                DpiHorizBox.Text = cal.HorizontalDpi.ToString("0.##", CultureInfo.InvariantCulture);
                DpiVertBox.Text = cal.VerticalDpi.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }
    }

    public void SelectTab(int tabIndex)
    {
        SettingsTabControl.SelectedIndex = Math.Clamp(tabIndex, 0, 4);
    }

    private void UseGridCalibrationCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (CalibByDimRadio is null || CalibByResRadio is null)
        {
            return;
        }

        bool manualEnabled = UseGridCalibrationCheck.IsChecked != true;
        CalibByDimRadio.IsEnabled = manualEnabled;
        CalibByResRadio.IsEnabled = manualEnabled;
        GridHorizSizeBox.IsEnabled = manualEnabled;
        GridVertSizeBox.IsEnabled = manualEnabled;
        DpiHorizBox.IsEnabled = manualEnabled;
        DpiVertBox.IsEnabled = manualEnabled;
        if (RestrictRoiCheck is not null)
        {
            RestrictRoiCheck.IsEnabled = manualEnabled;
        }
    }

    private void OpenCalibrationBtn_Click(object sender, RoutedEventArgs e)
    {
        var calWin = new CalibrationWindow(loadSampleStateB: false, viewModel: _viewModel) { Owner = this };
        calWin.ShowDialog();
    }

    private void CameraSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        // The original button opens the live camera acquisition surface.  The
        // previous clone had no Click handler, so the control looked enabled
        // but did nothing.  Keep the capture window owned by Settings and
        // route a captured still back through the existing image loader.
        if (_viewModel is null)
        {
            MessageBox.Show(this, "Camera settings require an active digitizer window.", "Camera", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var camera = new CameraCaptureWindow(
            path => _viewModel.LoadImageFromFile(path))
        {
            Owner = this
        };
        camera.ShowDialog();
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null && UseGridCalibrationCheck.IsChecked != true)
        {
            int imgW = _viewModel.CurrentRawImage?.Width ?? _viewModel.LoadedBitmap?.PixelWidth ?? 3456;
            int imgH = _viewModel.CurrentRawImage?.Height ?? _viewModel.LoadedBitmap?.PixelHeight ?? 2592;

            if (CalibByDimRadio.IsChecked == true)
            {
                // Mode B: Manual X/Y Rectangle Calibration by Dimensions (cm)
                if (double.TryParse(GridHorizSizeBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double wCm) &&
                    double.TryParse(GridVertSizeBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double hCm) &&
                    wCm > 0 && hCm > 0)
                {
                    _viewModel.ManualCalibrationWidthCm = wCm;
                    _viewModel.ManualCalibrationHeightCm = hCm;
                    _viewModel.RestrictDetectionToManualBox = RestrictRoiCheck.IsChecked == true;

                    CropBoxPixels roi = _viewModel.ManualCalibrationBoxPx.IsValid
                        ? _viewModel.ManualCalibrationBoxPx
                        : new CropBoxPixels(0, 0, imgW, imgH);

                    CalibrationProfile modeB = _viewModel.CalibrationEngine.CalibrateModeBManualRectangle(
                        roi,
                        wCm,
                        hCm,
                        imgW,
                        imgH,
                        restrictDetectionToRoi: _viewModel.RestrictDetectionToManualBox);

                    _viewModel.ActiveCalibration = modeB;
                    _viewModel.IsManualCalibrationBoxVisible = true;
                    _viewModel.StatusLeftText =
                        $"Mode B Manual X/Y Calibration applied: {wCm:0.##}cm x {hCm:0.##}cm -> Scale=({modeB.ScaleXMmPerPx:F4}, {modeB.ScaleYMmPerPx:F4}) mm/px";
                }
            }
            else if (CalibByResRadio.IsChecked == true)
            {
                if (double.TryParse(DpiHorizBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ppiX) &&
                    double.TryParse(DpiVertBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ppiY) &&
                    ppiX > 0 && ppiY > 0)
                {
                    CalibrationProfile dpiProf = _viewModel.CalibrationEngine.CalibrateByResolutionDpi(ppiX, ppiY, imgW, imgH);
                    _viewModel.ActiveCalibration = dpiProf;
                    _viewModel.IsManualCalibrationBoxVisible = false;
                    _viewModel.StatusLeftText =
                        $"Resolution Calibration applied: {ppiX:0.##} x {ppiY:0.##} PPI -> Scale=({dpiProf.ScaleXMmPerPx:F4}, {dpiProf.ScaleYMmPerPx:F4}) mm/px";
                }
            }
        }
        else if (_viewModel is not null && UseGridCalibrationCheck.IsChecked == true)
        {
            _viewModel.IsManualCalibrationBoxVisible = false;
        }

        DialogResult = true;
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
