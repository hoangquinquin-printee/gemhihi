using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.UI;
using PatternDigitizerPro.UI.Views;
using Xunit;

namespace PatternDigitizerPro.Tests;

public sealed class WpfWindowWorkflowTests
{
    private static readonly string ReferenceAssetsDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "images", "reference"));

    [Fact]
    public void MainWindow_RealWpf_LoadDetectRenderEdit_KeepsRasterAndVectorInOneCoordinateSurface()
    {
        Exception? failure = null;
        var finished = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                if (Application.Current is null)
                {
                    var app = new PatternDigitizerPro.UI.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.InitializeComponent();
                }

                window = new MainWindow();
                window.Show();
                window.UpdateLayout();

                Assert.False(window.ViewModel.HasImageLoaded);
                Assert.Empty(window.ViewModel.DomainPieces);
                Assert.Equal("gemhihi", window.Title);

                Button detectButton = FindVisualDescendants<Button>(window)
                    .Single(button => string.Equals(button.ToolTip?.ToString(), "Detect piece contur", StringComparison.Ordinal));
                detectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.ViewModel.HasImageLoaded);
                Assert.Empty(window.ViewModel.DomainPieces);
                Assert.Equal("No image loaded to detect patterns.", window.ViewModel.StatusLeftText);

                var calibrationWindow = new CalibrationWindow(viewModel: window.ViewModel);
                Assert.Null(calibrationWindow.ExecuteGridCalibration());
                Assert.Equal("Load a calibration grid image before running grid calibration.", window.ViewModel.StatusLeftText);
                string gridPath = Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg");
                calibrationWindow.LoadCalibrationBitmap(gridPath);
                TextBox startXBox = Assert.IsType<TextBox>(calibrationWindow.FindName("StartXBox"));
                TextBox startYBox = Assert.IsType<TextBox>(calibrationWindow.FindName("StartYBox"));
                TextBox stopXBox = Assert.IsType<TextBox>(calibrationWindow.FindName("StopXBox"));
                TextBox stopYBox = Assert.IsType<TextBox>(calibrationWindow.FindName("StopYBox"));
                TextBox squareXBox = Assert.IsType<TextBox>(calibrationWindow.FindName("SquareXBox"));
                TextBox squareYBox = Assert.IsType<TextBox>(calibrationWindow.FindName("SquareYBox"));
                TextBox activeWidthBox = Assert.IsType<TextBox>(calibrationWindow.FindName("ActiveWidthBox"));
                TextBox activeHeightBox = Assert.IsType<TextBox>(calibrationWindow.FindName("ActiveHeightBox"));
                Canvas calibrationOverlay = Assert.IsType<Canvas>(calibrationWindow.FindName("CalibrationOverlayCanvas"));
                Assert.Equal("0", startXBox.Text);
                Assert.Equal("0", startYBox.Text);
                Assert.Equal("0", stopXBox.Text);
                Assert.Equal("0", stopYBox.Text);
                squareXBox.Text = "4.995";
                squareYBox.Text = "4.995";
                CalibrationProfile calibration = Assert.IsType<CalibrationProfile>(calibrationWindow.ExecuteGridCalibration());
                Assert.Same(calibration, window.ViewModel.ActiveCalibration);
                Assert.Equal(26, calibration.PixelToPhysicalWarp.Count);
                Assert.InRange(calibration.HoldoutMaxResidualErrorMm, 0.5, 5.6);
                Assert.Equal("134.865", activeWidthBox.Text);
                Assert.Equal("99.9", activeHeightBox.Text);
                var calibrationMarks = calibrationOverlay.Children
                    .OfType<System.Windows.Shapes.Line>()
                    .ToArray();
                Assert.Equal(calibration.DetectedIntersections.Count * 2, calibrationMarks.Length);
                Assert.All(calibrationMarks, line => Assert.Equal(Colors.Red, ((SolidColorBrush)line.Stroke).Color));
                Assert.Contains("Grid Calibrated:", window.ViewModel.StatusLeftText, StringComparison.Ordinal);
                calibrationWindow.Close();

                string imagePath = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
                window.OpenImageFile(imagePath);
                detectButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateCanvasVisibility();
                window.RenderInteractiveVectorOverlay();
                window.UpdateLayout();

                Assert.True(window.ViewModel.HasImageLoaded);
                Assert.Equal(3, window.ViewModel.DomainPieces.Count);
                Assert.Equal(Visibility.Visible, window.RealImageCanvasElement.Visibility);
                BitmapSource bitmap = Assert.IsAssignableFrom<BitmapSource>(window.BackgroundImageElement.Source);
                Assert.Equal(bitmap.PixelWidth, (int)window.RealImageCanvasElement.Width);
                Assert.Equal(bitmap.PixelHeight, (int)window.RealImageCanvasElement.Height);
                Assert.Equal(window.RealImageCanvasElement.Width, window.VectorOverlayCanvasElement.Width);
                Assert.Equal(window.RealImageCanvasElement.Height, window.VectorOverlayCanvasElement.Height);
                Assert.NotEmpty(window.VectorOverlayCanvasElement.Children.Cast<UIElement>());

                PatternPiece piece = window.ViewModel.DomainPieces[0];
                MethodInfo clickContour = Assert.IsAssignableFrom<MethodInfo>(typeof(MainWindow).GetMethod(
                    "HandlePieceOrContourClick", BindingFlags.Instance | BindingFlags.NonPublic));
                var clickEvent = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left);
                PhysicalPointMm segmentMidpoint = piece.GetSegment(0).Evaluate(0.5);
                ImagePixelPoint segmentMidpointPx = window.ViewModel.ActiveCalibration.PhysicalMmToPixel(segmentMidpoint);
                window.ViewModel.ActiveTool = GeminiToolMode.LineToCurve;
                clickContour.Invoke(window, [piece, segmentMidpoint, new Point(segmentMidpointPx.X, segmentMidpointPx.Y), clickEvent]);
                Assert.True(piece.Vertices[0].OutgoingIsCurve);
                window.ViewModel.ActiveTool = GeminiToolMode.CurveToLine;
                PhysicalPointMm curveMidpoint = piece.GetSegment(0).Evaluate(0.5);
                ImagePixelPoint curveMidpointPx = window.ViewModel.ActiveCalibration.PhysicalMmToPixel(curveMidpoint);
                clickContour.Invoke(window, [piece, curveMidpoint, new Point(curveMidpointPx.X, curveMidpointPx.Y), clickEvent]);
                Assert.False(piece.Vertices[0].OutgoingIsCurve);

                window.ViewModel.SelectDomainPiece(piece, 0);
                window.ViewModel.ExecuteLineToCurveOnSegment(piece, 0);
                window.RenderInteractiveVectorOverlay();
                Assert.True(piece.Vertices[0].OutgoingIsCurve);

                // Selecting a piece alone must not show Bézier controls.  The
                // controls become visible only after the user selects/adds a
                // contour point, exactly as in the interactive workflow.
                Assert.DoesNotContain(window.VectorOverlayCanvasElement.Children.OfType<System.Windows.Shapes.Rectangle>(),
                    rectangle => rectangle.ToolTip?.ToString()?.Contains("Bézier Handle", StringComparison.Ordinal) == true);
                MethodInfo doubleClickContour = Assert.IsAssignableFrom<MethodInfo>(typeof(MainWindow).GetMethod(
                    "TryAddPointOnContourDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic));
                int vertexCountBeforeDoubleClick = piece.Vertices.Count;
                PhysicalPointMm editableMidpoint = piece.GetSegment(0).Evaluate(0.5);
                ImagePixelPoint editableMidpointPx = window.ViewModel.ActiveCalibration.PhysicalMmToPixel(editableMidpoint);
                window.ViewModel.ActiveTool = GeminiToolMode.SelectDefault;
                Assert.True((bool)doubleClickContour.Invoke(window, [piece, editableMidpoint, new Point(editableMidpointPx.X, editableMidpointPx.Y)])!);
                Assert.Equal(vertexCountBeforeDoubleClick + 1, piece.Vertices.Count);
                window.RenderInteractiveVectorOverlay();
                Assert.Contains(window.VectorOverlayCanvasElement.Children.OfType<System.Windows.Shapes.Rectangle>(),
                    rectangle => rectangle.ToolTip?.ToString()?.Contains("Bézier Handle", StringComparison.Ordinal) == true);

                // A double click in the working area (not necessarily on the
                // one-pixel contour stroke) still inserts on the selected
                // detail's nearest segment, matching Gemini's forgiving flow.
                MethodInfo canvasDoubleClick = Assert.IsAssignableFrom<MethodInfo>(typeof(MainWindow).GetMethod(
                    "TryAddPointAtCanvasDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic));
                int vertexCountBeforeCanvasDoubleClick = piece.Vertices.Count;
                var interiorClick = new PhysicalPointMm(piece.BoundingBoxMm.X + (piece.BoundingBoxMm.Width / 2.0),
                    piece.BoundingBoxMm.Y + (piece.BoundingBoxMm.Height / 2.0));
                ImagePixelPoint interiorClickPx = window.ViewModel.GetCalibrationForPiece(piece).PhysicalMmToPixel(interiorClick);
                Assert.True((bool)canvasDoubleClick.Invoke(window, [new Point(interiorClickPx.X, interiorClickPx.Y)])!);
                Assert.Equal(vertexCountBeforeCanvasDoubleClick + 1, piece.Vertices.Count);

                // Delete-internal-line must not remove a line merely because the user
                // clicked elsewhere inside a selected piece.
                var internalLine = window.ViewModel.EditingEngine.AddInternalLine(
                    piece,
                    new PhysicalPointMm(piece.BoundingBoxMm.X + 15, piece.BoundingBoxMm.Y + 15),
                    new PhysicalPointMm(piece.BoundingBoxMm.X + 45, piece.BoundingBoxMm.Y + 15),
                    InternalLineType.SimpleLine);
                window.ViewModel.ActiveTool = GeminiToolMode.DeleteInternalLine;
                clickContour.Invoke(window, [piece, new PhysicalPointMm(piece.BoundingBoxMm.Right - 5, piece.BoundingBoxMm.Bottom - 5), new Point(0, 0), clickEvent]);
                Assert.Single(piece.InternalLines);
                clickContour.Invoke(window, [piece, new PhysicalPointMm(internalLine.StartMm.XMm + 15, internalLine.StartMm.YMm), new Point(0, 0), clickEvent]);
                Assert.Empty(piece.InternalLines);

                PhysicalPointMm[] beforePan = piece.Vertices.Select(v => v.PositionMm).ToArray();
                window.ViewModel.ViewportEngine.PanBy(180, -95);
                window.ViewModel.ViewportEngine.ZoomAtCursor(1.25, new ViewportPoint(300, 220));
                Assert.Equal(beforePan, piece.Vertices.Select(v => v.PositionMm).ToArray());

                // Selection wiring must not manufacture an Undo entry. The overlay element
                // is a real WPF vertex control generated from the detected piece.
                window.ViewModel.EditingEngine.ClearHistory();
                window.ViewModel.ActiveTool = GeminiToolMode.SelectDefault;
                System.Windows.Shapes.Ellipse vertex = FindVisualDescendants<System.Windows.Shapes.Ellipse>(window)
                    .First(ellipse => ellipse.ToolTip?.ToString()?.Contains("Point #", StringComparison.Ordinal) == true);
                vertex.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = vertex
                });
                Assert.False(window.ViewModel.EditingEngine.CanUndo);

                string projectPath = Path.Combine(Path.GetTempPath(), $"pdp-wpf-workflow-{Guid.NewGuid():N}.pdp");
                string missingSourceProjectPath = Path.Combine(Path.GetTempPath(), $"pdp-wpf-missing-source-{Guid.NewGuid():N}.pdp");
                string dxfPath = Path.Combine(Path.GetTempPath(), $"pdp-wpf-workflow-{Guid.NewGuid():N}.dxf");
                try
                {
                    window.ViewModel.SaveProjectToFile(projectPath);
                    Assert.True(File.Exists(projectPath));
                    Assert.True(window.ViewModel.LoadProjectFromFile(projectPath));
                    window.UpdateCanvasVisibility();
                    window.RenderInteractiveVectorOverlay();
                    window.UpdateLayout();
                    Assert.True(window.ViewModel.HasImageLoaded);
                    Assert.Equal(3, window.ViewModel.DomainPieces.Count);
                    Assert.True(window.ViewModel.DomainPieces[0].Vertices[0].OutgoingIsCurve);
                    Assert.NotEmpty(window.VectorOverlayCanvasElement.Children.Cast<UIElement>());

                    var dxfReport = window.ViewModel.ExportDxfToFile(dxfPath);
                    Assert.True(dxfReport.IsValid, string.Join("; ", dxfReport.Errors));
                    Assert.Equal(3, dxfReport.ParsedPieces.Count);

                    var missingSourceDocument = window.ViewModel.FileEngine.LoadProject(projectPath);
                    missingSourceDocument.ImageFilePath = Path.Combine(Path.GetTempPath(), $"missing-source-{Guid.NewGuid():N}.jpg");
                    window.ViewModel.FileEngine.SaveProject(missingSourceDocument, missingSourceProjectPath);
                    Assert.True(window.ViewModel.LoadProjectFromFile(missingSourceProjectPath));
                    window.UpdateCanvasVisibility();
                    window.UpdateLayout();
                    Assert.False(window.ViewModel.HasImageLoaded);
                    Assert.Null(window.BackgroundImageElement.Source);
                    Assert.Equal(window.ViewModel.ActiveCalibration.ImageWidthPx, window.RealImageCanvasElement.Width);
                    Assert.Equal(window.ViewModel.ActiveCalibration.ImageHeightPx, window.RealImageCanvasElement.Height);
                    Assert.NotEmpty(window.VectorOverlayCanvasElement.Children.Cast<UIElement>());
                }
                finally
                {
                    if (File.Exists(projectPath)) File.Delete(projectPath);
                    if (File.Exists(missingSourceProjectPath)) File.Delete(missingSourceProjectPath);
                    if (File.Exists(dxfPath)) File.Delete(dxfPath);
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                finished.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "WPF workflow test timed out.");
        thread.Join(TimeSpan.FromSeconds(5));
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
