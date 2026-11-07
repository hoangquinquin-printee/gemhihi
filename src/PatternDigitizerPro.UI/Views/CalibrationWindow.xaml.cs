using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.UI.ViewModels;

namespace PatternDigitizerPro.UI.Views;

public partial class CalibrationWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;
    private readonly ImageEngine _imageEngine = new();
    private readonly CalibrationEngine _calibrationEngine = new();
    private RawImageBuffer? _loadedRawImage;
    private double _zoom = 1.0;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartH;
    private double _panStartV;

    // Interactive Crop Box drag / resize state on CalibrationOverlayCanvas
    private bool _isDraggingCropBox;
    private bool _isResizingCropCorner;
    private int _cropCornerIndex = -1; // 0=TL, 1=TR, 2=BR, 3=BL
    private CropBoxPixels _cropDragInitialBox;
    private Point _cropDragStartPx;
    private bool _suppressCropTextEvents;

    public CalibrationWindow(bool loadSampleStateB = false, MainWindowViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        InitializeComponent();
        StartXBox.TextChanged += CropTextBox_TextChanged;
        StartYBox.TextChanged += CropTextBox_TextChanged;
        StopXBox.TextChanged += CropTextBox_TextChanged;
        StopYBox.TextChanged += CropTextBox_TextChanged;

        if (loadSampleStateB)
        {
            Loaded += (_, _) => LoadDefaultSampleGridStateB();
        }
        else
        {
            Loaded += (_, _) => SetUnloadedStateA();
        }
    }

    public CalibrationProfile? LastComputedProfile { get; private set; }
    public Canvas OverlayCanvasElement => CalibrationOverlayCanvas;

    private void CropTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressCropTextEvents && _loadedRawImage is not null)
        {
            RenderCalibrationOverlay();
        }
    }

    /// <summary>
    /// State A: Unloaded calibration image (matches Ảnh 5 1:1).
    /// </summary>
    public void SetUnloadedStateA()
    {
        _loadedRawImage = null;
        LastComputedProfile = null;
        CalibrationImageControl.Source = null;
        CalibrationImageControl.Visibility = Visibility.Collapsed;
        CalibrationOverlayCanvas.Children.Clear();
        CalibrationOverlayCanvas.Visibility = Visibility.Collapsed;
        _suppressCropTextEvents = true;
        SquareXBox.Text = "5";
        SquareYBox.Text = "5";
        StartXBox.Text = "0";
        StartYBox.Text = "0";
        StopXBox.Text = "0";
        StopYBox.Text = "0";
        _suppressCropTextEvents = false;
        ActiveWidthBox.Text = string.Empty;
        ActiveHeightBox.Text = string.Empty;
    }

    /// <summary>
    /// State B: Loaded calibration grid image with sample demo parameters (matches gemini_calibration_screen.png).
    /// </summary>
    public void LoadDefaultSampleGridStateB()
    {
        string samplePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "assets", "images", "reference", "calibration_grid_sample.png");
        samplePath = System.IO.Path.GetFullPath(samplePath);
        if (!File.Exists(samplePath))
        {
            samplePath = @"E:\claude\GEMHIHI\assets\images\reference\calibration_grid_sample.png";
        }

        if (File.Exists(samplePath))
        {
            LoadCalibrationBitmap(samplePath);
            _suppressCropTextEvents = true;
            SquareXBox.Text = "10";
            SquareYBox.Text = "10";
            StartXBox.Text = "91";
            StartYBox.Text = "118";
            StopXBox.Text = "2446";
            StopYBox.Text = "1924";
            _suppressCropTextEvents = false;
            ActiveWidthBox.Text = "120";
            ActiveHeightBox.Text = "90";
            RenderCalibrationOverlay();
        }
    }

    public void LoadCalibrationBitmap(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        _loadedRawImage = _imageEngine.LoadFromFile(path);
        LastComputedProfile = null;
        BitmapSource bmp = _loadedRawImage.ToBitmapSource();
        CalibrationImageControl.Source = bmp;
        CalibrationImageControl.Width = _loadedRawImage.Width;
        CalibrationImageControl.Height = _loadedRawImage.Height;
        CalibrationImageControl.Visibility = Visibility.Visible;

        CalibrationOverlayCanvas.Width = _loadedRawImage.Width;
        CalibrationOverlayCanvas.Height = _loadedRawImage.Height;
        CalibrationOverlayCanvas.Visibility = Visibility.Visible;

        _suppressCropTextEvents = true;
        StartXBox.Text = "0";
        StartYBox.Text = "0";
        // Gemini represents the default full image extent as all zero crop inputs.
        // GetCurrentCropBoxPixels expands those zeros to the loaded image bounds for
        // the engine, without changing what the operator sees in the dialog.
        StopXBox.Text = "0";
        StopYBox.Text = "0";
        _suppressCropTextEvents = false;

        RenderCalibrationOverlay();
    }

    private void LoadPictureBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load calibration grid picture",
            Filter = "Image files (*.jpg;*.jpeg;*.bmp;*.png)|*.jpg;*.jpeg;*.bmp;*.png|All files (*.*)|*.*"
        };

        if (Directory.Exists(@"D:\pictures"))
        {
            dlg.InitialDirectory = @"D:\pictures";
        }

        if (dlg.ShowDialog(this) == true)
        {
            LoadCalibrationBitmap(dlg.FileName);
            ZoomFitBtn_Click(this, new RoutedEventArgs());
        }
    }

    private void LoadDemoGridMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string realGridPath = @"E:\claude\GEMHIHI\assets\images\reference\cal_grid_117cm_20o.jpg";
        if (!File.Exists(realGridPath) && File.Exists(@"D:\pictures\117cm--20o.jpg"))
        {
            realGridPath = @"D:\pictures\117cm--20o.jpg";
        }

        if (File.Exists(realGridPath))
        {
            LoadCalibrationBitmap(realGridPath);
            SquareXBox.Text = "5";
            SquareYBox.Text = "5";
            ZoomFitBtn_Click(this, new RoutedEventArgs());
        }
        else
        {
            LoadDefaultSampleGridStateB();
        }
    }

    private void ClearGridMenuItem_Click(object sender, RoutedEventArgs e) => SetUnloadedStateA();

    private void LoadPictureDropBtn_Click(object sender, RoutedEventArgs e)
    {
        if (LoadPictureDropBtn.ContextMenu is not null)
        {
            LoadPictureDropBtn.ContextMenu.PlacementTarget = LoadPictureBtn;
            LoadPictureDropBtn.ContextMenu.IsOpen = true;
        }
    }

    private void UnimplementedAcquire_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "No physical DirectShow Camera or TWAIN Scanner hardware detected.\nUse 'Load picture -> From file' to load a calibration grid photo.",
            "Pattern Digitizer Pro — Calibration",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void StepNumericBox(TextBox box, int delta)
    {
        if (!int.TryParse(box.Text, out int val))
        {
            val = 0;
        }

        val = Math.Max(0, val + delta);
        box.Text = val.ToString(CultureInfo.InvariantCulture);
    }

    private void StartXUp_Click(object sender, RoutedEventArgs e) => StepNumericBox(StartXBox, 1);
    private void StartXDown_Click(object sender, RoutedEventArgs e) => StepNumericBox(StartXBox, -1);
    private void StartYUp_Click(object sender, RoutedEventArgs e) => StepNumericBox(StartYBox, 1);
    private void StartYDown_Click(object sender, RoutedEventArgs e) => StepNumericBox(StartYBox, -1);
    private void StopXUp_Click(object sender, RoutedEventArgs e) => StepNumericBox(StopXBox, 1);
    private void StopXDown_Click(object sender, RoutedEventArgs e) => StepNumericBox(StopXBox, -1);
    private void StopYUp_Click(object sender, RoutedEventArgs e) => StepNumericBox(StopYBox, 1);
    private void StopYDown_Click(object sender, RoutedEventArgs e) => StepNumericBox(StopYBox, -1);

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);

    private void ZoomFitBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CalibrationImageControl.Source is BitmapSource bmp && GridScrollViewer.ViewportWidth > 10)
        {
            double sx = (GridScrollViewer.ViewportWidth - 8) / bmp.PixelWidth;
            double sy = (GridScrollViewer.ViewportHeight - 8) / bmp.PixelHeight;
            SetZoom(Math.Min(sx, sy));
        }
        else
        {
            SetZoom(1.0);
        }
    }

    private void SetZoom(double newZoom)
    {
        _zoom = Math.Clamp(newZoom, 0.15, 6.0);
        GridScaleTransform.ScaleX = _zoom;
        GridScaleTransform.ScaleY = _zoom;
    }

    private void GridScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetZoom(e.Delta > 0 ? _zoom * 1.15 : _zoom / 1.15);
        e.Handled = true;
    }

    private void GridScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && !_isDraggingCropBox && !_isResizingCropCorner))
        {
            _isPanning = true;
            _panStart = e.GetPosition(GridScrollViewer);
            _panStartH = GridScrollViewer.HorizontalOffset;
            _panStartV = GridScrollViewer.VerticalOffset;
            GridScrollViewer.CaptureMouse();
        }
    }

    private void GridScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if ((_isDraggingCropBox || _isResizingCropCorner) &&
            e.LeftButton == MouseButtonState.Pressed &&
            _loadedRawImage is not null)
        {
            Point curImg = e.GetPosition(CalibrationOverlayCanvas);
            int dx = (int)Math.Round(curImg.X - _cropDragStartPx.X);
            int dy = (int)Math.Round(curImg.Y - _cropDragStartPx.Y);
            CropBoxPixels b = _cropDragInitialBox;
            int imgW = _loadedRawImage.Width;
            int imgH = _loadedRawImage.Height;

            int x0 = b.StartX, y0 = b.StartY, x1 = b.StopX, y1 = b.StopY;
            if (_isDraggingCropBox)
            {
                int w = b.Width;
                int h = b.Height;
                x0 = Math.Clamp(b.StartX + dx, 0, Math.Max(0, imgW - w));
                y0 = Math.Clamp(b.StartY + dy, 0, Math.Max(0, imgH - h));
                x1 = x0 + w;
                y1 = y0 + h;
            }
            else if (_isResizingCropCorner)
            {
                switch (_cropCornerIndex)
                {
                    case 0: // TL
                        x0 = Math.Clamp(b.StartX + dx, 0, x1 - 40);
                        y0 = Math.Clamp(b.StartY + dy, 0, y1 - 40);
                        break;
                    case 1: // TR
                        x1 = Math.Clamp(b.StopX + dx, x0 + 40, imgW);
                        y0 = Math.Clamp(b.StartY + dy, 0, y1 - 40);
                        break;
                    case 2: // BR
                        x1 = Math.Clamp(b.StopX + dx, x0 + 40, imgW);
                        y1 = Math.Clamp(b.StopY + dy, y0 + 40, imgH);
                        break;
                    case 3: // BL
                        x0 = Math.Clamp(b.StartX + dx, 0, x1 - 40);
                        y1 = Math.Clamp(b.StopY + dy, y0 + 40, imgH);
                        break;
                }
            }

            SetCropBoxInputs(new CropBoxPixels(x0, y0, x1, y1));
            RenderCalibrationOverlay();
            return;
        }

        if (!_isPanning)
        {
            return;
        }

        Point current = e.GetPosition(GridScrollViewer);
        GridScrollViewer.ScrollToHorizontalOffset(_panStartH - (current.X - _panStart.X));
        GridScrollViewer.ScrollToVerticalOffset(_panStartV - (current.Y - _panStart.Y));
    }

    private void GridScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingCropBox || _isResizingCropCorner)
        {
            _isDraggingCropBox = false;
            _isResizingCropCorner = false;
            GridScrollViewer.ReleaseMouseCapture();
            RenderCalibrationOverlay();
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            GridScrollViewer.ReleaseMouseCapture();
        }
    }

    public CropBoxPixels GetCurrentCropBoxPixels()
    {
        int maxW = _loadedRawImage?.Width ?? 3456;
        int maxH = _loadedRawImage?.Height ?? 2592;
        int.TryParse(StartXBox.Text, out int sx);
        int.TryParse(StartYBox.Text, out int sy);
        int.TryParse(StopXBox.Text, out int ex);
        int.TryParse(StopYBox.Text, out int ey);
        sx = Math.Clamp(sx, 0, Math.Max(0, maxW - 10));
        sy = Math.Clamp(sy, 0, Math.Max(0, maxH - 10));
        if (ex <= sx) ex = maxW;
        if (ey <= sy) ey = maxH;
        ex = Math.Clamp(ex, sx + 10, maxW);
        ey = Math.Clamp(ey, sy + 10, maxH);
        return new CropBoxPixels(sx, sy, ex, ey);
    }

    public void SetCropBoxInputs(CropBoxPixels box)
    {
        _suppressCropTextEvents = true;
        StartXBox.Text = box.StartX.ToString(CultureInfo.InvariantCulture);
        StartYBox.Text = box.StartY.ToString(CultureInfo.InvariantCulture);
        StopXBox.Text = box.StopX.ToString(CultureInfo.InvariantCulture);
        StopYBox.Text = box.StopY.ToString(CultureInfo.InvariantCulture);
        _suppressCropTextEvents = false;
    }

    public void RenderCalibrationOverlay()
    {
        if (CalibrationOverlayCanvas is null || _loadedRawImage is null)
        {
            return;
        }

        CalibrationOverlayCanvas.Children.Clear();
        CropBoxPixels crop = GetCurrentCropBoxPixels();

        // The original calibration dialog leaves the photographed grid intact and
        // marks each detected intersection with a small red cross.  Do not paint a
        // second synthetic grid over the image: it hides whether the detector has
        // actually locked onto the photographed intersections.
        if (LastComputedProfile is not null)
        {
            var crossBrush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            crossBrush.Freeze();

            int nCols = LastComputedProfile.VerticalGridLinesPx.Count;
            int nRows = LastComputedProfile.HorizontalGridLinesPx.Count;

            if (LastComputedProfile.DetectedIntersections.Count == nCols * nRows)
            {
                for (int r = 0; r < nRows; r++)
                {
                    for (int c = 0; c < nCols; c++)
                    {
                        int k = (r * nCols) + c;
                        ImagePixelPoint pt = LastComputedProfile.DetectedIntersections[k];
                        AddIntersectionCross(pt.X, pt.Y, crossBrush);
                    }
                }
            }
            else
            {
                foreach (double vx in LastComputedProfile.VerticalGridLinesPx)
                {
                    foreach (double hy in LastComputedProfile.HorizontalGridLinesPx)
                    {
                        AddIntersectionCross(vx, hy, crossBrush);
                    }
                }
            }
        }

        // A zero crop means "use the entire image" in Gemini.  Its dialog does not
        // draw a crop frame in that state, so only expose the interactive frame once
        // the user has supplied an actual crop boundary.
        if (!HasExplicitCropBox())
        {
            return;
        }

        // 2. Render interactive Cropping Box (Red dashed border + 4 corner handles)
        var cropRect = new Rectangle
        {
            Width = crop.Width,
            Height = crop.Height,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 40, 40)),
            StrokeThickness = 2.4,
            StrokeDashArray = new DoubleCollection { 6, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(16, 255, 40, 40)),
            Cursor = Cursors.SizeAll,
            ToolTip = $"Cropping Box ({crop.StartX},{crop.StartY} -> {crop.StopX},{crop.StopY}) — Drag to move, drag corners to resize"
        };
        Canvas.SetLeft(cropRect, crop.StartX);
        Canvas.SetTop(cropRect, crop.StartY);
        cropRect.MouseLeftButtonDown += (s, ev) =>
        {
            _isDraggingCropBox = true;
            _cropDragInitialBox = crop;
            _cropDragStartPx = ev.GetPosition(CalibrationOverlayCanvas);
            GridScrollViewer.CaptureMouse();
            ev.Handled = true;
        };
        CalibrationOverlayCanvas.Children.Add(cropRect);

        Point[] corners =
        [
            new Point(crop.StartX, crop.StartY),
            new Point(crop.StopX, crop.StartY),
            new Point(crop.StopX, crop.StopY),
            new Point(crop.StartX, crop.StopY)
        ];
        Cursor[] cursors = [Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNWSE, Cursors.SizeNESW];

        for (int i = 0; i < 4; i++)
        {
            int capturedIdx = i;
            var handle = new Rectangle
            {
                Width = 14, Height = 14,
                Fill = new SolidColorBrush(Color.FromRgb(255, 60, 60)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Cursor = cursors[i]
            };
            Canvas.SetLeft(handle, corners[i].X - 7);
            Canvas.SetTop(handle, corners[i].Y - 7);
            handle.MouseLeftButtonDown += (s, ev) =>
            {
                _isResizingCropCorner = true;
                _cropCornerIndex = capturedIdx;
                _cropDragInitialBox = crop;
                _cropDragStartPx = ev.GetPosition(CalibrationOverlayCanvas);
                GridScrollViewer.CaptureMouse();
                ev.Handled = true;
            };
            CalibrationOverlayCanvas.Children.Add(handle);
        }
    }

    private void AddIntersectionCross(double x, double y, Brush brush)
    {
        const double arm = 4;
        CalibrationOverlayCanvas.Children.Add(new Line
        {
            X1 = x - arm, Y1 = y, X2 = x + arm, Y2 = y,
            Stroke = brush, StrokeThickness = 1.0, IsHitTestVisible = false
        });
        CalibrationOverlayCanvas.Children.Add(new Line
        {
            X1 = x, Y1 = y - arm, X2 = x, Y2 = y + arm,
            Stroke = brush, StrokeThickness = 1.0, IsHitTestVisible = false
        });
    }

    private bool HasExplicitCropBox()
    {
        return int.TryParse(StartXBox.Text, out int startX) && startX != 0
            || int.TryParse(StartYBox.Text, out int startY) && startY != 0
            || int.TryParse(StopXBox.Text, out int stopX) && stopX != 0
            || int.TryParse(StopYBox.Text, out int stopY) && stopY != 0;
    }

    public CalibrationProfile? ExecuteGridCalibration()
    {
        if (_loadedRawImage is null)
        {
            if (_viewModel is not null)
            {
                _viewModel.StatusLeftText = "Load a calibration grid image before running grid calibration.";
            }
            return null;
        }

        if (!double.TryParse(SquareXBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double cellW) || cellW <= 0)
        {
            cellW = 5.0;
        }

        if (!double.TryParse(SquareYBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double cellH) || cellH <= 0)
        {
            cellH = 5.0;
        }

        CropBoxPixels crop = GetCurrentCropBoxPixels();
        CalibrationProfile profile = _calibrationEngine.CalibrateModeAGrid(_loadedRawImage, cellW, cellH, crop);
        LastComputedProfile = profile;

        // Preserve three decimals for values such as 4.995 cm cells; rounding the
        // active area to two decimals loses the value visibly shown by Gemini.
        ActiveWidthBox.Text = profile.ActiveAreaWidthCm.ToString("0.###", CultureInfo.InvariantCulture);
        ActiveHeightBox.Text = profile.ActiveAreaHeightCm.ToString("0.###", CultureInfo.InvariantCulture);
        RenderCalibrationOverlay();

        if (_viewModel is not null)
        {
            _viewModel.ActiveCalibration = profile;
            string accuracy = profile.HoldoutMaxResidualErrorMm <= 1.0
                ? "high precision"
                : "limited accuracy — verify critical dimensions before production export";
            _viewModel.StatusLeftText =
                $"Grid Calibrated: {profile.GridColumns}x{profile.GridRows} cells ({profile.ActiveAreaWidthCm:0.##}cm x {profile.ActiveAreaHeightCm:0.##}cm), " +
                $"2D warp holdout mean/max: {profile.HoldoutMeanResidualErrorMm:F2}/{profile.HoldoutMaxResidualErrorMm:F2} mm ({accuracy})";
        }

        return profile;
    }

    private void CalibrateBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CalibrationProfile? profile = ExecuteGridCalibration();
            ExecuteGridCalibration();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Calibration error: {ex.Message}",
                "Pattern Digitizer Pro — Calibration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
