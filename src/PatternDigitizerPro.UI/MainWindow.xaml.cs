using System.ComponentModel;
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
using PatternDigitizerPro.UI.Views;

namespace PatternDigitizerPro.UI;

public partial class MainWindow : Window
{
    private enum VertexDragAxisConstraint
    {
        None,
        Horizontal,
        Vertical
    }

    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartH;
    private double _panStartV;

    // Interactive vertex / handle / internal object / piece dragging state on RealImageCanvas
    private bool _isDraggingVertex;
    private bool _isDraggingBezierHandle;
    private bool _isDraggingStartHandleC1;
    private bool _isDraggingWholePiece;
    private bool _isDraggingNotch;
    private bool _isDraggingInternalPoint;
    private bool _isDraggingInternalLine;
    private bool _isDraggingInternalLineEndpoint;
    private bool _isDraggingInternalLineStartEndpoint;
    private PatternPiece? _draggedPiece;
    private int _draggedVertexOrSegmentIndex = -1;
    private Guid _draggedInternalObjectId;
    private Point _dragLastImagePixelPos;
    // Established on the first Alt-drag motion and kept for the whole gesture,
    // so a hand tremor cannot switch an intended horizontal drag to vertical.
    private VertexDragAxisConstraint _vertexDragAxisConstraint;
    // A simple click selects an object; record an Undo state only when a drag moves it.
    private string? _pendingDragUndoAction;

    // Interactive two-click Internal Line creation state
    private PatternPiece? _pendingInternalLinePiece;
    private PhysicalPointMm? _pendingInternalLineStartMm;
    private PhysicalPointMm? _pendingInternalCurveControlMm;
    private Point? _pendingInternalLineCurrentMousePx;

    // Interactive Zoom Area rubber-band state
    private bool _isDraggingZoomAreaRect;
    private Point _zoomAreaStartPx;
    private Point _zoomAreaCurrentPx;

    // Per-pattern digitize frame.  It is stored in unscaled image pixels, so
    // scroll/pan/zoom only change the viewport and never the chosen geometry.
    private bool _isDraggingDigitizeRoi;
    private bool _isMovingDigitizeRoi;
    private bool _isResizingDigitizeRoi;
    private bool _isRotatingDigitizeRoi;
    private int _digitizeRoiResizeEdgeIndex = -1; // 0=left, 1=top, 2=right, 3=bottom
    private CropBoxPixels _digitizeRoiDragInitialBox = new(0, 0, 1, 1);
    private Point _digitizeRoiDragStartPx;
    private Point _digitizeRoiStartPx;
    private Point _digitizeRoiCurrentPx;


    // Interactive Multi-Point rubber-band selection state
    private bool _isDraggingSelectionRect;
    private Point _selectionRectStartPx;
    private Point _selectionRectCurrentPx;

    // Interactive Mode B ROI rectangle drag / resize state
    private bool _isMovingModeBRoi;
    private bool _isResizingModeBRoi;
    private int _modeBRoiResizeCornerIndex = -1; // 0=TL, 1=TR, 2=BR, 3=BL
    private CropBoxPixels _modeBRoiDragInitialBox;
    private Point _modeBRoiDragStartPx;

    // Interactive measurement state + rendered visual measurement overlay
    private PhysicalPointMm? _measureStartPointMm;
    private PhysicalPointMm? _measureEndPointMm;
    private Point? _measureCurrentHoverPx;
    private int _measureStartVertexIndex = -1;
    private int _measureEndVertexIndex = -1;
    private PatternPiece? _measurePerimeterPiece;
    private string _activeMeasurementOverlayText = string.Empty;
    private bool _isActiveMeasurementPerimeter;
    // Bézier controls stay hidden until an operator explicitly picks a point.
    private bool _showBezierHandlesForSelectedVertex;

    public MainWindow()
    {
        InitializeComponent();
        // Capture the second press before an overlay child can consume it. This
        // makes the Gemini-style double-click add-point gesture work whether
        // the pointer lands on the contour stroke, its filled interior, or a
        // clear part of the active working canvas.
        InteractiveVectorOverlayCanvas.PreviewMouseLeftButtonDown += InteractiveOverlay_PreviewMouseLeftButtonDown;
        Loaded += MainWindow_Loaded;
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
            vm.GeometryChanged += RenderInteractiveVectorOverlay;
        }
    }

    public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;
    public Image BackgroundImageElement => WorkspaceImageControl;
    public Canvas VectorOverlayCanvasElement => InteractiveVectorOverlayCanvas;
    public Canvas RealImageCanvasElement => RealImageCanvas;
    public ScrollViewer MainScrollViewerElement => WorkspaceScrollViewer;

    // The raster and its vector layer intentionally share one viewport transform so
    // coordinates always stay aligned.  Visual affordances must not inherit that
    // transform though: a 2 px contour should still look like a 2 px contour at
    // 25% or 400% zoom.  Convert screen-pixel styling back into image-space units
    // before putting the element on the scaled canvas.
    private double OverlayScale => Math.Max(0.01, ViewModel.ZoomScale);
    private double OverlayPixels(double screenPixels) => screenPixels / OverlayScale;
    private DoubleCollection OverlayDash(params double[] screenPixels)
        => new(screenPixels.Select(OverlayPixels));

    private void InteractiveOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || ViewModel.ActiveTool != GeminiToolMode.SelectDefault)
        {
            return;
        }

        if (e.OriginalSource is FrameworkElement source &&
            (source.ToolTip?.ToString()?.Contains("Bézier Handle", StringComparison.Ordinal) == true ||
             source.ToolTip?.ToString()?.Contains("Point #", StringComparison.Ordinal) == true))
        {
            return;
        }

        Point click = e.GetPosition(RealImageCanvas);
        if (TryAddPointAtCanvasDoubleClick(click))
        {
            e.Handled = true;
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadBuiltInV9SampleAssets();
        BuildReferenceDemoDashedGrid();
        UpdateCanvasVisibility();
    }

    private void LoadBuiltInV9SampleAssets()
    {
        string? photoPath = ResolveReferenceAssetPath("sample_03172025_111128.jpg");
        if (photoPath is not null && File.Exists(photoPath))
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(photoPath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            LiveV9SamplePhotoImage.Source = bmp;
        }

        string? magPath = ResolveReferenceAssetPath("v9_magnifier_idle_patch.png");
        if (magPath is not null && File.Exists(magPath))
        {
            var magBmp = new BitmapImage();
            magBmp.BeginInit();
            magBmp.CacheOption = BitmapCacheOption.OnLoad;
            magBmp.UriSource = new Uri(magPath, UriKind.Absolute);
            magBmp.EndInit();
            magBmp.Freeze();
            MagnifierIdleBoardImage.Source = magBmp;
            RenderOptions.SetBitmapScalingMode(MagnifierIdleBoardImage, BitmapScalingMode.NearestNeighbor);
        }
    }

    private static string? ResolveReferenceAssetPath(string fileName)
    {
        string[] candidates =
        {
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "images", "reference", fileName),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "assets", "images", "reference", fileName)),
            @"E:\claude\GEMHIHI\assets\images\reference\" + fileName,
            @"D:\pictures\03172025 111128.jpg"
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate) && candidate.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        if (fileName == "sample_03172025_111128.jpg" && File.Exists(@"D:\pictures\03172025 111128.jpg"))
        {
            return @"D:\pictures\03172025 111128.jpg";
        }

        return null;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsDemoReferenceLayoutActive)
            or nameof(MainWindowViewModel.IsV9ThreePieceDemoActive)
            or nameof(MainWindowViewModel.HasImageLoaded))
        {
            UpdateCanvasVisibility();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.ZoomPercent))
        {
            UpdateZoomThumbPosition();
            // Style dimensions are inverse-scaled to keep overlay strokes and
            // editing targets visually constant on the transformed viewport.
            RenderInteractiveVectorOverlay();
        }
    }

    public void UpdateCanvasVisibility()
    {
        if (ViewModel.IsDemoReferenceLayoutActive)
        {
            if (ViewModel.IsV9ThreePieceDemoActive)
            {
                LiveV9ReferenceCanvas.Visibility = Visibility.Visible;
                ReferenceDemoCanvas.Visibility = Visibility.Collapsed;
                RealImageCanvas.Visibility = Visibility.Collapsed;
                MagnifierIdleBoardImage.Visibility = Visibility.Visible;
                MagnifierDemoCornerCanvas.Visibility = Visibility.Collapsed;
                MagnifierLiveRect.Visibility = Visibility.Collapsed;
            }
            else
            {
                LiveV9ReferenceCanvas.Visibility = Visibility.Collapsed;
                ReferenceDemoCanvas.Visibility = Visibility.Visible;
                RealImageCanvas.Visibility = Visibility.Collapsed;
                MagnifierIdleBoardImage.Visibility = Visibility.Collapsed;
                MagnifierDemoCornerCanvas.Visibility = Visibility.Visible;
                MagnifierLiveRect.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            LiveV9ReferenceCanvas.Visibility = Visibility.Collapsed;
            ReferenceDemoCanvas.Visibility = Visibility.Collapsed;
            RealImageCanvas.Visibility = Visibility.Visible;
            MagnifierDemoCornerCanvas.Visibility = Visibility.Collapsed;
            if (ViewModel.HasImageLoaded && ViewModel.LoadedBitmap is not null)
            {
                double imgW = ViewModel.LoadedBitmap.PixelWidth;
                double imgH = ViewModel.LoadedBitmap.PixelHeight;
                WorkspaceImageControl.Source = ViewModel.LoadedBitmap;
                WorkspaceImageControl.Width = imgW;
                WorkspaceImageControl.Height = imgH;
                RealImageCanvas.Width = imgW;
                RealImageCanvas.Height = imgH;
                InteractiveVectorOverlayCanvas.Width = imgW;
                InteractiveVectorOverlayCanvas.Height = imgH;
                MagnifierIdleBoardImage.Visibility = Visibility.Collapsed;
                UpdateLiveMagnifier(new Point(imgW * 0.35, imgH * 0.35));
            }
            else
            {
                // A project can retain editable/calibrated geometry while its original
                // raster file is unavailable. Never leave the previous raster visible,
                // and retain the saved pixel coordinate surface so overlays do not shift
                // or clip against an arbitrary placeholder size.
                WorkspaceImageControl.Source = null;
                double savedWidth = Math.Max(1, ViewModel.ActiveCalibration.ImageWidthPx);
                double savedHeight = Math.Max(1, ViewModel.ActiveCalibration.ImageHeightPx);
                WorkspaceImageControl.Width = savedWidth;
                WorkspaceImageControl.Height = savedHeight;
                RealImageCanvas.Width = savedWidth;
                RealImageCanvas.Height = savedHeight;
                InteractiveVectorOverlayCanvas.Width = savedWidth;
                InteractiveVectorOverlayCanvas.Height = savedHeight;
                MagnifierIdleBoardImage.Visibility = Visibility.Visible;
                MagnifierLiveRect.Visibility = Visibility.Collapsed;
            }

            RenderInteractiveVectorOverlay();
        }
    }

    // =======================================================================
    // PHASE 2 INTERACTIVE VECTOR OVERLAY RENDERING
    // =======================================================================

    public void RenderInteractiveVectorOverlay()
    {
        if (InteractiveVectorOverlayCanvas is null || ViewModel.IsDemoReferenceLayoutActive)
        {
            return;
        }

        InteractiveVectorOverlayCanvas.Children.Clear();
        CalibrationProfile cal = ViewModel.ActiveCalibration;

        // 1. Optional Calibrated Grid Overlay (ShowGrid)
        if (ViewModel.ShowGrid)
        {
            DrawCalibratedGridOverlay(cal);
        }

        // 2. Optional Mode B Manual Calibration ROI Box (with interactive move & corner resize handles)
        if (ViewModel.IsManualCalibrationBoxVisible)
        {
            CropBoxPixels box = ViewModel.ManualCalibrationBoxPx.IsValid ? ViewModel.ManualCalibrationBoxPx : cal.CropBox;
            if (box.IsValid)
            {
                DrawInteractiveModeBRoiOverlay(box, cal);
            }
        }

        // 3. Persistent digitize frame (red like Gemini's selection frame).
        if (ViewModel.HasDigitizeRoi && ViewModel.DigitizeRoiBoxPx.IsValid)
        {
            DrawDigitizeRoiOverlay(ViewModel.DigitizeRoiBoxPx, isPreview: false);
        }


        // A completed digitize frame is measurement-only: visible as proof of
        // size but never interactive or resizable.
        foreach (PatternPiece lockedPiece in ViewModel.DomainPieces.Where(piece => piece.HasLockedDigitizeFrame))
        {
            DrawLockedDigitizeFrame(lockedPiece, cal);
        }

        // 4. Render all detected & edited PatternPiece contours, handles, notches, internal points & lines
        foreach (PatternPiece piece in ViewModel.DomainPieces)
        {
            bool isSelectedPiece = ReferenceEquals(piece, ViewModel.SelectedDomainPiece);
            DrawPatternPieceOnCanvas(piece, ViewModel.GetCalibrationForPiece(piece), isSelectedPiece);
        }

        // 5. Render two-click Internal Line rubber-band preview
        if (ViewModel.ActiveTool == GeminiToolMode.AddInternalLine &&
            _pendingInternalLineStartMm.HasValue &&
            _pendingInternalLineCurrentMousePx.HasValue)
        {
            // A framed piece owns an affine pixel/mm calibration.  Never use
            // the global calibration for its rubber-band start: doing so makes
            // the preview visibly jump away from the click on phone photos.
            CalibrationProfile lineCalibration = _pendingInternalLinePiece is not null
                ? ViewModel.GetCalibrationForPiece(_pendingInternalLinePiece)
                : cal;
            ImagePixelPoint p0 = lineCalibration.PhysicalMmToPixel(_pendingInternalLineStartMm.Value);
            Point p1 = _pendingInternalLineCurrentMousePx.Value;
            InteractiveVectorOverlayCanvas.Children.Add(new Line
            {
                X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 220, 0)),
                StrokeThickness = OverlayPixels(2.0),
                StrokeDashArray = OverlayDash(4, 2),
                IsHitTestVisible = false
            });
        }

        // 6. Render interactive Measurement Overlay (Direct & Perimeter dimension lines + labels)
        DrawMeasurementOverlay(ViewModel.GetCalibrationForPiece(ViewModel.SelectedDomainPiece));

        // 7. Render Zoom Area rubber-band selection rectangle
        if (_isDraggingZoomAreaRect)
        {
            double x = Math.Min(_zoomAreaStartPx.X, _zoomAreaCurrentPx.X);
            double y = Math.Min(_zoomAreaStartPx.Y, _zoomAreaCurrentPx.Y);
            double w = Math.Abs(_zoomAreaCurrentPx.X - _zoomAreaStartPx.X);
            double h = Math.Abs(_zoomAreaCurrentPx.Y - _zoomAreaStartPx.Y);
            var zoomRect = new Rectangle
            {
                Width = w,
                Height = h,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 235, 0)),
                StrokeThickness = OverlayPixels(2.0),
                StrokeDashArray = OverlayDash(4, 2),
                Fill = new SolidColorBrush(Color.FromArgb(35, 255, 235, 0)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(zoomRect, x);
            Canvas.SetTop(zoomRect, y);
            InteractiveVectorOverlayCanvas.Children.Add(zoomRect);
        }

        // 8. Render Multi-Point rubber-band selection rectangle
        if (_isDraggingSelectionRect)
        {
            double x = Math.Min(_selectionRectStartPx.X, _selectionRectCurrentPx.X);
            double y = Math.Min(_selectionRectStartPx.Y, _selectionRectCurrentPx.Y);
            double w = Math.Abs(_selectionRectCurrentPx.X - _selectionRectStartPx.X);
            double h = Math.Abs(_selectionRectCurrentPx.Y - _selectionRectStartPx.Y);
            var selRect = new Rectangle
            {
                Width = w,
                Height = h,
                Stroke = new SolidColorBrush(Color.FromRgb(0, 180, 255)),
                StrokeThickness = OverlayPixels(1.5),
                StrokeDashArray = OverlayDash(4, 2),
                Fill = new SolidColorBrush(Color.FromArgb(32, 0, 180, 255)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(selRect, x);
            Canvas.SetTop(selRect, y);
            InteractiveVectorOverlayCanvas.Children.Add(selRect);
        }

        if (_isDraggingDigitizeRoi)
        {
            int x0 = (int)Math.Round(Math.Min(_digitizeRoiStartPx.X, _digitizeRoiCurrentPx.X));
            int y0 = (int)Math.Round(Math.Min(_digitizeRoiStartPx.Y, _digitizeRoiCurrentPx.Y));
            int x1 = (int)Math.Round(Math.Max(_digitizeRoiStartPx.X, _digitizeRoiCurrentPx.X));
            int y1 = (int)Math.Round(Math.Max(_digitizeRoiStartPx.Y, _digitizeRoiCurrentPx.Y));
            DrawDigitizeRoiOverlay(new CropBoxPixels(x0, y0, x1, y1), isPreview: true);
        }
    }

    private void DrawDigitizeRoiOverlay(CropBoxPixels box, bool isPreview)
    {
        if (!box.IsValid)
        {
            return;
        }

        RotatedFramePixels frame = isPreview
            ? RotatedFramePixels.FromCropBox(box)
            : ViewModel.DigitizeRoiFramePx;
        if (!frame.IsValid)
        {
            return;
        }
        IReadOnlyList<ImagePixelPoint> corners = frame.Corners;
        var roiRect = new Polygon
        {
            Points = [.. corners.Select(p => new Point(p.X, p.Y))],
            Stroke = Brushes.Red,
            StrokeThickness = OverlayPixels(2.0),
            Fill = new SolidColorBrush(Color.FromArgb(18, 255, 0, 0)),
            IsHitTestVisible = false
        };
        if (isPreview)
        {
            roiRect.StrokeDashArray = OverlayDash(6, 3);
        }

        InteractiveVectorOverlayCanvas.Children.Add(roiRect);

        if (!isPreview && ViewModel.ActiveTool == GeminiToolMode.DigitizeRoi)
        {
            // Deliberately use edge grips rather than four free corner points.
            // A calibration frame must stay a rectangle; it must never become a
            // user-distorted quadrilateral while being fitted around a pattern.
            Point[] edgeMidpoints =
            [
                Midpoint(corners[0], corners[3]),
                Midpoint(corners[0], corners[1]),
                Midpoint(corners[1], corners[2]),
                Midpoint(corners[2], corners[3])
            ];
            for (int i = 0; i < edgeMidpoints.Length; i++)
            {
                var handle = new Rectangle
                {
                    Width = OverlayPixels(i is 0 or 2 ? 8 : 20),
                    Height = OverlayPixels(i is 0 or 2 ? 20 : 8),
                    Fill = Brushes.Red,
                    Stroke = Brushes.White,
                    StrokeThickness = OverlayPixels(1.2),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(handle, edgeMidpoints[i].X - (handle.Width / 2));
                Canvas.SetTop(handle, edgeMidpoints[i].Y - (handle.Height / 2));
                InteractiveVectorOverlayCanvas.Children.Add(handle);
            }

            // Rotation is deliberately a separate handle outside the top edge,
            // leaving the four frame corners rigid and non-distortable.
            Point top = edgeMidpoints[1];
            double a = frame.RotationRadians;
            Point rotate = new(top.X + (Math.Sin(a) * OverlayPixels(30)), top.Y - (Math.Cos(a) * OverlayPixels(30)));
            var rotateHandle = new Ellipse
            {
                Width = OverlayPixels(12), Height = OverlayPixels(12),
                Fill = new SolidColorBrush(Color.FromRgb(0, 210, 255)), Stroke = Brushes.Black,
                StrokeThickness = OverlayPixels(1), IsHitTestVisible = false
            };
            Canvas.SetLeft(rotateHandle, rotate.X - (rotateHandle.Width * .5));
            Canvas.SetTop(rotateHandle, rotate.Y - (rotateHandle.Height * .5));
            InteractiveVectorOverlayCanvas.Children.Add(rotateHandle);
        }
    }

    private static Point Midpoint(ImagePixelPoint a, ImagePixelPoint b) => new((a.X + b.X) * .5, (a.Y + b.Y) * .5);

    private void DrawLockedDigitizeFrame(PatternPiece piece, CalibrationProfile cal)
    {
        CropBoxPixels framePixels = piece.LockedDigitizeFramePixels;
        if (!framePixels.IsValid)
        {
            return;
        }

        RotatedFramePixels lockedFrame = RotatedFramePixels.FromCropBox(framePixels, piece.LockedDigitizeFrameRotationDegrees);
        var frame = new Polygon
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 230, 255)),
            StrokeThickness = OverlayPixels(1.5),
            StrokeDashArray = OverlayDash(6, 3),
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            ToolTip = $"Locked digitize frame: {piece.LockedDigitizeFrameWidthCm:0.###} x {piece.LockedDigitizeFrameHeightCm:0.###} cm"
        };
        foreach (ImagePixelPoint point in lockedFrame.Corners)
        {
            frame.Points.Add(new Point(point.X, point.Y));
        }
        InteractiveVectorOverlayCanvas.Children.Add(frame);
        // The cyan rectangle is the only needed frame indication.  A large
        // text badge obscures the photographed edge and makes precision edits
        // harder, especially at low zoom.
    }


    private void DrawInteractiveModeBRoiOverlay(CropBoxPixels box, CalibrationProfile cal)
    {
        var roiRect = new Rectangle
        {
            Width = box.Width,
            Height = box.Height,
            Stroke = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
            StrokeThickness = OverlayPixels(2.2),
            StrokeDashArray = OverlayDash(5, 3),
            Fill = new SolidColorBrush(Color.FromArgb(24, 0, 210, 255)),
            Cursor = Cursors.SizeAll,
            ToolTip = $"Mode B Calibration ROI ({box.StartX},{box.StartY} -> {box.StopX},{box.StopY} px) — Drag to move, drag corners to resize"
        };
        Canvas.SetLeft(roiRect, box.StartX);
        Canvas.SetTop(roiRect, box.StartY);

        roiRect.MouseLeftButtonDown += (s, ev) =>
        {
            _isMovingModeBRoi = true;
            _modeBRoiDragInitialBox = box;
            _modeBRoiDragStartPx = ev.GetPosition(RealImageCanvas);
            WorkspaceScrollViewer.CaptureMouse();
            ev.Handled = true;
        };
        InteractiveVectorOverlayCanvas.Children.Add(roiRect);

        // Mode B ROI header label
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 95, 135)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
            BorderThickness = new Thickness(OverlayPixels(1)),
            Padding = new Thickness(OverlayPixels(6), OverlayPixels(2), OverlayPixels(6), OverlayPixels(2)),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = $"Mode B ROI: {ViewModel.ManualCalibrationWidthCm:0.##} × {ViewModel.ManualCalibrationHeightCm:0.##} cm ({box.Width}×{box.Height} px)",
                Foreground = Brushes.White,
                FontSize = OverlayPixels(12),
                FontWeight = FontWeights.Bold
            }
        };
        Canvas.SetLeft(badge, box.StartX + OverlayPixels(6));
        Canvas.SetTop(badge, Math.Max(OverlayPixels(4), box.StartY - OverlayPixels(26)));
        InteractiveVectorOverlayCanvas.Children.Add(badge);

        // 4 Corner resize handles (0=TL, 1=TR, 2=BR, 3=BL)
        Point[] corners =
        [
            new Point(box.StartX, box.StartY),
            new Point(box.StopX, box.StartY),
            new Point(box.StopX, box.StopY),
            new Point(box.StartX, box.StopY)
        ];
        Cursor[] cursors = [Cursors.SizeNWSE, Cursors.SizeNESW, Cursors.SizeNWSE, Cursors.SizeNESW];

        for (int cIdx = 0; cIdx < 4; cIdx++)
        {
            int capturedCorner = cIdx;
            var handle = new Rectangle
            {
                Width = OverlayPixels(12),
                Height = OverlayPixels(12),
                Fill = new SolidColorBrush(Color.FromRgb(0, 230, 255)),
                Stroke = Brushes.Black,
                StrokeThickness = OverlayPixels(1.2),
                Cursor = cursors[cIdx]
            };
            Canvas.SetLeft(handle, corners[cIdx].X - (handle.Width / 2));
            Canvas.SetTop(handle, corners[cIdx].Y - (handle.Height / 2));
            handle.MouseLeftButtonDown += (s, ev) =>
            {
                _isResizingModeBRoi = true;
                _modeBRoiResizeCornerIndex = capturedCorner;
                _modeBRoiDragInitialBox = box;
                _modeBRoiDragStartPx = ev.GetPosition(RealImageCanvas);
                WorkspaceScrollViewer.CaptureMouse();
                ev.Handled = true;
            };
            InteractiveVectorOverlayCanvas.Children.Add(handle);
        }
    }

    private void DrawMeasurementOverlay(CalibrationProfile cal)
    {
        // Direct Measurement visual line & label
        if (_measureStartPointMm.HasValue && (_measureEndPointMm.HasValue || (ViewModel.ActiveTool == GeminiToolMode.MeasureDirect && _measureCurrentHoverPx.HasValue)))
        {
            ImagePixelPoint p0 = cal.PhysicalMmToPixel(_measureStartPointMm.Value);
            PhysicalPointMm endMm = _measureEndPointMm ?? cal.PixelToPhysicalMm(new ImagePixelPoint(_measureCurrentHoverPx!.Value.X, _measureCurrentHoverPx.Value.Y));
            ImagePixelPoint p1 = cal.PhysicalMmToPixel(endMm);
            double distCm = ViewModel.MeasurementEngine.ComputeStraightDistanceCm(_measureStartPointMm.Value, endMm);
            string labelText = _measureEndPointMm.HasValue && !string.IsNullOrEmpty(_activeMeasurementOverlayText) && !_isActiveMeasurementPerimeter
                ? _activeMeasurementOverlayText
                : $"{distCm:F2} cm";

            InteractiveVectorOverlayCanvas.Children.Add(new Line
            {
                X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 180)),
                StrokeThickness = OverlayPixels(2.6),
                StrokeDashArray = OverlayDash(5, 2),
                IsHitTestVisible = false
            });

            foreach (ImagePixelPoint ep in new[] { p0, p1 })
            {
                var marker = new Ellipse
                {
                    Width = OverlayPixels(10), Height = OverlayPixels(10),
                    Fill = new SolidColorBrush(Color.FromRgb(0, 255, 180)),
                    Stroke = Brushes.Black, StrokeThickness = OverlayPixels(1.2),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, ep.X - (marker.Width / 2));
                Canvas.SetTop(marker, ep.Y - (marker.Height / 2));
                InteractiveVectorOverlayCanvas.Children.Add(marker);
            }

            AddCanvasDimensionLabel((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5 - OverlayPixels(24), labelText, Color.FromRgb(0, 255, 180));
        }

        // Perimeter Measurement visual contour arc & label
        if (_isActiveMeasurementPerimeter &&
            _measurePerimeterPiece is not null &&
            _measureStartVertexIndex >= 0 &&
            _measureEndVertexIndex >= 0 &&
            _measureStartVertexIndex < _measurePerimeterPiece.Vertices.Count &&
            _measureEndVertexIndex < _measurePerimeterPiece.Vertices.Count)
        {
            PatternPiece p = _measurePerimeterPiece;
            int n = p.Vertices.Count;
            var (_, forwardMm, backwardMm) = ViewModel.MeasurementEngine.ComputeContourDistanceBetweenVerticesMm(p, _measureStartVertexIndex, _measureEndVertexIndex);
            bool useForward = forwardMm <= backwardMm;

            var poly = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                StrokeThickness = OverlayPixels(4.0),
                IsHitTestVisible = false
            };

            int curr = useForward ? _measureStartVertexIndex : _measureEndVertexIndex;
            int target = useForward ? _measureEndVertexIndex : _measureStartVertexIndex;
            int safety = 0;
            while (curr != target && safety++ <= n)
            {
                CubicBezierSegment seg = p.GetSegment(curr);
                for (int s = 0; s <= 16; s++)
                {
                    ImagePixelPoint px = cal.PhysicalMmToPixel(seg.Evaluate(s / 16.0));
                    poly.Points.Add(new Point(px.X, px.Y));
                }
                curr = (curr + 1) % n;
            }
            InteractiveVectorOverlayCanvas.Children.Add(poly);

            if (poly.Points.Count > 0)
            {
                Point midPt = poly.Points[poly.Points.Count / 2];
                AddCanvasDimensionLabel(midPt.X, midPt.Y - OverlayPixels(26), _activeMeasurementOverlayText, Color.FromRgb(255, 165, 0));
            }
        }
    }

    private void AddCanvasDimensionLabel(double xPx, double yPx, string text, Color borderColor)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 24, 28)),
            BorderBrush = new SolidColorBrush(borderColor),
            BorderThickness = new Thickness(OverlayPixels(1.5)),
            CornerRadius = new CornerRadius(OverlayPixels(3)),
            Padding = new Thickness(OverlayPixels(7), OverlayPixels(3), OverlayPixels(7), OverlayPixels(3)),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = OverlayPixels(13),
                FontWeight = FontWeights.Bold
            }
        };
        Canvas.SetLeft(border, Math.Max(OverlayPixels(4), xPx - OverlayPixels(60)));
        Canvas.SetTop(border, Math.Max(OverlayPixels(4), yPx));
        InteractiveVectorOverlayCanvas.Children.Add(border);
    }

    private void DrawCalibratedGridOverlay(CalibrationProfile cal)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(170, 225, 32, 32));
        gridBrush.Freeze();
        var dash = OverlayDash(4, 3);
        dash.Freeze();

        double w = RealImageCanvas.Width > 0 ? RealImageCanvas.Width : 3456;
        double h = RealImageCanvas.Height > 0 ? RealImageCanvas.Height : 2592;

        double stepXPx = (cal.CellWidthCm * 10.0) / Math.Max(0.01, cal.ScaleXMmPerPx);
        double stepYPx = (cal.CellHeightCm * 10.0) / Math.Max(0.01, cal.ScaleYMmPerPx);

        for (double x = 0; x <= w; x += Math.Max(20, stepXPx))
        {
            InteractiveVectorOverlayCanvas.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = gridBrush, StrokeThickness = OverlayPixels(1.0), StrokeDashArray = dash,
                IsHitTestVisible = false
            });
        }

        for (double y = 0; y <= h; y += Math.Max(20, stepYPx))
        {
            InteractiveVectorOverlayCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = gridBrush, StrokeThickness = OverlayPixels(1.0), StrokeDashArray = dash,
                IsHitTestVisible = false
            });
        }
    }

    private void DrawPatternPieceOnCanvas(PatternPiece piece, CalibrationProfile cal, bool isSelectedPiece)
    {
        if (piece.Vertices.Count < 2)
        {
            return;
        }

        // Build WPF PathGeometry from piece segments in image pixel space
        var pathGeom = new PathGeometry();
        ImagePixelPoint startPx = cal.PhysicalMmToPixel(piece.Vertices[0].PositionMm);
        var fig = new PathFigure
        {
            StartPoint = new Point(startPx.X, startPx.Y),
            IsClosed = true,
            IsFilled = true
        };

        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            CubicBezierSegment seg = piece.GetSegment(i);
            ImagePixelPoint p3Px = cal.PhysicalMmToPixel(seg.P3);
            if (seg.IsCurve)
            {
                ImagePixelPoint c1Px = cal.PhysicalMmToPixel(seg.C1);
                ImagePixelPoint c2Px = cal.PhysicalMmToPixel(seg.C2);
                fig.Segments.Add(new BezierSegment(
                    new Point(c1Px.X, c1Px.Y),
                    new Point(c2Px.X, c2Px.Y),
                    new Point(p3Px.X, p3Px.Y),
                    isStroked: true));
            }
            else
            {
                fig.Segments.Add(new LineSegment(new Point(p3Px.X, p3Px.Y), isStroked: true));
            }
        }

        pathGeom.Figures.Add(fig);

        var contourPath = new System.Windows.Shapes.Path
        {
            Data = pathGeom,
            Stroke = new SolidColorBrush(isSelectedPiece ? Color.FromRgb(255, 220, 0) : Color.FromRgb(228, 28, 28)),
            // Match Gemini's hairline contour: screen-space thickness stays
            // stable while zooming, but does not obscure the photographed edge.
            StrokeThickness = OverlayPixels(isSelectedPiece ? 1.45 : 0.95),
            Fill = new SolidColorBrush(isSelectedPiece ? Color.FromArgb(24, 255, 220, 0) : Color.FromArgb(8, 255, 255, 255)),
            Cursor = Cursors.Hand
        };

        contourPath.MouseLeftButtonDown += (s, ev) =>
        {
            Point clickPos = ev.GetPosition(RealImageCanvas);
            PhysicalPointMm clickMm = cal.PixelToPhysicalMm(new ImagePixelPoint(clickPos.X, clickPos.Y));
            if (ev.ClickCount == 2 && TryAddPointOnContourDoubleClick(piece, clickMm, clickPos))
            {
                ev.Handled = true;
                return;
            }
            HandlePieceOrContourClick(piece, clickMm, clickPos, ev);
            ev.Handled = true;
        };

        InteractiveVectorOverlayCanvas.Children.Add(contourPath);

        // Draw Notches (if ShowNotches)
        if (ViewModel.ShowNotches)
        {
            foreach (NotchItem notch in piece.Notches)
            {
                CubicBezierSegment seg = piece.GetSegment(notch.SegmentIndex);
                PhysicalPointMm posMm = seg.Evaluate(notch.ParameterT);
                PhysicalPointMm deriv = seg.Derivative(notch.ParameterT);
                double tangentRad = Math.Atan2(deriv.YMm, deriv.XMm);
                double notchRad = notch.IsRelativeAngle
                    ? tangentRad + (notch.AngleDegrees * Math.PI / 180.0)
                    : (notch.AngleDegrees * Math.PI / 180.0);

                PhysicalPointMm tipMm = new(
                    posMm.XMm + (Math.Cos(notchRad) * notch.LengthMm),
                    posMm.YMm + (Math.Sin(notchRad) * notch.LengthMm));

                ImagePixelPoint p0 = cal.PhysicalMmToPixel(posMm);
                ImagePixelPoint p1 = cal.PhysicalMmToPixel(tipMm);
                bool isSelectedNotch = isSelectedPiece && ViewModel.SelectedNotchId == notch.Id;

                var notchLine = new Line
                {
                    X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y,
                    Stroke = new SolidColorBrush(isSelectedNotch ? Color.FromRgb(255, 255, 60) : Color.FromRgb(0, 230, 255)),
                    StrokeThickness = OverlayPixels(isSelectedNotch ? 4.2 : 3.0),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{piece.Name} — Notch (Click to select/delete, drag along contour)"
                };

                var baseHandle = new Ellipse
                {
                    Width = OverlayPixels(isSelectedNotch ? 11 : 8),
                    Height = OverlayPixels(isSelectedNotch ? 11 : 8),
                    Fill = new SolidColorBrush(isSelectedNotch ? Color.FromRgb(255, 255, 60) : Color.FromRgb(0, 230, 255)),
                    Stroke = Brushes.Black,
                    StrokeThickness = OverlayPixels(1.0),
                    Cursor = Cursors.Hand,
                    ToolTip = notchLine.ToolTip
                };
                Canvas.SetLeft(baseHandle, p0.X - (baseHandle.Width * 0.5));
                Canvas.SetTop(baseHandle, p0.Y - (baseHandle.Height * 0.5));

                Guid capturedNotchId = notch.Id;
                MouseButtonEventHandler notchHandler = (s, ev) =>
                {
                    OnNotchMouseLeftButtonDown(piece, capturedNotchId, ev);
                    ev.Handled = true;
                };
                notchLine.MouseLeftButtonDown += notchHandler;
                baseHandle.MouseLeftButtonDown += notchHandler;

                InteractiveVectorOverlayCanvas.Children.Add(notchLine);
                InteractiveVectorOverlayCanvas.Children.Add(baseHandle);
            }
        }

        // Draw Internal Points / Drill Holes (if ShowInternalPoints)
        if (ViewModel.ShowInternalPoints)
        {
            foreach (InternalPointItem ip in piece.InternalPoints)
            {
                ImagePixelPoint px = cal.PhysicalMmToPixel(ip.PositionMm);
                double rPx = Math.Max(6.0, ip.LengthMm / Math.Max(0.1, cal.ScaleXMmPerPx));
                bool isSelectedIp = isSelectedPiece && ViewModel.SelectedInternalPointId == ip.Id;

                var circle = new Ellipse
                {
                    Width = rPx * 2,
                    Height = rPx * 2,
                    Stroke = new SolidColorBrush(isSelectedIp ? Color.FromRgb(255, 255, 60) : Color.FromRgb(255, 165, 0)),
                    StrokeThickness = OverlayPixels(isSelectedIp ? 3.2 : 2.2),
                    Fill = new SolidColorBrush(Color.FromArgb(40, 255, 165, 0)),
                    Cursor = Cursors.SizeAll,
                    ToolTip = $"{piece.Name} — Internal Point (Click to select/delete, drag to move)"
                };
                Canvas.SetLeft(circle, px.X - rPx);
                Canvas.SetTop(circle, px.Y - rPx);

                Guid capturedIpId = ip.Id;
                circle.MouseLeftButtonDown += (s, ev) =>
                {
                    OnInternalPointMouseLeftButtonDown(piece, capturedIpId, ev);
                    ev.Handled = true;
                };

                InteractiveVectorOverlayCanvas.Children.Add(circle);
            }
        }

        // Draw Internal Lines & Grain Axis (if ShowInternalLines)
        if (ViewModel.ShowInternalLines)
        {
            foreach (InternalLineItem il in piece.InternalLines)
            {
                ImagePixelPoint p0 = cal.PhysicalMmToPixel(il.StartMm);
                ImagePixelPoint p1 = cal.PhysicalMmToPixel(il.EndMm);
                bool isGrain = il.LineType == InternalLineType.GrainAxis;
                bool isSelectedLine = isSelectedPiece && ViewModel.SelectedInternalLineId == il.Id;
                Brush lineBrush = isSelectedLine
                    ? new SolidColorBrush(Color.FromRgb(255, 255, 60))
                    : (isGrain
                        ? new SolidColorBrush(Color.FromRgb(60, 225, 100))
                        : new SolidColorBrush(Color.FromRgb(180, 130, 255)));

                Shape ilLine;
                if (il.IsCurve)
                {
                    ImagePixelPoint control = cal.PhysicalMmToPixel(il.ControlPointMm);
                    var geometry = new PathGeometry();
                    var figure = new PathFigure { StartPoint = new Point(p0.X, p0.Y) };
                    figure.Segments.Add(new QuadraticBezierSegment(new Point(control.X, control.Y), new Point(p1.X, p1.Y), true));
                    geometry.Figures.Add(figure);
                    ilLine = new System.Windows.Shapes.Path { Data = geometry, Stroke = lineBrush, StrokeThickness = OverlayPixels(isSelectedLine ? 3.6 : 2.5), Cursor = Cursors.SizeAll,
                        ToolTip = $"{piece.Name} — Internal curve (Click to select/delete, drag to move)" };
                }
                else
                {
                    ilLine = new Line { X1 = p0.X, Y1 = p0.Y, X2 = p1.X, Y2 = p1.Y, Stroke = lineBrush,
                        StrokeThickness = OverlayPixels(isSelectedLine ? 3.6 : 2.5), Cursor = Cursors.SizeAll,
                        ToolTip = $"{piece.Name} — {(isGrain ? "Grain Axis" : "Internal Line")} (Click to select/delete, drag to move)" };
                }

                Guid capturedIlId = il.Id;
                ilLine.MouseLeftButtonDown += (s, ev) =>
                {
                    OnInternalLineMouseLeftButtonDown(piece, capturedIlId, ev);
                    ev.Handled = true;
                };
                InteractiveVectorOverlayCanvas.Children.Add(ilLine);

                if (isGrain)
                {
                    double angle = Math.Atan2(p1.Y - p0.Y, p1.X - p0.X);
                    double headLen = OverlayPixels(14.0);
                    var arrow = new Polygon
                    {
                        Points =
                        [
                            new Point(p1.X, p1.Y),
                            new Point(p1.X - (headLen * Math.Cos(angle - 0.45)), p1.Y - (headLen * Math.Sin(angle - 0.45))),
                            new Point(p1.X - (headLen * Math.Cos(angle + 0.45)), p1.Y - (headLen * Math.Sin(angle + 0.45)))
                        ],
                        Fill = lineBrush,
                        IsHitTestVisible = false
                    };
                    InteractiveVectorOverlayCanvas.Children.Add(arrow);
                }

                // Endpoint handles for selected internal line
                if (isSelectedLine)
                {
                    AddInternalLineEndpointHandle(piece, capturedIlId, isStartEndpoint: true, p0);
                    AddInternalLineEndpointHandle(piece, capturedIlId, isStartEndpoint: false, p1);
                }
            }
        }

        // Only expose handles for curves adjoining the selected point.  Merely
        // selecting a piece must leave the photographed contour unobscured.
        if (isSelectedPiece && _showBezierHandlesForSelectedVertex && ViewModel.SelectedVertexIndex >= 0)
        {
            var handleLineBrush = new SolidColorBrush(Color.FromRgb(60, 200, 255));
            handleLineBrush.Freeze();
            int selectedVertex = ViewModel.SelectedVertexIndex;

            for (int segIdx = 0; segIdx < piece.Vertices.Count; segIdx++)
            {
                int segmentEndVertex = (segIdx + 1) % piece.Vertices.Count;
                if (segIdx != selectedVertex && segmentEndVertex != selectedVertex)
                {
                    continue;
                }
                CubicBezierSegment seg = piece.GetSegment(segIdx);
                if (!seg.IsCurve)
                {
                    continue;
                }

                ImagePixelPoint p0 = cal.PhysicalMmToPixel(seg.P0);
                ImagePixelPoint c1 = cal.PhysicalMmToPixel(seg.C1);
                ImagePixelPoint c2 = cal.PhysicalMmToPixel(seg.C2);
                ImagePixelPoint p3 = cal.PhysicalMmToPixel(seg.P3);

                InteractiveVectorOverlayCanvas.Children.Add(new Line
                {
                    X1 = p0.X, Y1 = p0.Y, X2 = c1.X, Y2 = c1.Y,
                    Stroke = handleLineBrush, StrokeThickness = OverlayPixels(1.2), IsHitTestVisible = false
                });
                InteractiveVectorOverlayCanvas.Children.Add(new Line
                {
                    X1 = p3.X, Y1 = p3.Y, X2 = c2.X, Y2 = c2.Y,
                    Stroke = handleLineBrush, StrokeThickness = OverlayPixels(1.2), IsHitTestVisible = false
                });

                int capturedSeg = segIdx;
                AddBezierHandleThumb(piece, capturedSeg, isStartC1: true, c1);
                AddBezierHandleThumb(piece, capturedSeg, isStartC1: false, c2);
            }
        }

        // Draw Vertices (Red = Graded Corner, Green = Curve Point, Highlighted Ring = Selected Vertex)
        for (int vIdx = 0; vIdx < piece.Vertices.Count; vIdx++)
        {
            ContourVertex v = piece.Vertices[vIdx];
            ImagePixelPoint px = cal.PhysicalMmToPixel(v.PositionMm);
            bool isSelectedVertex = isSelectedPiece && (vIdx == ViewModel.SelectedVertexIndex || ViewModel.SelectedVertexIndices.Contains(vIdx));

            // Keep ordinary detected anchors unobtrusive.  Selection retains a
            // larger ring so the active edit target is still obvious.
            double size = OverlayPixels(isSelectedVertex ? 9.5 : 4.5);
            var dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(v.IsGradedCorner ? Color.FromRgb(255, 24, 24) : Color.FromRgb(40, 220, 80)),
                Stroke = isSelectedVertex ? Brushes.Yellow : Brushes.Black,
                StrokeThickness = OverlayPixels(isSelectedVertex ? 2.0 : 0.65),
                Cursor = Cursors.SizeAll,
                ToolTip = $"{piece.Name} - Point #{vIdx + 1} ({(v.IsGradedCorner ? "Graded" : "Curve")})"
            };

            Canvas.SetLeft(dot, px.X - (size * 0.5));
            Canvas.SetTop(dot, px.Y - (size * 0.5));

            int capturedIdx = vIdx;
            dot.MouseLeftButtonDown += (s, ev) =>
            {
                OnVertexMouseLeftButtonDown(piece, capturedIdx, ev);
                ev.Handled = true;
            };

            InteractiveVectorOverlayCanvas.Children.Add(dot);
        }
    }

    private void AddBezierHandleThumb(PatternPiece piece, int segmentIndex, bool isStartC1, ImagePixelPoint px)
    {
        var thumb = new Rectangle
        {
            // The former 8px target was too easy to miss on a photographed
            // pattern.  Keep the visible control precise but give it a usable
            // 14px hit target and place it above contour/vertex overlays.
            Width = OverlayPixels(14),
            Height = OverlayPixels(14),
            Fill = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
            Stroke = Brushes.Black,
            StrokeThickness = OverlayPixels(1.0),
            Cursor = Cursors.SizeAll,
            ToolTip = $"Bézier Handle {(isStartC1 ? "C1" : "C2")} (Drag to adjust curve)"
        };
        Canvas.SetLeft(thumb, px.X - (thumb.Width / 2));
        Canvas.SetTop(thumb, px.Y - (thumb.Height / 2));
        Canvas.SetZIndex(thumb, 500);

        thumb.MouseLeftButtonDown += (s, ev) =>
        {
            BeginObjectDrag("Edit Bézier Handle");
            _isDraggingBezierHandle = true;
            _isDraggingStartHandleC1 = isStartC1;
            _draggedPiece = piece;
            _draggedVertexOrSegmentIndex = segmentIndex;
            _dragLastImagePixelPos = ev.GetPosition(RealImageCanvas);
            WorkspaceScrollViewer.CaptureMouse();
            ViewModel.StatusLeftText = $"Bézier handle captured on {piece.Name}. Drag to reshape the curve inside the locked frame.";
            ev.Handled = true;
        };

        InteractiveVectorOverlayCanvas.Children.Add(thumb);
    }

    private void AddInternalLineEndpointHandle(PatternPiece piece, Guid internalLineId, bool isStartEndpoint, ImagePixelPoint px)
    {
        var thumb = new Rectangle
        {
            Width = OverlayPixels(9),
            Height = OverlayPixels(9),
            Fill = new SolidColorBrush(Color.FromRgb(255, 255, 60)),
            Stroke = Brushes.Black,
            StrokeThickness = OverlayPixels(1.1),
            Cursor = Cursors.Hand,
            ToolTip = $"Internal Line {(isStartEndpoint ? "Start" : "End")} Endpoint (Drag to adjust)"
        };
        Canvas.SetLeft(thumb, px.X - (thumb.Width / 2));
        Canvas.SetTop(thumb, px.Y - (thumb.Height / 2));

        thumb.MouseLeftButtonDown += (s, ev) =>
        {
            BeginObjectDrag("Resize Internal Line");
            _isDraggingInternalLineEndpoint = true;
            _isDraggingInternalLineStartEndpoint = isStartEndpoint;
            _draggedPiece = piece;
            _draggedInternalObjectId = internalLineId;
            _dragLastImagePixelPos = ev.GetPosition(RealImageCanvas);
            WorkspaceScrollViewer.CaptureMouse();
            ev.Handled = true;
        };

        InteractiveVectorOverlayCanvas.Children.Add(thumb);
    }

    private void BeginObjectDrag(string undoAction)
    {
        _pendingDragUndoAction = undoAction;
    }

    private void OnNotchMouseLeftButtonDown(PatternPiece piece, Guid notchId, MouseButtonEventArgs e)
    {
        if (TryStartWholePieceDragWithControl(piece, e))
        {
            return;
        }

        ViewModel.SelectNotchOnPiece(piece, notchId);
        if (ViewModel.ActiveTool == GeminiToolMode.DeleteNotch)
        {
            ViewModel.ExecuteDeleteNotch();
            return;
        }

        BeginObjectDrag("Move Notch");
        _isDraggingNotch = true;
        _draggedPiece = piece;
        _draggedInternalObjectId = notchId;
        _dragLastImagePixelPos = e.GetPosition(RealImageCanvas);
        WorkspaceScrollViewer.CaptureMouse();
    }

    private void OnInternalPointMouseLeftButtonDown(PatternPiece piece, Guid internalPointId, MouseButtonEventArgs e)
    {
        if (TryStartWholePieceDragWithControl(piece, e))
        {
            return;
        }

        ViewModel.SelectInternalPointOnPiece(piece, internalPointId);
        if (ViewModel.ActiveTool == GeminiToolMode.DeleteInternalPoint)
        {
            ViewModel.ExecuteDeleteInternalPoint();
            return;
        }

        BeginObjectDrag("Move Internal Point");
        _isDraggingInternalPoint = true;
        _draggedPiece = piece;
        _draggedInternalObjectId = internalPointId;
        _dragLastImagePixelPos = e.GetPosition(RealImageCanvas);
        WorkspaceScrollViewer.CaptureMouse();
    }

    private void OnInternalLineMouseLeftButtonDown(PatternPiece piece, Guid internalLineId, MouseButtonEventArgs e)
    {
        if (TryStartWholePieceDragWithControl(piece, e))
        {
            return;
        }

        ViewModel.SelectInternalLineOnPiece(piece, internalLineId);
        if (ViewModel.ActiveTool == GeminiToolMode.DeleteInternalLine)
        {
            ViewModel.ExecuteDeleteInternalLine();
            return;
        }

        BeginObjectDrag("Move Internal Line");
        _isDraggingInternalLine = true;
        _draggedPiece = piece;
        _draggedInternalObjectId = internalLineId;
        _dragLastImagePixelPos = e.GetPosition(RealImageCanvas);
        WorkspaceScrollViewer.CaptureMouse();
    }

    private void OnVertexMouseLeftButtonDown(PatternPiece piece, int vertexIndex, MouseButtonEventArgs e)
    {
        if (TryStartWholePieceDragWithControl(piece, e))
        {
            return;
        }

        // Vertices sit above the contour path and otherwise consume the click.  Let
        // line/curve tools operate on the outgoing segment when the user clicks its
        // endpoint, rather than silently starting a point drag.
        if (ViewModel.ActiveTool == GeminiToolMode.LineToCurve)
        {
            ViewModel.ExecuteLineToCurveOnSegment(piece, vertexIndex);
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.CurveToLine)
        {
            ViewModel.ExecuteCurveToLineOnSegment(piece, vertexIndex);
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.MeasurePerimeter)
        {
            if (_measureStartVertexIndex < 0 || !ReferenceEquals(_measurePerimeterPiece, piece))
            {
                _measurePerimeterPiece = piece;
                _measureStartVertexIndex = vertexIndex;
                _measureEndVertexIndex = -1;
                _isActiveMeasurementPerimeter = false;
                ViewModel.SelectDomainPiece(piece, vertexIndex);
                ViewModel.StatusLeftText = $"Measure on perimeter: Start vertex #{vertexIndex + 1} selected. Click second vertex on {piece.Name}...";
                RenderInteractiveVectorOverlay();
            }
            else
            {
                _measureEndVertexIndex = vertexIndex;
                _isActiveMeasurementPerimeter = true;
                var (shortestMm, forwardMm, _) =
                    ViewModel.MeasurementEngine.ComputeContourDistanceBetweenVerticesMm(piece, _measureStartVertexIndex, vertexIndex);
                _activeMeasurementOverlayText = $"{shortestMm / 10.0:F2} cm";
                ViewModel.SelectDomainPiece(piece, vertexIndex);
                ViewModel.StatusLeftText =
                    $"Contour distance (#{_measureStartVertexIndex + 1} -> #{vertexIndex + 1}): {shortestMm / 10.0:F2} cm | Forward: {forwardMm / 10.0:F2} cm";
                RenderInteractiveVectorOverlay();
            }
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.MeasureDirect)
        {
            PhysicalPointMm vMm = piece.Vertices[vertexIndex].PositionMm;
            HandleDirectMeasureClick(vMm);
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.DeletePoint)
        {
            ViewModel.SelectDomainPiece(piece, vertexIndex);
            ViewModel.ExecuteDeleteSelectedPoint();
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.DeleteCorner)
        {
            ViewModel.SelectDomainPiece(piece, vertexIndex);
            ViewModel.ExecuteDemoteCornerToCurvePoint();
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.SmartCorner)
        {
            ViewModel.ExecuteSmartCornerAt(piece, piece.Vertices[vertexIndex].PositionMm);
            return;
        }

        _showBezierHandlesForSelectedVertex = true;
        ViewModel.SelectDomainPiece(piece, vertexIndex);
        BeginObjectDrag("Move Point");
        _isDraggingVertex = true;
        _draggedPiece = piece;
        _draggedVertexOrSegmentIndex = vertexIndex;
        _dragLastImagePixelPos = e.GetPosition(RealImageCanvas);
        _vertexDragAxisConstraint = VertexDragAxisConstraint.None;
        ViewModel.StatusLeftText = "Drag point freely. Hold ALT to lock X/Y; hold CTRL to snap to the digitize-frame edge.";
        WorkspaceScrollViewer.CaptureMouse();
    }

    private void HandleDirectMeasureClick(PhysicalPointMm clickMm)
    {
        _isActiveMeasurementPerimeter = false;
        if (!_measureStartPointMm.HasValue || _measureEndPointMm.HasValue)
        {
            _measureStartPointMm = clickMm;
            _measureEndPointMm = null;
            _activeMeasurementOverlayText = string.Empty;
            ViewModel.StatusLeftText = $"Direct measure start: ({clickMm.XCm:F2}, {clickMm.YCm:F2}) cm — Click second point on canvas...";
        }
        else
        {
            _measureEndPointMm = clickMm;
            double distCm = ViewModel.MeasurementEngine.ComputeStraightDistanceCm(_measureStartPointMm.Value, clickMm);
            _activeMeasurementOverlayText = $"{distCm:F2} cm";
            ViewModel.StatusLeftText = $"Direct Euclidean distance: {distCm:F2} cm";
        }
        RenderInteractiveVectorOverlay();
    }

    private void HandlePieceOrContourClick(PatternPiece piece, PhysicalPointMm clickMm, Point clickPx, MouseButtonEventArgs e)
    {
        if (TryStartWholePieceDragWithControl(piece, e))
        {
            return;
        }

        (int nearestSeg, double t, PhysicalPointMm projMm, double distMm) =
            ViewModel.GeometryEngine.FindNearestContourLocation(piece, clickMm);

        switch (ViewModel.ActiveTool)
        {
            case GeminiToolMode.AddPoint when nearestSeg >= 0:
                ViewModel.ExecuteAddPointAtContourClick(piece, clickMm, isGradedCorner: false);
                break;

            case GeminiToolMode.DeletePoint:
                // The vertex itself is a direct hit target. Keep the contour fallback
                // deliberately tight so a click on an adjacent segment cannot delete
                // an unrelated point tens of millimetres away.
                int bestV = -1;
                double bestD = double.MaxValue;
                for (int i = 0; i < piece.Vertices.Count; i++)
                {
                    double d = piece.Vertices[i].PositionMm.DistanceTo(clickMm);
                    if (d < bestD)
                    {
                        bestD = d;
                        bestV = i;
                    }
                }
                if (bestV >= 0 && bestD <= 8.0)
                {
                    ViewModel.SelectDomainPiece(piece, bestV);
                    ViewModel.ExecuteDeleteSelectedPoint();
                }
                break;

            case GeminiToolMode.LineToCurve when nearestSeg >= 0:
                if (IsContourSegmentHit(piece, nearestSeg, clickPx, cal: ViewModel.GetCalibrationForPiece(piece)) && !piece.GetSegment(nearestSeg).IsCurve)
                {
                    ViewModel.ExecuteLineToCurveOnSegment(piece, nearestSeg);
                }
                else
                {
                    ViewModel.StatusLeftText = "Transform line to curve: click directly on a straight contour segment.";
                }
                break;

            case GeminiToolMode.CurveToLine when nearestSeg >= 0:
                if (IsContourSegmentHit(piece, nearestSeg, clickPx, cal: ViewModel.GetCalibrationForPiece(piece)) && piece.GetSegment(nearestSeg).IsCurve)
                {
                    ViewModel.ExecuteCurveToLineOnSegment(piece, nearestSeg);
                }
                else
                {
                    ViewModel.StatusLeftText = "Transform curve to line: click directly on a curved contour segment.";
                }
                break;

            case GeminiToolMode.AddNotch:
                ViewModel.SelectDomainPiece(piece, nearestSeg >= 0 ? nearestSeg : 0);
                ViewModel.ExecuteAddNotch(projMm);
                break;

            case GeminiToolMode.DeleteNotch:
                if (piece.Notches.Count > 0)
                {
                    NotchItem nearestNotch = piece.Notches.OrderBy(n => n.PositionMm.DistanceTo(clickMm)).First();
                    if (nearestNotch.PositionMm.DistanceTo(clickMm) <= 10.0)
                    {
                        ViewModel.SelectNotchOnPiece(piece, nearestNotch.Id);
                        ViewModel.ExecuteDeleteNotch();
                    }
                }
                break;

            case GeminiToolMode.AddInternalPoint:
                ViewModel.SelectDomainPiece(piece, nearestSeg >= 0 ? nearestSeg : 0);
                ViewModel.ExecuteAddInternalPoint(clickMm);
                break;

            case GeminiToolMode.DeleteInternalPoint:
                if (piece.InternalPoints.Count > 0)
                {
                    InternalPointItem nearestIp = piece.InternalPoints.OrderBy(p => p.PositionMm.DistanceTo(clickMm)).First();
                    if (nearestIp.PositionMm.DistanceTo(clickMm) <= 10.0)
                    {
                        ViewModel.SelectInternalPointOnPiece(piece, nearestIp.Id);
                        ViewModel.ExecuteDeleteInternalPoint();
                    }
                }
                break;

            case GeminiToolMode.AddInternalLine:
                if (!_pendingInternalLineStartMm.HasValue || !ReferenceEquals(_pendingInternalLinePiece, piece))
                {
                    _pendingInternalLinePiece = piece;
                    _pendingInternalLineStartMm = clickMm;
                    _pendingInternalLineCurrentMousePx = clickPx;
                    ViewModel.SelectDomainPiece(piece, nearestSeg >= 0 ? nearestSeg : 0);
                    ViewModel.StatusLeftText = $"Create internal line on {piece.Name}: Start ({clickMm.XCm:F1}, {clickMm.YCm:F1}) cm — Click end point inside piece...";
                    RenderInteractiveVectorOverlay();
                }
                else
                {
                    PhysicalPointMm startMm = _pendingInternalLineStartMm.Value;
                    _pendingInternalLineStartMm = null;
                    _pendingInternalLineCurrentMousePx = null;
                    _pendingInternalLinePiece = null;
                    ViewModel.SelectDomainPiece(piece, nearestSeg >= 0 ? nearestSeg : 0);
                    ViewModel.ExecuteAddInternalLine(startMm, clickMm);
                }
                break;

            case GeminiToolMode.AddInternalCurve:
                if (!_pendingInternalLineStartMm.HasValue || !ReferenceEquals(_pendingInternalLinePiece, piece))
                {
                    _pendingInternalLinePiece = piece;
                    _pendingInternalLineStartMm = clickMm;
                    _pendingInternalCurveControlMm = null;
                    ViewModel.SelectDomainPiece(piece, nearestSeg >= 0 ? nearestSeg : 0);
                    ViewModel.StatusLeftText = $"Internal curve on {piece.Name}: click a control point, then click the end point.";
                }
                else if (!_pendingInternalCurveControlMm.HasValue)
                {
                    _pendingInternalCurveControlMm = clickMm;
                    ViewModel.StatusLeftText = "Internal curve: click the end point.";
                }
                else
                {
                    ViewModel.EditingEngine.PushUndoSnapshot(ViewModel.DomainPieces, "Add Internal Curve");
                    InternalLineItem curve = ViewModel.EditingEngine.AddInternalCurve(piece, _pendingInternalLineStartMm.Value, _pendingInternalCurveControlMm.Value, clickMm);
                    ViewModel.SelectInternalLineOnPiece(piece, curve.Id);
                    _pendingInternalLineStartMm = null;
                    _pendingInternalCurveControlMm = null;
                    _pendingInternalLinePiece = null;
                    ViewModel.StatusLeftText = $"Added internal curve to {piece.Name}.";
                    ViewModel.RaiseGeometryChanged();
                }
                break;

            case GeminiToolMode.DeleteInternalLine:
                if (piece.InternalLines.Count > 0)
                {
                    InternalLineItem nearestIl = piece.InternalLines
                        .OrderBy(l => DistanceToInternalLineMm(l, clickMm))
                        .First();
                    if (DistanceToInternalLineMm(nearestIl, clickMm) <= 10.0)
                    {
                        ViewModel.SelectInternalLineOnPiece(piece, nearestIl.Id);
                        ViewModel.ExecuteDeleteInternalLine();
                    }
                }
                break;

            case GeminiToolMode.SmartCorner:
                ViewModel.ExecuteSmartCornerAt(piece, clickMm);
                break;
            case GeminiToolMode.SmartNotch:
                ViewModel.ExecuteSmartNotchAt(piece, clickMm);
                break;
            case GeminiToolMode.SmartInternalPoint:
                ViewModel.ExecuteSmartInternalPointAt(piece, clickMm);
                break;
            case GeminiToolMode.SmartInternalLine:
                ViewModel.ExecuteSmartInternalLineAt(piece, clickMm);
                break;

            case GeminiToolMode.MeasureDirect:
                HandleDirectMeasureClick(clickMm);
                break;

            case GeminiToolMode.MeasurePerimeter:
                // Snap to nearest vertex on piece contour
                if (piece.Vertices.Count >= 2)
                {
                    int closestV = 0;
                    double minVD = double.MaxValue;
                    for (int i = 0; i < piece.Vertices.Count; i++)
                    {
                        double d = piece.Vertices[i].PositionMm.DistanceTo(clickMm);
                        if (d < minVD)
                        {
                            minVD = d;
                            closestV = i;
                        }
                    }
                    OnVertexMouseLeftButtonDown(piece, closestV, e);
                }
                break;

            default:
                _showBezierHandlesForSelectedVertex = false;
                ViewModel.SelectDomainPiece(piece, nearestSeg >= 0 ? nearestSeg : 0);
                if (distMm > 15.0)
                {
                    // Drag whole piece when clicking inside piece interior
                    BeginObjectDrag($"Move {piece.Name}");
                    _isDraggingWholePiece = true;
                    _draggedPiece = piece;
                    _dragLastImagePixelPos = clickPx;
                    WorkspaceScrollViewer.CaptureMouse();
                }
                break;
        }
    }

    private bool TryAddPointOnContourDoubleClick(PatternPiece piece, PhysicalPointMm clickMm, Point clickPx)
    {
        if (ViewModel.ActiveTool != GeminiToolMode.SelectDefault)
        {
            return false;
        }

        (int nearestSegment, _, _, _) = ViewModel.GeometryEngine.FindNearestContourLocation(piece, clickMm);
        if (nearestSegment < 0)
        {
            return false;
        }

        ViewModel.ExecuteAddPointAtContourClick(piece, clickMm, isGradedCorner: false);
        _showBezierHandlesForSelectedVertex = true;
        ViewModel.StatusLeftText = $"Added editable point on {piece.Name}. Drag the point to refine the contour.";
        return true;
    }

    private bool TryAddPointAtCanvasDoubleClick(Point clickPx)
    {
        if (ViewModel.ActiveTool != GeminiToolMode.SelectDefault)
        {
            return false;
        }

        // Pieces created from a digitize frame carry their own calibration.
        // Converting a screen click through ActiveCalibration (the old code)
        // moves the query before the nearest-contour calculation, so a point
        // can appear visibly away from the double-click position.  Resolve
        // every candidate in its own image/physical coordinate system and
        // select by actual image-pixel distance.
        PatternPiece? piece = null;
        PhysicalPointMm clickMm = default;
        double bestDistancePx = double.MaxValue;
        foreach (PatternPiece candidate in ViewModel.DomainPieces.Where(candidate => candidate.Vertices.Count >= 2))
        {
            CalibrationProfile candidateCalibration = ViewModel.GetCalibrationForPiece(candidate);
            PhysicalPointMm candidateClickMm = candidateCalibration.PixelToPhysicalMm(new ImagePixelPoint(clickPx.X, clickPx.Y));
            (int segmentIndex, _, PhysicalPointMm closestMm, _) = ViewModel.GeometryEngine.FindNearestContourLocation(candidate, candidateClickMm);
            if (segmentIndex < 0)
            {
                continue;
            }

            ImagePixelPoint closestPx = candidateCalibration.PhysicalMmToPixel(closestMm);
            double dx = closestPx.X - clickPx.X;
            double dy = closestPx.Y - clickPx.Y;
            double distancePx = (dx * dx) + (dy * dy);
            if (distancePx < bestDistancePx)
            {
                bestDistancePx = distancePx;
                piece = candidate;
                clickMm = candidateClickMm;
            }
        }

        if (piece is null)
        {
            return false;
        }

        ViewModel.ExecuteAddPointAtContourClick(piece, clickMm, isGradedCorner: false);
        _showBezierHandlesForSelectedVertex = true;
        ViewModel.StatusLeftText = $"Added editable point to {piece.Name}. Drag the point to refine the contour.";
        return true;
    }

    private static double DistanceToInternalLineMm(InternalLineItem line, PhysicalPointMm point)
    {
        double dx = line.EndMm.XMm - line.StartMm.XMm;
        double dy = line.EndMm.YMm - line.StartMm.YMm;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 1e-12)
        {
            return line.StartMm.DistanceTo(point);
        }

        double projection = (((point.XMm - line.StartMm.XMm) * dx) + ((point.YMm - line.StartMm.YMm) * dy)) / lengthSquared;
        projection = Math.Clamp(projection, 0.0, 1.0);
        var nearest = new PhysicalPointMm(line.StartMm.XMm + (projection * dx), line.StartMm.YMm + (projection * dy));
        return nearest.DistanceTo(point);
    }

    private bool TryStartWholePieceDragWithControl(PatternPiece piece, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return false;
        }

        ViewModel.SelectDomainPiece(piece, ViewModel.SelectedVertexIndex);
        BeginObjectDrag($"Move {piece.Name}");
        _isDraggingWholePiece = true;
        _draggedPiece = piece;
        _dragLastImagePixelPos = e.GetPosition(RealImageCanvas);
        WorkspaceScrollViewer.CaptureMouse();
        ViewModel.StatusLeftText = $"Move {piece.Name}: drag while holding Ctrl.";
        return true;
    }

    private bool IsContourSegmentHit(PatternPiece piece, int segmentIndex, Point clickPx, CalibrationProfile cal)
    {
        CubicBezierSegment segment = piece.GetSegment(segmentIndex);
        const int samples = 32;
        double bestDistanceSquared = double.MaxValue;
        ImagePixelPoint previous = cal.PhysicalMmToPixel(segment.Evaluate(0.0));
        for (int i = 1; i <= samples; i++)
        {
            ImagePixelPoint current = cal.PhysicalMmToPixel(segment.Evaluate(i / (double)samples));
            double dx = current.X - previous.X;
            double dy = current.Y - previous.Y;
            double lengthSquared = (dx * dx) + (dy * dy);
            double parameter = lengthSquared <= 1e-12 ? 0.0 : Math.Clamp((((clickPx.X - previous.X) * dx) + ((clickPx.Y - previous.Y) * dy)) / lengthSquared, 0.0, 1.0);
            double nearestX = previous.X + (parameter * dx);
            double nearestY = previous.Y + (parameter * dy);
            double ddx = clickPx.X - nearestX;
            double ddy = clickPx.Y - nearestY;
            bestDistanceSquared = Math.Min(bestDistanceSquared, (ddx * ddx) + (ddy * ddy));
            previous = current;
        }

        // A stable screen-space hit zone: it remains usable at all zoom levels and
        // cannot select a distant segment merely because the filled piece was hit.
        double imageTolerancePx = 8.0 / Math.Max(0.15, ViewModel.ViewportEngine.ZoomScale);
        return bestDistanceSquared <= imageTolerancePx * imageTolerancePx;
    }

    private void BuildReferenceDemoDashedGrid()
    {
        DemoGridOverlayCanvas.Children.Clear();
        var redBrush = new SolidColorBrush(Color.FromRgb(220, 24, 24));
        redBrush.Freeze();

        var dashDouble = new DoubleCollection { 3, 2 };
        dashDouble.Freeze();

        for (int r = 0; r <= 8; r++)
        {
            double t = r / 8.0;
            double x1 = 84 - t * 6;
            double y1 = 74 + t * 342;
            double x2 = 576 + t * 8;
            double y2 = 82 + t * 336;

            var line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = redBrush,
                StrokeThickness = 1.0,
                StrokeDashArray = dashDouble
            };
            DemoGridOverlayCanvas.Children.Add(line);
        }

        for (int c = 0; c <= 11; c++)
        {
            double s = c / 11.0;
            double xTop = 84 + s * (576 - 84);
            double yTop = 74 + s * (82 - 74);
            double xBot = 78 + s * (584 - 78);
            double yBot = 416 + s * (418 - 416);

            var line = new Line
            {
                X1 = xTop,
                Y1 = yTop,
                X2 = xBot,
                Y2 = yBot,
                Stroke = redBrush,
                StrokeThickness = 1.0,
                StrokeDashArray = dashDouble
            };
            DemoGridOverlayCanvas.Children.Add(line);
        }
    }

    // =======================================================================
    // VIEWPORT & IMAGE ACTIONS (Phase 2.1 Engine Integration)
    // =======================================================================

    public void OpenImageFile(string filePath)
    {
        if (ViewModel.LoadImageFromFile(filePath))
        {
            UpdateCanvasVisibility();
            ZoomFit();
        }
    }

    private void LoadPicture_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load picture or project",
            Filter = "Supported Files (*.jpg;*.jpeg;*.bmp;*.png;*.pdp;*.gem)|*.jpg;*.jpeg;*.bmp;*.png;*.pdp;*.gem|Image files (*.jpg;*.jpeg;*.bmp;*.png)|*.jpg;*.jpeg;*.bmp;*.png|Project files (*.pdp;*.gem)|*.pdp;*.gem|All files (*.*)|*.*"
        };

        if (Directory.Exists(@"D:\pictures"))
        {
            dlg.InitialDirectory = @"D:\pictures";
        }

        if (dlg.ShowDialog(this) == true)
        {
            string ext = System.IO.Path.GetExtension(dlg.FileName);
            if (ext.Equals(".pdp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".gem", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.LoadProjectFromFile(dlg.FileName);
                UpdateCanvasVisibility();
            }
            else
            {
                OpenImageFile(dlg.FileName);
            }
        }
    }

    private void ReloadLiveV9Reference_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyLiveV9ReferenceState();
        ViewModel.ZoomPercent = 100.0;
        UpdateCanvasVisibility();
    }

    private void ReloadDemoReference_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyReferenceDemoState();
        ViewModel.ZoomPercent = 100.0;
        UpdateCanvasVisibility();
    }

    private void ReloadReferenceRecent_Click(object sender, RoutedEventArgs e)
    {
        string header = (sender as MenuItem)?.Header?.ToString() ?? string.Empty;
        string path = header.StartsWith(@"D:\LOREDANA", StringComparison.OrdinalIgnoreCase)
            ? @"D:\LOREDANA\foto digi\fotografii\Traian\Picture1.jpg"
            : @"D:\pictures\03172025 111128.jpg";

        if (!File.Exists(path))
        {
            ViewModel.StatusLeftText = $"Recent image is unavailable: {path}";
            return;
        }

        OpenImageFile(path);
    }

    private void ReloadRealSample1_Click(object sender, RoutedEventArgs e)
    {
        string? sample = ResolveReferenceAssetPath("real_01232026_093723.jpg") ?? @"D:\pictures\01232026 093723.jpg";
        if (File.Exists(sample))
        {
            OpenImageFile(sample);
            ViewModel.RunDetectPatterns();
        }
    }

    private void ReloadRealSample2_Click(object sender, RoutedEventArgs e)
    {
        string? sample = ResolveReferenceAssetPath("real_01082026_091623.jpg") ?? @"D:\pictures\01082026 091623.jpg";
        if (File.Exists(sample))
        {
            OpenImageFile(sample);
            ViewModel.RunDetectPatterns();
        }
    }

    public void ZoomIn()
    {
        ViewModel.ViewportEngine.ZoomIn();
        ViewModel.ZoomPercent = ViewModel.ViewportEngine.ZoomPercent;
        ViewModel.StatusLeftText = $"Zoom: {ViewModel.ZoomPercent:F0}%";
    }

    public void ZoomOut()
    {
        ViewModel.ViewportEngine.ZoomOut();
        ViewModel.ZoomPercent = ViewModel.ViewportEngine.ZoomPercent;
        ViewModel.StatusLeftText = $"Zoom: {ViewModel.ZoomPercent:F0}%";
    }

    public void ZoomFit()
    {
        double vw = Math.Max(100, WorkspaceScrollViewer.ViewportWidth - 8);
        double vh = Math.Max(100, WorkspaceScrollViewer.ViewportHeight - 8);

        if (!ViewModel.IsDemoReferenceLayoutActive && ViewModel.HasImageLoaded && ViewModel.LoadedBitmap is not null)
        {
            ViewModel.ViewportEngine.ZoomToFit(vw, vh, ViewModel.LoadedBitmap.PixelWidth, ViewModel.LoadedBitmap.PixelHeight);
            ViewModel.ZoomPercent = ViewModel.ViewportEngine.ZoomPercent;
        }
        else
        {
            ViewModel.ZoomPercent = 100.0;
        }

        WorkspaceScrollViewer.ScrollToHorizontalOffset(0);
        WorkspaceScrollViewer.ScrollToVerticalOffset(0);
    }

    private void ZoomAreaTool_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveTool = ViewModel.ActiveTool == GeminiToolMode.ZoomArea
            ? GeminiToolMode.SelectDefault
            : GeminiToolMode.ZoomArea;
        ViewModel.StatusLeftText = ViewModel.ActiveTool == GeminiToolMode.ZoomArea
            ? "Zoom to the selected area"
            : string.Empty;
    }

    private void ZoomToSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDomainPiece is not null && WorkspaceScrollViewer.ViewportWidth > 20)
        {
            Rect bboxMm = ViewModel.SelectedDomainPiece.BoundingBoxMm;
            CalibrationProfile pieceCalibration = ViewModel.GetCalibrationForPiece(ViewModel.SelectedDomainPiece);
            ImagePixelPoint p0 = pieceCalibration.PhysicalMmToPixel(new PhysicalPointMm(bboxMm.X, bboxMm.Y));
            ImagePixelPoint p1 = pieceCalibration.PhysicalMmToPixel(new PhysicalPointMm(bboxMm.Right, bboxMm.Bottom));
            var imgRect = new Rect(new Point(p0.X, p0.Y), new Point(p1.X, p1.Y));
            double padding = Math.Max(12.0, Math.Max(imgRect.Width, imgRect.Height) * 0.08);
            imgRect.Inflate(padding, padding);

            ViewModel.ViewportEngine.ZoomToImageRect(
                imgRect,
                WorkspaceScrollViewer.ViewportWidth,
                WorkspaceScrollViewer.ViewportHeight);
            ViewModel.ZoomPercent = ViewModel.ViewportEngine.ZoomPercent;
            WorkspaceScrollViewer.ScrollToHorizontalOffset(Math.Max(0, -ViewModel.ViewportEngine.PanOffsetX));
            WorkspaceScrollViewer.ScrollToVerticalOffset(Math.Max(0, -ViewModel.ViewportEngine.PanOffsetY));
            return;
        }

        ViewModel.ZoomPercent = Math.Clamp(ViewModel.ZoomPercent * 1.35, 15.0, 600.0);
    }

    private void ZoomToAll_Click(object sender, RoutedEventArgs e) => ZoomFit();

    private void ZoomLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el)
        {
            Point pt = e.GetPosition(el);
            if (pt.X < el.ActualWidth / 2)
            {
                ZoomOut();
            }
            else
            {
                ZoomIn();
            }
        }
    }

    private void ZoomTrackBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el)
        {
            ApplyTrackBarClick(e.GetPosition(el).X, el.ActualWidth);
            el.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ZoomTrackBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement el && el.IsMouseCaptured)
        {
            ApplyTrackBarClick(e.GetPosition(el).X, el.ActualWidth);
            e.Handled = true;
        }
    }

    private void ZoomTrackBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.IsMouseCaptured)
        {
            ApplyTrackBarClick(e.GetPosition(el).X, el.ActualWidth);
            el.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void ApplyTrackBarClick(double x, double width)
    {
        // Match the actual 52px track whose thumb travels from x=2 to x=48.
        // Keeping the pointer captured avoids jumps/stalls while dragging fast.
        double ratio = Math.Clamp((x - 2.0) / Math.Max(1.0, width - 6.0), 0.0, 1.0);
        ViewModel.ZoomSliderPosition = (int)Math.Round(ratio * 100.0);
        UpdateZoomThumbPosition();
    }

    private void UpdateZoomThumbPosition()
    {
        double norm = Math.Clamp((ViewModel.ZoomPercent - 20.0) / 160.0, 0.0, 1.0);
        Canvas.SetLeft(ZoomThumbBorder, 2.0 + norm * 46.0);
    }

    private void WorkspaceScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point cursorInViewport = e.GetPosition(WorkspaceScrollViewer);
        var vpCursor = new ViewportPoint(cursorInViewport.X, cursorInViewport.Y);

        ViewModel.ViewportEngine.SetPanOffset(-WorkspaceScrollViewer.HorizontalOffset, -WorkspaceScrollViewer.VerticalOffset);
        // WPF can report high-resolution wheel deltas.  Preserve their magnitude
        // so trackpad/wheel zoom is continuous instead of jumping one fixed step.
        double factor = Math.Pow(1.15, e.Delta / 120.0);
        ViewModel.ViewportEngine.ZoomAtCursor(factor, vpCursor);
        ViewModel.ZoomPercent = ViewModel.ViewportEngine.ZoomPercent;
        WorkspaceScrollViewer.UpdateLayout();
        WorkspaceScrollViewer.ScrollToHorizontalOffset(Math.Max(0, -ViewModel.ViewportEngine.PanOffsetX));
        WorkspaceScrollViewer.ScrollToVerticalOffset(Math.Max(0, -ViewModel.ViewportEngine.PanOffsetY));
        e.Handled = true;
    }

    private void WorkspaceScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        ViewModel.ViewportEngine.SetPanOffset(-e.HorizontalOffset, -e.VerticalOffset);
    }

    private bool IsAnyCanvasItemBeingDragged =>
        _isDraggingVertex ||
        _isDraggingBezierHandle ||
        _isDraggingWholePiece ||
        _isDraggingNotch ||
        _isDraggingInternalPoint ||
        _isDraggingInternalLine ||
        _isDraggingInternalLineEndpoint ||
        _isMovingDigitizeRoi ||
        _isResizingDigitizeRoi ||
        _isRotatingDigitizeRoi ||
        _isMovingModeBRoi ||
        _isResizingModeBRoi;

    private void WorkspaceScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left &&
            ViewModel.ActiveTool == GeminiToolMode.DigitizeRoi &&
            ViewModel.HasImageLoaded)
        {
            Point click = e.GetPosition(RealImageCanvas);
            CropBoxPixels frame = ViewModel.DigitizeRoiBoxPx;
            RotatedFramePixels rotatedFrame = ViewModel.DigitizeRoiFramePx;
            if (ViewModel.HasDigitizeRoi && rotatedFrame.IsValid)
            {
                IReadOnlyList<ImagePixelPoint> corners = rotatedFrame.Corners;
                Point top = Midpoint(corners[0], corners[1]);
                double a = rotatedFrame.RotationRadians;
                Point rotateHandle = new(top.X + (Math.Sin(a) * OverlayPixels(30)), top.Y - (Math.Cos(a) * OverlayPixels(30)));
                if ((click - rotateHandle).Length <= OverlayPixels(16))
                {
                    _isRotatingDigitizeRoi = true;
                    WorkspaceScrollViewer.Cursor = Cursors.Hand;
                }
                else if (rotatedFrame.Contains(click.X, click.Y))
                {
                    // Resizing remains available before rotation.  Once tilted,
                    // dragging inside the rigid frame translates it; only the
                    // cyan handle changes its angle.
                    _digitizeRoiResizeEdgeIndex = Math.Abs(ViewModel.DigitizeRoiRotationDegrees) < 1e-6
                        ? FindDigitizeRoiResizeEdge(frame, click) : -1;
                    _isResizingDigitizeRoi = _digitizeRoiResizeEdgeIndex >= 0;
                    _isMovingDigitizeRoi = !_isResizingDigitizeRoi;
                    _digitizeRoiDragInitialBox = frame;
                    _digitizeRoiDragStartPx = click;
                    WorkspaceScrollViewer.Cursor = _isResizingDigitizeRoi
                        ? (_digitizeRoiResizeEdgeIndex is 0 or 2 ? Cursors.SizeWE : Cursors.SizeNS)
                        : Cursors.SizeAll;
                }
                else
                {
                    _isDraggingDigitizeRoi = true;
                    _digitizeRoiStartPx = click;
                    _digitizeRoiCurrentPx = click;
                }
            }
            else
            {
                _isDraggingDigitizeRoi = true;
                _digitizeRoiStartPx = click;
                _digitizeRoiCurrentPx = click;
            }
            WorkspaceScrollViewer.CaptureMouse();
            RenderInteractiveVectorOverlay();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && ViewModel.ActiveTool == GeminiToolMode.ZoomArea)
        {
            if (ViewModel.IsDemoReferenceLayoutActive)
            {
                ViewModel.EnsureRealDomainPiecesReady();
                UpdateCanvasVisibility();
            }

            _isDraggingZoomAreaRect = true;
            _zoomAreaStartPx = e.GetPosition(RealImageCanvas);
            _zoomAreaCurrentPx = _zoomAreaStartPx;
            WorkspaceScrollViewer.CaptureMouse();
            RenderInteractiveVectorOverlay();
            e.Handled = true;
            return;
        }

        // Universal Pan: Middle Mouse Button, Pan Viewport Tool, or Alt/Space + Left Click.
        // Alt on a contour vertex is intentionally reserved for the precision
        // horizontal/vertical vertex drag below.
        bool altOnContourVertex = e.OriginalSource is FrameworkElement source &&
                                  source.ToolTip?.ToString()?.Contains("Point #", StringComparison.Ordinal) == true;
        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && ViewModel.ActiveTool == GeminiToolMode.PanViewport) ||
            (e.ChangedButton == MouseButton.Left && !altOnContourVertex &&
             ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 || Keyboard.IsKeyDown(Key.Space))))
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(WorkspaceScrollViewer);
            _panStartH = WorkspaceScrollViewer.HorizontalOffset;
            _panStartV = WorkspaceScrollViewer.VerticalOffset;
            WorkspaceScrollViewer.Cursor = Cursors.ScrollAll;
            WorkspaceScrollViewer.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Multi-Point Selection rubber-band in SelectDefault mode.  A drag may
        // start inside a pattern; only a direct hit on a vertex is reserved for
        // that vertex's click/drag handler.
        if (e.ChangedButton == MouseButton.Left &&
            ViewModel.ActiveTool == GeminiToolMode.SelectDefault &&
            !IsAnyCanvasItemBeingDragged)
        {
            Point clickPos = e.GetPosition(RealImageCanvas);
            bool clickedNearVertex = false;
            foreach (PatternPiece piece in ViewModel.DomainPieces)
            {
                foreach (ContourVertex vertex in piece.Vertices)
                {
                    ImagePixelPoint vertexPx = ViewModel.GetCalibrationForPiece(piece).PhysicalMmToPixel(vertex.PositionMm);
                    if (Math.Abs(vertexPx.X - clickPos.X) <= 8 && Math.Abs(vertexPx.Y - clickPos.Y) <= 8)
                    {
                        clickedNearVertex = true;
                        break;
                    }
                }
                if (clickedNearVertex) break;
            }

            if (!clickedNearVertex)
            {
                if (ViewModel.IsDemoReferenceLayoutActive)
                {
                    ViewModel.EnsureRealDomainPiecesReady();
                    UpdateCanvasVisibility();
                }

                _isDraggingSelectionRect = true;
                _selectionRectStartPx = clickPos;
                _selectionRectCurrentPx = _selectionRectStartPx;
                WorkspaceScrollViewer.CaptureMouse();
                RenderInteractiveVectorOverlay();
            }
        }
    }

    private void WorkspaceScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        Point posInContent = e.GetPosition(ViewportContentGrid);
        UpdateLiveMagnifier(posInContent);

        Point curImgPos = e.GetPosition(RealImageCanvas);

        if (_isDraggingZoomAreaRect && e.LeftButton == MouseButtonState.Pressed)
        {
            _zoomAreaCurrentPx = curImgPos;
            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isDraggingDigitizeRoi && e.LeftButton == MouseButtonState.Pressed)
        {
            _digitizeRoiCurrentPx = curImgPos;
            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isRotatingDigitizeRoi && e.LeftButton == MouseButtonState.Pressed)
        {
            RotatedFramePixels frame = ViewModel.DigitizeRoiFramePx;
            double angle = (Math.Atan2(curImgPos.Y - frame.CenterY, curImgPos.X - frame.CenterX) * 180.0 / Math.PI) + 90.0;
            if (!ViewModel.TryRotateDigitizeRoi(angle, out string rotationMessage))
            {
                ViewModel.StatusLeftText = rotationMessage;
            }
            RenderInteractiveVectorOverlay();
            return;
        }

        if ((_isMovingDigitizeRoi || _isResizingDigitizeRoi) && e.LeftButton == MouseButtonState.Pressed)
        {
            int imageWidth = ViewModel.CurrentRawImage?.Width ?? ViewModel.LoadedBitmap?.PixelWidth ?? 0;
            int imageHeight = ViewModel.CurrentRawImage?.Height ?? ViewModel.LoadedBitmap?.PixelHeight ?? 0;
            int dx = (int)Math.Round(curImgPos.X - _digitizeRoiDragStartPx.X);
            int dy = (int)Math.Round(curImgPos.Y - _digitizeRoiDragStartPx.Y);
            CropBoxPixels b = _digitizeRoiDragInitialBox;
            int x0 = b.StartX, y0 = b.StartY, x1 = b.StopX, y1 = b.StopY;
            if (_isMovingDigitizeRoi)
            {
                x0 = Math.Clamp(b.StartX + dx, 0, Math.Max(0, imageWidth - b.Width));
                y0 = Math.Clamp(b.StartY + dy, 0, Math.Max(0, imageHeight - b.Height));
                x1 = x0 + b.Width;
                y1 = y0 + b.Height;
            }
            else
            {
                switch (_digitizeRoiResizeEdgeIndex)
                {
                    case 0: x0 = Math.Clamp(b.StartX + dx, 0, x1 - 40); break;
                    case 1: y0 = Math.Clamp(b.StartY + dy, 0, y1 - 40); break;
                    case 2: x1 = Math.Clamp(b.StopX + dx, x0 + 40, imageWidth); break;
                    case 3: y1 = Math.Clamp(b.StopY + dy, y0 + 40, imageHeight); break;
                }
            }
            if (_isMovingDigitizeRoi && Math.Abs(ViewModel.DigitizeRoiRotationDegrees) > 1e-6)
            {
                ViewModel.TryMoveDigitizeRoi(x0 - b.StartX, y0 - b.StartY, out _);
            }
            else
            {
                ViewModel.TrySetDigitizeRoi(x0, y0, x1 - x0, y1 - y0, out _);
            }
            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isDraggingSelectionRect && e.LeftButton == MouseButtonState.Pressed)
        {
            _selectionRectCurrentPx = curImgPos;
            RenderInteractiveVectorOverlay();
            return;
        }

        if ((_isMovingModeBRoi || _isResizingModeBRoi) && e.LeftButton == MouseButtonState.Pressed)
        {
            int imgW = ViewModel.CurrentRawImage?.Width ?? ViewModel.LoadedBitmap?.PixelWidth ?? 3456;
            int imgH = ViewModel.CurrentRawImage?.Height ?? ViewModel.LoadedBitmap?.PixelHeight ?? 2592;
            int dx = (int)Math.Round(curImgPos.X - _modeBRoiDragStartPx.X);
            int dy = (int)Math.Round(curImgPos.Y - _modeBRoiDragStartPx.Y);
            CropBoxPixels b = _modeBRoiDragInitialBox;

            if (_isMovingModeBRoi)
            {
                int w = b.Width;
                int h = b.Height;
                int nx0 = Math.Clamp(b.StartX + dx, 0, Math.Max(0, imgW - w));
                int ny0 = Math.Clamp(b.StartY + dy, 0, Math.Max(0, imgH - h));
                ViewModel.ManualCalibrationBoxPx = new CropBoxPixels(nx0, ny0, nx0 + w, ny0 + h);
            }
            else if (_isResizingModeBRoi)
            {
                int x0 = b.StartX, y0 = b.StartY, x1 = b.StopX, y1 = b.StopY;
                switch (_modeBRoiResizeCornerIndex)
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
                ViewModel.ManualCalibrationBoxPx = new CropBoxPixels(x0, y0, x1, y1);
            }

            if (ViewModel.ActiveCalibration.Mode == CalibrationMode.ManualRectangleModeB)
            {
                ViewModel.ActiveCalibration.CropBox = ViewModel.ManualCalibrationBoxPx;
            }
            RenderInteractiveVectorOverlay();
            return;
        }

        if (ViewModel.ActiveTool == GeminiToolMode.AddInternalLine && _pendingInternalLineStartMm.HasValue)
        {
            _pendingInternalLineCurrentMousePx = curImgPos;
            RenderInteractiveVectorOverlay();
        }
        else if (ViewModel.ActiveTool == GeminiToolMode.MeasureDirect && _measureStartPointMm.HasValue && !_measureEndPointMm.HasValue)
        {
            _measureCurrentHoverPx = curImgPos;
            RenderInteractiveVectorOverlay();
        }

        if ((_isDraggingVertex || _isDraggingBezierHandle || _isDraggingWholePiece ||
             _isDraggingNotch || _isDraggingInternalPoint || _isDraggingInternalLine || _isDraggingInternalLineEndpoint) &&
            e.LeftButton == MouseButtonState.Pressed &&
            _draggedPiece is not null)
        {
            ImagePixelPoint previousPx = new(_dragLastImagePixelPos.X, _dragLastImagePixelPos.Y);
            _dragLastImagePixelPos = curImgPos;

            ImagePixelPoint currentPx = new(curImgPos.X, curImgPos.Y);
            CalibrationProfile dragCalibration = ViewModel.GetCalibrationForPiece(_draggedPiece);
            PhysicalPointMm dragDeltaMm = dragCalibration.PixelDragDeltaToPhysicalMm(previousPx, currentPx);
            double dxMm = dragDeltaMm.XMm;
            double dyMm = dragDeltaMm.YMm;
            PhysicalPointMm curMm = dragCalibration.PixelToPhysicalMm(new ImagePixelPoint(curImgPos.X, curImgPos.Y));

            if (_pendingDragUndoAction is not null && previousPx.DistanceTo(currentPx) > 0.0)
            {
                ViewModel.EditingEngine.PushUndoSnapshot(ViewModel.DomainPieces, _pendingDragUndoAction);
                _pendingDragUndoAction = null;
            }

            if (_isDraggingVertex)
            {
                bool altHeld = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                if (altHeld && _vertexDragAxisConstraint == VertexDragAxisConstraint.None)
                {
                    _vertexDragAxisConstraint = Math.Abs(currentPx.X - previousPx.X) >= Math.Abs(currentPx.Y - previousPx.Y)
                        ? VertexDragAxisConstraint.Horizontal
                        : VertexDragAxisConstraint.Vertical;
                }

                if (!altHeld)
                {
                    _vertexDragAxisConstraint = VertexDragAxisConstraint.None;
                }
                else if (_vertexDragAxisConstraint == VertexDragAxisConstraint.Horizontal)
                {
                    dyMm = 0.0;
                    ViewModel.StatusLeftText = "ALT precision drag: horizontal (Y locked).";
                }
                else if (_vertexDragAxisConstraint == VertexDragAxisConstraint.Vertical)
                {
                    dxMm = 0.0;
                    ViewModel.StatusLeftText = "ALT precision drag: vertical (X locked).";
                }

                ContourVertex vertex = _draggedPiece.Vertices[_draggedVertexOrSegmentIndex];
                PhysicalPointMm intended = new(vertex.PositionMm.XMm + dxMm, vertex.PositionMm.YMm + dyMm);
                // The digitize frame is a measurement reference, not a permanent
                // magnetic barrier.  A user must be able to position a contour
                // against the photographed edge freely; CTRL explicitly enables
                // precise snapping to the calibrated frame ruler.
                bool snappedX = false;
                bool snappedY = false;
                PhysicalPointMm snapped = intended;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    snapped = SnapVertexToLockedDigitizeFrame(
                        _draggedPiece,
                        intended,
                        _vertexDragAxisConstraint,
                        out snappedX,
                        out snappedY);
                }
                dxMm = snapped.XMm - vertex.PositionMm.XMm;
                dyMm = snapped.YMm - vertex.PositionMm.YMm;
                if (snappedX || snappedY)
                {
                    ViewModel.StatusLeftText = snappedX && snappedY
                        ? "Point snapped exactly to the locked frame corner."
                        : snappedX
                            ? "Point snapped exactly to the locked frame width boundary."
                            : "Point snapped exactly to the locked frame height boundary.";
                }

                ViewModel.EditingEngine.MoveVertexByDelta(
                    _draggedPiece,
                    _draggedVertexOrSegmentIndex,
                    dxMm,
                    dyMm,
                    dragCalibration);
            }
            else if (_isDraggingBezierHandle)
            {
                ViewModel.EditingEngine.MoveBezierControlHandle(
                    _draggedPiece,
                    _draggedVertexOrSegmentIndex,
                    _isDraggingStartHandleC1,
                    curMm);
            }
            else if (_isDraggingWholePiece)
            {
                ViewModel.EditingEngine.MovePieceByDelta(
                    _draggedPiece,
                    dxMm,
                    dyMm,
                    dragCalibration);
            }
            else if (_isDraggingNotch)
            {
                ViewModel.EditingEngine.MoveNotchAlongContour(_draggedPiece, _draggedInternalObjectId, curMm);
            }
            else if (_isDraggingInternalPoint)
            {
                ViewModel.EditingEngine.MoveInternalPointTo(_draggedPiece, _draggedInternalObjectId, curMm);
            }
            else if (_isDraggingInternalLine)
            {
                ViewModel.EditingEngine.MoveInternalLineByDelta(_draggedPiece, _draggedInternalObjectId, dxMm, dyMm);
            }
            else if (_isDraggingInternalLineEndpoint)
            {
                ViewModel.EditingEngine.MoveInternalLineEndpoint(
                    _draggedPiece,
                    _draggedInternalObjectId,
                    _isDraggingInternalLineStartEndpoint,
                    curMm);
            }

            ViewModel.RefreshSelectedPieceProperties();
            RenderInteractiveVectorOverlay();
            return;
        }

        if (!_isPanning || (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed))
        {
            return;
        }

        Point cur = e.GetPosition(WorkspaceScrollViewer);
        double panDx = cur.X - _panStartPoint.X;
        double panDy = cur.Y - _panStartPoint.Y;
        WorkspaceScrollViewer.ScrollToHorizontalOffset(_panStartH - panDx);
        WorkspaceScrollViewer.ScrollToVerticalOffset(_panStartV - panDy);
        ViewModel.ViewportEngine.SetPanOffset(-WorkspaceScrollViewer.HorizontalOffset, -WorkspaceScrollViewer.VerticalOffset);
    }

    private void WorkspaceScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {

        if (_isMovingDigitizeRoi || _isResizingDigitizeRoi || _isRotatingDigitizeRoi)
        {
            _isMovingDigitizeRoi = false;
            _isResizingDigitizeRoi = false;
            _isRotatingDigitizeRoi = false;
            _digitizeRoiResizeEdgeIndex = -1;
            WorkspaceScrollViewer.ReleaseMouseCapture();
            WorkspaceScrollViewer.Cursor = Cursors.Arrow;
            ViewModel.StatusLeftText = "Frame adjusted without distortion. Press Enter or File -> Enter digitize-frame dimensions when it is correct.";
            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isDraggingDigitizeRoi)
        {
            _isDraggingDigitizeRoi = false;
            WorkspaceScrollViewer.ReleaseMouseCapture();

            int imageWidth = ViewModel.CurrentRawImage?.Width ?? ViewModel.LoadedBitmap?.PixelWidth ?? 0;
            int imageHeight = ViewModel.CurrentRawImage?.Height ?? ViewModel.LoadedBitmap?.PixelHeight ?? 0;
            int x0 = Math.Clamp((int)Math.Round(Math.Min(_digitizeRoiStartPx.X, _digitizeRoiCurrentPx.X)), 0, imageWidth);
            int y0 = Math.Clamp((int)Math.Round(Math.Min(_digitizeRoiStartPx.Y, _digitizeRoiCurrentPx.Y)), 0, imageHeight);
            int x1 = Math.Clamp((int)Math.Round(Math.Max(_digitizeRoiStartPx.X, _digitizeRoiCurrentPx.X)), 0, imageWidth);
            int y1 = Math.Clamp((int)Math.Round(Math.Max(_digitizeRoiStartPx.Y, _digitizeRoiCurrentPx.Y)), 0, imageHeight);
            var frame = new CropBoxPixels(x0, y0, x1, y1);

            if (frame.Width >= 40 && frame.Height >= 40)
            {
                ViewModel.TrySetDigitizeRoi(frame.StartX, frame.StartY, frame.Width, frame.Height, out string message);
                ViewModel.StatusLeftText = $"{message} Drag inside to move; drag a red edge handle to resize without distortion. Press Enter when ready.";
            }
            else
            {
                ViewModel.StatusLeftText = "Digitize frame cancelled: drag a frame at least 40x40 pixels.";
            }

            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isDraggingSelectionRect)
        {
            _isDraggingSelectionRect = false;
            WorkspaceScrollViewer.ReleaseMouseCapture();

            double x0 = Math.Min(_selectionRectStartPx.X, _selectionRectCurrentPx.X);
            double y0 = Math.Min(_selectionRectStartPx.Y, _selectionRectCurrentPx.Y);
            double x1 = Math.Max(_selectionRectStartPx.X, _selectionRectCurrentPx.X);
            double y1 = Math.Max(_selectionRectStartPx.Y, _selectionRectCurrentPx.Y);
            var selRect = new Rect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));

            if (selRect.Width >= 4 && selRect.Height >= 4)
            {
                var targetPiece = ViewModel.SelectedDomainPiece ?? ViewModel.DomainPieces.FirstOrDefault();
                if (targetPiece is not null)
                {
                    var selectedIndices = new List<int>();
                    for (int i = 0; i < targetPiece.Vertices.Count; i++)
                    {
                    ImagePixelPoint px = ViewModel.GetCalibrationForPiece(targetPiece).PhysicalMmToPixel(targetPiece.Vertices[i].PositionMm);
                        if (selRect.Contains(new Point(px.X, px.Y)))
                        {
                            selectedIndices.Add(i);
                        }
                    }

                    if (selectedIndices.Count > 0)
                    {
                        ViewModel.SelectMultipleVertices(targetPiece, selectedIndices);
                    }
                    else
                    {
                        ViewModel.ClearVertexSelection();
                    }
                }
            }
            else
            {
                ViewModel.ClearVertexSelection();
            }

            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isDraggingZoomAreaRect)
        {
            _isDraggingZoomAreaRect = false;
            WorkspaceScrollViewer.ReleaseMouseCapture();
            double x = Math.Min(_zoomAreaStartPx.X, _zoomAreaCurrentPx.X);
            double y = Math.Min(_zoomAreaStartPx.Y, _zoomAreaCurrentPx.Y);
            double w = Math.Abs(_zoomAreaCurrentPx.X - _zoomAreaStartPx.X);
            double h = Math.Abs(_zoomAreaCurrentPx.Y - _zoomAreaStartPx.Y);

            if (w >= 12 && h >= 12)
            {
                double vw = Math.Max(100, WorkspaceScrollViewer.ViewportWidth);
                double vh = Math.Max(100, WorkspaceScrollViewer.ViewportHeight);
                ViewModel.ViewportEngine.ZoomToImageRect(new Rect(x, y, w, h), vw, vh);
                ViewModel.ZoomPercent = ViewModel.ViewportEngine.ZoomPercent;
                WorkspaceScrollViewer.UpdateLayout();
                WorkspaceScrollViewer.ScrollToHorizontalOffset(Math.Max(0, -ViewModel.ViewportEngine.PanOffsetX));
                WorkspaceScrollViewer.ScrollToVerticalOffset(Math.Max(0, -ViewModel.ViewportEngine.PanOffsetY));
                ViewModel.StatusLeftText = $"Zoomed to selected area ({w:F0}x{h:F0} px) -> Zoom: {ViewModel.ZoomPercent:F0}%";
            }
            else
            {
                ZoomIn();
            }

            RenderInteractiveVectorOverlay();
            return;
        }

        if (IsAnyCanvasItemBeingDragged)
        {
            _isDraggingVertex = false;
            _isDraggingBezierHandle = false;
            _isDraggingWholePiece = false;
            _isDraggingNotch = false;
            _isDraggingInternalPoint = false;
            _isDraggingInternalLine = false;
            _isDraggingInternalLineEndpoint = false;
            _vertexDragAxisConstraint = VertexDragAxisConstraint.None;
            _isMovingModeBRoi = false;
            _isResizingModeBRoi = false;
            _pendingDragUndoAction = null;
            WorkspaceScrollViewer.ReleaseMouseCapture();
            WorkspaceScrollViewer.Cursor = Cursors.Arrow;
            RenderInteractiveVectorOverlay();
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            WorkspaceScrollViewer.ReleaseMouseCapture();
            WorkspaceScrollViewer.Cursor = Cursors.Arrow;
        }
    }

    private void CanvasEmptySpace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.ActiveTool is GeminiToolMode.AddInternalLine or GeminiToolMode.AddInternalCurve or GeminiToolMode.AddInternalPoint)
        {
            Point clickPos = e.GetPosition(RealImageCanvas);
            // The canvas may receive a click through a transparent overlay
            // instead of the contour Path. Resolve each candidate in its own
            // calibration; ActiveCalibration is not valid for independently
            // calibrated digitize-frame pieces.
            PatternPiece? hitPiece = null;
            PhysicalPointMm hitMm = default;
            foreach (PatternPiece candidate in ViewModel.DomainPieces)
            {
                CalibrationProfile candidateCalibration = ViewModel.GetCalibrationForPiece(candidate);
                PhysicalPointMm candidateMm = candidateCalibration.PixelToPhysicalMm(new ImagePixelPoint(clickPos.X, clickPos.Y));
                if (ViewModel.GeometryEngine.IsPointInsidePiece(candidate, candidateMm))
                {
                    hitPiece = candidate;
                    hitMm = candidateMm;
                    break;
                }
            }

            if (hitPiece is not null)
            {
                HandlePieceOrContourClick(hitPiece, hitMm, clickPos, e);
                e.Handled = true;
                return;
            }

            ViewModel.StatusLeftText = "Click inside the selected pattern to place an internal element.";
            e.Handled = true;
            return;
        }

        if (!ViewModel.IsDemoReferenceLayoutActive && ViewModel.ActiveTool == GeminiToolMode.MeasureDirect)
        {
            Point clickPos = e.GetPosition(RealImageCanvas);
            PhysicalPointMm clickMm = ViewModel.ActiveCalibration.PixelToPhysicalMm(new ImagePixelPoint(clickPos.X, clickPos.Y));
            HandleDirectMeasureClick(clickMm);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            Point clickPos = e.GetPosition(RealImageCanvas);
            if (TryAddPointAtCanvasDoubleClick(clickPos))
            {
                e.Handled = true;
                return;
            }
            _showBezierHandlesForSelectedVertex = false;
            ViewModel.ClearPieceSelection();
        }
    }

    private void UpdateLiveMagnifier(Point posInContent)
    {
        if (ViewModel.IsDemoReferenceLayoutActive || !ViewModel.HasImageLoaded || ViewModel.LoadedBitmap is null)
        {
            return;
        }

        MagnifierIdleBoardImage.Visibility = Visibility.Collapsed;
        MagnifierDemoCornerCanvas.Visibility = Visibility.Collapsed;
        MagnifierLiveRect.Visibility = Visibility.Visible;

        double w = ViewModel.LoadedBitmap.PixelWidth;
        double h = ViewModel.LoadedBitmap.PixelHeight;
        double patchW = Math.Max(20, 160.0 / ViewModel.MagnifierZoomFactor);
        double patchH = Math.Max(20, 100.0 / ViewModel.MagnifierZoomFactor);

        Rect viewbox = new(
            Math.Clamp(posInContent.X - patchW / 2, 0, Math.Max(1, w - patchW)),
            Math.Clamp(posInContent.Y - patchH / 2, 0, Math.Max(1, h - patchH)),
            patchW,
            patchH);

        MagnifierLiveRect.Fill = new ImageBrush(ViewModel.LoadedBitmap)
        {
            Viewbox = viewbox,
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.UniformToFill
        };
    }

    private void MagnifierZoomIn_Click(object sender, RoutedEventArgs e) => ViewModel.MagnifierZoomFactor += 0.5;
    private void MagnifierZoomOut_Click(object sender, RoutedEventArgs e) => ViewModel.MagnifierZoomFactor -= 0.5;

    private void StockPieceCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DemoPieceCardItem piece })
        {
            bool wasDemoLayout = ViewModel.IsDemoReferenceLayoutActive;
            ViewModel.SelectDemoPiece(piece);
            if (wasDemoLayout && !ViewModel.IsDemoReferenceLayoutActive)
            {
                UpdateCanvasVisibility();
            }
            // A selected thumbnail is a request to inspect that exact pattern,
            // not the whole photo.  Run after any demo-to-workspace transition.
            Dispatcher.BeginInvoke(ZoomToSelected_Click, this, new RoutedEventArgs());
        }
    }

    private void ClosePropertiesDock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _showBezierHandlesForSelectedVertex = false;
        ViewModel.ClearPieceSelection();
    }

    // =======================================================================
    // TOOLBAR SPLIT-BUTTON DROPDOWNS
    // =======================================================================

    private void ToolbarLoadDropBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarLoadDropBtn.ContextMenu is not null)
        {
            ToolbarLoadDropBtn.ContextMenu.PlacementTarget = ToolbarLoadBtn;
            ToolbarLoadDropBtn.ContextMenu.IsOpen = true;
        }
    }

    private void ToolbarSaveDropBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarSaveDropBtn.ContextMenu is not null)
        {
            ToolbarSaveDropBtn.ContextMenu.PlacementTarget = ToolbarSaveBtn;
            ToolbarSaveDropBtn.ContextMenu.IsOpen = true;
        }
    }

    private void ToolbarShowDropBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarShowDropBtn.ContextMenu is not null)
        {
            ToolbarShowDropBtn.ContextMenu.PlacementTarget = ToolbarShowGridBtn;
            ToolbarShowDropBtn.ContextMenu.IsOpen = true;
        }
    }

    private void ToolbarPlayDropBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ToolbarPlayDropBtn.ContextMenu is not null)
        {
            ToolbarPlayDropBtn.ContextMenu.PlacementTarget = ToolbarPlayBtn;
            ToolbarPlayDropBtn.ContextMenu.IsOpen = true;
        }
    }

    // =======================================================================
    // PHASE 2 REAL DIGITIZER & EDITING TOOL HANDLERS
    // =======================================================================

    private void DetectPieces_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RunDetectPatterns();
        UpdateCanvasVisibility();
        ZoomFit();
    }

    private void DrawDigitizeFrame_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasImageLoaded)
        {
            ViewModel.StatusLeftText = "Load a photo before drawing a digitize frame.";
            return;
        }

        CancelActiveCanvasInteraction();
        ViewModel.ClearDigitizeRoi();
        ViewModel.ActiveTool = GeminiToolMode.DigitizeRoi;
        ViewModel.StatusLeftText = "Draw a red rectangle around the pattern. Then enter the rectangle's real dimensions in cm.";
        RenderInteractiveVectorOverlay();
    }

    private void EnterDigitizeFrameDimensions_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasDigitizeRoi || !ViewModel.DigitizeRoiBoxPx.IsValid)
        {
            ViewModel.StatusLeftText = "Draw a digitize frame before entering its dimensions.";
            return;
        }

        ShowDigitizeFrameDimensionsDialog();
    }

    private static int FindDigitizeRoiResizeEdge(CropBoxPixels frame, Point point)
    {
        const double edgeTolerance = 14;
        bool verticallyWithinFrame = point.Y >= frame.StartY - edgeTolerance && point.Y <= frame.StopY + edgeTolerance;
        bool horizontallyWithinFrame = point.X >= frame.StartX - edgeTolerance && point.X <= frame.StopX + edgeTolerance;
        if (verticallyWithinFrame && Math.Abs(point.X - frame.StartX) <= edgeTolerance) return 0;
        if (horizontallyWithinFrame && Math.Abs(point.Y - frame.StartY) <= edgeTolerance) return 1;
        if (verticallyWithinFrame && Math.Abs(point.X - frame.StopX) <= edgeTolerance) return 2;
        if (horizontallyWithinFrame && Math.Abs(point.Y - frame.StopY) <= edgeTolerance) return 3;
        return -1;
    }

    /// <summary>
    /// A locked digitize frame is the calibration ruler.  When CTRL is held,
    /// snapping a manually corrected vector point to its edge uses the exact
    /// image pixels of that ruler, not a rounded cm approximation.  Without
    /// CTRL callers do not invoke this method, so ordinary point placement is
    /// never constrained by the frame.
    /// </summary>
    private PhysicalPointMm SnapVertexToLockedDigitizeFrame(
        PatternPiece piece,
        PhysicalPointMm targetMm,
        VertexDragAxisConstraint axisConstraint,
        out bool snappedX,
        out bool snappedY)
    {
        snappedX = false;
        snappedY = false;
        if (!piece.HasLockedDigitizeFrame || !piece.LockedDigitizeFramePixels.IsValid)
        {
            return targetMm;
        }

        CalibrationProfile pieceCalibration = ViewModel.GetCalibrationForPiece(piece);
        ImagePixelPoint targetPx = pieceCalibration.PhysicalMmToPixel(targetMm);
        CropBoxPixels frame = piece.LockedDigitizeFramePixels;
        RotatedFramePixels rotatedFrame = RotatedFramePixels.FromCropBox(frame, piece.LockedDigitizeFrameRotationDegrees);
        const double snapTolerancePx = 10.0;

        double a = rotatedFrame.RotationRadians;
        double dx = targetPx.X - rotatedFrame.CenterX;
        double dy = targetPx.Y - rotatedFrame.CenterY;
        double localX = (dx * Math.Cos(a)) + (dy * Math.Sin(a));
        double localY = (-dx * Math.Sin(a)) + (dy * Math.Cos(a));
        double nearestX = localX < 0 ? -rotatedFrame.Width * .5 : rotatedFrame.Width * .5;
        double nearestY = localY < 0 ? -rotatedFrame.Height * .5 : rotatedFrame.Height * .5;
        if (axisConstraint != VertexDragAxisConstraint.Vertical && Math.Abs(localX - nearestX) <= snapTolerancePx)
        {
            localX = nearestX;
            snappedX = true;
        }
        if (axisConstraint != VertexDragAxisConstraint.Horizontal && Math.Abs(localY - nearestY) <= snapTolerancePx)
        {
            localY = nearestY;
            snappedY = true;
        }
        return snappedX || snappedY
            ? pieceCalibration.PixelToPhysicalMm(new ImagePixelPoint(
                rotatedFrame.CenterX + (localX * Math.Cos(a)) - (localY * Math.Sin(a)),
                rotatedFrame.CenterY + (localX * Math.Sin(a)) + (localY * Math.Cos(a))))
            : targetMm;
    }

    private void ShowDigitizeFrameDimensionsDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "Digitize frame",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            ShowInTaskbar = false
        };
        var panel = new Grid { Margin = new Thickness(16) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        for (int row = 0; row < 3; row++)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Enter the real size of the rectangle you just drew.\nThese centimetres become the calibration and DXF scale.",
            Margin = new Thickness(0, 0, 0, 10)
        });
        Grid.SetColumnSpan((UIElement)panel.Children[^1], 2);

        TextBox horizontalCm = AddFrameField(panel, 1, "Width — left to right (cm):", ViewModel.ActiveCalibration.ActiveAreaWidthCm.ToString("0.###", CultureInfo.InvariantCulture));
        TextBox verticalCm = AddFrameField(panel, 2, "Height — top to bottom (cm):", ViewModel.ActiveCalibration.ActiveAreaHeightCm.ToString("0.###", CultureInfo.InvariantCulture));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(buttons, 3);
        Grid.SetColumnSpan(buttons, 2);
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var applyButton = new Button { Content = "Apply calibration", MinWidth = 120, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        applyButton.Click += (_, _) =>
        {
            if (!TryParseCm(horizontalCm.Text, out double horizontal) || !TryParseCm(verticalCm.Text, out double vertical))
            {
                MessageBox.Show(dialog, "Enter positive dimensions in centimetres.", "Digitize frame", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ViewModel.TryCalibrateDigitizeRoi(horizontal, vertical, out string message))
            {
                MessageBox.Show(dialog, message, "Digitize frame", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ViewModel.StatusLeftText = $"{message} Click Detect pieces to trace this frame only.";
            dialog.DialogResult = true;
        };
        buttons.Children.Add(applyButton);
        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 72, IsCancel = true });
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.ShowDialog();
        RenderInteractiveVectorOverlay();
    }

    private static TextBox AddFrameField(Grid panel, int row, string label, string value)
    {
        var labelBlock = new TextBlock { Text = label, Margin = new Thickness(0, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(labelBlock, row);
        panel.Children.Add(labelBlock);
        var input = new TextBox { Text = value, Width = 125, Margin = new Thickness(0, 3, 0, 3) };
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        panel.Children.Add(input);
        return input;
    }

    private static bool TryParseCm(string value, out double parsed) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) && parsed > 0;

    private void ClearDigitizeFrame_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearDigitizeRoi();
        ViewModel.StatusLeftText = "Digitize frame cleared. Detect pieces will use the whole image.";
        RenderInteractiveVectorOverlay();
    }

    private void SaveGem_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save Pattern Digitizer Project",
            Filter = "Pattern Digitizer Project (*.pdp;*.gem)|*.pdp;*.gem|All files (*.*)|*.*",
            FileName = "PatternProject.pdp"
        };
        if (dlg.ShowDialog(this) == true)
        {
            ViewModel.SaveProjectToFile(dlg.FileName);
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => ViewModel.ExecuteUndo();
    private void Redo_Click(object sender, RoutedEventArgs e) => ViewModel.ExecuteRedo();

    private void EnsureInteractiveWorkspaceReadyForTool()
    {
        bool wasDemo = ViewModel.IsDemoReferenceLayoutActive;
        ViewModel.EnsureRealDomainPiecesReady();
        if (wasDemo && !ViewModel.IsDemoReferenceLayoutActive)
        {
            UpdateCanvasVisibility();
            ZoomFit();
        }
    }

    private void AddPoint_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.AddPoint;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Points;
        ViewModel.StatusLeftText = "Add point: Click anywhere on a pattern piece contour segment to split at that exact location.";
    }

    private void DeletePoint_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.DeletePoint;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Points;
        ViewModel.StatusLeftText = "Delete points: Click a vertex on a pattern piece contour to delete it.";
    }

    private void SmartCorner_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActivateSmartToolStatus(GeminiToolMode.SmartCorner, "Detect corner");
    }

    private void DeleteCorner_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.DeleteCorner;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Points;
        ViewModel.StatusLeftText = "Delete corner: Click a graded corner vertex on the contour to convert it to a curve point.";
    }

    private void LineToCurve_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.LineToCurve;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Points;
        ViewModel.StatusLeftText = "Transform line to curve: Click a straight contour segment on the canvas.";
    }

    private void CurveToLine_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.CurveToLine;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Points;
        ViewModel.StatusLeftText = "Transform curve to line: Click a curved contour segment on the canvas.";
    }

    private void SmartNotch_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActivateSmartToolStatus(GeminiToolMode.SmartNotch, "Detect notch");
    }

    private void AddNotch_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.AddNotch;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Notch;
        ViewModel.StatusLeftText = "Create new notch: Click on a pattern piece contour where you want to place the notch.";
    }

    private void DeleteNotch_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.DeleteNotch;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.Notch;
        if (ViewModel.SelectedNotchId.HasValue)
        {
            ViewModel.ExecuteDeleteNotch();
        }
        else
        {
            ViewModel.StatusLeftText = "Delete selected notch: Click a notch on the canvas to delete it.";
        }
    }

    private void SmartInternalPoint_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActivateSmartToolStatus(GeminiToolMode.SmartInternalPoint, "Detect internal point");
    }

    private void AddInternalPoint_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.AddInternalPoint;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.InternalPoint;
        ViewModel.StatusLeftText = "Create new internal point: Click inside a pattern piece to place a drill hole / internal point.";
    }

    private void DeleteInternalPoint_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.DeleteInternalPoint;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.InternalPoint;
        if (ViewModel.SelectedInternalPointId.HasValue)
        {
            ViewModel.ExecuteDeleteInternalPoint();
        }
        else
        {
            ViewModel.StatusLeftText = "Delete selected internal point: Click an internal point on the canvas to delete it.";
        }
    }

    private void SmartInternalLine_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActivateSmartToolStatus(GeminiToolMode.SmartInternalLine, "Detect internal line");
    }

    private void AddInternalLine_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        _pendingInternalLineStartMm = null;
        _pendingInternalLineCurrentMousePx = null;
        _pendingInternalLinePiece = null;
        ViewModel.ActiveTool = GeminiToolMode.AddInternalLine;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.InternalLine;
        ViewModel.StatusLeftText = "Create new internal line: Click start point inside a pattern piece, then click end point.";
        RenderInteractiveVectorOverlay();
    }

    private void AddInternalCurve_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveTool = GeminiToolMode.AddInternalCurve;
        ViewModel.StatusLeftText = "Create internal curve: click start, control point, then end point. Esc cancels.";
    }

    private void DeleteInternalLine_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.DeleteInternalLine;
        ViewModel.ActivePropertiesDockMode = PropertiesDockMode.InternalLine;
        if (ViewModel.SelectedInternalLineId.HasValue)
        {
            ViewModel.ExecuteDeleteInternalLine();
        }
        else
        {
            ViewModel.StatusLeftText = "Delete selected internal line: Click an internal line on the canvas to delete it.";
        }
    }

    private void AddAuxPiece_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ExecuteAddAuxiliaryPiece();
        UpdateCanvasVisibility();
    }

    private void DeleteAuxPiece_Click(object sender, RoutedEventArgs e) => ViewModel.ExecuteDeleteSelectedPiece();

    private void MeasurePerimeter_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.MeasurePerimeter;
        _measureStartVertexIndex = -1;
        _measureEndVertexIndex = -1;
        _measureStartPointMm = null;
        _measureEndPointMm = null;
        _isActiveMeasurementPerimeter = false;
        ViewModel.StatusLeftText = "Measure on perimeter: Click two vertices along a pattern piece contour.";
        RenderInteractiveVectorOverlay();
    }

    private void MeasureDirect_Click(object sender, RoutedEventArgs e)
    {
        EnsureInteractiveWorkspaceReadyForTool();
        ViewModel.ActiveTool = GeminiToolMode.MeasureDirect;
        _measureStartPointMm = null;
        _measureEndPointMm = null;
        _measureCurrentHoverPx = null;
        _isActiveMeasurementPerimeter = false;
        ViewModel.StatusLeftText = "Measure distance between two points: Click first point on canvas, then click second point.";
        RenderInteractiveVectorOverlay();
    }

    private void PlayContour_Click(object sender, RoutedEventArgs e) => ViewModel.StepPlayNextPoint(PropertiesDockMode.Points);
    private void PlayGradePoints_Click(object sender, RoutedEventArgs e) => ViewModel.StepPlayNextGradedPoint();
    private void PlayNotch_Click(object sender, RoutedEventArgs e) => ViewModel.StepPlayNextPoint(PropertiesDockMode.Notch);
    private void PlayInternalPoints_Click(object sender, RoutedEventArgs e) => ViewModel.StepPlayNextPoint(PropertiesDockMode.InternalPoint);
    private void PlayInternalLines_Click(object sender, RoutedEventArgs e) => ViewModel.StepPlayNextPoint(PropertiesDockMode.InternalLine);

    // =======================================================================
    // RIGHT DOCK PROPERTY PANEL APPLY HANDLERS
    // =======================================================================

    private void MovePointTo_Click(object sender, RoutedEventArgs e) => ViewModel.ExecuteMovePointToInputs();
    private void MovePointWith_Click(object sender, RoutedEventArgs e) => ViewModel.ExecuteMovePointWithDeltaInputs();

    private void ApplyNotch_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDomainPiece is null || !ViewModel.SelectedNotchId.HasValue)
        {
            return;
        }

        NotchItem? notch = ViewModel.SelectedDomainPiece.Notches.FirstOrDefault(n => n.Id == ViewModel.SelectedNotchId.Value);
        if (notch is null)
        {
            return;
        }

        if (!double.TryParse(ViewModel.NotchLength.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double lenCm) ||
            !double.TryParse(ViewModel.NotchAngle.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double angDeg))
        {
            ViewModel.StatusLeftText = "Notch update requires valid numeric length and angle.";
            return;
        }

        double lengthMm = Math.Max(1.0, lenCm * 10.0);
        bool changed = Math.Abs(notch.LengthMm - lengthMm) > 1e-9 ||
                       Math.Abs(notch.AngleDegrees - angDeg) > 1e-9 ||
                       notch.IsRelativeAngle != ViewModel.NotchRelativeAngle;
        if (!changed)
        {
            ViewModel.StatusLeftText = "Notch unchanged.";
            return;
        }

        ViewModel.EditingEngine.PushUndoSnapshot(ViewModel.DomainPieces, "Update Notch");
        notch.LengthMm = lengthMm;
        notch.AngleDegrees = angDeg;
        notch.IsRelativeAngle = ViewModel.NotchRelativeAngle;
        ViewModel.RefreshSelectedPieceProperties();
        ViewModel.RaiseGeometryChanged();
    }

    private void ApplyInternalPoint_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDomainPiece is null || !ViewModel.SelectedInternalPointId.HasValue)
        {
            return;
        }

        InternalPointItem? ip = ViewModel.SelectedDomainPiece.InternalPoints.FirstOrDefault(p => p.Id == ViewModel.SelectedInternalPointId.Value);
        if (ip is null)
        {
            return;
        }

        if (!double.TryParse(ViewModel.InternalPointX.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double xCm) ||
            !double.TryParse(ViewModel.InternalPointY.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double yCm) ||
            !double.TryParse(ViewModel.InternalPointLength.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double lenCm))
        {
            ViewModel.StatusLeftText = "Internal point update requires valid numeric X, Y and length.";
            return;
        }

        PhysicalPointMm position = PhysicalPointMm.FromCm(xCm, yCm);
        double lengthMm = Math.Max(1.0, lenCm * 10.0);
        if (ip.PositionMm.DistanceTo(position) <= 1e-9 && Math.Abs(ip.LengthMm - lengthMm) <= 1e-9)
        {
            ViewModel.StatusLeftText = "Internal point unchanged.";
            return;
        }

        ViewModel.EditingEngine.PushUndoSnapshot(ViewModel.DomainPieces, "Update Internal Point");
        ip.PositionMm = position;
        ip.LengthMm = lengthMm;
        ViewModel.RefreshSelectedPieceProperties();
        ViewModel.RaiseGeometryChanged();
    }

    private void ApplyInternalLine_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDomainPiece is null || !ViewModel.SelectedInternalLineId.HasValue)
        {
            return;
        }

        InternalLineItem? line = ViewModel.SelectedDomainPiece.InternalLines.FirstOrDefault(l => l.Id == ViewModel.SelectedInternalLineId.Value);
        if (line is null)
        {
            return;
        }

        if (!double.TryParse(ViewModel.InternalLineX.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double xCm) ||
            !double.TryParse(ViewModel.InternalLineY.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double yCm) ||
            !double.TryParse(ViewModel.InternalLineAngle.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double angleDeg) ||
            !double.TryParse(ViewModel.InternalLineLength.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double lenCm))
        {
            ViewModel.StatusLeftText = "Internal line update requires valid numeric X, Y, angle and length.";
            return;
        }

        PhysicalPointMm start = PhysicalPointMm.FromCm(xCm, yCm);
        double rad = angleDeg * Math.PI / 180.0;
        double lengthMm = Math.Max(1.0, lenCm * 10.0);
        PhysicalPointMm end = new(start.XMm + (Math.Cos(rad) * lengthMm), start.YMm + (Math.Sin(rad) * lengthMm));
        if (line.StartMm.DistanceTo(start) <= 1e-9 && line.EndMm.DistanceTo(end) <= 1e-9)
        {
            ViewModel.StatusLeftText = "Internal line unchanged.";
            return;
        }

        ViewModel.EditingEngine.PushUndoSnapshot(ViewModel.DomainPieces, "Update Internal Line");
        line.StartMm = start;
        line.EndMm = end;
        line.PolylinePointsMm = [start, end];
        ViewModel.RefreshSelectedPieceProperties();
        ViewModel.RaiseGeometryChanged();
    }

    private void FlipInternalLine_Click(object sender, RoutedEventArgs e) => ViewModel.ExecuteFlipInternalLine();

    // =======================================================================
    // DIALOG WINDOWS & SHORTCUTS
    // =======================================================================

    private void UnimplementedAcquire_Click(object sender, RoutedEventArgs e)
    {
        var camera = new CameraCaptureWindow(OpenImageFile) { Owner = this };
        camera.ShowDialog();
    }

    private void ExportDxf_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.EnsureRealDomainPiecesReady();
        // Do not route DXF export through the legacy Gemini-profile editor.
        // LookStailor exposes a direct DXF export command; this application
        // therefore asks only for the destination file and writes DXF-AAMA.
        var dlg = new SaveFileDialog
        {
            Title = "Export DXF-AAMA",
            Filter = "DXF files (*.dxf)|*.dxf|All files (*.*)|*.*",
            FileName = "PatternExport.dxf",
            AddExtension = true,
            DefaultExt = ".dxf"
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        AamaDxfExportResult result = ViewModel.ExportAamaDxfToFile(dlg.FileName);
        MessageBox.Show(
            this,
            $"DXF-AAMA exported.\n\n" +
            $"File: {result.DxfPath}\n" +
            $"Pieces: {result.PieceCount}\n" +
            $"Notches: {result.NotchCount}; Grain lines: {result.GrainLineCount}\n" +
            $"Internal lines: {result.InternalLineCount}; Drill points: {result.DrillPointCount}",
            "gemhihi — Export DXF-AAMA",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(initialTabIndex: 0, viewModel: ViewModel) { Owner = this };
        dlg.ShowDialog();
    }

    private void OpenCalibration_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CalibrationWindow(loadSampleStateB: false, viewModel: ViewModel) { Owner = this };
        dlg.ShowDialog();
    }

    private void OpenHelp_Click(object sender, RoutedEventArgs e) =>
        ViewModel.StatusLeftText = "Help — Gemini Photo Digitizer User Manual";

    private void WhatIsThis_Click(object sender, RoutedEventArgs e) =>
        ViewModel.StatusLeftText = "What is this ? (Shift+F1)";

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AboutWindow { Owner = this };
        dlg.ShowDialog();
    }

    private void OpenRegister_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RegisterProductWindow { Owner = this };
        dlg.ShowDialog();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel.ActiveTool == GeminiToolMode.DigitizeRoi &&
            ViewModel.HasDigitizeRoi && ViewModel.DigitizeRoiBoxPx.IsValid)
        {
            ShowDigitizeFrameDimensionsDialog();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            LoadPicture_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveGem_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            Undo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            Redo_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.G)
        {
            ViewModel.ShowGrid = !ViewModel.ShowGrid;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.H)
        {
            ViewModel.ShowPicture = !ViewModel.ShowPicture;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.N)
        {
            ViewModel.ShowNotches = !ViewModel.ShowNotches;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.L)
        {
            ViewModel.ShowInternalLines = !ViewModel.ShowInternalLines;
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.P)
        {
            ViewModel.ShowInternalPoints = !ViewModel.ShowInternalPoints;
            e.Handled = true;
        }
        else if (e.Key == Key.F2)
        {
            ZoomIn();
            e.Handled = true;
        }
        else if (e.Key == Key.F3)
        {
            ZoomOut();
            e.Handled = true;
        }
        else if (e.Key == Key.F4)
        {
            ZoomToSelected_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            ZoomFit();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            ViewModel.ExecuteDeleteCurrentSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelActiveCanvasInteraction();
            ViewModel.ActiveTool = GeminiToolMode.SelectDefault;
            ViewModel.ClearVertexSelection();
            ViewModel.StatusLeftText = "Command cancelled. Select tool active.";
            RenderInteractiveVectorOverlay();
            e.Handled = true;
        }
    }

    private void CancelActiveCanvasInteraction()
    {
        _isPanning = false;
        _isDraggingVertex = false;
        _isDraggingBezierHandle = false;
        _isDraggingWholePiece = false;
        _isDraggingNotch = false;
        _isDraggingInternalPoint = false;
        _isDraggingInternalLine = false;
        _isDraggingInternalLineEndpoint = false;
        _isMovingModeBRoi = false;
        _isResizingModeBRoi = false;
        _isDraggingSelectionRect = false;
        _isDraggingZoomAreaRect = false;
        _isDraggingDigitizeRoi = false;
        _isMovingDigitizeRoi = false;
        _isResizingDigitizeRoi = false;
        _isRotatingDigitizeRoi = false;
        _digitizeRoiResizeEdgeIndex = -1;
        _pendingDragUndoAction = null;
        _pendingInternalLineStartMm = null;
        _pendingInternalCurveControlMm = null;
        _pendingInternalLinePiece = null;
        _pendingInternalLineCurrentMousePx = null;
        _measureStartPointMm = null;
        _measureEndPointMm = null;
        _measureCurrentHoverPx = null;
        _measureStartVertexIndex = -1;
        _measureEndVertexIndex = -1;
        _measurePerimeterPiece = null;
        _isActiveMeasurementPerimeter = false;
        WorkspaceScrollViewer.ReleaseMouseCapture();
        WorkspaceScrollViewer.Cursor = Cursors.Arrow;
    }
}
