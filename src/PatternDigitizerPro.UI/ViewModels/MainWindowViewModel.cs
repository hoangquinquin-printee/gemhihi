using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.Core.Mvvm;

namespace PatternDigitizerPro.UI.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private string _windowTitle = "gemhihi";
    private string _loadedImagePath = string.Empty;
    private BitmapSource? _loadedBitmap;
    private RawImageBuffer? _currentRawImage;
    private bool _hasImageLoaded;
    private bool _isDemoReferenceLayoutActive;
    private bool _isV9ThreePieceDemoActive;
    private bool _isPieceSelected;
    private int _detectedPiecesCount;
    private string _pictureResolutionText = "Picture resolution: 0 x 0    Grid dimensions: 0cm x 0cm";
    private string _pieceName = string.Empty;
    private string _pieceAreaCm2 = string.Empty;
    private string _piecePerimeterCm = string.Empty;
    private string _notchesSummary = string.Empty;
    private string _cornersSummary = string.Empty;

    private bool _showGrid;
    private bool _showPicture = true;
    private bool _showNotches = true;
    private bool _showInternalLines = true;
    private bool _showInternalPoints = true;

    private GeminiToolMode _activeTool = GeminiToolMode.SelectDefault;
    private PropertiesDockMode _activePropertiesDockMode = PropertiesDockMode.NothingSelected;

    private double _zoomPercent = 100.0;
    private int _zoomSliderPosition = 50;
    private double _magnifierZoomFactor = 2.0;

    // TSelectedPointsPropertiesFrm fields
    private bool _isGradedPoint = true;
    private bool _isCurvePoint;
    private string _pointMoveToX = "25,4";
    private string _pointMoveToY = "-16,4";
    private string _pointMoveWithDx = "0";
    private string _pointMoveWithDy = "0";
    private string _tangentAngleEnding = "107,5";
    private string _tangentAngleStarting = "195,9";
    private string _segmentBezierLength = "0";
    private bool _isPieceOrigin;

    // TNotchPropertiesFrm fields
    private string _notchX = "142,0";
    private string _notchY = "85,0";
    private string _notchDistance = "45,0";
    private string _notchLength = "6,0";
    private string _notchAngle = "90,0";
    private bool _notchRelativeAngle = true;

    // TInternalPointPropertiesFrm fields
    private string _internalPointX = "120,0";
    private string _internalPointY = "95,0";
    private string _internalPointLength = "5,0";

    // TInternalLinePropertiesFrm fields
    private int _internalLineTypeIndex;
    private string _internalLineX = "80,0";
    private string _internalLineY = "60,0";
    private string _internalLineAngle = "0,0";
    private string _internalLineLength = "210,0";

    private string _statusLeftText = string.Empty;
    private string _statusRightText = string.Empty;

    private CalibrationProfile _activeCalibration;
    private PatternPiece? _selectedDomainPiece;
    private int _selectedVertexIndex = -1;
    private int _selectedSegmentIndex = -1;
    private Guid? _selectedNotchId;
    private Guid? _selectedInternalPointId;
    private Guid? _selectedInternalLineId;
    private bool _suppressPropertySync;

    // Mode B Manual X/Y Calibration state
    private bool _isManualCalibrationBoxVisible;
    private CropBoxPixels _manualCalibrationBoxPx = new(180, 140, 3276, 2452);
    private double _manualCalibrationWidthCm = 135.0;
    private double _manualCalibrationHeightCm = 100.0;
    private bool _restrictDetectionToManualBox;

    // A per-detection image-space frame.  This is deliberately separate from
    // the Mode B calibration rectangle: it scopes which paper piece is traced,
    // but must never alter the calibration or the resulting global geometry.
    private bool _hasDigitizeRoi;
    private bool _isDigitizeRoiCalibrated;
    private CropBoxPixels _digitizeRoiBoxPx;
    private double _digitizeRoiRotationDegrees;
    private CalibrationProfile? _digitizeFrameCalibration;

    public MainWindowViewModel()
    {
        ImageEngine = new ImageEngine();
        ViewportEngine = new ViewportEngine();
        CalibrationEngine = new CalibrationEngine();
        GeometryEngine = new GeometryEngine();
        MeasurementEngine = new MeasurementEngine();
        SmartFeatureEngine = new SmartFeatureEngine(GeometryEngine);
        DetectionEngine = new DetectionEngine(ImageEngine, GeometryEngine, MeasurementEngine);
        EditingEngine = new EditingEngine(GeometryEngine, MeasurementEngine);
        FileEngine = new FileEngine(MeasurementEngine);

        _activeCalibration = CalibrationProfile.CreateDefaultV9GridProfile(3456, 2592);

        DomainPieces = [];
        StockPieces = new ObservableCollection<DemoPieceCardItem>(GeminiReferenceDemoData.CreateLiveV9ReferencePieces());
        RecentFiles = new ObservableCollection<string>
        {
            @"D:\pictures\03172025 111128.jpg",
            @"D:\LOREDANA\foto digi\fotografii\Traian\Picture1.jpg",
            @"D:\pictures\01232026 093723.jpg",
            @"D:\pictures\01082026 091623.jpg"
        };

        // Production startup must not masquerade a hard-coded screenshot state
        // as a loaded/detected project. Reference states remain explicitly
        // available to the Phase 1 capture/inspector commands.
        ApplyCleanStartupOrImageState();
    }

    // =======================================================================
    // BACKEND ENGINES & DOMAIN STATE
    // =======================================================================

    public ImageEngine ImageEngine { get; }
    public ViewportEngine ViewportEngine { get; }
    public CalibrationEngine CalibrationEngine { get; }
    public GeometryEngine GeometryEngine { get; }
    public MeasurementEngine MeasurementEngine { get; }
    public SmartFeatureEngine SmartFeatureEngine { get; }
    public DetectionEngine DetectionEngine { get; }
    public EditingEngine EditingEngine { get; }
    public FileEngine FileEngine { get; }

    public ObservableCollection<PatternPiece> DomainPieces { get; }
    public ObservableCollection<DemoPieceCardItem> StockPieces { get; }
    public ObservableCollection<string> RecentFiles { get; }

    public event Action? GeometryChanged;

    public string ApplicationVersion
    {
        get
        {
            Version? version = Assembly.GetEntryAssembly()?.GetName().Version ??
                typeof(MainWindowViewModel).Assembly.GetName().Version;
            return $"gemhihi v{version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
        }
    }

    public RawImageBuffer? CurrentRawImage
    {
        get => _currentRawImage;
        private set => _currentRawImage = value;
    }

    public CalibrationProfile ActiveCalibration
    {
        get => _activeCalibration;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _activeCalibration = value;
            OnPropertyChanged();
            UpdateResolutionHeaderText();
            RaiseGeometryChanged();
        }
    }

    public PatternPiece? SelectedDomainPiece
    {
        get => _selectedDomainPiece;
        private set => SetProperty(ref _selectedDomainPiece, value);
    }

    public int SelectedVertexIndex
    {
        get => _selectedVertexIndex;
        private set => SetProperty(ref _selectedVertexIndex, value);
    }

    public int SelectedSegmentIndex
    {
        get => _selectedSegmentIndex;
        private set => SetProperty(ref _selectedSegmentIndex, value);
    }

    public ObservableCollection<int> SelectedVertexIndices { get; } = [];

    public Guid? SelectedNotchId => _selectedNotchId;
    public Guid? SelectedInternalPointId => _selectedInternalPointId;
    public Guid? SelectedInternalLineId => _selectedInternalLineId;

    public bool IsManualCalibrationBoxVisible
    {
        get => _isManualCalibrationBoxVisible;
        set
        {
            if (SetProperty(ref _isManualCalibrationBoxVisible, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public CropBoxPixels ManualCalibrationBoxPx
    {
        get => _manualCalibrationBoxPx;
        set
        {
            _manualCalibrationBoxPx = value;
            OnPropertyChanged();
            RaiseGeometryChanged();
        }
    }

    public double ManualCalibrationWidthCm
    {
        get => _manualCalibrationWidthCm;
        set => SetProperty(ref _manualCalibrationWidthCm, Math.Max(0.1, value));
    }

    public double ManualCalibrationHeightCm
    {
        get => _manualCalibrationHeightCm;
        set => SetProperty(ref _manualCalibrationHeightCm, Math.Max(0.1, value));
    }

    public bool RestrictDetectionToManualBox
    {
        get => _restrictDetectionToManualBox;
        set => SetProperty(ref _restrictDetectionToManualBox, value);
    }

    public bool HasDigitizeRoi
    {
        get => _hasDigitizeRoi;
        set
        {
            if (SetProperty(ref _hasDigitizeRoi, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public CropBoxPixels DigitizeRoiBoxPx
    {
        get => _digitizeRoiBoxPx;
        set
        {
            _digitizeRoiBoxPx = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DigitizeRoiFramePx));
            RaiseGeometryChanged();
        }
    }

    /// <summary>
    /// Rotation of the rigid digitize frame in image pixels.  The stored crop
    /// remains the frame's unrotated local dimensions; its bounding box is only
    /// used as a detector work area.
    /// </summary>
    public double DigitizeRoiRotationDegrees
    {
        get => _digitizeRoiRotationDegrees;
        private set
        {
            if (SetProperty(ref _digitizeRoiRotationDegrees, value))
            {
                IsDigitizeRoiCalibrated = false;
                RaiseGeometryChanged();
            }
        }
    }

    public RotatedFramePixels DigitizeRoiFramePx =>
        RotatedFramePixels.FromCropBox(DigitizeRoiBoxPx, DigitizeRoiRotationDegrees);

    public bool IsDigitizeRoiCalibrated
    {
        get => _isDigitizeRoiCalibrated;
        private set => SetProperty(ref _isDigitizeRoiCalibrated, value);
    }

    /// <summary>Temporary calibration for the frame currently being digitized; never the global calibration.</summary>
    public CalibrationProfile? DigitizeFrameCalibration => _digitizeFrameCalibration;

    public void ClearDigitizeRoi()
    {
        DigitizeRoiBoxPx = default;
        _digitizeRoiRotationDegrees = 0;
        OnPropertyChanged(nameof(DigitizeRoiRotationDegrees));
        OnPropertyChanged(nameof(DigitizeRoiFramePx));
        HasDigitizeRoi = false;
        IsDigitizeRoiCalibrated = false;
        _digitizeFrameCalibration = null;
    }

    /// <summary>
    /// Applies an explicitly entered digitize frame.  Values are image pixels,
    /// not viewport coordinates, so their meaning is unchanged by pan/zoom.
    /// Reject out-of-image input instead of silently clamping it: an entered
    /// measurement must describe exactly the frame the operator requested.
    /// </summary>
    public bool TrySetDigitizeRoi(int startX, int startY, int width, int height, out string validationMessage)
    {
        int imageWidth = CurrentRawImage?.Width ?? LoadedBitmap?.PixelWidth ?? 0;
        int imageHeight = CurrentRawImage?.Height ?? LoadedBitmap?.PixelHeight ?? 0;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            validationMessage = "Load a photo before setting a digitize frame.";
            return false;
        }

        if (width < 40 || height < 40)
        {
            validationMessage = "Digitize frame must be at least 40x40 pixels.";
            return false;
        }

        if (startX < 0 || startY < 0 || startX + width > imageWidth || startY + height > imageHeight)
        {
            validationMessage = $"Frame must stay inside the image (0..{imageWidth} x 0..{imageHeight} px).";
            return false;
        }

        DigitizeRoiBoxPx = new CropBoxPixels(startX, startY, startX + width, startY + height);
        _digitizeRoiRotationDegrees = 0;
        OnPropertyChanged(nameof(DigitizeRoiRotationDegrees));
        OnPropertyChanged(nameof(DigitizeRoiFramePx));
        HasDigitizeRoi = true;
        IsDigitizeRoiCalibrated = false;
        validationMessage = "Frame drawn. Enter its real horizontal and vertical dimensions in centimetres.";
        return true;
    }

    public bool TryRotateDigitizeRoi(double rotationDegrees, out string validationMessage)
    {
        if (!HasDigitizeRoi || !DigitizeRoiBoxPx.IsValid)
        {
            validationMessage = "Draw a digitize frame before rotating it.";
            return false;
        }

        int imageWidth = CurrentRawImage?.Width ?? LoadedBitmap?.PixelWidth ?? 0;
        int imageHeight = CurrentRawImage?.Height ?? LoadedBitmap?.PixelHeight ?? 0;
        double normalized = ((rotationDegrees + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        RotatedFramePixels candidate = RotatedFramePixels.FromCropBox(DigitizeRoiBoxPx, normalized);
        if (!candidate.IsInsideImage(imageWidth, imageHeight))
        {
            validationMessage = "Rotate frame only while all four corners remain inside the image.";
            return false;
        }

        DigitizeRoiRotationDegrees = normalized;
        OnPropertyChanged(nameof(DigitizeRoiFramePx));
        validationMessage = $"Frame rotation: {normalized:0.##} degrees.";
        return true;
    }

    /// <summary>Translates a rotated frame without discarding its rigid angle.</summary>
    public bool TryMoveDigitizeRoi(int deltaX, int deltaY, out string validationMessage)
    {
        if (!HasDigitizeRoi || !DigitizeRoiBoxPx.IsValid)
        {
            validationMessage = "Draw a digitize frame before moving it.";
            return false;
        }
        int imageWidth = CurrentRawImage?.Width ?? LoadedBitmap?.PixelWidth ?? 0;
        int imageHeight = CurrentRawImage?.Height ?? LoadedBitmap?.PixelHeight ?? 0;
        CropBoxPixels box = DigitizeRoiBoxPx;
        var candidateBox = new CropBoxPixels(box.StartX + deltaX, box.StartY + deltaY, box.StopX + deltaX, box.StopY + deltaY);
        if (!RotatedFramePixels.FromCropBox(candidateBox, DigitizeRoiRotationDegrees).IsInsideImage(imageWidth, imageHeight))
        {
            validationMessage = "Frame must stay inside the image.";
            return false;
        }
        DigitizeRoiBoxPx = candidateBox;
        IsDigitizeRoiCalibrated = false;
        validationMessage = "Frame moved.";
        return true;
    }


    /// <summary>
    /// Makes the just-drawn reference rectangle the physical calibration basis
    /// for this photograph. Pixel values remain internal only; the resulting
    /// pattern geometry and DXF use the dimensions explicitly supplied in cm.
    /// </summary>
    public bool TryCalibrateDigitizeRoi(double horizontalCm, double verticalCm, out string validationMessage)
    {
        if (!HasDigitizeRoi || !DigitizeRoiBoxPx.IsValid)
        {
            validationMessage = "Draw a digitize frame before entering its dimensions.";
            return false;
        }

        if (!double.IsFinite(horizontalCm) || !double.IsFinite(verticalCm) || horizontalCm <= 0 || verticalCm <= 0)
        {
            validationMessage = "Both physical dimensions must be greater than zero centimetres.";
            return false;
        }

        int imageWidth = CurrentRawImage?.Width ?? LoadedBitmap?.PixelWidth ?? 0;
        int imageHeight = CurrentRawImage?.Height ?? LoadedBitmap?.PixelHeight ?? 0;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            validationMessage = "Load a photo before calibrating the digitize frame.";
            return false;
        }

        _digitizeFrameCalibration = CalibrationEngine.CalibrateModeBManualRotatedRectangle(
            DigitizeRoiFramePx,
            horizontalCm,
            verticalCm,
            imageWidth,
            imageHeight,
            restrictDetectionToRoi: true);
        // A 2 x 2 cm lattice is retained as an internal frame reference only.
        // It is deliberately never rendered over the photograph; the detector
        // still traces original pixels rather than forcing the contour to cells.
        _digitizeFrameCalibration.CellWidthCm = 2.0;
        _digitizeFrameCalibration.CellHeightCm = 2.0;
        _digitizeFrameCalibration.GridColumns = Math.Max(1, (int)Math.Ceiling(horizontalCm / 2.0));
        _digitizeFrameCalibration.GridRows = Math.Max(1, (int)Math.Ceiling(verticalCm / 2.0));
        IsDigitizeRoiCalibrated = true;
        validationMessage = $"Frame calibrated separately: {horizontalCm:0.###} cm x {verticalCm:0.###} cm. Grid/camera calibration was not changed.";
        return true;
    }

    public CalibrationProfile GetCalibrationForPiece(PatternPiece? piece) =>
        piece?.DigitizeFrameCalibration ?? ActiveCalibration;

    // =======================================================================
    // BINDABLE UI PROPERTIES (PHASE 1 1:1 PARITY PRESERVED)
    // =======================================================================

    public string WindowTitle
    {
        get => _windowTitle;
        set => SetProperty(ref _windowTitle, value);
    }

    public string LoadedImagePath
    {
        get => _loadedImagePath;
        private set => SetProperty(ref _loadedImagePath, value);
    }

    public BitmapSource? LoadedBitmap
    {
        get => _loadedBitmap;
        private set => SetProperty(ref _loadedBitmap, value);
    }

    public bool HasImageLoaded
    {
        get => _hasImageLoaded;
        private set => SetProperty(ref _hasImageLoaded, value);
    }

    public bool IsDemoReferenceLayoutActive
    {
        get => _isDemoReferenceLayoutActive;
        set
        {
            if (SetProperty(ref _isDemoReferenceLayoutActive, value))
            {
                if (value)
                {
                    ApplyLiveV9ReferenceState();
                }
                else
                {
                    ApplyCleanStartupOrImageState();
                }
            }
        }
    }

    public bool IsV9ThreePieceDemoActive
    {
        get => _isV9ThreePieceDemoActive;
        set => SetProperty(ref _isV9ThreePieceDemoActive, value);
    }

    public bool IsPieceSelected
    {
        get => _isPieceSelected;
        set
        {
            if (SetProperty(ref _isPieceSelected, value))
            {
                OnPropertyChanged(nameof(IsNoPieceSelected));
            }
        }
    }

    public bool IsNoPieceSelected => !_isPieceSelected;

    public int DetectedPiecesCount
    {
        get => _detectedPiecesCount;
        set
        {
            if (SetProperty(ref _detectedPiecesCount, value))
            {
                OnPropertyChanged(nameof(DetectedPiecesText));
            }
        }
    }

    public string DetectedPiecesText => $"Detected patterns: {DetectedPiecesCount}";

    public string PictureResolutionText
    {
        get => _pictureResolutionText;
        set => SetProperty(ref _pictureResolutionText, value);
    }

    public string PieceName
    {
        get => _pieceName;
        set
        {
            if (SetProperty(ref _pieceName, value) && !_suppressPropertySync && SelectedDomainPiece is not null && !string.IsNullOrWhiteSpace(value))
            {
                SelectedDomainPiece.Name = value;
                SyncDomainPiecesToStockCards();
                RaiseGeometryChanged();
            }
        }
    }

    public string PieceAreaCm2
    {
        get => _pieceAreaCm2;
        set => SetProperty(ref _pieceAreaCm2, value);
    }

    public string PiecePerimeterCm
    {
        get => _piecePerimeterCm;
        set => SetProperty(ref _piecePerimeterCm, value);
    }

    public string NotchesSummary
    {
        get => _notchesSummary;
        set => SetProperty(ref _notchesSummary, value);
    }

    public string CornersSummary
    {
        get => _cornersSummary;
        set => SetProperty(ref _cornersSummary, value);
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetProperty(ref _showGrid, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public bool ShowPicture
    {
        get => _showPicture;
        set
        {
            if (SetProperty(ref _showPicture, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public bool ShowNotches
    {
        get => _showNotches;
        set
        {
            if (SetProperty(ref _showNotches, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public bool ShowInternalLines
    {
        get => _showInternalLines;
        set
        {
            if (SetProperty(ref _showInternalLines, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public bool ShowInternalPoints
    {
        get => _showInternalPoints;
        set
        {
            if (SetProperty(ref _showInternalPoints, value))
            {
                RaiseGeometryChanged();
            }
        }
    }

    public GeminiToolMode ActiveTool
    {
        get => _activeTool;
        set
        {
            if (SetProperty(ref _activeTool, value))
            {
                OnPropertyChanged(nameof(IsZoomAreaToolActive));
                OnPropertyChanged(nameof(IsSmartCornerActive));
                OnPropertyChanged(nameof(IsDeleteCornerActive));
                OnPropertyChanged(nameof(IsLineToCurveActive));
                OnPropertyChanged(nameof(IsCurveToLineActive));
                OnPropertyChanged(nameof(IsSmartNotchActive));
                OnPropertyChanged(nameof(IsAddNotchActive));
                OnPropertyChanged(nameof(IsDeleteNotchActive));
                OnPropertyChanged(nameof(IsSmartInternalPointActive));
                OnPropertyChanged(nameof(IsAddInternalPointActive));
                OnPropertyChanged(nameof(IsDeleteInternalPointActive));
                OnPropertyChanged(nameof(IsSmartInternalLineActive));
                OnPropertyChanged(nameof(IsAddInternalLineActive));
                OnPropertyChanged(nameof(IsDeleteInternalLineActive));
                OnPropertyChanged(nameof(IsMeasurePerimeterActive));
                OnPropertyChanged(nameof(IsMeasureDirectActive));
            }
        }
    }

    public bool IsZoomAreaToolActive => ActiveTool == GeminiToolMode.ZoomArea;
    public bool IsSmartCornerActive => ActiveTool == GeminiToolMode.SmartCorner;
    public bool IsDeleteCornerActive => ActiveTool == GeminiToolMode.DeleteCorner;
    public bool IsLineToCurveActive => ActiveTool == GeminiToolMode.LineToCurve;
    public bool IsCurveToLineActive => ActiveTool == GeminiToolMode.CurveToLine;
    public bool IsSmartNotchActive => ActiveTool == GeminiToolMode.SmartNotch;
    public bool IsAddNotchActive => ActiveTool == GeminiToolMode.AddNotch;
    public bool IsDeleteNotchActive => ActiveTool == GeminiToolMode.DeleteNotch;
    public bool IsSmartInternalPointActive => ActiveTool == GeminiToolMode.SmartInternalPoint;
    public bool IsAddInternalPointActive => ActiveTool == GeminiToolMode.AddInternalPoint;
    public bool IsDeleteInternalPointActive => ActiveTool == GeminiToolMode.DeleteInternalPoint;
    public bool IsSmartInternalLineActive => ActiveTool == GeminiToolMode.SmartInternalLine;
    public bool IsAddInternalLineActive => ActiveTool == GeminiToolMode.AddInternalLine;
    public bool IsDeleteInternalLineActive => ActiveTool == GeminiToolMode.DeleteInternalLine;
    public bool IsMeasurePerimeterActive => ActiveTool == GeminiToolMode.MeasurePerimeter;
    public bool IsMeasureDirectActive => ActiveTool == GeminiToolMode.MeasureDirect;

    public PropertiesDockMode ActivePropertiesDockMode
    {
        get => _activePropertiesDockMode;
        set
        {
            if (SetProperty(ref _activePropertiesDockMode, value))
            {
                OnPropertyChanged(nameof(IsNothingSelectedDockVisible));
                OnPropertyChanged(nameof(IsPointsDockVisible));
                OnPropertyChanged(nameof(IsNotchDockVisible));
                OnPropertyChanged(nameof(IsInternalPointDockVisible));
                OnPropertyChanged(nameof(IsInternalLineDockVisible));
            }
        }
    }

    public bool IsNothingSelectedDockVisible => ActivePropertiesDockMode == PropertiesDockMode.NothingSelected;
    public bool IsPointsDockVisible => ActivePropertiesDockMode == PropertiesDockMode.Points;
    public bool IsNotchDockVisible => ActivePropertiesDockMode == PropertiesDockMode.Notch;
    public bool IsInternalPointDockVisible => ActivePropertiesDockMode == PropertiesDockMode.InternalPoint;
    public bool IsInternalLineDockVisible => ActivePropertiesDockMode == PropertiesDockMode.InternalLine;

    public double ZoomPercent
    {
        get => _zoomPercent;
        set
        {
            double clamped = Math.Clamp(value, ViewportEngine.MinZoomPercent, ViewportEngine.MaxZoomPercent);
            if (SetProperty(ref _zoomPercent, clamped))
            {
                ViewportEngine.SetZoomPercent(clamped);
                OnPropertyChanged(nameof(ZoomScale));
            }
        }
    }

    public double ZoomScale => ZoomPercent / 100.0;

    public int ZoomSliderPosition
    {
        get => _zoomSliderPosition;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _zoomSliderPosition, clamped))
            {
                ZoomPercent = 100.0 + ((clamped - 50) * 1.6);
            }
        }
    }

    public double MagnifierZoomFactor
    {
        get => _magnifierZoomFactor;
        set => SetProperty(ref _magnifierZoomFactor, Math.Clamp(value, 1.0, 8.0));
    }

    public bool IsGradedPoint
    {
        get => _isGradedPoint;
        set
        {
            if (SetProperty(ref _isGradedPoint, value) && value)
            {
                IsCurvePoint = false;
                if (!_suppressPropertySync && SelectedDomainPiece is not null && SelectedVertexIndex >= 0)
                {
                    EditingEngine.PushUndoSnapshot(DomainPieces, "Set Graded Point");
                    EditingEngine.SetVertexGradedCorner(SelectedDomainPiece, SelectedVertexIndex, true);
                    RefreshSelectedPieceProperties();
                    RaiseGeometryChanged();
                }
            }
        }
    }

    public bool IsCurvePoint
    {
        get => _isCurvePoint;
        set
        {
            if (SetProperty(ref _isCurvePoint, value) && value)
            {
                IsGradedPoint = false;
                if (!_suppressPropertySync && SelectedDomainPiece is not null && SelectedVertexIndex >= 0)
                {
                    EditingEngine.PushUndoSnapshot(DomainPieces, "Set Curve Point");
                    EditingEngine.SetVertexGradedCorner(SelectedDomainPiece, SelectedVertexIndex, false);
                    RefreshSelectedPieceProperties();
                    RaiseGeometryChanged();
                }
            }
        }
    }

    public string PointMoveToX
    {
        get => _pointMoveToX;
        set => SetProperty(ref _pointMoveToX, value);
    }

    public string PointMoveToY
    {
        get => _pointMoveToY;
        set => SetProperty(ref _pointMoveToY, value);
    }

    public string PointMoveWithDx
    {
        get => _pointMoveWithDx;
        set => SetProperty(ref _pointMoveWithDx, value);
    }

    public string PointMoveWithDy
    {
        get => _pointMoveWithDy;
        set => SetProperty(ref _pointMoveWithDy, value);
    }

    public string TangentAngleEnding
    {
        get => _tangentAngleEnding;
        set => SetProperty(ref _tangentAngleEnding, value);
    }

    public string TangentAngleStarting
    {
        get => _tangentAngleStarting;
        set => SetProperty(ref _tangentAngleStarting, value);
    }

    public string SegmentBezierLength
    {
        get => _segmentBezierLength;
        set => SetProperty(ref _segmentBezierLength, value);
    }

    public bool IsPieceOrigin
    {
        get => _isPieceOrigin;
        set
        {
            if (SetProperty(ref _isPieceOrigin, value) && !_suppressPropertySync && SelectedDomainPiece is not null && SelectedVertexIndex >= 0 && value)
            {
                EditingEngine.PushUndoSnapshot(DomainPieces, "Set Piece Origin");
                EditingEngine.SetPieceOrigin(SelectedDomainPiece, SelectedVertexIndex);
                RaiseGeometryChanged();
            }
        }
    }

    public string NotchX
    {
        get => _notchX;
        set => SetProperty(ref _notchX, value);
    }

    public string NotchY
    {
        get => _notchY;
        set => SetProperty(ref _notchY, value);
    }

    public string NotchDistance
    {
        get => _notchDistance;
        set => SetProperty(ref _notchDistance, value);
    }

    public string NotchLength
    {
        get => _notchLength;
        set => SetProperty(ref _notchLength, value);
    }

    public string NotchAngle
    {
        get => _notchAngle;
        set => SetProperty(ref _notchAngle, value);
    }

    public bool NotchRelativeAngle
    {
        get => _notchRelativeAngle;
        set => SetProperty(ref _notchRelativeAngle, value);
    }

    public string InternalPointX
    {
        get => _internalPointX;
        set => SetProperty(ref _internalPointX, value);
    }

    public string InternalPointY
    {
        get => _internalPointY;
        set => SetProperty(ref _internalPointY, value);
    }

    public string InternalPointLength
    {
        get => _internalPointLength;
        set => SetProperty(ref _internalPointLength, value);
    }

    public int InternalLineTypeIndex
    {
        get => _internalLineTypeIndex;
        set
        {
            if (SetProperty(ref _internalLineTypeIndex, value) && !_suppressPropertySync && SelectedDomainPiece is not null && _selectedInternalLineId.HasValue)
            {
                InternalLineItem? line = SelectedDomainPiece.InternalLines.FirstOrDefault(l => l.Id == _selectedInternalLineId.Value);
                if (line is not null)
                {
                    EditingEngine.PushUndoSnapshot(DomainPieces, "Change Internal Line Type");
                    line.LineType = value switch
                    {
                        1 => InternalLineType.CuttingLine,
                        2 => InternalLineType.GrainAxis,
                        3 => InternalLineType.SymmetryAxis,
                        _ => InternalLineType.SimpleLine
                    };
                    RefreshSelectedPieceProperties();
                    RaiseGeometryChanged();
                }
            }
        }
    }

    public string InternalLineX
    {
        get => _internalLineX;
        set => SetProperty(ref _internalLineX, value);
    }

    public string InternalLineY
    {
        get => _internalLineY;
        set => SetProperty(ref _internalLineY, value);
    }

    public string InternalLineAngle
    {
        get => _internalLineAngle;
        set => SetProperty(ref _internalLineAngle, value);
    }

    public string InternalLineLength
    {
        get => _internalLineLength;
        set => SetProperty(ref _internalLineLength, value);
    }

    public string StatusLeftText
    {
        get => _statusLeftText;
        set => SetProperty(ref _statusLeftText, value);
    }

    public string StatusRightText
    {
        get => _statusRightText;
        set => SetProperty(ref _statusRightText, value);
    }

    // =======================================================================
    // REFERENCE DEMO STATES (REQUIRED BY PHASE 1 PASS 2 VISUAL PARITY TESTS)
    // =======================================================================

    public void ApplyLiveV9ReferenceState()
    {
        _isDemoReferenceLayoutActive = true;
        _isV9ThreePieceDemoActive = true;
        OnPropertyChanged(nameof(IsDemoReferenceLayoutActive));
        OnPropertyChanged(nameof(IsV9ThreePieceDemoActive));

        WindowTitle = @"gemhihi - [D:\pictures\03172025 111128.jpg]";
        DetectedPiecesCount = 3;
        PictureResolutionText = "Picture resolution: 2918 x 2214    Grid dimensions: 135cm x 100cm";
        IsPieceSelected = false;
        PieceName = string.Empty;
        PieceAreaCm2 = string.Empty;
        PiecePerimeterCm = string.Empty;
        NotchesSummary = string.Empty;
        CornersSummary = string.Empty;
        ShowGrid = false;
        ActivePropertiesDockMode = PropertiesDockMode.NothingSelected;
        StatusLeftText = string.Empty;
        StatusRightText = "Done detecting patterns!";

        StockPieces.Clear();
        foreach (var p in GeminiReferenceDemoData.CreateLiveV9ReferencePieces())
        {
            StockPieces.Add(p);
        }
    }

    public void ApplyReferenceDemoState()
    {
        _isDemoReferenceLayoutActive = true;
        _isV9ThreePieceDemoActive = false;
        OnPropertyChanged(nameof(IsDemoReferenceLayoutActive));
        OnPropertyChanged(nameof(IsV9ThreePieceDemoActive));

        WindowTitle = @"gemhihi - [D:\LOREDANA\foto digi\fotografii\Traian\Picture1.jpg]";
        DetectedPiecesCount = 4;
        PictureResolutionText = "Picture resolution: 2592 x 1944    Grid dimensions: 110cm x 80cm";
        IsPieceSelected = true;
        _suppressPropertySync = true;
        PieceName = "Piece 3";
        PieceAreaCm2 = "1213,75";
        PiecePerimeterCm = "167,43";
        NotchesSummary = "6 / 0 / 0";
        CornersSummary = "5 / 5";
        ShowGrid = true;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        IsGradedPoint = true;
        IsCurvePoint = false;
        PointMoveToX = "25,4";
        PointMoveToY = "-16,4";
        PointMoveWithDx = "0";
        PointMoveWithDy = "0";
        TangentAngleEnding = "107,5";
        TangentAngleStarting = "195,9";
        SegmentBezierLength = "0";
        IsPieceOrigin = false;
        _suppressPropertySync = false;
        StatusLeftText = string.Empty;
        StatusRightText = "Done detecting patterns!";

        StockPieces.Clear();
        foreach (var p in GeminiReferenceDemoData.CreateReferencePieces())
        {
            StockPieces.Add(p);
        }
    }

    public void ApplyCleanStartupOrImageState()
    {
        _isDemoReferenceLayoutActive = false;
        _isV9ThreePieceDemoActive = false;
        OnPropertyChanged(nameof(IsDemoReferenceLayoutActive));
        OnPropertyChanged(nameof(IsV9ThreePieceDemoActive));

        DomainPieces.Clear();
        SelectedDomainPiece = null;
        SelectedVertexIndex = -1;
        SelectedSegmentIndex = -1;
        EditingEngine.ClearHistory();

        if (!HasImageLoaded || LoadedBitmap is null)
        {
        WindowTitle = "gemhihi";
            DetectedPiecesCount = 0;
            PictureResolutionText = "Picture resolution: 0 x 0    Grid dimensions: 0cm x 0cm";
            IsPieceSelected = false;
            PieceName = string.Empty;
            PieceAreaCm2 = string.Empty;
            PiecePerimeterCm = string.Empty;
            NotchesSummary = string.Empty;
            CornersSummary = string.Empty;
            ActivePropertiesDockMode = PropertiesDockMode.NothingSelected;
            StockPieces.Clear();
            StatusLeftText = string.Empty;
            StatusRightText = string.Empty;
        }
        else
        {
            WindowTitle = $"gemhihi - [{LoadedImagePath}]";
            DetectedPiecesCount = 0;
            UpdateResolutionHeaderText();
            IsPieceSelected = false;
            PieceName = string.Empty;
            PieceAreaCm2 = string.Empty;
            PiecePerimeterCm = string.Empty;
            NotchesSummary = string.Empty;
            CornersSummary = string.Empty;
            ActivePropertiesDockMode = PropertiesDockMode.NothingSelected;
            StockPieces.Clear();
            StatusLeftText = $"Loaded image ({LoadedBitmap.PixelWidth}x{LoadedBitmap.PixelHeight} px)";
            StatusRightText = "Done detecting patterns!";
        }

        RaiseGeometryChanged();
    }

    // =======================================================================
    // REAL PHASE 2 BACKEND OPERATIONS
    // =======================================================================

    public bool LoadImageFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        RawImageBuffer raw = ImageEngine.LoadFromFile(filePath);
        CurrentRawImage = raw;
        LoadedBitmap = raw.ToBitmapSource();
        LoadedImagePath = filePath;
        HasImageLoaded = true;
        ClearDigitizeRoi();

        if (!RecentFiles.Contains(filePath))
        {
            RecentFiles.Insert(0, filePath);
        }

        ApplyCleanStartupOrImageState();
        return true;
    }

    /// <summary>
    /// Runs the real 8-stage Pattern Detection Engine on the image explicitly loaded by the user.
    /// </summary>
    public DetectionPipelineResult? RunDetectPatterns(string? debugOutputDir = null)
    {
        if (CurrentRawImage is null)
        {
            StatusLeftText = "No image loaded to detect patterns.";
            return null;
        }

        _isDemoReferenceLayoutActive = false;
        _isV9ThreePieceDemoActive = false;
        OnPropertyChanged(nameof(IsDemoReferenceLayoutActive));
        OnPropertyChanged(nameof(IsV9ThreePieceDemoActive));

        // Frame calibration is explicitly separate from the global grid/camera
        // calibration and applies only to the one selected detail.
        CalibrationProfile detectionCalibration = ActiveCalibration;
        bool useDigitizeRoi = HasDigitizeRoi && DigitizeRoiBoxPx.IsValid;
        if (useDigitizeRoi && !IsDigitizeRoiCalibrated)
        {
            StatusLeftText = "Enter the digitize-frame dimensions in centimetres before Detect pieces.";
            return null;
        }
        if (useDigitizeRoi)
        {
            if (_digitizeFrameCalibration is null)
            {
                StatusLeftText = "Digitize frame calibration is missing. Enter its dimensions again.";
                return null;
            }
            detectionCalibration = _digitizeFrameCalibration;
        }

        DetectionPipelineResult result = DetectionEngine.DetectPatterns(CurrentRawImage, detectionCalibration, debugOutputDir);

        PatternPiece? addedFramePiece = null;
        if (useDigitizeRoi)
        {
            // A frame is an operator's request to digitize one detail, not an
            // instruction to re-digitize every piece in the photograph.  The
            // detector can return small secondary islands, so retain only the
            // largest closed contour from this deliberately selected region.
            PatternPiece? detectedPiece = result.Pieces
                .OrderByDescending(piece => piece.AreaMm2)
                .FirstOrDefault();

            if (detectedPiece is not null)
            {
                // The manually drawn rectangle is the calibration reference.
                // Do not rescale a noisy detected contour to pretend it fills
                // that rectangle: the operator must be able to correct the
                // contour against the original photograph without changing the
                // physical scale of the frame.
                double targetWidthCm = detectionCalibration.ActiveAreaWidthCm;
                double targetHeightCm = detectionCalibration.ActiveAreaHeightCm;
                EditingEngine.PushUndoSnapshot(DomainPieces, "Digitize framed piece");
                detectedPiece.Index = DomainPieces.Count + 1;
                detectedPiece.Name = $"Piece {detectedPiece.Index}";
                detectedPiece.LockedDigitizeFramePixels = DigitizeRoiBoxPx;
                detectedPiece.LockedDigitizeFrameRotationDegrees = DigitizeRoiRotationDegrees;
                detectedPiece.LockedDigitizeFrameWidthCm = targetWidthCm;
                detectedPiece.LockedDigitizeFrameHeightCm = targetHeightCm;
                detectedPiece.HasLockedDigitizeFrame = true;
                detectedPiece.DigitizeFrameCalibration = detectionCalibration.Clone();
                DomainPieces.Add(detectedPiece);
                addedFramePiece = detectedPiece;

                // The editable ROI is now complete.  The separate immutable
                // per-piece metric frame stays visible as a measurement guide.
                ClearDigitizeRoi();
                ActiveTool = GeminiToolMode.SelectDefault;
            }
        }
        else
        {
            // Whole-image detection intentionally remains a fresh operation.
            EditingEngine.ClearHistory();
            DomainPieces.Clear();
            foreach (PatternPiece piece in result.Pieces)
            {
                DomainPieces.Add(piece);
            }
        }

        DetectedPiecesCount = DomainPieces.Count;
        UpdateResolutionHeaderText();
        SyncDomainPiecesToStockCards();
        if (addedFramePiece is not null)
        {
            SelectDomainPiece(addedFramePiece, 0);
        }
        else if (!useDigitizeRoi)
        {
            ClearPieceSelection();
        }

        StatusLeftText = useDigitizeRoi
            ? addedFramePiece is not null
                ? $"Digitized '{addedFramePiece.Name}' using fixed frame scale {addedFramePiece.LockedDigitizeFrameWidthCm:0.###} cm x {addedFramePiece.LockedDigitizeFrameHeightCm:0.###} cm. Project now has {DomainPieces.Count} pattern(s) | Rejected {result.RejectedComponents.Count} noise/magnet/glare region(s)"
                : $"No pattern found in the digitize frame. Existing {DomainPieces.Count} pattern(s) were kept."
            : $"Detected {DomainPieces.Count} pattern(s) | Rejected {result.RejectedComponents.Count} noise/magnet/glare region(s)";
        StatusRightText = "Done detecting patterns!";
        RaiseGeometryChanged();
        return result;
    }

    public void SelectDomainPiece(PatternPiece piece, int vertexIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(piece);
        SelectedDomainPiece = piece;
        SelectedVertexIndices.Clear();

        if (piece.Vertices.Count > 0)
        {
            SelectedVertexIndex = Math.Clamp(vertexIndex, 0, piece.Vertices.Count - 1);
            SelectedSegmentIndex = SelectedVertexIndex;
        }
        else
        {
            SelectedVertexIndex = -1;
            SelectedSegmentIndex = -1;
        }

        _selectedNotchId = null;
        _selectedInternalPointId = null;
        _selectedInternalLineId = null;

        for (int i = 0; i < StockPieces.Count; i++)
        {
            StockPieces[i].IsSelected = (StockPieces[i].Index == piece.Index);
        }

        IsPieceSelected = true;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        RefreshSelectedPieceProperties();
        RaiseGeometryChanged();
    }

    public void SelectNotchOnPiece(PatternPiece piece, Guid notchId)
    {
        ArgumentNullException.ThrowIfNull(piece);
        SelectedDomainPiece = piece;
        SelectedVertexIndices.Clear();
        SelectedVertexIndex = -1;
        SelectedSegmentIndex = -1;
        _selectedNotchId = notchId;
        _selectedInternalPointId = null;
        _selectedInternalLineId = null;
        for (int i = 0; i < StockPieces.Count; i++)
        {
            StockPieces[i].IsSelected = (StockPieces[i].Index == piece.Index);
        }

        IsPieceSelected = true;
        ActivePropertiesDockMode = PropertiesDockMode.Notch;
        RefreshSelectedPieceProperties();
        RaiseGeometryChanged();
    }

    public void SelectInternalPointOnPiece(PatternPiece piece, Guid internalPointId)
    {
        ArgumentNullException.ThrowIfNull(piece);
        SelectedDomainPiece = piece;
        SelectedVertexIndices.Clear();
        SelectedVertexIndex = -1;
        SelectedSegmentIndex = -1;
        _selectedInternalPointId = internalPointId;
        _selectedNotchId = null;
        _selectedInternalLineId = null;
        for (int i = 0; i < StockPieces.Count; i++)
        {
            StockPieces[i].IsSelected = (StockPieces[i].Index == piece.Index);
        }

        IsPieceSelected = true;
        ActivePropertiesDockMode = PropertiesDockMode.InternalPoint;
        RefreshSelectedPieceProperties();
        RaiseGeometryChanged();
    }

    public void SelectInternalLineOnPiece(PatternPiece piece, Guid internalLineId)
    {
        ArgumentNullException.ThrowIfNull(piece);
        SelectedDomainPiece = piece;
        SelectedVertexIndices.Clear();
        SelectedVertexIndex = -1;
        SelectedSegmentIndex = -1;
        _selectedInternalLineId = internalLineId;
        _selectedNotchId = null;
        _selectedInternalPointId = null;
        for (int i = 0; i < StockPieces.Count; i++)
        {
            StockPieces[i].IsSelected = (StockPieces[i].Index == piece.Index);
        }

        IsPieceSelected = true;
        ActivePropertiesDockMode = PropertiesDockMode.InternalLine;
        RefreshSelectedPieceProperties();
        RaiseGeometryChanged();
    }

    public int ExecuteAddPointAtContourClick(PatternPiece piece, PhysicalPointMm clickMm, bool isGradedCorner = false)
    {
        ArgumentNullException.ThrowIfNull(piece);
        (int segIdx, double t, _, _) = GeometryEngine.FindNearestContourLocation(piece, clickMm);
        if (segIdx < 0)
        {
            return -1;
        }

        EditingEngine.PushUndoSnapshot(DomainPieces, "Add point");
        int newIdx = EditingEngine.AddPointOnSegment(piece, segIdx, t, isGradedCorner, GetCalibrationForPiece(piece));
        SelectDomainPiece(piece, newIdx);
        StatusLeftText = $"Added point #{newIdx + 1} on {piece.Name} at t={t:F2} (De Casteljau split)";
        return newIdx;
    }

    public void ExecuteLineToCurveOnSegment(PatternPiece piece, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        EditingEngine.PushUndoSnapshot(DomainPieces, "Transform line to curve");
        EditingEngine.ConvertLineToCurve(piece, segmentIndex);
        SelectDomainPiece(piece, segmentIndex);
        StatusLeftText = $"Transformed segment #{segmentIndex + 1} to Cubic Bézier curve on {piece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteCurveToLineOnSegment(PatternPiece piece, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        EditingEngine.PushUndoSnapshot(DomainPieces, "Transform curve to line");
        EditingEngine.ConvertCurveToLine(piece, segmentIndex);
        SelectDomainPiece(piece, segmentIndex);
        StatusLeftText = $"Transformed segment #{segmentIndex + 1} to straight line on {piece.Name}";
        RaiseGeometryChanged();
    }

    public void ActivateSmartToolStatus(GeminiToolMode smartMode, string toolName)
    {
        EnsureRealDomainPiecesReady();
        ActiveTool = smartMode;
        StatusLeftText = smartMode switch
        {
            GeminiToolMode.SmartCorner => "Smart corner: click an image area near the contour to detect a strong corner.",
            GeminiToolMode.SmartNotch => "Smart notch: click near a sharp concave contour feature.",
            GeminiToolMode.SmartInternalPoint => "Smart internal point: click around an isolated internal image mark.",
            GeminiToolMode.SmartInternalLine => "Smart internal line: click around an elongated dark internal mark.",
            _ => $"NOT IMPLEMENTED: {toolName} (no verified Gemini native algorithm or validated clean-room detector)."
        };
    }

    public void ExecuteSmartCornerAt(PatternPiece piece, PhysicalPointMm areaCenterMm)
    {
        if (CurrentRawImage is null)
        {
            StatusLeftText = "Smart corner requires a loaded source image.";
            return;
        }

        ImagePixelPoint centerPx = ActiveCalibration.PhysicalMmToPixel(areaCenterMm);
        if (!SmartFeatureEngine.TryDetectCornerOnContour(CurrentRawImage, ActiveCalibration, piece, centerPx, 70.0,
                out PhysicalPointMm cornerMm, out double confidence))
        {
            StatusLeftText = "Smart corner: no sufficiently strong image corner was detected in the selected area.";
            return;
        }

        int closestVertex = Enumerable.Range(0, piece.Vertices.Count)
            .OrderBy(i => piece.Vertices[i].PositionMm.DistanceTo(cornerMm))
            .First();
        double vertexDistance = piece.Vertices[closestVertex].PositionMm.DistanceTo(cornerMm);
        EditingEngine.PushUndoSnapshot(DomainPieces, "Smart Detect Corner");
        int selectedIndex;
        if (vertexDistance <= 12.0)
        {
            EditingEngine.SetVertexGradedCorner(piece, closestVertex, true);
            selectedIndex = closestVertex;
        }
        else
        {
            (int segmentIndex, double t, _, _) = GeometryEngine.FindNearestContourLocation(piece, cornerMm);
            selectedIndex = EditingEngine.AddPointOnSegment(piece, segmentIndex, t, true, ActiveCalibration);
        }

        SelectDomainPiece(piece, selectedIndex);
        ActiveTool = GeminiToolMode.SmartCorner;
        StatusLeftText = $"Smart corner detected at point #{selectedIndex + 1} (confidence {confidence:P0}, clean-room image detector).";
        RaiseGeometryChanged();
    }

    public void ExecuteSmartInternalPointAt(PatternPiece piece, PhysicalPointMm areaCenterMm)
    {
        if (CurrentRawImage is null)
        {
            StatusLeftText = "Smart internal point requires a loaded source image.";
            return;
        }

        ImagePixelPoint centerPx = ActiveCalibration.PhysicalMmToPixel(areaCenterMm);
        if (!SmartFeatureEngine.TryDetectInternalPoint(CurrentRawImage, ActiveCalibration, piece, centerPx, 35.0,
                out PhysicalPointMm detectedMm, out double confidence))
        {
            StatusLeftText = "Smart internal point: no isolated contrast feature passed the confidence threshold.";
            return;
        }

        SelectDomainPiece(piece);
        ExecuteAddInternalPoint(detectedMm);
        ActiveTool = GeminiToolMode.SmartInternalPoint;
        StatusLeftText = $"Smart internal point detected (confidence {confidence:P0}, clean-room image detector).";
    }

    public void ExecuteSmartNotchAt(PatternPiece piece, PhysicalPointMm areaCenterMm)
    {
        if (!SmartFeatureEngine.TryDetectNotchOnContour(piece, areaCenterMm, 45.0,
                out PhysicalPointMm detectedMm, out double confidence))
        {
            StatusLeftText = "Smart notch: no sharp concave contour feature passed the confidence threshold.";
            return;
        }

        SelectDomainPiece(piece);
        ExecuteAddNotch(detectedMm);
        ActiveTool = GeminiToolMode.SmartNotch;
        StatusLeftText = $"Smart notch detected (confidence {confidence:P0}, clean-room contour detector).";
    }

    public void ExecuteSmartInternalLineAt(PatternPiece piece, PhysicalPointMm areaCenterMm)
    {
        if (CurrentRawImage is null)
        {
            StatusLeftText = "Smart internal line requires a loaded source image.";
            return;
        }

        ImagePixelPoint centerPx = ActiveCalibration.PhysicalMmToPixel(areaCenterMm);
        if (!SmartFeatureEngine.TryDetectInternalLine(CurrentRawImage, ActiveCalibration, piece, centerPx, 95.0,
                out PhysicalPointMm startMm, out PhysicalPointMm endMm, out double confidence))
        {
            StatusLeftText = "Smart internal line: no elongated dark feature passed the confidence threshold.";
            return;
        }

        SelectDomainPiece(piece);
        ExecuteAddInternalLine(startMm, endMm);
        ActiveTool = GeminiToolMode.SmartInternalLine;
        StatusLeftText = $"Smart internal line detected (confidence {confidence:P0}, clean-room image detector).";
    }

    public void SelectDemoPiece(DemoPieceCardItem pieceCard)
    {
        if (DomainPieces.Count == 0 && IsDemoReferenceLayoutActive && IsV9ThreePieceDemoActive)
        {
            RunDetectPatterns();
        }

        PatternPiece? matchingDomain = DomainPieces.FirstOrDefault(p => p.Index == pieceCard.Index || p.Name == pieceCard.Name);
        if (matchingDomain is not null)
        {
            SelectDomainPiece(matchingDomain, 0);
            return;
        }

        foreach (var item in StockPieces)
        {
            item.IsSelected = ReferenceEquals(item, pieceCard);
        }

        IsPieceSelected = true;
        _suppressPropertySync = true;
        PieceName = pieceCard.Name;
        PieceAreaCm2 = pieceCard.AreaCm2;
        PiecePerimeterCm = pieceCard.PerimeterCm;
        NotchesSummary = pieceCard.NotchesInternalSummary;
        CornersSummary = pieceCard.CornersGradedSummary;
        _suppressPropertySync = false;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
    }

    public void ClearPieceSelection()
    {
        SelectedDomainPiece = null;
        SelectedVertexIndices.Clear();
        SelectedVertexIndex = -1;
        SelectedSegmentIndex = -1;
        _selectedNotchId = null;
        _selectedInternalPointId = null;
        _selectedInternalLineId = null;

        foreach (var item in StockPieces)
        {
            item.IsSelected = false;
        }

        IsPieceSelected = false;
        _suppressPropertySync = true;
        PieceName = string.Empty;
        PieceAreaCm2 = string.Empty;
        PiecePerimeterCm = string.Empty;
        NotchesSummary = string.Empty;
        CornersSummary = string.Empty;
        _suppressPropertySync = false;
        ActivePropertiesDockMode = PropertiesDockMode.NothingSelected;
        RaiseGeometryChanged();
    }

    public void ClearVertexSelection()
    {
        SelectedVertexIndices.Clear();
        SelectedVertexIndex = -1;
        RaiseGeometryChanged();
    }

    public void SelectMultipleVertices(PatternPiece piece, IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(piece);
        SelectedDomainPiece = piece;
        SelectedVertexIndices.Clear();
        foreach (int idx in indices.Where(i => i >= 0 && i < piece.Vertices.Count).Distinct())
        {
            SelectedVertexIndices.Add(idx);
        }

        if (SelectedVertexIndices.Count > 0)
        {
            SelectedVertexIndex = SelectedVertexIndices[0];
            SelectedSegmentIndex = SelectedVertexIndex;
            ActivePropertiesDockMode = PropertiesDockMode.Points;
            IsPieceSelected = true;
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Selected {SelectedVertexIndices.Count} points on {piece.Name} (Press Delete to remove)";
        }
        else
        {
            SelectedVertexIndex = -1;
        }

        RaiseGeometryChanged();
    }

    public void ExecuteDeleteSelectedVertices()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        if (SelectedVertexIndices.Count == 0 && SelectedVertexIndex >= 0)
        {
            ExecuteDeleteSelectedPoint();
            return;
        }

        if (SelectedDomainPiece.Vertices.Count <= 3)
        {
            StatusLeftText = "Cannot delete points: A closed pattern piece requires at least 3 vertices.";
            return;
        }

        int toDeleteCount = SelectedVertexIndices.Count;
        EditingEngine.PushUndoSnapshot(DomainPieces, $"Delete {toDeleteCount} points");
        int deleted = EditingEngine.DeleteMultipleVertices(SelectedDomainPiece, SelectedVertexIndices);
        SelectedVertexIndices.Clear();
        SelectedVertexIndex = Math.Clamp(0, 0, SelectedDomainPiece.Vertices.Count - 1);
        SelectedSegmentIndex = SelectedVertexIndex;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Deleted {deleted} points from {SelectedDomainPiece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteDeleteCurrentSelection()
    {
        if (SelectedNotchId.HasValue)
        {
            ExecuteDeleteNotch();
            return;
        }

        if (SelectedInternalPointId.HasValue)
        {
            ExecuteDeleteInternalPoint();
            return;
        }

        if (SelectedInternalLineId.HasValue)
        {
            ExecuteDeleteInternalLine();
            return;
        }

        if (SelectedVertexIndices.Count > 0)
        {
            ExecuteDeleteSelectedVertices();
            return;
        }

        if (SelectedDomainPiece is not null && SelectedVertexIndex >= 0)
        {
            ExecuteDeleteSelectedPoint();
            return;
        }

        StatusLeftText = "Nothing selected to delete.";
    }

    public void RefreshSelectedPieceProperties()
    {
        if (SelectedDomainPiece is null)
        {
            return;
        }

        _suppressPropertySync = true;
        try
        {
            MeasurementEngine.RecalculatePieceMetrics(SelectedDomainPiece);

            PieceName = SelectedDomainPiece.Name;
            PieceAreaCm2 = FormatNumber(SelectedDomainPiece.AreaCm2, 2);
            PiecePerimeterCm = FormatNumber(SelectedDomainPiece.PerimeterCm, 2);

            int graded = SelectedDomainPiece.GradedPointsCount;
            int notches = SelectedDomainPiece.Notches.Count;
            int intPts = SelectedDomainPiece.InternalPoints.Count;
            int intLines = SelectedDomainPiece.InternalLines.Count;

            NotchesSummary = $"{graded} / {notches} / {intPts} / {intLines}";
            CornersSummary = $"{SelectedDomainPiece.TotalVerticesCount} / {graded}";

            if (SelectedVertexIndex >= 0 && SelectedVertexIndex < SelectedDomainPiece.Vertices.Count)
            {
                ContourVertex v = SelectedDomainPiece.Vertices[SelectedVertexIndex];
                IsGradedPoint = v.IsGradedCorner;
                IsCurvePoint = !v.IsGradedCorner;
                IsPieceOrigin = v.IsPieceOrigin;

                PointMoveToX = FormatNumber(v.PositionMm.XCm, 1);
                PointMoveToY = FormatNumber(v.PositionMm.YCm, 1);
                PointMoveWithDx = "0";
                PointMoveWithDy = "0";

                (double endDeg, double startDeg, double bezLenCm) =
                    MeasurementEngine.ComputeVertexTangentProperties(SelectedDomainPiece, SelectedVertexIndex);

                TangentAngleEnding = FormatNumber(endDeg, 1);
                TangentAngleStarting = FormatNumber(startDeg, 1);
                SegmentBezierLength = FormatNumber(bezLenCm, 1);
            }

            if (_selectedNotchId.HasValue)
            {
                NotchItem? notch = SelectedDomainPiece.Notches.FirstOrDefault(n => n.Id == _selectedNotchId.Value);
                if (notch is not null)
                {
                    NotchX = FormatNumber(notch.PositionMm.XCm, 1);
                    NotchY = FormatNumber(notch.PositionMm.YCm, 1);
                    NotchDistance = FormatNumber(notch.DistanceFromPrevGradedPointMm / 10.0, 1);
                    NotchLength = FormatNumber(notch.LengthMm / 10.0, 2);
                    NotchAngle = FormatNumber(notch.AngleDegrees, 1);
                    NotchRelativeAngle = notch.IsRelativeAngle;
                }
            }

            if (_selectedInternalPointId.HasValue)
            {
                InternalPointItem? ip = SelectedDomainPiece.InternalPoints.FirstOrDefault(p => p.Id == _selectedInternalPointId.Value);
                if (ip is not null)
                {
                    InternalPointX = FormatNumber(ip.PositionMm.XCm, 1);
                    InternalPointY = FormatNumber(ip.PositionMm.YCm, 1);
                    InternalPointLength = FormatNumber(ip.LengthMm / 10.0, 2);
                }
            }

            if (_selectedInternalLineId.HasValue)
            {
                InternalLineItem? il = SelectedDomainPiece.InternalLines.FirstOrDefault(l => l.Id == _selectedInternalLineId.Value);
                if (il is not null)
                {
                    InternalLineTypeIndex = il.LineType switch
                    {
                        InternalLineType.CuttingLine => 1,
                        InternalLineType.GrainAxis => 2,
                        InternalLineType.SymmetryAxis => 3,
                        _ => 0
                    };
                    InternalLineX = FormatNumber(il.StartMm.XCm, 1);
                    InternalLineY = FormatNumber(il.StartMm.YCm, 1);
                    InternalLineAngle = FormatNumber(il.AngleDegrees, 1);
                    InternalLineLength = FormatNumber(il.LengthMm / 10.0, 1);
                }
            }

            SyncDomainPiecesToStockCards();
        }
        finally
        {
            _suppressPropertySync = false;
        }
    }

    // =======================================================================
    // EDITING ACTIONS (CALLED BY TOOLBAR & CANVAS INTERACTIONS)
    // =======================================================================

    public void EnsureRealDomainPiecesReady()
    {
        if (DomainPieces.Count == 0)
        {
            RunDetectPatterns();
        }

        if (SelectedDomainPiece is null && DomainPieces.Count > 0)
        {
            SelectDomainPiece(DomainPieces[0], 0);
        }
    }

    public void ExecuteAddPointOnActiveSegment()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        int segIdx = SelectedSegmentIndex >= 0 ? SelectedSegmentIndex : Math.Max(0, SelectedVertexIndex);
        EditingEngine.PushUndoSnapshot(DomainPieces, "Add point");
        int newIdx = EditingEngine.AddPointOnSegment(SelectedDomainPiece, segIdx, 0.5, isGradedCorner: false, ActiveCalibration);
        SelectedVertexIndex = newIdx;
        SelectedSegmentIndex = newIdx;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Added point #{newIdx + 1} on {SelectedDomainPiece.Name} (De Casteljau split)";
        RaiseGeometryChanged();
    }

    public void ExecuteDeleteSelectedPoint()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null || SelectedVertexIndex < 0)
        {
            return;
        }

        if (SelectedDomainPiece.Vertices.Count <= 3)
        {
            StatusLeftText = "Cannot delete point: A closed pattern piece requires at least 3 vertices.";
            return;
        }

        EditingEngine.PushUndoSnapshot(DomainPieces, "Delete point");
        if (EditingEngine.DeleteVertex(SelectedDomainPiece, SelectedVertexIndex))
        {
            SelectedVertexIndex = Math.Clamp(SelectedVertexIndex, 0, SelectedDomainPiece.Vertices.Count - 1);
            SelectedSegmentIndex = SelectedVertexIndex;
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Deleted vertex from {SelectedDomainPiece.Name}";
            RaiseGeometryChanged();
        }
    }

    public void ExecutePromoteToCorner()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null || SelectedVertexIndex < 0)
        {
            return;
        }

        EditingEngine.PushUndoSnapshot(DomainPieces, "Set Graded Corner");
        EditingEngine.SetVertexGradedCorner(SelectedDomainPiece, SelectedVertexIndex, true);
        ActiveTool = GeminiToolMode.SmartCorner;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Marked vertex #{SelectedVertexIndex + 1} as Graded Corner on {SelectedDomainPiece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteDemoteCornerToCurvePoint()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null || SelectedVertexIndex < 0)
        {
            return;
        }

        EditingEngine.PushUndoSnapshot(DomainPieces, "Delete Corner (Demote to Curve Point)");
        EditingEngine.SetVertexGradedCorner(SelectedDomainPiece, SelectedVertexIndex, false);
        ActiveTool = GeminiToolMode.DeleteCorner;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Converted vertex #{SelectedVertexIndex + 1} to Curve Point on {SelectedDomainPiece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteLineToCurve()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        int segIdx = SelectedSegmentIndex >= 0 ? SelectedSegmentIndex : Math.Max(0, SelectedVertexIndex);
        EditingEngine.PushUndoSnapshot(DomainPieces, "Transform line to curve");
        EditingEngine.ConvertLineToCurve(SelectedDomainPiece, segIdx);
        ActiveTool = GeminiToolMode.LineToCurve;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Transformed segment #{segIdx + 1} to Cubic Bézier curve on {SelectedDomainPiece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteCurveToLine()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        int segIdx = SelectedSegmentIndex >= 0 ? SelectedSegmentIndex : Math.Max(0, SelectedVertexIndex);
        EditingEngine.PushUndoSnapshot(DomainPieces, "Transform curve to line");
        EditingEngine.ConvertCurveToLine(SelectedDomainPiece, segIdx);
        ActiveTool = GeminiToolMode.CurveToLine;
        ActivePropertiesDockMode = PropertiesDockMode.Points;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Transformed segment #{segIdx + 1} to straight line on {SelectedDomainPiece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteMovePointToInputs()
    {
        if (SelectedDomainPiece is null || SelectedVertexIndex < 0)
        {
            return;
        }

        if (TryParseNumber(PointMoveToX, out double xCm) && TryParseNumber(PointMoveToY, out double yCm))
        {
            EditingEngine.PushUndoSnapshot(DomainPieces, "Move point to (X,Y)");
            EditingEngine.MoveVertexToAbsolute(
                SelectedDomainPiece,
                SelectedVertexIndex,
                PhysicalPointMm.FromCm(xCm, yCm),
                ActiveCalibration);
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Moved point #{SelectedVertexIndex + 1} to ({xCm:F1}, {yCm:F1}) cm";
            RaiseGeometryChanged();
        }
    }

    public void ExecuteMovePointWithDeltaInputs()
    {
        if (SelectedDomainPiece is null || SelectedVertexIndex < 0)
        {
            return;
        }

        if (TryParseNumber(PointMoveWithDx, out double dxCm) && TryParseNumber(PointMoveWithDy, out double dyCm))
        {
            EditingEngine.PushUndoSnapshot(DomainPieces, "Move point with (dX,dY)");
            EditingEngine.MoveVertexByDelta(
                SelectedDomainPiece,
                SelectedVertexIndex,
                dxCm * 10.0,
                dyCm * 10.0,
                ActiveCalibration);
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Moved point #{SelectedVertexIndex + 1} by ({dxCm:F1}, {dyCm:F1}) cm";
            RaiseGeometryChanged();
        }
    }

    public void ExecuteAddNotch(PhysicalPointMm? targetMm = null)
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        int segIdx = SelectedSegmentIndex >= 0 ? SelectedSegmentIndex : Math.Max(0, SelectedVertexIndex);
        PhysicalPointMm pt = targetMm ?? SelectedDomainPiece.GetSegment(segIdx).Evaluate(0.5);

        EditingEngine.PushUndoSnapshot(DomainPieces, "Add Notch");
        NotchItem notch = EditingEngine.AddNotch(SelectedDomainPiece, pt, lengthMm: 6.0, angleDegrees: 90.0);
        _selectedNotchId = notch.Id;
        ActiveTool = GeminiToolMode.AddNotch;
        ActivePropertiesDockMode = PropertiesDockMode.Notch;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Added notch on {SelectedDomainPiece.Name} at ({notch.PositionMm.XCm:F1}, {notch.PositionMm.YCm:F1}) cm";
        RaiseGeometryChanged();
    }

    public void ExecuteDeleteNotch()
    {
        if (SelectedDomainPiece is null || SelectedDomainPiece.Notches.Count == 0)
        {
            StatusLeftText = "No notch to delete on selected piece.";
            return;
        }

        Guid idToDelete = _selectedNotchId ?? SelectedDomainPiece.Notches[^1].Id;
        EditingEngine.PushUndoSnapshot(DomainPieces, "Delete Notch");
        if (EditingEngine.DeleteNotch(SelectedDomainPiece, idToDelete))
        {
            _selectedNotchId = SelectedDomainPiece.Notches.LastOrDefault()?.Id;
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Deleted notch from {SelectedDomainPiece.Name}";
            RaiseGeometryChanged();
        }
    }

    public void ExecuteAddInternalPoint(PhysicalPointMm? positionMm = null)
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        Rect bbox = SelectedDomainPiece.BoundingBoxMm;
        PhysicalPointMm pos = positionMm ?? new PhysicalPointMm(bbox.X + (bbox.Width * 0.5), bbox.Y + (bbox.Height * 0.5));

        EditingEngine.PushUndoSnapshot(DomainPieces, "Add Internal Point");
        InternalPointItem ip = EditingEngine.AddInternalPoint(SelectedDomainPiece, pos, lengthMm: 5.0);
        _selectedInternalPointId = ip.Id;
        ActiveTool = GeminiToolMode.AddInternalPoint;
        ActivePropertiesDockMode = PropertiesDockMode.InternalPoint;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Added internal point on {SelectedDomainPiece.Name} at ({ip.PositionMm.XCm:F1}, {ip.PositionMm.YCm:F1}) cm";
        RaiseGeometryChanged();
    }

    public void ExecuteDeleteInternalPoint()
    {
        if (SelectedDomainPiece is null || SelectedDomainPiece.InternalPoints.Count == 0)
        {
            StatusLeftText = "No internal point to delete on selected piece.";
            return;
        }

        Guid idToDelete = _selectedInternalPointId ?? SelectedDomainPiece.InternalPoints[^1].Id;
        EditingEngine.PushUndoSnapshot(DomainPieces, "Delete Internal Point");
        if (EditingEngine.DeleteInternalPoint(SelectedDomainPiece, idToDelete))
        {
            _selectedInternalPointId = SelectedDomainPiece.InternalPoints.LastOrDefault()?.Id;
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Deleted internal point from {SelectedDomainPiece.Name}";
            RaiseGeometryChanged();
        }
    }

    public void ExecuteAddInternalLine(PhysicalPointMm? startMm = null, PhysicalPointMm? endMm = null, InternalLineType? lineTypeOverride = null)
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        Rect bbox = SelectedDomainPiece.BoundingBoxMm;
        PhysicalPointMm p0 = startMm ?? new PhysicalPointMm(bbox.X + (bbox.Width * 0.25), bbox.Y + (bbox.Height * 0.5));
        PhysicalPointMm p1 = endMm ?? new PhysicalPointMm(bbox.X + (bbox.Width * 0.75), bbox.Y + (bbox.Height * 0.5));
        InternalLineType lineType = lineTypeOverride ?? InternalLineTypeIndex switch
        {
            1 => InternalLineType.CuttingLine,
            2 => InternalLineType.GrainAxis,
            3 => InternalLineType.SymmetryAxis,
            _ => InternalLineType.SimpleLine
        };

        EditingEngine.PushUndoSnapshot(DomainPieces, "Add Internal Line");
        InternalLineItem line = EditingEngine.AddInternalLine(SelectedDomainPiece, p0, p1, lineType);
        _selectedInternalLineId = line.Id;
        ActiveTool = GeminiToolMode.AddInternalLine;
        ActivePropertiesDockMode = PropertiesDockMode.InternalLine;
        RefreshSelectedPieceProperties();
        StatusLeftText = $"Added {(lineType == InternalLineType.GrainAxis ? "Grain axis" : "Internal line")} ({line.LengthMm / 10.0:F1} cm) on {SelectedDomainPiece.Name}";
        RaiseGeometryChanged();
    }

    public void ExecuteFlipInternalLine()
    {
        if (SelectedDomainPiece is null || !_selectedInternalLineId.HasValue)
        {
            return;
        }

        InternalLineItem? line = SelectedDomainPiece.InternalLines.FirstOrDefault(l => l.Id == _selectedInternalLineId.Value);
        if (line is not null)
        {
            EditingEngine.PushUndoSnapshot(DomainPieces, "Flip Internal Line");
            line.FlipDirection();
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Flipped internal line direction ({line.AngleDegrees:F1}°)";
            RaiseGeometryChanged();
        }
    }

    public void ExecuteDeleteInternalLine()
    {
        if (SelectedDomainPiece is null || SelectedDomainPiece.InternalLines.Count == 0)
        {
            StatusLeftText = "No internal line to delete on selected piece.";
            return;
        }

        Guid idToDelete = _selectedInternalLineId ?? SelectedDomainPiece.InternalLines[^1].Id;
        EditingEngine.PushUndoSnapshot(DomainPieces, "Delete Internal Line");
        if (EditingEngine.DeleteInternalLine(SelectedDomainPiece, idToDelete))
        {
            _selectedInternalLineId = SelectedDomainPiece.InternalLines.LastOrDefault()?.Id;
            RefreshSelectedPieceProperties();
            StatusLeftText = $"Deleted internal line from {SelectedDomainPiece.Name}";
            RaiseGeometryChanged();
        }
    }

    public void ExecuteAddAuxiliaryPiece()
    {
        _isDemoReferenceLayoutActive = false;
        _isV9ThreePieceDemoActive = false;
        OnPropertyChanged(nameof(IsDemoReferenceLayoutActive));
        OnPropertyChanged(nameof(IsV9ThreePieceDemoActive));

        EditingEngine.PushUndoSnapshot(DomainPieces, "Add Auxiliary Piece");
        int nextIdx = DomainPieces.Count + 1;
        PatternPiece aux = EditingEngine.CreateAuxiliaryRectanglePiece(
            nextIdx,
            new PhysicalPointMm(120.0 + (nextIdx * 30.0), 120.0 + (nextIdx * 20.0)),
            200.0,
            150.0,
            ActiveCalibration);

        DomainPieces.Add(aux);
        DetectedPiecesCount = DomainPieces.Count;
        SyncDomainPiecesToStockCards();
        SelectDomainPiece(aux, 0);
        StatusLeftText = $"Added auxiliary piece '{aux.Name}' (20.0 cm x 15.0 cm)";
        RaiseGeometryChanged();
    }

    public void ExecuteDeleteSelectedPiece()
    {
        if (SelectedDomainPiece is null)
        {
            return;
        }

        string deletedName = SelectedDomainPiece.Name;
        EditingEngine.PushUndoSnapshot(DomainPieces, $"Delete {deletedName}");
        DomainPieces.Remove(SelectedDomainPiece);
        for (int i = 0; i < DomainPieces.Count; i++)
        {
            DomainPieces[i].Index = i + 1;
        }

        DetectedPiecesCount = DomainPieces.Count;
        SyncDomainPiecesToStockCards();
        ClearPieceSelection();
        StatusLeftText = $"Deleted piece '{deletedName}'";
        RaiseGeometryChanged();
    }

    public void ExecuteUndo()
    {
        if (!EditingEngine.CanUndo)
        {
            StatusLeftText = "Nothing to Undo.";
            return;
        }

        var (success, restored, actionName) = EditingEngine.Undo(DomainPieces);
        if (success)
        {
            RestoreDomainPieces(restored);
            StatusLeftText = $"Undo: {actionName}";
        }
    }

    public void ExecuteRedo()
    {
        if (!EditingEngine.CanRedo)
        {
            StatusLeftText = "Nothing to Redo.";
            return;
        }

        var (success, restored, actionName) = EditingEngine.Redo(DomainPieces);
        if (success)
        {
            RestoreDomainPieces(restored);
            StatusLeftText = $"Redo: {actionName}";
        }
    }

    public void StepPlayNextPoint(PropertiesDockMode mode)
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null || SelectedDomainPiece.Vertices.Count == 0)
        {
            return;
        }

        int n = SelectedDomainPiece.Vertices.Count;
        int next = (SelectedVertexIndex + 1) % n;
        if (mode == PropertiesDockMode.Points)
        {
            SelectDomainPiece(SelectedDomainPiece, next);
            double angle = MeasurementEngine.ComputeVertexAngleDegrees(SelectedDomainPiece, next);
            StatusLeftText = $"{SelectedDomainPiece.Name} — Vertex #{next + 1}/{n} ({(SelectedDomainPiece.Vertices[next].IsGradedCorner ? "Graded" : "Curve")}), Interior angle: {angle:F1}°";
        }
        else if (mode == PropertiesDockMode.Notch && SelectedDomainPiece.Notches.Count > 0)
        {
            int current = _selectedNotchId.HasValue
                ? SelectedDomainPiece.Notches.FindIndex(n => n.Id == _selectedNotchId.Value)
                : -1;
            NotchItem nextNotch = SelectedDomainPiece.Notches[(current + 1) % SelectedDomainPiece.Notches.Count];
            SelectNotchOnPiece(SelectedDomainPiece, nextNotch.Id);
            StatusLeftText = $"{SelectedDomainPiece.Name} — Notch #{((current + 1) % SelectedDomainPiece.Notches.Count) + 1}/{SelectedDomainPiece.Notches.Count}";
        }
        else if (mode == PropertiesDockMode.InternalPoint && SelectedDomainPiece.InternalPoints.Count > 0)
        {
            int current = _selectedInternalPointId.HasValue
                ? SelectedDomainPiece.InternalPoints.FindIndex(p => p.Id == _selectedInternalPointId.Value)
                : -1;
            InternalPointItem nextPoint = SelectedDomainPiece.InternalPoints[(current + 1) % SelectedDomainPiece.InternalPoints.Count];
            SelectInternalPointOnPiece(SelectedDomainPiece, nextPoint.Id);
            StatusLeftText = $"{SelectedDomainPiece.Name} — Internal point #{((current + 1) % SelectedDomainPiece.InternalPoints.Count) + 1}/{SelectedDomainPiece.InternalPoints.Count}";
        }
        else if (mode == PropertiesDockMode.InternalLine && SelectedDomainPiece.InternalLines.Count > 0)
        {
            int current = _selectedInternalLineId.HasValue
                ? SelectedDomainPiece.InternalLines.FindIndex(l => l.Id == _selectedInternalLineId.Value)
                : -1;
            InternalLineItem nextLine = SelectedDomainPiece.InternalLines[(current + 1) % SelectedDomainPiece.InternalLines.Count];
            SelectInternalLineOnPiece(SelectedDomainPiece, nextLine.Id);
            StatusLeftText = $"{SelectedDomainPiece.Name} — Internal line #{((current + 1) % SelectedDomainPiece.InternalLines.Count) + 1}/{SelectedDomainPiece.InternalLines.Count}";
        }
        else
        {
            ActivePropertiesDockMode = mode;
            StatusLeftText = $"No {mode.ToString().ToLowerInvariant()} objects on {SelectedDomainPiece.Name}.";
        }
    }

    public void StepPlayNextGradedPoint()
    {
        EnsureRealDomainPiecesReady();
        if (SelectedDomainPiece is null)
        {
            return;
        }

        List<int> graded = SelectedDomainPiece.Vertices
            .Select((vertex, index) => (vertex, index))
            .Where(item => item.vertex.IsGradedCorner)
            .Select(item => item.index)
            .ToList();
        if (graded.Count == 0)
        {
            StatusLeftText = $"No graded points on {SelectedDomainPiece.Name}.";
            return;
        }

        int currentPosition = graded.IndexOf(SelectedVertexIndex);
        int nextIndex = graded[(currentPosition + 1) % graded.Count];
        SelectDomainPiece(SelectedDomainPiece, nextIndex);
        double angle = MeasurementEngine.ComputeVertexAngleDegrees(SelectedDomainPiece, nextIndex);
        StatusLeftText = $"{SelectedDomainPiece.Name} — Graded point #{nextIndex + 1} ({((currentPosition + 1) % graded.Count) + 1}/{graded.Count}), Interior angle: {angle:F1}°";
    }

    public void SaveProjectToFile(string filePath)
    {
        EnsureRealDomainPiecesReady();
        var doc = new DigitizerProjectDocument
        {
            ImageFilePath = LoadedImagePath,
            ImageWidthPx = CurrentRawImage?.Width ?? LoadedBitmap?.PixelWidth ?? 3456,
            ImageHeightPx = CurrentRawImage?.Height ?? LoadedBitmap?.PixelHeight ?? 2592,
            Calibration = ActiveCalibration.Clone(),
            Pieces = DomainPieces.Select(p => p.Clone()).ToList()
        };

        FileEngine.SaveProject(doc, filePath);
        StatusLeftText = $"Saved project to {Path.GetFileName(filePath)} ({DomainPieces.Count} pieces)";
    }

    public bool LoadProjectFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        DigitizerProjectDocument doc = FileEngine.LoadProject(filePath);
        bool sourceImageAvailable = !string.IsNullOrWhiteSpace(doc.ImageFilePath) && File.Exists(doc.ImageFilePath);
        if (sourceImageAvailable)
        {
            LoadImageFromFile(doc.ImageFilePath);
        }
        else
        {
            // Never leave a raster from the previously opened project behind
            // the newly loaded geometry. A missing source image is a visible,
            // recoverable project state, not permission to use stale pixels.
            CurrentRawImage = null;
            LoadedBitmap = null;
            LoadedImagePath = doc.ImageFilePath ?? string.Empty;
            HasImageLoaded = false;
        }

        _isDemoReferenceLayoutActive = false;
        _isV9ThreePieceDemoActive = false;
        OnPropertyChanged(nameof(IsDemoReferenceLayoutActive));
        OnPropertyChanged(nameof(IsV9ThreePieceDemoActive));

        ActiveCalibration = doc.Calibration;
        RestoreDomainPieces(doc.Pieces);
        WindowTitle = $"gemhihi - [{filePath}]";
        StatusLeftText = sourceImageAvailable
            ? $"Opened project {Path.GetFileName(filePath)} ({DomainPieces.Count} pieces)"
            : $"Opened project {Path.GetFileName(filePath)} ({DomainPieces.Count} pieces); source image is unavailable.";
        return true;
    }

    public DxfValidationReport ExportDxfToFile(string filePath, string profileName = "General")
    {
        EnsureRealDomainPiecesReady();
        FileEngine.ExportDxf(DomainPieces.ToList(), filePath, profileName);
        DxfValidationReport report = FileEngine.ParseAndValidateDxfFile(filePath, DomainPieces.ToList());
        StatusLeftText = report.IsValid
            ? $"Exported & Verified DXF: {Path.GetFileName(filePath)} ({report.ParsedPieces.Count} pieces, max error {report.MaxDimensionErrorMm:F3} mm)"
            : $"DXF Export Warning: {string.Join("; ", report.Errors)}";
        return report;
    }

    public AamaDxfExportResult ExportAamaDxfToFile(string filePath)
    {
        EnsureRealDomainPiecesReady();
        AamaDxfExportResult result = FileEngine.ExportAamaDxf(DomainPieces.ToList(), filePath);
        StatusLeftText = $"Exported DXF-AAMA: {Path.GetFileName(result.DxfPath)} ({result.PieceCount} pieces)";
        return result;
    }

    public void SetDigitizerToolWithPhase1Notice(GeminiToolMode tool, string toolCaption, PropertiesDockMode? dockMode = null)
    {
        ActiveTool = tool;
        if (dockMode.HasValue)
        {
            ActivePropertiesDockMode = dockMode.Value;
        }

        StatusLeftText = toolCaption;
        StatusRightText = "Done detecting patterns!";
    }

    public void RaiseGeometryChanged() => GeometryChanged?.Invoke();

    private void RestoreDomainPieces(List<PatternPiece> pieces)
    {
        Guid? prevSelectedPieceId = SelectedDomainPiece?.Id;
        int prevVertexIdx = SelectedVertexIndex;

        DomainPieces.Clear();
        foreach (PatternPiece p in pieces)
        {
            MeasurementEngine.RecalculatePieceMetrics(p);
            DomainPieces.Add(p);
        }

        DetectedPiecesCount = DomainPieces.Count;
        SyncDomainPiecesToStockCards();

        PatternPiece? reselect = prevSelectedPieceId.HasValue
            ? DomainPieces.FirstOrDefault(p => p.Id == prevSelectedPieceId.Value)
            : DomainPieces.FirstOrDefault();

        if (reselect is not null)
        {
            SelectDomainPiece(reselect, Math.Max(0, prevVertexIdx));
        }
        else
        {
            ClearPieceSelection();
        }

        RaiseGeometryChanged();
    }

    private void SyncDomainPiecesToStockCards()
    {
        StockPieces.Clear();
        foreach (PatternPiece p in DomainPieces)
        {
            int graded = p.GradedPointsCount;
            string thumbPath = GeometryEngine.BuildStockThumbnailPathData(p);
            StockPieces.Add(new DemoPieceCardItem
            {
                Index = p.Index,
                Name = p.Name,
                AreaCm2 = FormatNumber(p.AreaCm2, 2),
                PerimeterCm = FormatNumber(p.PerimeterCm, 2),
                NotchesInternalSummary = $"{graded} / {p.Notches.Count} / {p.InternalPoints.Count} / {p.InternalLines.Count}",
                CornersGradedSummary = $"{p.TotalVerticesCount} / {graded}",
                ThumbnailPathData = thumbPath,
                IsSelected = ReferenceEquals(p, SelectedDomainPiece)
            });
        }
    }

    private void UpdateResolutionHeaderText()
    {
        int w = CurrentRawImage?.Width ?? LoadedBitmap?.PixelWidth ?? ActiveCalibration.ImageWidthPx;
        int h = CurrentRawImage?.Height ?? LoadedBitmap?.PixelHeight ?? ActiveCalibration.ImageHeightPx;
        PictureResolutionText = ActiveCalibration.FormatHeaderSummary(w, h);
    }

    private static string FormatNumber(double value, int decimals)
    {
        string fmt = $"F{decimals}";
        return value.ToString(fmt, CultureInfo.InvariantCulture).Replace('.', ',');
    }

    private static bool TryParseNumber(string text, out double value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return false;
        }

        string normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
