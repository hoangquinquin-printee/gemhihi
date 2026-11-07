using System.Text.Json.Serialization;
using System.Windows;

namespace PatternDigitizerPro.Core.Models;

/// <summary>
/// Point in raw source image pixel coordinates (Origin: top-left (0,0), +X right, +Y down).
/// </summary>
public readonly record struct ImagePixelPoint(double X, double Y)
{
    public double DistanceTo(ImagePixelPoint other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static ImagePixelPoint operator +(ImagePixelPoint a, ImagePixelPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static ImagePixelPoint operator -(ImagePixelPoint a, ImagePixelPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static ImagePixelPoint operator *(ImagePixelPoint a, double s) => new(a.X * s, a.Y * s);
}

/// <summary>
/// Point in viewport screen coordinates after zoom and pan transform.
/// </summary>
public readonly record struct ViewportPoint(double X, double Y)
{
    public double DistanceTo(ViewportPoint other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

/// <summary>
/// Point in calibrated physical coordinates on the digitizer board (stored in millimeters).
/// </summary>
public readonly record struct PhysicalPointMm(double XMm, double YMm)
{
    [JsonIgnore]
    public double XCm => XMm / 10.0;

    [JsonIgnore]
    public double YCm => YMm / 10.0;

    public static PhysicalPointMm FromCm(double xCm, double yCm) => new(xCm * 10.0, yCm * 10.0);

    public double DistanceTo(PhysicalPointMm other)
    {
        double dx = XMm - other.XMm;
        double dy = YMm - other.YMm;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public PhysicalPointMm Lerp(PhysicalPointMm target, double t) =>
        new(XMm + ((target.XMm - XMm) * t), YMm + ((target.YMm - YMm) * t));

    public static PhysicalPointMm operator +(PhysicalPointMm a, PhysicalPointMm b) => new(a.XMm + b.XMm, a.YMm + b.YMm);
    public static PhysicalPointMm operator -(PhysicalPointMm a, PhysicalPointMm b) => new(a.XMm - b.XMm, a.YMm - b.YMm);
    public static PhysicalPointMm operator *(PhysicalPointMm a, double s) => new(a.XMm * s, a.YMm * s);
}

/// <summary>
/// Supported calibration modes in Pattern Digitizer Pro.
/// </summary>
public enum CalibrationMode
{
    Unset = 0,
    GridModeA = 1,
    ManualRectangleModeB = 2,
    ResolutionDpi = 3
}

/// <summary>
/// Pixel crop box used in Calibration Window and ROI-restricted detection.
/// </summary>
public readonly record struct CropBoxPixels(int StartX, int StartY, int StopX, int StopY)
{
    [JsonIgnore]
    public int Width => Math.Max(0, StopX - StartX);

    [JsonIgnore]
    public int Height => Math.Max(0, StopY - StartY);

    [JsonIgnore]
    public bool IsValid => StopX > StartX && StopY > StartY && StartX >= 0 && StartY >= 0;

    public bool Contains(double x, double y) =>
        x >= StartX && x <= StopX && y >= StartY && y <= StopY;
}

/// <summary>
/// A rigid image-space digitize frame.  Unlike <see cref="CropBoxPixels"/>,
/// its four corners stay rectangular while the operator rotates it to match a
/// tilted photograph.  Width and height are expressed along the frame's local
/// horizontal and vertical axes, never along the screen axes.
/// </summary>
public readonly record struct RotatedFramePixels(
    double CenterX,
    double CenterY,
    double Width,
    double Height,
    double RotationDegrees)
{
    [JsonIgnore]
    public bool IsValid =>
        double.IsFinite(CenterX) && double.IsFinite(CenterY) &&
        double.IsFinite(Width) && double.IsFinite(Height) &&
        double.IsFinite(RotationDegrees) && Width >= 40 && Height >= 40;

    [JsonIgnore]
    public double RotationRadians => RotationDegrees * Math.PI / 180.0;

    public static RotatedFramePixels FromCropBox(CropBoxPixels box, double rotationDegrees = 0) =>
        new(box.StartX + (box.Width * 0.5), box.StartY + (box.Height * 0.5), box.Width, box.Height, rotationDegrees);

    public IReadOnlyList<ImagePixelPoint> Corners
    {
        get
        {
            double a = RotationRadians;
            double cos = Math.Cos(a), sin = Math.Sin(a);
            double hx = Width * 0.5, hy = Height * 0.5;
            return
            [
                Transform(-hx, -hy, cos, sin),
                Transform( hx, -hy, cos, sin),
                Transform( hx,  hy, cos, sin),
                Transform(-hx,  hy, cos, sin)
            ];
        }
    }

    public CropBoxPixels BoundingBox
    {
        get
        {
            IReadOnlyList<ImagePixelPoint> corners = Corners;
            int minX = (int)Math.Floor(corners.Min(p => p.X));
            int minY = (int)Math.Floor(corners.Min(p => p.Y));
            int maxX = (int)Math.Ceiling(corners.Max(p => p.X));
            int maxY = (int)Math.Ceiling(corners.Max(p => p.Y));
            return new CropBoxPixels(minX, minY, maxX, maxY);
        }
    }

    public bool Contains(double x, double y)
    {
        double dx = x - CenterX, dy = y - CenterY;
        double a = RotationRadians;
        double localX = (dx * Math.Cos(a)) + (dy * Math.Sin(a));
        double localY = (-dx * Math.Sin(a)) + (dy * Math.Cos(a));
        return Math.Abs(localX) <= (Width * 0.5) && Math.Abs(localY) <= (Height * 0.5);
    }

    public bool IsInsideImage(int imageWidth, int imageHeight) =>
        IsValid && Corners.All(p => p.X >= 0 && p.Y >= 0 && p.X <= imageWidth && p.Y <= imageHeight);

    private ImagePixelPoint Transform(double localX, double localY, double cos, double sin) =>
        new(CenterX + (localX * cos) - (localY * sin), CenterY + (localX * sin) + (localY * cos));
}

/// <summary>
/// Complete calibration state mapping between ImagePixelPoint and PhysicalPointMm.
/// Supports both Mode A (Grid Calibration) and Mode B (Manual X/Y Rectangle Calibration).
/// </summary>
public sealed class CalibrationProfile
{
    public CalibrationMode Mode { get; set; } = CalibrationMode.GridModeA;
    public double ScaleXMmPerPx { get; set; } = 1350.0 / 2918.0;
    public double ScaleYMmPerPx { get; set; } = 1000.0 / 2214.0;
    public double CellWidthCm { get; set; } = 5.0;
    public double CellHeightCm { get; set; } = 5.0;
    public int GridColumns { get; set; } = 27;
    public int GridRows { get; set; } = 20;
    public int GridLeadingBoundaryColumns { get; set; }
    public int GridTrailingBoundaryColumns { get; set; }
    public int GridLeadingBoundaryRows { get; set; }
    public int GridTrailingBoundaryRows { get; set; }
    public double ActiveAreaWidthCm { get; set; } = 135.0;
    public double ActiveAreaHeightCm { get; set; } = 100.0;
    public int ImageWidthPx { get; set; } = 2918;
    public int ImageHeightPx { get; set; } = 2214;
    public CropBoxPixels CropBox { get; set; } = new(0, 0, 2918, 2214);
    public bool RestrictDetectionToCropBox { get; set; }
    /// <summary>Optional rigid ROI mask for a phone-photo frame rotated relative to image axes.</summary>
    public RotatedFramePixels? RotatedDetectionFrame { get; set; }
    public List<double> VerticalGridLinesPx { get; set; } = [];
    public List<double> HorizontalGridLinesPx { get; set; } = [];
    public List<ImagePixelPoint> DetectedIntersections { get; set; } = [];
    public List<double> IntersectionResidualsMm { get; set; } = [];
    /// <summary>
    /// Optional row-major 3x3 projective transform from source-image pixels to
    /// calibrated millimetres. Grid Mode A populates this from all detected
    /// intersections so perspective is corrected instead of merely measured.
    /// Other calibration modes intentionally use the independent X/Y scales.
    /// </summary>
    public List<double> PixelToPhysicalTransform { get; set; } = [];
    public List<double> PhysicalToPixelTransform { get; set; } = [];
    /// <summary>
    /// Normalized polynomial image-to-board warp. Layout: source mean/scale
    /// (3), target mean/scale (3), then either six quadratic or ten cubic
    /// coefficients per output axis. Cubic is accepted only when independent
    /// spatial holdout improves over quadratic.
    /// </summary>
    public List<double> PixelToPhysicalWarp { get; set; } = [];
    public double MeanResidualErrorMm { get; set; }
    public double MaxResidualErrorMm { get; set; }
    public double ProjectiveMeanResidualErrorMm { get; set; }
    public double ProjectiveMaxResidualErrorMm { get; set; }
    public double HoldoutMeanResidualErrorMm { get; set; }
    public double HoldoutMaxResidualErrorMm { get; set; }
    public string SourceImagePath { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsValid =>
        ScaleXMmPerPx > 1e-6 &&
        ScaleYMmPerPx > 1e-6 &&
        !double.IsNaN(ScaleXMmPerPx) &&
        !double.IsNaN(ScaleYMmPerPx) &&
        !double.IsInfinity(ScaleXMmPerPx) &&
        !double.IsInfinity(ScaleYMmPerPx);

    [JsonIgnore]
    public double HorizontalDpi => IsValid ? 25.4 / ScaleXMmPerPx : 0.0;

    [JsonIgnore]
    public double VerticalDpi => IsValid ? 25.4 / ScaleYMmPerPx : 0.0;

    public PhysicalPointMm PixelToPhysicalMm(ImagePixelPoint pixelPoint)
    {
        if (TryMapPixelThroughGrid(pixelPoint, out PhysicalPointMm gridPoint))
        {
            return gridPoint;
        }

        if (TryApplyPolynomialWarp(PixelToPhysicalWarp, pixelPoint.X, pixelPoint.Y, out double warpXmm, out double warpYmm))
        {
            return new PhysicalPointMm(warpXmm, warpYmm);
        }

        if (TryApplyProjective(PixelToPhysicalTransform, pixelPoint.X, pixelPoint.Y, out double xMm, out double yMm))
        {
            return new PhysicalPointMm(xMm, yMm);
        }

        return new PhysicalPointMm(
            pixelPoint.X * ScaleXMmPerPx,
            pixelPoint.Y * ScaleYMmPerPx);
    }

    public ImagePixelPoint PhysicalMmToPixel(PhysicalPointMm physicalPoint)
    {
        if (TryMapPhysicalThroughGrid(physicalPoint, out ImagePixelPoint gridPoint))
        {
            return gridPoint;
        }

        if (PixelToPhysicalWarp.Count is 18 or 26 &&
            TryInvertPolynomialWarp(PixelToPhysicalWarp, PhysicalToPixelTransform, physicalPoint.XMm, physicalPoint.YMm, out double warpXpx, out double warpYpx))
        {
            return new ImagePixelPoint(warpXpx, warpYpx);
        }

        if (TryApplyProjective(PhysicalToPixelTransform, physicalPoint.XMm, physicalPoint.YMm, out double xPx, out double yPx))
        {
            return new ImagePixelPoint(xPx, yPx);
        }

        double sx = ScaleXMmPerPx > 1e-9 ? ScaleXMmPerPx : 1.0;
        double sy = ScaleYMmPerPx > 1e-9 ? ScaleYMmPerPx : 1.0;
        return new ImagePixelPoint(
            physicalPoint.XMm / sx,
            physicalPoint.YMm / sy);
    }

    /// <summary>
    /// Converts an image-space drag into its physical displacement using the
    /// active mapping at both endpoints. This is essential for projective or
    /// polynomial calibration, where mm-per-pixel is not spatially constant.
    /// </summary>
    public PhysicalPointMm PixelDragDeltaToPhysicalMm(ImagePixelPoint previousPixel, ImagePixelPoint currentPixel)
    {
        PhysicalPointMm previous = PixelToPhysicalMm(previousPixel);
        PhysicalPointMm current = PixelToPhysicalMm(currentPixel);
        return new PhysicalPointMm(current.XMm - previous.XMm, current.YMm - previous.YMm);
    }

    /// <summary>
    /// Maps a pixel inside a measured grid quad with inverse bilinear interpolation.
    /// This preserves every measured intersection exactly and avoids forcing a single
    /// global polynomial scale on an image with local lens/perspective distortion.
    /// Outside the measured lattice, callers retain the polynomial/projective fallback.
    /// </summary>
    private bool TryMapPixelThroughGrid(ImagePixelPoint pixelPoint, out PhysicalPointMm physicalPoint)
    {
        physicalPoint = default;
        int columns = VerticalGridLinesPx.Count;
        int rows = HorizontalGridLinesPx.Count;
        if (Mode != CalibrationMode.GridModeA || columns < 2 || rows < 2 ||
            DetectedIntersections.Count != columns * rows || CellWidthCm <= 0 || CellHeightCm <= 0)
        {
            return false;
        }

        for (int row = 0; row < rows - 1; row++)
        {
            for (int column = 0; column < columns - 1; column++)
            {
                ImagePixelPoint topLeft = DetectedIntersections[(row * columns) + column];
                ImagePixelPoint topRight = DetectedIntersections[(row * columns) + column + 1];
                ImagePixelPoint bottomLeft = DetectedIntersections[((row + 1) * columns) + column];
                ImagePixelPoint bottomRight = DetectedIntersections[((row + 1) * columns) + column + 1];
                if (!TryInvertBilinear(topLeft, topRight, bottomLeft, bottomRight, pixelPoint, out double u, out double v) ||
                    u < -0.002 || u > 1.002 || v < -0.002 || v > 1.002)
                {
                    continue;
                }

                physicalPoint = new PhysicalPointMm(
                    (GridLeadingBoundaryColumns + column + Math.Clamp(u, 0.0, 1.0)) * CellWidthCm * 10.0,
                    (GridLeadingBoundaryRows + row + Math.Clamp(v, 0.0, 1.0)) * CellHeightCm * 10.0);
                return true;
            }
        }

        return false;
    }

    private bool TryMapPhysicalThroughGrid(PhysicalPointMm physicalPoint, out ImagePixelPoint pixelPoint)
    {
        pixelPoint = default;
        int columns = VerticalGridLinesPx.Count;
        int rows = HorizontalGridLinesPx.Count;
        double cellWidthMm = CellWidthCm * 10.0;
        double cellHeightMm = CellHeightCm * 10.0;
        if (Mode != CalibrationMode.GridModeA || columns < 2 || rows < 2 ||
            DetectedIntersections.Count != columns * rows || cellWidthMm <= 0 || cellHeightMm <= 0)
        {
            return false;
        }

        double gridX = (physicalPoint.XMm / cellWidthMm) - GridLeadingBoundaryColumns;
        double gridY = (physicalPoint.YMm / cellHeightMm) - GridLeadingBoundaryRows;
        if (gridX < -1e-9 || gridY < -1e-9 || gridX > (columns - 1) + 1e-9 || gridY > (rows - 1) + 1e-9)
        {
            return false;
        }

        int column = Math.Min(columns - 2, Math.Max(0, (int)Math.Floor(gridX)));
        int row = Math.Min(rows - 2, Math.Max(0, (int)Math.Floor(gridY)));
        double u = Math.Clamp(gridX - column, 0.0, 1.0);
        double v = Math.Clamp(gridY - row, 0.0, 1.0);
        ImagePixelPoint topLeft = DetectedIntersections[(row * columns) + column];
        ImagePixelPoint topRight = DetectedIntersections[(row * columns) + column + 1];
        ImagePixelPoint bottomLeft = DetectedIntersections[((row + 1) * columns) + column];
        ImagePixelPoint bottomRight = DetectedIntersections[((row + 1) * columns) + column + 1];
        pixelPoint = Bilinear(topLeft, topRight, bottomLeft, bottomRight, u, v);
        return true;
    }

    private static ImagePixelPoint Bilinear(
        ImagePixelPoint topLeft,
        ImagePixelPoint topRight,
        ImagePixelPoint bottomLeft,
        ImagePixelPoint bottomRight,
        double u,
        double v)
    {
        double topX = topLeft.X + ((topRight.X - topLeft.X) * u);
        double topY = topLeft.Y + ((topRight.Y - topLeft.Y) * u);
        double bottomX = bottomLeft.X + ((bottomRight.X - bottomLeft.X) * u);
        double bottomY = bottomLeft.Y + ((bottomRight.Y - bottomLeft.Y) * u);
        return new ImagePixelPoint(topX + ((bottomX - topX) * v), topY + ((bottomY - topY) * v));
    }

    private static bool TryInvertBilinear(
        ImagePixelPoint topLeft,
        ImagePixelPoint topRight,
        ImagePixelPoint bottomLeft,
        ImagePixelPoint bottomRight,
        ImagePixelPoint target,
        out double u,
        out double v)
    {
        u = 0.5;
        v = 0.5;
        for (int iteration = 0; iteration < 10; iteration++)
        {
            ImagePixelPoint current = Bilinear(topLeft, topRight, bottomLeft, bottomRight, u, v);
            double errorX = current.X - target.X;
            double errorY = current.Y - target.Y;
            if ((errorX * errorX) + (errorY * errorY) < 1e-8)
            {
                return true;
            }

            double derivativeUx = ((topRight.X - topLeft.X) * (1.0 - v)) + ((bottomRight.X - bottomLeft.X) * v);
            double derivativeUy = ((topRight.Y - topLeft.Y) * (1.0 - v)) + ((bottomRight.Y - bottomLeft.Y) * v);
            double derivativeVx = ((bottomLeft.X - topLeft.X) * (1.0 - u)) + ((bottomRight.X - topRight.X) * u);
            double derivativeVy = ((bottomLeft.Y - topLeft.Y) * (1.0 - u)) + ((bottomRight.Y - topRight.Y) * u);
            double determinant = (derivativeUx * derivativeVy) - (derivativeVx * derivativeUy);
            if (Math.Abs(determinant) < 1e-10)
            {
                return false;
            }

            double deltaU = ((derivativeVy * errorX) - (derivativeVx * errorY)) / determinant;
            double deltaV = ((-derivativeUy * errorX) + (derivativeUx * errorY)) / determinant;
            u -= deltaU;
            v -= deltaV;
            if (!double.IsFinite(u) || !double.IsFinite(v) || Math.Abs(u) > 3.0 || Math.Abs(v) > 3.0)
            {
                return false;
            }
        }

        ImagePixelPoint final = Bilinear(topLeft, topRight, bottomLeft, bottomRight, u, v);
        double finalX = final.X - target.X;
        double finalY = final.Y - target.Y;
        return (finalX * finalX) + (finalY * finalY) < 0.01;
    }

    private static bool TryApplyProjective(IReadOnlyList<double>? m, double x, double y, out double tx, out double ty)
    {
        tx = 0;
        ty = 0;
        if (m is null || m.Count != 9)
        {
            return false;
        }

        double w = (m[6] * x) + (m[7] * y) + m[8];
        if (Math.Abs(w) < 1e-10 || double.IsNaN(w) || double.IsInfinity(w))
        {
            return false;
        }

        tx = ((m[0] * x) + (m[1] * y) + m[2]) / w;
        ty = ((m[3] * x) + (m[4] * y) + m[5]) / w;
        return double.IsFinite(tx) && double.IsFinite(ty);
    }

    private static bool TryApplyPolynomialWarp(IReadOnlyList<double>? w, double x, double y, out double tx, out double ty)
    {
        tx = 0;
        ty = 0;
        if (w is null || (w.Count != 18 && w.Count != 26) || Math.Abs(w[2]) < 1e-12 || Math.Abs(w[5]) < 1e-12)
        {
            return false;
        }

        double nx = (x - w[0]) * w[2];
        double ny = (y - w[1]) * w[2];
        int termCount = (w.Count - 6) / 2;
        bool cubic = termCount == 10;
        double[] basis = cubic
            ? [1.0, nx, ny, nx * nx, nx * ny, ny * ny, nx * nx * nx, nx * nx * ny, nx * ny * ny, ny * ny * ny]
            : [1.0, nx, ny, nx * nx, nx * ny, ny * ny];
        double ux = 0;
        double uy = 0;
        for (int k = 0; k < termCount; k++)
        {
            ux += w[6 + k] * basis[k];
            uy += w[6 + termCount + k] * basis[k];
        }

        tx = (ux / w[5]) + w[3];
        ty = (uy / w[5]) + w[4];
        return double.IsFinite(tx) && double.IsFinite(ty);
    }

    private static bool TryInvertPolynomialWarp(
        IReadOnlyList<double> warp,
        IReadOnlyList<double> initialInverse,
        double targetX,
        double targetY,
        out double x,
        out double y)
    {
        if (!TryApplyProjective(initialInverse, targetX, targetY, out x, out y))
        {
            x = targetX / Math.Max(ScaleFallback(warp), 1e-6);
            y = targetY / Math.Max(ScaleFallback(warp), 1e-6);
        }

        const double epsilonPx = 0.25;
        for (int iteration = 0; iteration < 12; iteration++)
        {
            if (!TryApplyPolynomialWarp(warp, x, y, out double fx, out double fy))
            {
                return false;
            }

            double ex = fx - targetX;
            double ey = fy - targetY;
            if (Math.Sqrt((ex * ex) + (ey * ey)) < 1e-7)
            {
                return true;
            }

            TryApplyPolynomialWarp(warp, x + epsilonPx, y, out double fxDx, out double fyDx);
            TryApplyPolynomialWarp(warp, x, y + epsilonPx, out double fxDy, out double fyDy);
            double j00 = (fxDx - fx) / epsilonPx;
            double j10 = (fyDx - fy) / epsilonPx;
            double j01 = (fxDy - fx) / epsilonPx;
            double j11 = (fyDy - fy) / epsilonPx;
            double det = (j00 * j11) - (j01 * j10);
            if (Math.Abs(det) < 1e-12)
            {
                return false;
            }

            double dx = ((j11 * ex) - (j01 * ey)) / det;
            double dy = ((-j10 * ex) + (j00 * ey)) / det;
            x -= dx;
            y -= dy;
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                return false;
            }
        }

        return TryApplyPolynomialWarp(warp, x, y, out double finalX, out double finalY) &&
               Math.Sqrt(((finalX - targetX) * (finalX - targetX)) + ((finalY - targetY) * (finalY - targetY))) < 1e-4;
    }

    private static double ScaleFallback(IReadOnlyList<double> warp) => warp.Count is 18 or 26 ? 1.0 / Math.Max(warp[2], 1e-9) : 1.0;

    public string FormatHeaderSummary(int currentImageWidth, int currentImageHeight)
    {
        int w = currentImageWidth > 0 ? currentImageWidth : ImageWidthPx;
        int h = currentImageHeight > 0 ? currentImageHeight : ImageHeightPx;
        return $"Picture resolution: {w} x {h}    Grid dimensions: {ActiveAreaWidthCm:0.##}cm x {ActiveAreaHeightCm:0.##}cm";
    }

    public CalibrationProfile Clone() => new()
    {
        Mode = Mode,
        ScaleXMmPerPx = ScaleXMmPerPx,
        ScaleYMmPerPx = ScaleYMmPerPx,
        CellWidthCm = CellWidthCm,
        CellHeightCm = CellHeightCm,
        GridColumns = GridColumns,
        GridRows = GridRows,
        GridLeadingBoundaryColumns = GridLeadingBoundaryColumns,
        GridTrailingBoundaryColumns = GridTrailingBoundaryColumns,
        GridLeadingBoundaryRows = GridLeadingBoundaryRows,
        GridTrailingBoundaryRows = GridTrailingBoundaryRows,
        ActiveAreaWidthCm = ActiveAreaWidthCm,
        ActiveAreaHeightCm = ActiveAreaHeightCm,
        ImageWidthPx = ImageWidthPx,
        ImageHeightPx = ImageHeightPx,
        CropBox = CropBox,
        RestrictDetectionToCropBox = RestrictDetectionToCropBox,
        RotatedDetectionFrame = RotatedDetectionFrame,
        VerticalGridLinesPx = [.. VerticalGridLinesPx],
        HorizontalGridLinesPx = [.. HorizontalGridLinesPx],
        DetectedIntersections = [.. DetectedIntersections],
        IntersectionResidualsMm = [.. IntersectionResidualsMm],
        PixelToPhysicalTransform = [.. PixelToPhysicalTransform],
        PhysicalToPixelTransform = [.. PhysicalToPixelTransform],
        PixelToPhysicalWarp = [.. PixelToPhysicalWarp],
        MeanResidualErrorMm = MeanResidualErrorMm,
        MaxResidualErrorMm = MaxResidualErrorMm,
        ProjectiveMeanResidualErrorMm = ProjectiveMeanResidualErrorMm,
        ProjectiveMaxResidualErrorMm = ProjectiveMaxResidualErrorMm,
        HoldoutMeanResidualErrorMm = HoldoutMeanResidualErrorMm,
        HoldoutMaxResidualErrorMm = HoldoutMaxResidualErrorMm,
        SourceImagePath = SourceImagePath
    };

    /// <summary>
    /// Creates the default Gemini v9.0 calibration profile matching Ảnh 1-5 (2918x2214 -> 135cm x 100cm, 27x20 cells of 5x5cm).
    /// For 3456x2592 images from the same camera, GridPitch is ~120.3px per 5cm cell (0.4156 mm/px).
    /// </summary>
    public static CalibrationProfile CreateDefaultV9GridProfile(int imageWidth = 3456, int imageHeight = 2592)
    {
        // 27x20 grid of 5cm cells = 1350mm x 1000mm active area (~120.3 px per cell on 3456x2592 sensor)
        double scaleX = imageWidth == 2918 ? (1350.0 / 2918.0) : (50.0 / 120.3);
        double scaleY = imageHeight == 2214 ? (1000.0 / 2214.0) : (50.0 / 120.1);
        return new CalibrationProfile
        {
            Mode = CalibrationMode.GridModeA,
            ScaleXMmPerPx = scaleX,
            ScaleYMmPerPx = scaleY,
            CellWidthCm = 5.0,
            CellHeightCm = 5.0,
            GridColumns = 27,
            GridRows = 20,
            ActiveAreaWidthCm = 135.0,
            ActiveAreaHeightCm = 100.0,
            ImageWidthPx = imageWidth,
            ImageHeightPx = imageHeight,
            CropBox = new CropBoxPixels(0, 0, imageWidth, imageHeight),
            RestrictDetectionToCropBox = false
        };
    }
}

/// <summary>
/// Vertex along a closed pattern piece outer contour.
/// Stores position and attached Cubic Bézier control handles in physical millimeters (mm).
/// Moving a vertex translates both PositionMm and its attached InControlHandleMm / OutControlHandleMm
/// by the same delta (matching verified Gemini native behavior in MiscaPuncteSimple / FUN_004b8b1c).
/// </summary>
public sealed class ContourVertex
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PhysicalPointMm PositionMm { get; set; }
    public ImagePixelPoint SourcePixelPosition { get; set; }
    public bool IsGradedCorner { get; set; } = true;

    [JsonIgnore]
    public bool IsCurvePoint
    {
        get => !IsGradedCorner;
        set => IsGradedCorner = !value;
    }

    public bool IsPieceOrigin { get; set; }

    /// <summary>
    /// True if the outgoing segment from this vertex to the next vertex (i -> i+1) is a Cubic Bézier curve;
    /// false if it is a straight line segment.
    /// </summary>
    public bool OutgoingIsCurve { get; set; }

    /// <summary>
    /// Incoming cubic Bézier control handle (C2 of segment (i-1 -> i)), in absolute physical mm coordinates.
    /// </summary>
    public PhysicalPointMm InControlHandleMm { get; set; }

    /// <summary>
    /// Outgoing cubic Bézier control handle (C1 of segment (i -> i+1)), in absolute physical mm coordinates.
    /// </summary>
    public PhysicalPointMm OutControlHandleMm { get; set; }

    public ContourVertex Clone() => new()
    {
        Id = Id,
        PositionMm = PositionMm,
        SourcePixelPosition = SourcePixelPosition,
        IsGradedCorner = IsGradedCorner,
        IsPieceOrigin = IsPieceOrigin,
        OutgoingIsCurve = OutgoingIsCurve,
        InControlHandleMm = InControlHandleMm,
        OutControlHandleMm = OutControlHandleMm
    };
}

/// <summary>
/// Represents a single contour segment (either straight line or Cubic Bézier curve) between P0 and P3.
/// </summary>
public readonly record struct CubicBezierSegment(
    PhysicalPointMm P0,
    PhysicalPointMm C1,
    PhysicalPointMm C2,
    PhysicalPointMm P3,
    bool IsCurve)
{
    public PhysicalPointMm Evaluate(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (!IsCurve)
        {
            return P0.Lerp(P3, t);
        }

        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        double uuu = uu * u;
        double ttt = tt * t;

        double x = (uuu * P0.XMm) +
                   (3.0 * uu * t * C1.XMm) +
                   (3.0 * u * tt * C2.XMm) +
                   (ttt * P3.XMm);
        double y = (uuu * P0.YMm) +
                   (3.0 * uu * t * C1.YMm) +
                   (3.0 * u * tt * C2.YMm) +
                   (ttt * P3.YMm);
        return new PhysicalPointMm(x, y);
    }

    public PhysicalPointMm Derivative(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (!IsCurve)
        {
            return P3 - P0;
        }

        double u = 1.0 - t;
        double dx = (3.0 * u * u * (C1.XMm - P0.XMm)) +
                    (6.0 * u * t * (C2.XMm - C1.XMm)) +
                    (3.0 * t * t * (P3.XMm - C2.XMm));
        double dy = (3.0 * u * u * (C1.YMm - P0.YMm)) +
                    (6.0 * u * t * (C2.YMm - C1.YMm)) +
                    (3.0 * t * t * (P3.YMm - C2.YMm));
        return new PhysicalPointMm(dx, dy);
    }

    /// <summary>
    /// Exact De Casteljau subdivision at parameter t in (0, 1) matching Gemini's SplitCurve (FUN_004bdc70).
    /// Preserves the exact geometric path of the curve.
    /// </summary>
    public (CubicBezierSegment Left, CubicBezierSegment Right, PhysicalPointMm SplitPoint) SplitAt(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (!IsCurve)
        {
            PhysicalPointMm mid = P0.Lerp(P3, t);
            var leftLine = new CubicBezierSegment(P0, P0.Lerp(mid, 1.0 / 3.0), P0.Lerp(mid, 2.0 / 3.0), mid, false);
            var rightLine = new CubicBezierSegment(mid, mid.Lerp(P3, 1.0 / 3.0), mid.Lerp(P3, 2.0 / 3.0), P3, false);
            return (leftLine, rightLine, mid);
        }

        PhysicalPointMm q0 = P0.Lerp(C1, t);
        PhysicalPointMm q1 = C1.Lerp(C2, t);
        PhysicalPointMm q2 = C2.Lerp(P3, t);

        PhysicalPointMm r0 = q0.Lerp(q1, t);
        PhysicalPointMm r1 = q1.Lerp(q2, t);

        PhysicalPointMm s = r0.Lerp(r1, t);

        var left = new CubicBezierSegment(P0, q0, r0, s, true);
        var right = new CubicBezierSegment(s, r1, q2, P3, true);
        return (left, right, s);
    }
}

/// <summary>
/// Notch attached to a pattern piece outer contour segment.
/// </summary>
public sealed class NotchItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SegmentIndex { get; set; }
    public double ParameterT { get; set; } = 0.5;
    public PhysicalPointMm PositionMm { get; set; }
    public double DistanceFromPrevGradedPointMm { get; set; }
    public double LengthMm { get; set; } = 6.0;
    public double AngleDegrees { get; set; } = 90.0;
    public bool IsRelativeAngle { get; set; } = true;

    public NotchItem Clone() => new()
    {
        Id = Id,
        SegmentIndex = SegmentIndex,
        ParameterT = ParameterT,
        PositionMm = PositionMm,
        DistanceFromPrevGradedPointMm = DistanceFromPrevGradedPointMm,
        LengthMm = LengthMm,
        AngleDegrees = AngleDegrees,
        IsRelativeAngle = IsRelativeAngle
    };
}

/// <summary>
/// Internal drill hole / point marker inside a pattern piece.
/// </summary>
public sealed class InternalPointItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PhysicalPointMm PositionMm { get; set; }
    public double LengthMm { get; set; } = 5.0;

    public InternalPointItem Clone() => new()
    {
        Id = Id,
        PositionMm = PositionMm,
        LengthMm = LengthMm
    };
}

public enum InternalLineType
{
    // Numeric values 0/1 are retained for existing saved projects.
    SimpleLine = 0, // Gemini: For drawing
    GrainAxis = 1,
    CuttingLine = 2,
    SymmetryAxis = 3
}

/// <summary>
/// Internal line or Grain axis inside a pattern piece.
/// </summary>
public sealed class InternalLineItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public InternalLineType LineType { get; set; } = InternalLineType.SimpleLine;
    public PhysicalPointMm StartMm { get; set; }
    public PhysicalPointMm EndMm { get; set; }
    public List<PhysicalPointMm> PolylinePointsMm { get; set; } = [];
    /// <summary>Quadratic Bézier internal line. The control point is ignored for straight and grain lines.</summary>
    public bool IsCurve { get; set; }
    public PhysicalPointMm ControlPointMm { get; set; }
    public bool SymmetrizeWait { get; set; }
    public bool ExtendToContour { get; set; }

    [JsonIgnore]
    public double LengthMm => StartMm.DistanceTo(EndMm);

    [JsonIgnore]
    public double AngleDegrees
    {
        get
        {
            double rad = Math.Atan2(EndMm.YMm - StartMm.YMm, EndMm.XMm - StartMm.XMm);
            double deg = rad * 180.0 / Math.PI;
            return deg < 0 ? deg + 360.0 : deg;
        }
    }

    public void FlipDirection()
    {
        (StartMm, EndMm) = (EndMm, StartMm);
        PolylinePointsMm.Reverse();
    }

    public IReadOnlyList<PhysicalPointMm> GetExportPoints(int curveSteps = 16)
    {
        if (!IsCurve)
        {
            return PolylinePointsMm.Count >= 2 ? PolylinePointsMm : [StartMm, EndMm];
        }

        curveSteps = Math.Max(2, curveSteps);
        var points = new List<PhysicalPointMm>(curveSteps + 1);
        for (int i = 0; i <= curveSteps; i++)
        {
            double t = (double)i / curveSteps;
            double u = 1.0 - t;
            points.Add(new PhysicalPointMm(
                (u * u * StartMm.XMm) + (2 * u * t * ControlPointMm.XMm) + (t * t * EndMm.XMm),
                (u * u * StartMm.YMm) + (2 * u * t * ControlPointMm.YMm) + (t * t * EndMm.YMm)));
        }
        return points;
    }

    public InternalLineItem Clone() => new()
    {
        Id = Id,
        LineType = LineType,
        StartMm = StartMm,
        EndMm = EndMm,
        PolylinePointsMm = [.. PolylinePointsMm],
        IsCurve = IsCurve,
        ControlPointMm = ControlPointMm,
        SymmetrizeWait = SymmetrizeWait,
        ExtendToContour = ExtendToContour
    };
}

/// <summary>
/// Digitized garment pattern piece with closed outer contour, notches, internal points, and internal/grain lines.
/// All physical coordinates are stored in millimeters (mm) and displayed in centimeters (cm) or user-selected unit.
/// </summary>
public sealed class PatternPiece
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; }
    public string Name { get; set; } = "Piece 1";
    public List<ContourVertex> Vertices { get; set; } = [];
    public List<NotchItem> Notches { get; set; } = [];
    public List<InternalPointItem> InternalPoints { get; set; } = [];
    public List<InternalLineItem> InternalLines { get; set; } = [];
    public double AreaMm2 { get; set; }
    public double PerimeterMm { get; set; }
    public Rect BoundingBoxMm { get; set; }
    public Rect SourcePixelBoundingBox { get; set; }
    public bool IsAuxiliaryPiece { get; set; }

    // For a piece digitized from an operator-measured frame, this is the fixed
    // physical envelope.  It is persisted with the project so later edits and
    // reopens cannot silently grow the piece beyond the declared dimensions.
    public bool HasLockedDigitizeBounds { get; set; }
    public Rect LockedDigitizeBoundsMm { get; set; }

    // Immutable copy of the rectangle drawn by the operator before detection.
    // Unlike BoundingBoxMm it must never be derived from noisy recognition.
    public bool HasLockedDigitizeFrame { get; set; }
    public CropBoxPixels LockedDigitizeFramePixels { get; set; }
    public double LockedDigitizeFrameRotationDegrees { get; set; }
    public double LockedDigitizeFrameWidthCm { get; set; }
    public double LockedDigitizeFrameHeightCm { get; set; }

    // A digitize frame is a separate, per-piece calibration.  Keep it with
    // the piece so later frame work never overwrites the project's grid or
    // camera calibration.
    public CalibrationProfile? DigitizeFrameCalibration { get; set; }

    [JsonIgnore]
    public double AreaCm2 => AreaMm2 / 100.0;

    [JsonIgnore]
    public double PerimeterCm => PerimeterMm / 10.0;

    [JsonIgnore]
    public int GradedPointsCount => Vertices.Count(v => v.IsGradedCorner);

    [JsonIgnore]
    public int TotalVerticesCount => Vertices.Count;

    [JsonIgnore]
    public InternalLineItem? GrainLine =>
        InternalLines.FirstOrDefault(l => l.LineType == InternalLineType.GrainAxis);

    public CubicBezierSegment GetSegment(int index)
    {
        if (Vertices.Count < 2)
        {
            return default;
        }

        int i0 = ((index % Vertices.Count) + Vertices.Count) % Vertices.Count;
        int i1 = (i0 + 1) % Vertices.Count;
        ContourVertex v0 = Vertices[i0];
        ContourVertex v1 = Vertices[i1];
        return new CubicBezierSegment(
            v0.PositionMm,
            v0.OutControlHandleMm,
            v1.InControlHandleMm,
            v1.PositionMm,
            v0.OutgoingIsCurve);
    }

    public IEnumerable<CubicBezierSegment> EnumerateSegments()
    {
        for (int i = 0; i < Vertices.Count; i++)
        {
            yield return GetSegment(i);
        }
    }

    public PatternPiece Clone() => new()
    {
        Id = Id,
        Index = Index,
        Name = Name,
        Vertices = Vertices.Select(v => v.Clone()).ToList(),
        Notches = Notches.Select(n => n.Clone()).ToList(),
        InternalPoints = InternalPoints.Select(p => p.Clone()).ToList(),
        InternalLines = InternalLines.Select(l => l.Clone()).ToList(),
        AreaMm2 = AreaMm2,
        PerimeterMm = PerimeterMm,
        BoundingBoxMm = BoundingBoxMm,
        SourcePixelBoundingBox = SourcePixelBoundingBox,
        IsAuxiliaryPiece = IsAuxiliaryPiece,
        HasLockedDigitizeBounds = HasLockedDigitizeBounds,
        LockedDigitizeBoundsMm = LockedDigitizeBoundsMm,
        HasLockedDigitizeFrame = HasLockedDigitizeFrame,
        LockedDigitizeFramePixels = LockedDigitizeFramePixels,
        LockedDigitizeFrameRotationDegrees = LockedDigitizeFrameRotationDegrees,
        LockedDigitizeFrameWidthCm = LockedDigitizeFrameWidthCm,
        LockedDigitizeFrameHeightCm = LockedDigitizeFrameHeightCm,
        DigitizeFrameCalibration = DigitizeFrameCalibration?.Clone()
    };
}
