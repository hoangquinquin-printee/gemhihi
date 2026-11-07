using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using OpenCvSharp;

namespace PatternDigitizerPro.UI.Views;

/// <summary>Windows webcam preview and still capture.  A captured frame becomes the normal digitizer input.</summary>
public sealed class CameraCaptureWindow : System.Windows.Window
{
    private readonly Action<string> _openImage;
    private readonly ComboBox _devices = new() { Width = 270, Margin = new Thickness(8) };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _status = new() { Margin = new Thickness(8) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private VideoCapture? _capture;

    public CameraCaptureWindow(Action<string> openImage)
    {
        _openImage = openImage;
        Title = "gemhihi — Capture picture";
        Width = 860; Height = 650; MinWidth = 640; MinHeight = 480;
        var root = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        var refresh = new Button { Content = "Detect cameras", Margin = new Thickness(2), Padding = new Thickness(10, 3, 10, 3) };
        var start = new Button { Content = "Start preview", Margin = new Thickness(2), Padding = new Thickness(10, 3, 10, 3) };
        var shot = new Button { Content = "Capture picture", Margin = new Thickness(2), IsEnabled = false };
        var wia = new Button { Content = "Acquire still camera", Margin = new Thickness(2), Padding = new Thickness(10, 3, 10, 3) };
        var gemini = new Button { Content = "Gemini Capture", Margin = new Thickness(2), Padding = new Thickness(10, 3, 10, 3) };
        refresh.Click += (_, _) => Detect();
        start.Click += (_, _) => Start();
        shot.Click += (_, _) => CaptureStill();
        wia.Click += (_, _) => AcquireStillWithWindows();
        gemini.Click += (_, _) => LaunchGeminiCapture();
        bar.Children.Add(refresh); bar.Children.Add(_devices); bar.Children.Add(start); bar.Children.Add(shot); bar.Children.Add(wia); bar.Children.Add(gemini);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status); root.Children.Add(_preview);
        Content = root;
        _timer.Tick += (_, _) => RenderFrame();
        Closed += (_, _) => Stop();
        Detect();
    }

    private void Detect()
    {
        Stop(); _devices.Items.Clear();
        // Different Windows camera drivers expose different OpenCV backends.
        // Gemini accepts both ordinary webcams and capture cards, so probe
        // DirectShow first and Media Foundation as a fallback instead of
        // assuming every device is DSHOW-only.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (VideoCaptureAPIs api in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
        {
            for (int index = 0; index < 10; index++)
            {
                try
                {
                    using var probe = new VideoCapture(index, api);
                    if (!probe.IsOpened()) continue;
                    // A device that opens but cannot deliver a frame is not usable
                    // for Gemini-style acquisition. Give the driver a brief warm-up.
                    using var frame = new Mat();
                    probe.Read(frame);
                    if (frame.Empty()) continue;
                    string key = $"{index}:{api}";
                    if (seen.Add(key)) _devices.Items.Add(new CameraDevice(index, api, $"Camera {index} ({api})"));
                }
                catch (OpenCVException)
                {
                    // Some Windows drivers reject a backend/index combination.
                    // Continue probing the remaining backends instead of aborting detection.
                }
            }
        }
        _devices.SelectedIndex = _devices.Items.Count > 0 ? 0 : -1;
        _status.Text = _devices.Items.Count == 0 ? "No Windows camera detected. Connect the camera, unlock it, then Detect cameras." : $"{_devices.Items.Count} camera(s) detected.";
    }

    private void Start()
    {
        Stop();
        if (_devices.SelectedItem is not CameraDevice device) return;
        try
        {
            _capture = new VideoCapture(device.Index, device.Api);
            _capture.Set(VideoCaptureProperties.FrameWidth, 1920);
            _capture.Set(VideoCaptureProperties.FrameHeight, 1080);
        }
        catch (OpenCVException)
        {
            Stop();
            _status.Text = "The selected camera driver could not be opened. Close other camera applications and retry.";
            return;
        }
        if (!_capture.IsOpened()) { Stop(); _status.Text = "Camera found but Windows could not open it. Close other camera applications and retry."; return; }
        _timer.Start(); _status.Text = $"Previewing {device.Name}. Capture picture sends a full frame to digitize.";
        ((Button)((StackPanel)((DockPanel)Content).Children[0]).Children[3]).IsEnabled = true;
    }

    private void RenderFrame()
    {
        if (_capture is null) return;
        using var frame = new Mat(); if (!_capture.Read(frame) || frame.Empty()) return;
        Cv2.ImEncode(".bmp", frame, out byte[] encoded);
        using var stream = new MemoryStream(encoded);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        _preview.Source = image;
    }

    private void CaptureStill()
    {
        if (_capture is null) return;
        using var frame = new Mat(); if (!_capture.Read(frame) || frame.Empty()) { _status.Text = "No frame received; retry preview."; return; }
        string path = Path.Combine(Path.GetTempPath(), $"gemhihi-capture-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
        Cv2.ImWrite(path, frame); Stop(); _openImage(path); DialogResult = true;
    }

    // PTP still cameras (for example Canon PowerShot models) are not exposed
    // as DirectShow webcams. Gemini's original Capture helper talks to the
    // camera SDK; on Windows the clean-room fallback is WIA, which can acquire
    // a full-resolution still without shipping or modifying Gemini DLLs.
    private void AcquireStillWithWindows()
    {
        try
        {
            Type? dialogType = Type.GetTypeFromProgID("WIA.CommonDialog");
            if (dialogType is null)
            {
                _status.Text = "Windows Image Acquisition is unavailable on this PC.";
                return;
            }

            dynamic dialog = Activator.CreateInstance(dialogType)!;
            dynamic image = dialog.ShowAcquireImage(1, 0, 0, "", false, true, false);
            string path = Path.Combine(Path.GetTempPath(), $"gemhihi-camera-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
            image.SaveFile(path);
            _openImage(path);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _status.Text = $"Camera acquisition was cancelled or unavailable: {ex.Message}";
        }
    }

    private async void LaunchGeminiCapture()
    {
        string capture = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Gemini", "Gemini Photo Digitizer", "capture", "Capture.exe");
        if (!File.Exists(capture))
        {
            _status.Text = "Gemini Capture.exe was not found on this computer.";
            return;
        }

        DateTime started = DateTime.UtcNow;
        string[] roots = [Path.GetTempPath(), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)];
        try
        {
            using var process = Process.Start(new ProcessStartInfo(capture)
            {
                WorkingDirectory = Path.GetDirectoryName(capture)!,
                UseShellExecute = true
            });
            if (process is null) { _status.Text = "Gemini Capture.exe could not be started."; return; }
            _status.Text = "Gemini Capture is open. Save the captured image, then close its window.";
            await process.WaitForExitAsync();

            FileInfo? newest = roots.SelectMany(EnumerateRecentImages)
                .Where(f => f.LastWriteTimeUtc >= started.AddSeconds(-2))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest is null)
            {
                _status.Text = "Gemini Capture closed without a newly saved image.";
                return;
            }
            _openImage(newest.FullName);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _status.Text = $"Gemini Capture failed: {ex.Message}";
        }
    }

    private static IEnumerable<FileInfo> EnumerateRecentImages(string root)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly); }
        catch { yield break; }
        foreach (string path in files)
        {
            string ext = Path.GetExtension(path);
            if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)) continue;
            FileInfo info;
            try { info = new FileInfo(path); if (info.Length == 0) continue; }
            catch { continue; }
            yield return info;
        }
    }

    private void Stop() { _timer.Stop(); _capture?.Release(); _capture?.Dispose(); _capture = null; }
    private sealed record CameraDevice(int Index, VideoCaptureAPIs Api, string Name) { public override string ToString() => Name; }
}
