using System.Windows;
using System.Windows.Media;

namespace PatternDigitizerPro.UI.Controls;

public enum GeminiIconKind
{
    None,
    AppLogo,
    LoadPicture,
    CapturePicture,
    ScanPicture,
    DetectPieces,
    Save,
    Undo,
    Redo,
    ZoomArea,
    ZoomSelected,
    ZoomAll,
    ZoomIn,
    ZoomOut,
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
    DeleteInternalLine,
    AddAuxPiece,
    DeleteAuxPiece,
    ShowGrid,
    ShowPicture,
    ShowNotches,
    ShowInternalLines,
    ShowInternalPoints,
    MeasurePerimeter,
    MeasureDirect,
    PlayContour,
    PlayGradePoints,
    PlayNotches,
    PlayInternalPoints,
    PlayInternalLines,
    ContextHelp,
    Exit
}

/// <summary>
/// Custom pixel-snapped vector icon renderer reproducing all Gemini Photo Digitizer v9.0 ImageListMainTB and GridImageIL icons.
/// Drawn on a 20x20 logical coordinate grid with SnapsToDevicePixels and authentic TTBToolbar monochrome grey disabled state.
/// </summary>
public sealed class GeminiToolbarIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(
            nameof(Kind),
            typeof(GeminiIconKind),
            typeof(GeminiToolbarIcon),
            new FrameworkPropertyMetadata(GeminiIconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDisabledLookProperty =
        DependencyProperty.Register(
            nameof(IsDisabledLook),
            typeof(bool),
            typeof(GeminiToolbarIcon),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    static GeminiToolbarIcon()
    {
        IsEnabledProperty.OverrideMetadata(
            typeof(GeminiToolbarIcon),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public GeminiToolbarIcon()
    {
        Width = 20;
        Height = 20;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public GeminiIconKind Kind
    {
        get => (GeminiIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public bool IsDisabledLook
    {
        get => (bool)GetValue(IsDisabledLookProperty);
        set => SetValue(IsDisabledLookProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsNaN(Width) || Width <= 0 ? 20 : Width;
        double h = double.IsNaN(Height) || Height <= 0 ? 20 : Height;
        return new Size(w, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Kind == GeminiIconKind.None)
        {
            return;
        }

        double scaleX = ActualWidth > 0 ? ActualWidth / 20.0 : 1.0;
        double scaleY = ActualHeight > 0 ? ActualHeight / 20.0 : 1.0;
        dc.PushTransform(new ScaleTransform(scaleX, scaleY));

        bool disabled = !IsEnabled || IsDisabledLook;

        // Shared palette matching Gemini Photo Digitizer v9.0 ImageListMainTB & TTBToolbar disabled silhouette
        Brush disabledGrey = BrushOf("#969696");
        Brush yellowPiece = disabled ? disabledGrey : BrushOf("#FFF200");
        Brush yellowGold = disabled ? disabledGrey : BrushOf("#FFA800");
        Brush blackOutline = disabled ? BrushOf("#888888") : BrushOf("#141414");
        Brush darkGray = disabled ? disabledGrey : BrushOf("#555555");
        Brush lightGray = disabled ? BrushOf("#CFCFCF") : BrushOf("#D4D4D4");
        Brush whiteFill = disabled ? BrushOf("#ECECEC") : Brushes.White;
        Brush redAccent = disabled ? disabledGrey : BrushOf("#E61414");
        Brush greenSmart = disabled ? disabledGrey : BrushOf("#00C800");
        Brush greenPlay = disabled ? disabledGrey : BrushOf("#36B036");
        Brush blueAccent = disabled ? disabledGrey : BrushOf("#2B579A");
        Brush cyanRuler = disabled ? disabledGrey : BrushOf("#00FFFF");

        Pen blackPen1 = new(blackOutline, 1.0);
        Pen grayPen1 = new(darkGray, 1.0);
        Pen redPen1 = new(redAccent, 1.0);
        Pen redPen15 = new(redAccent, 1.6);

        switch (Kind)
        {
            case GeminiIconKind.AppLogo:
                DrawAppLogo(dc, blueAccent, yellowPiece, whiteFill, blackPen1);
                break;

            case GeminiIconKind.LoadPicture:
                DrawLoadPicture(dc, yellowGold, yellowPiece, whiteFill, disabled);
                break;

            case GeminiIconKind.CapturePicture:
                DrawCapturePicture(dc, lightGray, blueAccent, blackPen1);
                break;

            case GeminiIconKind.ScanPicture:
                DrawScanPicture(dc, lightGray, whiteFill, greenPlay, blackPen1);
                break;

            case GeminiIconKind.DetectPieces:
                DrawDetectPieces(dc, greenPlay, disabled);
                break;

            case GeminiIconKind.Save:
                DrawSaveFloppy(dc, whiteFill, disabled);
                break;

            case GeminiIconKind.Undo:
                DrawUndoArrow(dc, disabled ? disabledGrey : BrushOf("#4A76B8"), isRedo: false, disabled);
                break;

            case GeminiIconKind.Redo:
                DrawUndoArrow(dc, disabled ? disabledGrey : BrushOf("#4A76B8"), isRedo: true, disabled);
                break;

            case GeminiIconKind.ZoomArea:
                DrawZoomArea(dc, redPen1, whiteFill, lightGray, blackPen1, blackOutline);
                break;

            case GeminiIconKind.ZoomSelected:
                DrawZoomSelected(dc, yellowPiece, blackPen1, whiteFill, lightGray, blackOutline);
                break;

            case GeminiIconKind.ZoomAll:
                DrawZoomAll(dc, whiteFill, grayPen1, lightGray, blackPen1, blackOutline);
                break;

            case GeminiIconKind.ZoomIn:
                DrawZoomPlusMinus(dc, whiteFill, lightGray, blackPen1, blackOutline, isPlus: true);
                break;

            case GeminiIconKind.ZoomOut:
                DrawZoomPlusMinus(dc, whiteFill, lightGray, blackPen1, blackOutline, isPlus: false);
                break;

            case GeminiIconKind.AddPoint:
                DrawPointTool(dc, yellowPiece, blackPen1, redAccent, isAdd: true);
                break;

            case GeminiIconKind.DeletePoint:
                DrawPointTool(dc, yellowPiece, blackPen1, redAccent, isAdd: false);
                break;

            case GeminiIconKind.SmartCorner:
                DrawCornerTool(dc, yellowPiece, blackPen1, redPen15, isDelete: false);
                break;

            case GeminiIconKind.DeleteCorner:
                DrawCornerTool(dc, yellowPiece, blackPen1, redPen15, isDelete: true);
                break;

            case GeminiIconKind.LineToCurve:
                DrawLineToCurve(dc, grayPen1, blackPen1, whiteFill, disabled);
                break;

            case GeminiIconKind.CurveToLine:
                DrawCurveToLine(dc, grayPen1, blackPen1, whiteFill, disabled);
                break;

            case GeminiIconKind.SmartNotch:
                DrawNotchTool(dc, yellowPiece, blackPen1, greenSmart, blackOutline, redPen15, badgeMode: 0);
                break;

            case GeminiIconKind.AddNotch:
                DrawNotchTool(dc, yellowPiece, blackPen1, redAccent, redAccent, redPen15, badgeMode: 1);
                break;

            case GeminiIconKind.DeleteNotch:
                DrawNotchTool(dc, yellowPiece, blackPen1, redAccent, redAccent, redPen15, badgeMode: 2);
                break;

            case GeminiIconKind.SmartInternalPoint:
                DrawInternalPointTool(dc, yellowPiece, blackPen1, redPen1, greenSmart, blackOutline, badgeMode: 0);
                break;

            case GeminiIconKind.AddInternalPoint:
                DrawInternalPointTool(dc, yellowPiece, blackPen1, redPen1, redAccent, redAccent, badgeMode: 1);
                break;

            case GeminiIconKind.DeleteInternalPoint:
                DrawInternalPointTool(dc, yellowPiece, blackPen1, redPen1, redAccent, redAccent, badgeMode: 2);
                break;

            case GeminiIconKind.SmartInternalLine:
                DrawInternalLineTool(dc, yellowPiece, blackPen1, blackOutline, redAccent, greenSmart, blackOutline, badgeMode: 0);
                break;

            case GeminiIconKind.AddInternalLine:
                DrawInternalLineTool(dc, yellowPiece, blackPen1, blackOutline, null, redAccent, redAccent, badgeMode: 1);
                break;

            case GeminiIconKind.DeleteInternalLine:
                DrawInternalLineTool(dc, yellowPiece, blackPen1, blackOutline, null, redAccent, redAccent, badgeMode: 2);
                break;

            case GeminiIconKind.AddAuxPiece:
                DrawAuxPieceTool(dc, yellowPiece, blackPen1, greenSmart, blackOutline, disabled, isAdd: true);
                break;

            case GeminiIconKind.DeleteAuxPiece:
                DrawAuxPieceTool(dc, yellowPiece, blackPen1, redAccent, redAccent, disabled, isAdd: false);
                break;

            case GeminiIconKind.ShowGrid:
                DrawShowGrid(dc, cyanRuler, blackPen1);
                break;

            case GeminiIconKind.ShowPicture:
                DrawLoadPicture(dc, yellowGold, yellowPiece, whiteFill, disabled);
                break;

            case GeminiIconKind.ShowNotches:
                DrawNotchTool(dc, yellowPiece, blackPen1, greenSmart, blackOutline, redPen15, badgeMode: -1);
                break;

            case GeminiIconKind.ShowInternalLines:
                DrawInternalLineTool(dc, yellowPiece, blackPen1, blackOutline, null, greenSmart, blackOutline, badgeMode: -1);
                break;

            case GeminiIconKind.ShowInternalPoints:
                DrawInternalPointTool(dc, yellowPiece, blackPen1, redPen1, greenSmart, blackOutline, badgeMode: -1);
                break;

            case GeminiIconKind.MeasurePerimeter:
                DrawMeasureTool(dc, yellowPiece, blackPen1, redPen1, isPerimeter: true);
                break;

            case GeminiIconKind.MeasureDirect:
                DrawMeasureTool(dc, yellowPiece, blackPen1, redPen1, isPerimeter: false);
                break;

            case GeminiIconKind.PlayContour:
            case GeminiIconKind.PlayGradePoints:
            case GeminiIconKind.PlayNotches:
            case GeminiIconKind.PlayInternalPoints:
            case GeminiIconKind.PlayInternalLines:
                DrawPlayContour(dc, yellowPiece, whiteFill, redAccent, greenPlay, disabled);
                break;

            case GeminiIconKind.ContextHelp:
                DrawContextHelp(dc, blackOutline, blueAccent);
                break;

            case GeminiIconKind.Exit:
                DrawExitIcon(dc, redAccent, whiteFill);
                break;
        }

        dc.Pop();
    }

    private static SolidColorBrush BrushOf(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static void DrawAppLogo(DrawingContext dc, Brush blue, Brush yellow, Brush white, Pen blackPen)
    {
        dc.DrawRectangle(blue, blackPen, new Rect(2, 2, 16, 16));
        var geom = StreamGeometry.Parse("M 4,15 L 8,5 L 15,8 L 13,15 Z");
        dc.DrawGeometry(yellow, new Pen(white, 1.0), geom);
    }

    private static void DrawLoadPicture(DrawingContext dc, Brush gold, Brush yellow, Brush white, bool disabled)
    {
        Pen borderPen = new(disabled ? BrushOf("#888888") : BrushOf("#DC8800"), 1.0);
        var backFolder = StreamGeometry.Parse("M 1.5,7 L 15,2 L 18,14 L 5,18 Z");
        dc.DrawGeometry(gold, borderPen, backFolder);
        var photoSheet = StreamGeometry.Parse("M 5,5.5 L 14.5,3.5 L 16.5,13.5 L 7,15.5 Z");
        dc.DrawGeometry(white, borderPen, photoSheet);
        var frontFlap = StreamGeometry.Parse("M 2,10.5 L 11,8.5 L 16,14.5 L 5,18 Z");
        dc.DrawGeometry(yellow, borderPen, frontFlap);
    }

    private static void DrawCapturePicture(DrawingContext dc, Brush body, Brush lens, Pen pen)
    {
        dc.DrawRoundedRectangle(body, pen, new Rect(2, 5, 16, 11), 2, 2);
        dc.DrawRectangle(body, pen, new Rect(6, 3, 5, 2));
        dc.DrawEllipse(lens, pen, new Point(10, 10.5), 3.5, 3.5);
    }

    private static void DrawScanPicture(DrawingContext dc, Brush body, Brush sheet, Brush green, Pen pen)
    {
        dc.DrawRectangle(body, pen, new Rect(2, 10, 16, 6));
        var lid = StreamGeometry.Parse("M 2,10 L 15,3 L 18,5 L 5,10 Z");
        dc.DrawGeometry(sheet, pen, lid);
        dc.DrawEllipse(green, null, new Point(15, 13), 1.5, 1.5);
    }

    private static void DrawDetectPieces(DrawingContext dc, Brush greenFill, bool disabled)
    {
        Pen pen = new(disabled ? BrushOf("#7A7A7A") : BrushOf("#227826"), 1.0);
        var tri = StreamGeometry.Parse("M 4,3 L 17,10.5 L 4,17.5 Z");
        dc.DrawGeometry(greenFill, pen, tri);
        if (!disabled)
        {
            var hi = StreamGeometry.Parse("M 5,4.8 L 14.5,10.2 L 5,10.2 Z");
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(85, 180, 255, 180)), null, hi);
        }
    }

    private static void DrawSaveFloppy(DrawingContext dc, Brush white, bool disabled)
    {
        // Matches ImageIndex 47 in ImageListMainTB: classic 13x13 floppy disk centered
        Brush body = disabled ? BrushOf("#969696") : BrushOf("#585848");
        Brush shutter = disabled ? BrushOf("#B8B8B8") : BrushOf("#B8B898");
        Pen border = new(disabled ? BrushOf("#7E7E7E") : BrushOf("#303028"), 1.0);
        dc.DrawRectangle(body, border, new Rect(4, 4, 12, 12));
        dc.DrawRectangle(white, border, new Rect(6, 4, 8, 6));
        dc.DrawRectangle(shutter, border, new Rect(6, 11, 8, 5));
        dc.DrawRectangle(body, null, new Rect(7.5, 12, 2, 3));
    }

    private static void DrawUndoArrow(DrawingContext dc, Brush fill, bool isRedo, bool disabled)
    {
        if (isRedo)
        {
            dc.PushTransform(new ScaleTransform(-1, 1, 10, 10));
        }

        Pen outline = new(disabled ? BrushOf("#888888") : BrushOf("#2B4C7E"), 0.7);
        var arrow = StreamGeometry.Parse("M 8,3.5 L 2.5,8.5 L 8,13 L 8,10 C 11.8,9.2 13.2,12 10.5,16.8 C 15.5,14.8 16.5,7.2 8,6.5 Z");
        dc.DrawGeometry(fill, outline, arrow);

        if (isRedo)
        {
            dc.Pop();
        }
    }

    private static void DrawMagnifier(DrawingContext dc, Brush white, Brush glassShade, Pen blackPen, Brush handleBrush, double cx, double cy, double r)
    {
        dc.DrawEllipse(glassShade, blackPen, new Point(cx, cy), r, r);
        dc.DrawEllipse(white, null, new Point(cx - 1.2, cy - 1.2), r * 0.45, r * 0.45);
        Pen handlePen = new(handleBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(handlePen, new Point(cx + r * 0.7, cy + r * 0.7), new Point(cx + r * 1.65, cy + r * 1.65));
    }

    private static void DrawZoomArea(DrawingContext dc, Pen redPen, Brush white, Brush glassShade, Pen blackPen, Brush handleBrush)
    {
        Pen dottedRed = new(redPen.Brush, 1.0) { DashStyle = new DashStyle([1.8, 1.5], 0) };
        dc.DrawRectangle(null, dottedRed, new Rect(3.5, 2.5, 12.5, 14.0));
        DrawMagnifier(dc, white, glassShade, blackPen, handleBrush, 8.0, 7.5, 4.5);
    }

    private static void DrawZoomSelected(DrawingContext dc, Brush yellowPiece, Pen blackPen, Brush white, Brush glassShade, Brush handleBrush)
    {
        // Matches ImageIndex 4 & GridImageIL index 3: magnifying glass with yellow rectangle and yellow triangle inside lens
        dc.DrawEllipse(white, blackPen, new Point(8.5, 8.0), 5.0, 5.0);
        dc.DrawRectangle(yellowPiece, blackPen, new Rect(5.5, 5.2, 4.2, 3.8));
        var tri = StreamGeometry.Parse("M 7.0,10.8 L 10.2,7.2 L 12.2,10.8 Z");
        dc.DrawGeometry(yellowPiece, blackPen, tri);
        Pen handlePen = new(handleBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(handlePen, new Point(12.0, 11.5), new Point(16.2, 15.8));
    }

    private static void DrawZoomAll(DrawingContext dc, Brush white, Pen grayPen, Brush glassShade, Pen blackPen, Brush handleBrush)
    {
        dc.DrawRectangle(white, grayPen, new Rect(4, 2.5, 12, 14.5));
        DrawMagnifier(dc, white, glassShade, blackPen, handleBrush, 8.0, 8.0, 4.5);
    }

    private static void DrawZoomPlusMinus(DrawingContext dc, Brush white, Brush glassShade, Pen blackPen, Brush handleBrush, bool isPlus)
    {
        dc.DrawEllipse(white, blackPen, new Point(8.5, 8.0), 5.2, 5.2);
        Pen handlePen = new(handleBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(handlePen, new Point(12.2, 11.7), new Point(16.5, 16.0));
        Pen symPen = new(handleBrush, 1.8);
        dc.DrawLine(symPen, new Point(6.0, 8.0), new Point(11.0, 8.0));
        if (isPlus)
        {
            dc.DrawLine(symPen, new Point(8.5, 5.5), new Point(8.5, 10.5));
        }
    }

    private static void DrawPointTool(DrawingContext dc, Brush yellow, Pen blackPen, Brush red, bool isAdd)
    {
        var piece = StreamGeometry.Parse("M 3,4 L 14,4 L 14,15 L 3,15 Z");
        dc.DrawGeometry(yellow, blackPen, piece);
        dc.DrawEllipse(red, blackPen, new Point(9, 9), 2.2, 2.2);
        if (isAdd)
        {
            DrawPlusBadge(dc, red, 14.5, 14.5);
        }
        else
        {
            DrawMinusBadge(dc, red, 14.5, 14.5);
        }
    }

    private static void DrawCornerTool(DrawingContext dc, Brush yellowPiece, Pen blackPen, Pen redPen, bool isDelete)
    {
        var shape = StreamGeometry.Parse("M 2,2.5 L 15,2.5 C 14,10 10,14 2,16.5 Z");
        dc.DrawGeometry(yellowPiece, blackPen, shape);
        dc.DrawLine(redPen, new Point(15, 2.5), new Point(15, 16.5));
        dc.DrawLine(redPen, new Point(2, 16.5), new Point(15, 16.5));
        if (isDelete)
        {
            dc.DrawLine(redPen, new Point(11.5, 11.5), new Point(17.5, 17.5));
            dc.DrawLine(redPen, new Point(17.5, 11.5), new Point(11.5, 17.5));
        }
        else
        {
            dc.DrawRectangle(redPen.Brush, null, new Rect(13.0, 13.0, 3.5, 3.5));
        }
    }

    private static void DrawLineToCurve(DrawingContext dc, Pen grayPen, Pen blackPen, Brush white, bool disabled)
    {
        // Matches ImageIndex 41: curved arc from bottom-left to top-right with control squares
        Pen mainPen = disabled ? new Pen(BrushOf("#909090"), 1.1) : blackPen;
        var arc = StreamGeometry.Parse("M 6,15.5 C 6,8.5 9,6 15,6");
        dc.DrawGeometry(null, mainPen, arc);
        dc.DrawRectangle(white, mainPen, new Rect(4.8, 14.5, 2.2, 2.2));
        dc.DrawRectangle(white, mainPen, new Rect(14.0, 4.8, 2.2, 2.2));
        dc.DrawRectangle(white, mainPen, new Rect(6.8, 7.2, 2.0, 2.0));
    }

    private static void DrawCurveToLine(DrawingContext dc, Pen grayPen, Pen blackPen, Brush white, bool disabled)
    {
        // Matches ImageIndex 42: straight diagonal segment from bottom-left to top-right with endpoint squares
        Pen mainPen = disabled ? new Pen(BrushOf("#909090"), 1.1) : blackPen;
        dc.DrawLine(mainPen, new Point(5.5, 15.0), new Point(14.5, 6.0));
        dc.DrawRectangle(white, mainPen, new Rect(4.2, 14.0, 2.2, 2.2));
        dc.DrawRectangle(white, mainPen, new Rect(13.8, 4.8, 2.2, 2.2));
    }

    private static void DrawNotchTool(DrawingContext dc, Brush yellowPiece, Pen blackPen, Brush badgeFill, Brush badgeOutline, Pen redNotchPen, int badgeMode)
    {
        // Matches ImageIndex 18, 22, 31: yellow sector at top-left with red notch line and + / - badge
        var wedge = StreamGeometry.Parse("M 2,3.5 L 14,3.5 C 12.5,9.5 8.5,13.5 2,15.5 Z");
        dc.DrawGeometry(yellowPiece, blackPen, wedge);
        if (badgeMode == 0)
        {
            dc.DrawLine(new Pen(badgeFill, 2.0), new Point(9.8, 10.2), new Point(6.0, 6.5));
            DrawOutlinedPlusBadge(dc, badgeFill, badgeOutline, 13.8, 14.2);
        }
        else if (badgeMode == 1)
        {
            dc.DrawLine(redNotchPen, new Point(9.8, 10.2), new Point(6.0, 6.5));
            DrawPlusBadge(dc, badgeFill, 13.8, 14.2);
        }
        else if (badgeMode == 2)
        {
            dc.DrawLine(redNotchPen, new Point(9.8, 10.2), new Point(6.0, 6.5));
            DrawMinusBadge(dc, badgeFill, 13.8, 14.2);
        }
        else
        {
            dc.DrawLine(redNotchPen, new Point(9.8, 10.2), new Point(6.0, 6.5));
        }
    }

    private static void DrawInternalPointTool(DrawingContext dc, Brush yellowPiece, Pen blackPen, Pen redPen, Brush badgeFill, Brush badgeOutline, int badgeMode)
    {
        // Matches ImageIndex 30, 17, 21: yellow vertical pattern piece with red dashed crosshair and target circle
        var piece = StreamGeometry.Parse("M 5,2 L 13,2 L 13,16 L 4,16 L 3,10 Z");
        dc.DrawGeometry(yellowPiece, blackPen, piece);

        Pen redDashed = new(redPen.Brush, 1.0) { DashStyle = new DashStyle([1.5, 1.2], 0) };
        dc.DrawLine(redDashed, new Point(1, 10), new Point(15, 10));
        dc.DrawLine(redDashed, new Point(9, 1), new Point(9, 18));

        Brush centerFill = badgeMode == 0 ? badgeFill : yellowPiece;
        dc.DrawEllipse(centerFill, new Pen( badgeMode == 0 ? Brushes.Black : redPen.Brush, 1.2), new Point(9, 10), 2.4, 2.4);

        if (badgeMode == 0)
        {
            DrawOutlinedPlusBadge(dc, badgeFill, badgeOutline, 14.2, 14.8);
        }
        else if (badgeMode == 1)
        {
            DrawPlusBadge(dc, badgeFill, 14.2, 14.8);
        }
        else if (badgeMode == 2)
        {
            DrawMinusBadge(dc, badgeFill, 14.2, 14.8);
        }
    }

    private static void DrawInternalLineTool(DrawingContext dc, Brush yellowPiece, Pen blackPen, Brush arrowBrush, Brush? haloBrush, Brush badgeFill, Brush badgeOutline, int badgeMode)
    {
        // Matches ImageIndex 29, 16, 20: yellow horizontal piece with right-pointing arrow inside
        var piece = StreamGeometry.Parse("M 1,6 L 14,4 L 16,7 L 16,14 L 1,14 Z");
        dc.DrawGeometry(yellowPiece, blackPen, piece);

        if (haloBrush != null)
        {
            Pen haloPen = new(haloBrush, 2.8);
            dc.DrawLine(haloPen, new Point(2.5, 10.0), new Point(12.2, 10.0));
        }

        Pen arrowPen = new(arrowBrush, 1.4);
        dc.DrawLine(arrowPen, new Point(2.5, 10.0), new Point(12.5, 10.0));
        var head = StreamGeometry.Parse("M 9.8,7.5 L 13.2,10.0 L 9.8,12.5 Z");
        dc.DrawGeometry(arrowBrush, null, head);

        if (badgeMode == 0)
        {
            DrawOutlinedPlusBadge(dc, badgeFill, badgeOutline, 14.2, 14.8);
        }
        else if (badgeMode == 1)
        {
            DrawPlusBadge(dc, badgeFill, 14.2, 14.8);
        }
        else if (badgeMode == 2)
        {
            DrawMinusBadge(dc, badgeFill, 14.2, 14.8);
        }
    }

    private static void DrawAuxPieceTool(DrawingContext dc, Brush yellowPiece, Pen blackPen, Brush badgeFill, Brush badgeOutline, bool disabled, bool isAdd)
    {
        // Matches ImageIndex 50 (AddFigAux) and 49 (DelFigAux) in ImageListMainTB & crop_img2_toolbar.png
        if (disabled)
        {
            Brush grey = BrushOf("#969696");
            // Characteristic grey silhouette with right-side notch cutout where the pencil enters and bottom +/-
            var silhouette = isAdd
                ? StreamGeometry.Parse("M 5,3 L 15,3 L 17,5 L 12,10 L 16,14 L 16,16 L 4,16 L 3,10 Z")
                : StreamGeometry.Parse("M 5,3 L 15,3 L 17,5 L 12,10 L 16,14 L 16,16 L 4,16 L 3,10 Z");
            dc.DrawGeometry(grey, null, silhouette);
            if (isAdd)
            {
                DrawPlusBadge(dc, grey, 13.8, 15.2);
            }
            else
            {
                DrawMinusBadge(dc, grey, 13.8, 15.2);
            }
            return;
        }

        var piece = StreamGeometry.Parse("M 4,3 L 14,3 L 15,16 L 4,16 L 3,10 Z");
        dc.DrawGeometry(yellowPiece, blackPen, piece);
        var diamond = StreamGeometry.Parse("M 8.5,7.5 L 12,11 L 8.5,14.5 L 5,11 Z");
        dc.DrawGeometry(isAdd ? BrushOf("#78E048") : yellowPiece, new Pen(isAdd ? Brushes.Black : badgeFill, 1.0), diamond);
        var pencil = StreamGeometry.Parse("M 10,10 L 16,4 L 18,6 L 12,12 Z");
        dc.DrawGeometry(BrushOf("#F0B878"), new Pen(BrushOf("#604020"), 0.8), pencil);

        if (isAdd)
        {
            DrawOutlinedPlusBadge(dc, badgeFill, badgeOutline, 14.2, 15.0);
        }
        else
        {
            DrawMinusBadge(dc, badgeFill, 14.2, 15.0);
        }
    }

    private static void DrawShowGrid(DrawingContext dc, Brush cyanRuler, Pen blackPen)
    {
        // Matches ImageIndex 13: cyan L-square ruler with black outline and tick marks
        var lShape = StreamGeometry.Parse("M 5,3 L 16,3 L 16,7 L 9,7 L 9,16 L 5,16 Z");
        dc.DrawGeometry(cyanRuler, blackPen, lShape);
        dc.DrawLine(blackPen, new Point(8, 3), new Point(8, 5));
        dc.DrawLine(blackPen, new Point(11, 3), new Point(11, 5));
        dc.DrawLine(blackPen, new Point(14, 3), new Point(14, 5));
        dc.DrawLine(blackPen, new Point(5, 9), new Point(7, 9));
        dc.DrawLine(blackPen, new Point(5, 12), new Point(7, 12));
        dc.DrawLine(blackPen, new Point(5, 14.5), new Point(7, 14.5));
    }

    private static void DrawMeasureTool(DrawingContext dc, Brush yellowPiece, Pen blackPen, Pen redPen, bool isPerimeter)
    {
        // Matches ImageIndex 32 & 33
        var piece = StreamGeometry.Parse("M 2,4 L 5,4 C 6,11 9,14 14,17 L 2,17 Z");
        dc.DrawGeometry(yellowPiece, blackPen, piece);
        if (isPerimeter)
        {
            var curveArrow = StreamGeometry.Parse("M 6,3.5 C 7.5,10 10.5,13 16,15.5");
            dc.DrawGeometry(null, blackPen, curveArrow);
        }
        else
        {
            dc.DrawLine(blackPen, new Point(6, 3.5), new Point(16, 15.5));
        }
        dc.DrawRectangle(redPen.Brush, null, new Rect(2.0, 2.5, 2.2, 2.2));
        dc.DrawRectangle(redPen.Brush, null, new Rect(14.8, 15.2, 2.2, 2.2));
    }

    private static void DrawPlayContour(DrawingContext dc, Brush yellowPiece, Brush white, Brush redAccent, Brush greenPlay, bool disabled)
    {
        if (disabled)
        {
            // Authentic TTBToolbar disabled silhouette for ImageIndex 26 (grey right arrow on top + grey lower block)
            Brush grey = BrushOf("#969696");
            var topArrow = StreamGeometry.Parse("M 4,3.5 L 11,3.5 L 11,2 L 15.5,4.8 L 11,7.5 L 11,6 L 4,6 Z");
            dc.DrawGeometry(grey, null, topArrow);
            dc.DrawRectangle(grey, null, new Rect(2, 8.5, 15, 8.5));
            return;
        }

        dc.DrawRectangle(white, null, new Rect(2, 2, 15, 6));
        dc.DrawRectangle(yellowPiece, null, new Rect(2, 9, 15, 8));
        Pen redBar = new(redAccent, 2.2);
        dc.DrawLine(redBar, new Point(2, 9), new Point(17, 9));
        dc.DrawRectangle(Brushes.Black, null, new Rect(2, 7.5, 2.5, 3.0));
        dc.DrawRectangle(Brushes.Black, null, new Rect(14.5, 7.5, 2.5, 3.0));

        var arrow = StreamGeometry.Parse("M 4,4.5 L 11,4.5 L 11,2.5 L 15,5.2 L 11,7.8 L 11,5.8 L 4,5.8 Z");
        dc.DrawGeometry(greenPlay, new Pen(Brushes.Black, 0.7), arrow);
    }

    private static void DrawContextHelp(DrawingContext dc, Brush blackOutline, Brush blueAccent)
    {
        // Matches ImageIndex 48 (TBCeiAsta): black left pointer arrow + 3D blue question mark '?'
        var pointer = StreamGeometry.Parse("M 2.5,4.5 L 2.5,16.5 L 5.2,13.8 L 7.0,17.5 L 8.6,16.7 L 6.8,13.0 L 10.2,13.0 Z");
        dc.DrawGeometry(blackOutline, null, pointer);

        Pen qShadow = new(BrushOf("#1A3560"), 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        Pen qMain = new(BrushOf("#3B73C4"), 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var qCurve = StreamGeometry.Parse("M 11.2,6.5 C 11.2,3.2 16.8,3.2 16.8,6.8 C 16.8,9.2 13.8,9.8 13.8,12.5");
        dc.DrawGeometry(null, qShadow, qCurve);
        dc.DrawGeometry(null, qMain, qCurve);
        dc.DrawEllipse(BrushOf("#2B579A"), new Pen(BrushOf("#1A3560"), 0.6), new Point(13.8, 15.8), 1.4, 1.4);
    }

    private static void DrawExitIcon(DrawingContext dc, Brush red, Brush white)
    {
        dc.DrawRoundedRectangle(red, new Pen(Brushes.DarkRed, 1.0), new Rect(3, 3, 14, 14), 2, 2);
        Pen wPen = new(white, 1.6);
        dc.DrawEllipse(null, wPen, new Point(10, 10.5), 4.0, 4.0);
        dc.DrawLine(wPen, new Point(10, 4.8), new Point(10, 9.5));
    }

    private static void DrawOutlinedPlusBadge(DrawingContext dc, Brush fill, Brush outline, double cx, double cy)
    {
        var plus = StreamGeometry.Parse(
            $"M {cx - 1.2:F1},{cy - 3.8:F1} L {cx + 1.2:F1},{cy - 3.8:F1} L {cx + 1.2:F1},{cy - 1.2:F1} " +
            $"L {cx + 3.8:F1},{cy - 1.2:F1} L {cx + 3.8:F1},{cy + 1.2:F1} L {cx + 1.2:F1},{cy + 1.2:F1} " +
            $"L {cx + 1.2:F1},{cy + 3.8:F1} L {cx - 1.2:F1},{cy + 3.8:F1} L {cx - 1.2:F1},{cy + 1.2:F1} " +
            $"L {cx - 3.8:F1},{cy + 1.2:F1} L {cx - 3.8:F1},{cy - 1.2:F1} L {cx - 1.2:F1},{cy - 1.2:F1} Z");
        dc.DrawGeometry(fill, new Pen(outline, 0.8), plus);
    }

    private static void DrawPlusBadge(DrawingContext dc, Brush fill, double cx, double cy)
    {
        Pen p = new(fill, 2.3);
        dc.DrawLine(p, new Point(cx - 3.8, cy), new Point(cx + 3.8, cy));
        dc.DrawLine(p, new Point(cx, cy - 3.8), new Point(cx, cy + 3.8));
    }

    private static void DrawMinusBadge(DrawingContext dc, Brush fill, double cx, double cy)
    {
        dc.DrawRectangle(fill, null, new Rect(cx - 3.8, cy - 1.3, 7.6, 2.6));
    }
}
