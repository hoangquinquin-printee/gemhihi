namespace PatternDigitizerPro.Core.Models;

/// <summary>
/// Active tool or viewport mode on the Gemini main toolbar.
/// In Phase 1, only viewport navigation tools (Select/Pan/Zoom) perform live viewport actions;
/// digitization tools update UI selection/status and report Phase 1 unimplemented backend status.
/// </summary>
public enum GeminiToolMode
{
    SelectDefault,
    PanViewport,
    ZoomArea,
    /// <summary>Draws the image-space frame used to limit the next pattern detection run.</summary>
    DigitizeRoi,
    AddPoint,
    DeletePoint,
    SmartCorner,
    DeleteCorner,
    LineToCurve,
    CurveToLine,
    SmartNotch,
    AddNotch,
    DeleteNotch,
    SmartInternalPoint,
    AddInternalPoint,
    DeleteInternalPoint,
    SmartInternalLine,
    AddInternalLine,
    AddInternalCurve,
    DeleteInternalLine,
    AddAuxPiece,
    DeleteAuxPiece,
    MeasurePerimeter,
    MeasureDirect
}

/// <summary>
/// Contextual sub-form displayed inside PropertiesDockSite on the right panel.
/// Matches TMainFrm.PropertiesDockSite in GeminiPhotoDigitizer.exe.
/// </summary>
public enum PropertiesDockMode
{
    NothingSelected,
    Points,
    Notch,
    InternalPoint,
    InternalLine
}

/// <summary>
/// Demo UI Piece item for verifying Stock Area (PieceList) and ObjectPropertiesPanel layout in Phase 1.
/// Strictly labeled as Demo UI layout data (not a backend digitization result).
/// </summary>
public sealed class DemoPieceCardItem
{
    public int Index { get; init; }
    public string Name { get; set; } = string.Empty;
    public string AreaCm2 { get; init; } = "0,00";
    public string PerimeterCm { get; init; } = "0,00";
    public string NotchesInternalSummary { get; init; } = "0 / 0 / 0";
    public string CornersGradedSummary { get; init; } = "0 / 0";
    public string ThumbnailPathData { get; init; } = string.Empty;
    public bool IsSelected { get; set; }
}

/// <summary>
/// Provides the exact reference UI states for 1:1 visual comparison (both live v9.0 reference state and manual state).
/// </summary>
public static class GeminiReferenceDemoData
{
    /// <summary>
    /// Creates the exact 3 demo pattern cards matching Ảnh 1–5 (D:\pictures\03172025 111128.jpg in GeminiPhotoDigitizer v9.0).
    /// </summary>
    public static IReadOnlyList<DemoPieceCardItem> CreateLiveV9ReferencePieces() =>
    [
        new DemoPieceCardItem
        {
            Index = 1,
            Name = "Piece 1",
            AreaCm2 = "450,00",
            PerimeterCm = "90,00",
            NotchesInternalSummary = "4 / 0 / 0 / 0",
            CornersGradedSummary = "4 / 4",
            IsSelected = false,
            ThumbnailPathData = "M 15,20 L 49,7 L 60,32 L 25,45 Z"
        },
        new DemoPieceCardItem
        {
            Index = 2,
            Name = "Piece 2",
            AreaCm2 = "450,00",
            PerimeterCm = "90,00",
            NotchesInternalSummary = "4 / 0 / 0 / 0",
            CornersGradedSummary = "4 / 4",
            IsSelected = false,
            ThumbnailPathData = "M 25,7 L 49,11 L 44,46 L 20,42 Z"
        },
        new DemoPieceCardItem
        {
            Index = 3,
            Name = "Piece 3",
            AreaCm2 = "450,00",
            PerimeterCm = "90,00",
            NotchesInternalSummary = "4 / 0 / 0 / 0",
            CornersGradedSummary = "4 / 4",
            IsSelected = false,
            ThumbnailPathData = "M 41,7 L 56,24 L 31,46 L 16,29 Z"
        }
    ];

    /// <summary>
    /// Creates the 4 demo pattern cards from the older PDF manual screenshot (gemini_main_window.jpg).
    /// </summary>
    public static IReadOnlyList<DemoPieceCardItem> CreateReferencePieces() =>
    [
        new DemoPieceCardItem
        {
            Index = 1,
            Name = "Piece 1",
            AreaCm2 = "864,11",
            PerimeterCm = "127,08",
            NotchesInternalSummary = "4 / 0 / 0",
            CornersGradedSummary = "5 / 5",
            IsSelected = false,
            ThumbnailPathData = "M 22,6 L 31,6 L 34,26 C 36,34 39,40 42,43 L 28,46 L 26,48 L 19,46 Z"
        },
        new DemoPieceCardItem
        {
            Index = 2,
            Name = "Piece 2",
            AreaCm2 = "942,30",
            PerimeterCm = "154,20",
            NotchesInternalSummary = "5 / 0 / 0",
            CornersGradedSummary = "5 / 5",
            IsSelected = false,
            ThumbnailPathData = "M 12,18 L 60,21 C 64,22 66,25 67,29 L 10,27 L 9,23 C 11,22 12,20 12,18 Z"
        },
        new DemoPieceCardItem
        {
            Index = 3,
            Name = "Piece 3",
            AreaCm2 = "1213,75",
            PerimeterCm = "167,43",
            NotchesInternalSummary = "6 / 0 / 0",
            CornersGradedSummary = "5 / 5",
            IsSelected = true,
            ThumbnailPathData = "M 10,14 L 46,16 C 47,21 53,23 63,21 L 66,32 C 48,35 28,35 9,33 Z"
        },
        new DemoPieceCardItem
        {
            Index = 4,
            Name = "Piece 4",
            AreaCm2 = "618,40",
            PerimeterCm = "118,60",
            NotchesInternalSummary = "3 / 0 / 0",
            CornersGradedSummary = "4 / 4",
            IsSelected = false,
            ThumbnailPathData = "M 8,16 L 64,21 L 62,31 C 46,30 34,30 24,31 Z"
        }
    ];

    public static IReadOnlyList<string> GetDxfProfiles() =>
    [
        "General",
        "Gemini Cut Plan",
        "Gerber Accumark",
        "Gerber TMP",
        "Lectra Modaris",
        "Lectraiba"
    ];
}
