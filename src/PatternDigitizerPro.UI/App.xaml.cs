using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.Core.Licensing;
using PatternDigitizerPro.UI.Controls;
using PatternDigitizerPro.UI.Views;

namespace PatternDigitizerPro.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            if (e.Args.Contains("--license-issuer", StringComparer.OrdinalIgnoreCase))
            {
                var issuer = new LicenseIssuerWindow();
                MainWindow = issuer;
                issuer.Show();
                return;
            }
            LicenseStatus license = LicenseService.GetStatus();
            if (!license.IsValid && new ActivationWindow().ShowDialog() != true)
            {
                Shutdown(0);
                return;
            }
            var mainWin = new MainWindow();
            MainWindow = mainWin;
            mainWin.Show();

            if (e.Args.Contains("--capture-ui-comparison", StringComparer.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    try
                    {
                        CaptureAllUiComparisonArtifacts(mainWin);
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(@"E:\claude\GEMHIHI\artifacts\ui-comparison\pass2\capture_error.txt", ex.ToString());
                    }
                    finally
                    {
                        Shutdown(0);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"E:\claude\GEMHIHI\artifacts\ui-comparison\pass2\startup_error.txt", ex.ToString());
            Shutdown(1);
        }
    }

    private static void CaptureAllUiComparisonArtifacts(MainWindow mainWin)
    {
        string outDir = @"E:\claude\GEMHIHI\artifacts\ui-comparison";
        string pass2Dir = Path.Combine(outDir, "pass2");
        string iconsOutDir = Path.Combine(pass2Dir, "pdp_icons");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(pass2Dir);
        Directory.CreateDirectory(iconsOutDir);

        // 1A. Capture MainWindow in Live v9.0 3-Piece State at 1920x1040 (Client area 1920x1008 matching Ảnh 1-2)
        mainWin.WindowState = WindowState.Normal;
        mainWin.Width = 1936;
        mainWin.Height = 1047;
        mainWin.ViewModel.ApplyLiveV9ReferenceState();
        mainWin.UpdateCanvasVisibility();
        PumpLayout(mainWin);
        SaveElementBitmap(mainWin, Path.Combine(pass2Dir, "pdp_pass2_main_window_1920x1008_v9.png"));

        // 1B. Capture MainWindow in Manual 4-Piece Points Dock State (1024x742)
        mainWin.Width = 1024;
        mainWin.Height = 742;
        mainWin.ViewModel.ApplyReferenceDemoState();
        mainWin.UpdateCanvasVisibility();
        PumpLayout(mainWin);
        SaveElementBitmap(mainWin, Path.Combine(outDir, "pdp_main_window_1024x742.png"));
        SaveElementBitmap(mainWin, Path.Combine(pass2Dir, "pdp_pass2_main_window_points_dock_1024x742.png"));

        // 2. Capture contextual dock modes (Notch, InternalPoint, InternalLine)
        mainWin.ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Notch;
        PumpLayout(mainWin);
        SaveElementBitmap(mainWin, Path.Combine(outDir, "pdp_main_window_notch_dock.png"));

        mainWin.ViewModel.ActivePropertiesDockMode = PropertiesDockMode.InternalPoint;
        PumpLayout(mainWin);
        SaveElementBitmap(mainWin, Path.Combine(outDir, "pdp_main_window_internal_point_dock.png"));

        mainWin.ViewModel.ActivePropertiesDockMode = PropertiesDockMode.InternalLine;
        PumpLayout(mainWin);
        SaveElementBitmap(mainWin, Path.Combine(outDir, "pdp_main_window_internal_line_dock.png"));

        // 3. Capture Clean Empty Startup state
        mainWin.ViewModel.IsDemoReferenceLayoutActive = false;
        mainWin.ViewModel.ApplyCleanStartupOrImageState();
        mainWin.UpdateCanvasVisibility();
        PumpLayout(mainWin);
        SaveElementBitmap(mainWin, Path.Combine(outDir, "pdp_main_window_empty_startup.png"));

        // 4. Restore Live v9.0 state
        mainWin.ViewModel.ApplyLiveV9ReferenceState();
        mainWin.UpdateCanvasVisibility();
        PumpLayout(mainWin);

        // 5. Capture CalibrationWindow in State A (Unloaded, matching Ảnh 5) & State B (Loaded, matching gemini_calibration_screen.png)
        var calWin = new CalibrationWindow();
        calWin.Show();
        calWin.SetUnloadedStateA();
        PumpLayout(calWin);
        SaveElementBitmap(calWin, Path.Combine(pass2Dir, "pdp_pass2_calibration_state_A_unloaded.png"));

        calWin.LoadDefaultSampleGridStateB();
        PumpLayout(calWin);
        SaveElementBitmap(calWin, Path.Combine(pass2Dir, "pdp_pass2_calibration_state_B_loaded.png"));
        SaveElementBitmap(calWin, Path.Combine(outDir, "pdp_calibration_window.png"));
        calWin.Close();

        // 6. Capture SettingsWindow (all 5 tabs, especially Tab 0 matching Ảnh 3 and Tab 1 matching Ảnh 4)
        string[] tabSlugs = ["0_general", "1_camera_calibration", "2_interpretation", "3_interface", "4_export"];
        var setWin = new SettingsWindow(initialTabIndex: 0);
        setWin.Show();
        for (int i = 0; i < tabSlugs.Length; i++)
        {
            setWin.SelectTab(i);
            PumpLayout(setWin);
            SaveElementBitmap(setWin, Path.Combine(outDir, $"pdp_settings_tab_{tabSlugs[i]}.png"));
            SaveElementBitmap(setWin, Path.Combine(pass2Dir, $"pdp_pass2_settings_tab_{tabSlugs[i]}.png"));
        }
        setWin.Close();

        // 7. Capture the remaining legacy-visualization window.
        var structWin = new ExportDxfStructureWindow();
        structWin.Show();
        PumpLayout(structWin);
        SaveElementBitmap(structWin, Path.Combine(outDir, "pdp_export_dxf_structure_window.png"));
        structWin.Close();

        // 8. Capture ExportInvestronicaDbWindow, AboutWindow, RegisterProductWindow
        var invWin = new ExportInvestronicaDbWindow();
        invWin.Show();
        PumpLayout(invWin);
        SaveElementBitmap(invWin, Path.Combine(outDir, "pdp_export_investronica_db_window.png"));
        invWin.Close();

        var aboutWin = new AboutWindow();
        aboutWin.Show();
        PumpLayout(aboutWin);
        SaveElementBitmap(aboutWin, Path.Combine(outDir, "pdp_about_window.png"));
        aboutWin.Close();

        var regWin = new RegisterProductWindow();
        regWin.Show();
        PumpLayout(regWin);
        SaveElementBitmap(regWin, Path.Combine(outDir, "pdp_register_window.png"));
        regWin.Close();

        // 9. Render all 25 v9.0 toolbar icons individually for Pass 2 Icon Audit
        GeminiIconKind[] v9ToolbarKinds =
        [
            GeminiIconKind.LoadPicture,
            GeminiIconKind.DetectPieces,
            GeminiIconKind.Save,
            GeminiIconKind.Undo,
            GeminiIconKind.Redo,
            GeminiIconKind.ZoomArea,
            GeminiIconKind.ZoomSelected,
            GeminiIconKind.ZoomAll,
            GeminiIconKind.LineToCurve,
            GeminiIconKind.CurveToLine,
            GeminiIconKind.AddNotch,
            GeminiIconKind.DeleteNotch,
            GeminiIconKind.SmartInternalPoint,
            GeminiIconKind.AddInternalPoint,
            GeminiIconKind.DeleteInternalPoint,
            GeminiIconKind.SmartInternalLine,
            GeminiIconKind.AddInternalLine,
            GeminiIconKind.DeleteInternalLine,
            GeminiIconKind.AddAuxPiece,
            GeminiIconKind.DeleteAuxPiece,
            GeminiIconKind.ShowGrid,
            GeminiIconKind.MeasurePerimeter,
            GeminiIconKind.MeasureDirect,
            GeminiIconKind.PlayContour,
            GeminiIconKind.ContextHelp
        ];

        for (int i = 0; i < v9ToolbarKinds.Length; i++)
        {
            bool isDisabledInV9Idle = v9ToolbarKinds[i] is
                GeminiIconKind.Undo or
                GeminiIconKind.Redo or
                GeminiIconKind.LineToCurve or
                GeminiIconKind.CurveToLine or
                GeminiIconKind.AddAuxPiece or
                GeminiIconKind.DeleteAuxPiece or
                GeminiIconKind.PlayContour;

            var icon = new GeminiToolbarIcon
            {
                Kind = v9ToolbarKinds[i],
                Width = 24,
                Height = 24,
                IsDisabledLook = isDisabledInV9Idle
            };
            var host = new System.Windows.Controls.Border
            {
                Width = 24,
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                Child = icon
            };
            host.Measure(new Size(24, 24));
            host.Arrange(new Rect(0, 0, 24, 24));
            host.UpdateLayout();
            SaveElementBitmap(host, Path.Combine(iconsOutDir, $"pdp_v9_{i + 1:D2}_{v9ToolbarKinds[i]}.png"));
        }
    }

    private static void PumpLayout(Window win)
    {
        win.UpdateLayout();
        win.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        win.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }

    private static void SaveElementBitmap(FrameworkElement element, string filePath)
    {
        if (element is Window { Content: FrameworkElement content } && content.ActualWidth > 10 && content.ActualHeight > 10)
        {
            element = content;
        }

        int w = Math.Max(1, (int)Math.Round(element.ActualWidth));
        int h = Math.Max(1, (int)Math.Round(element.ActualHeight));

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }
}
