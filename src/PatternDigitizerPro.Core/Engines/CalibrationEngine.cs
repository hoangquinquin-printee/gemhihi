using System.IO;
using System.Text.Json;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

/// <summary>
/// Phase 2.2 Calibration Engine:
/// Implements Mode A (Grid Calibration with default 5x5 cm cells) and
/// Mode B (Manual X/Y Rectangle Calibration with independent X and Y scales).
/// </summary>
public sealed class CalibrationEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Mode A — Grid Calibration:
    /// Detects horizontal and vertical grid lines / intersections inside the cropped calibration image,
    /// calculates pixel-to-mm scale in X and Y, active area dimensions (cm), and residual error (mm).
    /// </summary>
    public CalibrationProfile CalibrateModeAGrid(
        RawImageBuffer gridImage,
        double cellWidthCm = 5.0,
        double cellHeightCm = 5.0,
        CropBoxPixels? cropBoxOverride = null)
    {
        ArgumentNullException.ThrowIfNull(gridImage);
        if (cellWidthCm <= 0 || double.IsNaN(cellWidthCm) || double.IsInfinity(cellWidthCm))
        {
            throw new ArgumentOutOfRangeException(nameof(cellWidthCm), "Cell width (cm) must be greater than zero.");
        }

        if (cellHeightCm <= 0 || double.IsNaN(cellHeightCm) || double.IsInfinity(cellHeightCm))
        {
            throw new ArgumentOutOfRangeException(nameof(cellHeightCm), "Cell height (cm) must be greater than zero.");
        }

        CropBoxPixels crop = cropBoxOverride ?? new CropBoxPixels(0, 0, gridImage.Width, gridImage.Height);
        int startX = Math.Clamp(crop.StartX, 0, gridImage.Width - 2);
        int startY = Math.Clamp(crop.StartY, 0, gridImage.Height - 2);
        int stopX = crop.StopX <= startX ? gridImage.Width : Math.Clamp(crop.StopX, startX + 10, gridImage.Width);
        int stopY = crop.StopY <= startY ? gridImage.Height : Math.Clamp(crop.StopY, startY + 10, gridImage.Height);
        var normalizedCrop = new CropBoxPixels(startX, startY, stopX, stopY);

        if (normalizedCrop.Width < 20 || normalizedCrop.Height < 20)
        {
            throw new InvalidOperationException("Calibration crop box is too small.");
        }

        (List<double> vLines, double pitchX, int cols, double meanErrXPx, double maxErrXPx, int leadingCols, int trailingCols) =
            DetectGridAxisLines(gridImage, normalizedCrop, isVerticalLines: true);

        (List<double> hLines, double pitchY, int rows, double meanErrYPx, double maxErrYPx, int leadingRows, int trailingRows) =
            DetectGridAxisLines(gridImage, normalizedCrop, isVerticalLines: false);

        double scaleXMmPerPx = (cellWidthCm * 10.0) / pitchX;
        double scaleYMmPerPx = (cellHeightCm * 10.0) / pitchY;

        (List<ImagePixelPoint> intersections, List<double> latticeResidualsMm, _, _) =
            RefineGridIntersections2D(gridImage, vLines, hLines, scaleXMmPerPx, scaleYMmPerPx);

        (List<double> pixelToPhysical, List<double> physicalToPixel, List<double> projectiveResidualsMm) =
            FitProjectiveGridMapping(intersections, vLines.Count, hLines.Count, cellWidthCm * 10.0, cellHeightCm * 10.0, leadingCols, leadingRows);
        (List<double> quadraticWarp, List<double> warpResidualsMm) =
            FitQuadraticGridWarp(intersections, vLines.Count, hLines.Count, cellWidthCm * 10.0, cellHeightCm * 10.0, leadingCols, leadingRows);
        List<double> quadraticHoldoutResidualsMm = ComputePolynomialHoldoutResiduals(
            intersections, vLines.Count, hLines.Count, cellWidthCm * 10.0, cellHeightCm * 10.0, leadingCols, leadingRows, cubic: false);
        (List<double> cubicWarp, List<double> cubicResidualsMm) =
            FitCubicGridWarp(intersections, vLines.Count, hLines.Count, cellWidthCm * 10.0, cellHeightCm * 10.0, leadingCols, leadingRows);
        List<double> cubicHoldoutResidualsMm = ComputePolynomialHoldoutResiduals(
            intersections, vLines.Count, hLines.Count, cellWidthCm * 10.0, cellHeightCm * 10.0, leadingCols, leadingRows, cubic: true);

        bool useCubicWarp = cubicWarp.Count == 26 &&
                            cubicHoldoutResidualsMm.Count > 0 && quadraticHoldoutResidualsMm.Count > 0 &&
                            cubicHoldoutResidualsMm.Average() < quadraticHoldoutResidualsMm.Average() * 0.98 &&
                            cubicHoldoutResidualsMm.Max() < quadraticHoldoutResidualsMm.Max() * 0.98;
        List<double> activeWarp = useCubicWarp ? cubicWarp : quadraticWarp;
        List<double> activeWarpResidualsMm = useCubicWarp ? cubicResidualsMm : warpResidualsMm;
        List<double> holdoutResidualsMm = useCubicWarp ? cubicHoldoutResidualsMm : quadraticHoldoutResidualsMm;

        // Keep the legacy axis-lattice diagnostic separate from the new
        // projective-model validation. Existing UI/tests display this axis
        // statistic; it must not be mislabeled as perspective-corrected error.
        double meanErrMm = 0.5 * ((meanErrXPx * scaleXMmPerPx) + (meanErrYPx * scaleYMmPerPx));
        double maxErrMm = Math.Max(maxErrXPx * scaleXMmPerPx, maxErrYPx * scaleYMmPerPx);
        List<double> activeModelResiduals = activeWarpResidualsMm.Count > 0 ? activeWarpResidualsMm : projectiveResidualsMm;
        double projectiveMeanErrMm = activeModelResiduals.Count > 0 ? activeModelResiduals.Average() : 0.0;
        double projectiveMaxErrMm = activeModelResiduals.Count > 0 ? activeModelResiduals.Max() : 0.0;

        // The active area is an exact cell-count calculation.  Keep the same
        // three-decimal precision accepted by the calibration dialog (for example
        // 27 × 4.995 = 134.865 cm); rounding here would permanently corrupt the
        // physical calibration that is saved and exported.
        double activeWidthCm = Math.Round(cols * cellWidthCm, 3, MidpointRounding.AwayFromZero);
        double activeHeightCm = Math.Round(rows * cellHeightCm, 3, MidpointRounding.AwayFromZero);

        return new CalibrationProfile
        {
            Mode = CalibrationMode.GridModeA,
            ScaleXMmPerPx = scaleXMmPerPx,
            ScaleYMmPerPx = scaleYMmPerPx,
            CellWidthCm = cellWidthCm,
            CellHeightCm = cellHeightCm,
            GridColumns = cols,
            GridRows = rows,
            GridLeadingBoundaryColumns = leadingCols,
            GridTrailingBoundaryColumns = trailingCols,
            GridLeadingBoundaryRows = leadingRows,
            GridTrailingBoundaryRows = trailingRows,
            ActiveAreaWidthCm = activeWidthCm,
            ActiveAreaHeightCm = activeHeightCm,
            ImageWidthPx = gridImage.Width,
            ImageHeightPx = gridImage.Height,
            CropBox = normalizedCrop,
            RestrictDetectionToCropBox = false,
            VerticalGridLinesPx = vLines,
            HorizontalGridLinesPx = hLines,
            DetectedIntersections = intersections,
            IntersectionResidualsMm = latticeResidualsMm,
            PixelToPhysicalTransform = pixelToPhysical,
            PhysicalToPixelTransform = physicalToPixel,
            PixelToPhysicalWarp = activeWarp,
            MeanResidualErrorMm = Math.Round(meanErrMm, 3),
            MaxResidualErrorMm = Math.Round(maxErrMm, 3),
            ProjectiveMeanResidualErrorMm = Math.Round(projectiveMeanErrMm, 3),
            ProjectiveMaxResidualErrorMm = Math.Round(projectiveMaxErrMm, 3),
            HoldoutMeanResidualErrorMm = Math.Round(holdoutResidualsMm.Count > 0 ? holdoutResidualsMm.Average() : 0.0, 3),
            HoldoutMaxResidualErrorMm = Math.Round(holdoutResidualsMm.Count > 0 ? holdoutResidualsMm.Max() : 0.0, 3),
            SourceImagePath = gridImage.FilePath
        };
    }

    /// <summary>
    /// Mode B — Manual X/Y Rectangle Calibration:
    /// Uses a user-specified rectangular region in pixels (StartX, StartY, StopX, StopY) and physical
    /// dimensions (WidthCm, HeightCm) to compute independent horizontal and vertical scales (mm/px)
    /// without forcing pieces to X*Y or requiring 4-corner homography.
    /// </summary>
    public CalibrationProfile CalibrateModeBManualRectangle(
        CropBoxPixels referenceRectPx,
        double physicalWidthCm,
        double physicalHeightCm,
        int imageWidthPx = 3456,
        int imageHeightPx = 2592,
        bool restrictDetectionToRoi = false)
    {
        if (physicalWidthCm <= 0 || double.IsNaN(physicalWidthCm) || double.IsInfinity(physicalWidthCm))
        {
            throw new ArgumentOutOfRangeException(nameof(physicalWidthCm), "Physical X width (cm) must be greater than zero.");
        }

        if (physicalHeightCm <= 0 || double.IsNaN(physicalHeightCm) || double.IsInfinity(physicalHeightCm))
        {
            throw new ArgumentOutOfRangeException(nameof(physicalHeightCm), "Physical Y height (cm) must be greater than zero.");
        }

        if (!referenceRectPx.IsValid || referenceRectPx.Width < 5 || referenceRectPx.Height < 5)
        {
            throw new ArgumentException("Reference calibration rectangle in pixels must have Width >= 5 and Height >= 5.", nameof(referenceRectPx));
        }

        double widthMm = physicalWidthCm * 10.0;
        double heightMm = physicalHeightCm * 10.0;

        double scaleXMmPerPx = widthMm / referenceRectPx.Width;
        double scaleYMmPerPx = heightMm / referenceRectPx.Height;

        int cols = Math.Max(1, (int)Math.Round(physicalWidthCm / 5.0));
        int rows = Math.Max(1, (int)Math.Round(physicalHeightCm / 5.0));

        return new CalibrationProfile
        {
            Mode = CalibrationMode.ManualRectangleModeB,
            ScaleXMmPerPx = scaleXMmPerPx,
            ScaleYMmPerPx = scaleYMmPerPx,
            CellWidthCm = 5.0,
            CellHeightCm = 5.0,
            GridColumns = cols,
            GridRows = rows,
            ActiveAreaWidthCm = Math.Round(physicalWidthCm, 2),
            ActiveAreaHeightCm = Math.Round(physicalHeightCm, 2),
            ImageWidthPx = imageWidthPx > 0 ? imageWidthPx : referenceRectPx.StopX,
            ImageHeightPx = imageHeightPx > 0 ? imageHeightPx : referenceRectPx.StopY,
            CropBox = referenceRectPx,
            RestrictDetectionToCropBox = restrictDetectionToRoi,
            MeanResidualErrorMm = 0.0,
            MaxResidualErrorMm = 0.0
        };
    }

    /// <summary>
    /// Manual calibration from a rigid, rotated reference frame.  The frame is
    /// still a rectangle in its own local axes, so the entered width/height map
    /// exactly to those axes even when a phone camera was held at an angle.
    /// The returned affine matrices are used in both directions by the shared
    /// calibration API, keeping image overlay, hit testing, editing and DXF in
    /// the same coordinate system.
    /// </summary>
    public CalibrationProfile CalibrateModeBManualRotatedRectangle(
        RotatedFramePixels referenceFramePx,
        double physicalWidthCm,
        double physicalHeightCm,
        int imageWidthPx = 3456,
        int imageHeightPx = 2592,
        bool restrictDetectionToRoi = false)
    {
        if (!referenceFramePx.IsValid)
        {
            throw new ArgumentException("Rotated reference frame must be at least 40x40 pixels.", nameof(referenceFramePx));
        }

        CalibrationProfile profile = CalibrateModeBManualRectangle(
            referenceFramePx.BoundingBox,
            physicalWidthCm,
            physicalHeightCm,
            imageWidthPx,
            imageHeightPx,
            restrictDetectionToRoi);

        double sx = (physicalWidthCm * 10.0) / referenceFramePx.Width;
        double sy = (physicalHeightCm * 10.0) / referenceFramePx.Height;
        double angle = referenceFramePx.RotationRadians;
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        ImagePixelPoint topLeft = referenceFramePx.Corners[0];

        // source pixel -> local physical frame (millimetres)
        profile.PixelToPhysicalTransform =
        [
            cos * sx, sin * sx, -((topLeft.X * cos) + (topLeft.Y * sin)) * sx,
            -sin * sy, cos * sy, -((-topLeft.X * sin) + (topLeft.Y * cos)) * sy,
            0, 0, 1
        ];
        // local physical frame -> source pixel
        profile.PhysicalToPixelTransform =
        [
            cos / sx, -sin / sy, topLeft.X,
            sin / sx, cos / sy, topLeft.Y,
            0, 0, 1
        ];
        profile.ScaleXMmPerPx = sx;
        profile.ScaleYMmPerPx = sy;
        profile.CropBox = referenceFramePx.BoundingBox;
        profile.RotatedDetectionFrame = restrictDetectionToRoi ? referenceFramePx : null;
        return profile;
    }

    /// <summary>
    /// Resolution (Pixels Per Inch) Calibration from Settings -> Camera settings / Calibration.
    /// </summary>
    public CalibrationProfile CalibrateByResolutionDpi(
        double horizontalPpi,
        double verticalPpi,
        int imageWidthPx = 3456,
        int imageHeightPx = 2592)
    {
        if (horizontalPpi <= 0 || double.IsNaN(horizontalPpi) || double.IsInfinity(horizontalPpi))
        {
            throw new ArgumentOutOfRangeException(nameof(horizontalPpi), "Horizontal pixels per inch must be > 0.");
        }

        if (verticalPpi <= 0 || double.IsNaN(verticalPpi) || double.IsInfinity(verticalPpi))
        {
            throw new ArgumentOutOfRangeException(nameof(verticalPpi), "Vertical pixels per inch must be > 0.");
        }

        double scaleX = 25.4 / horizontalPpi;
        double scaleY = 25.4 / verticalPpi;
        double widthCm = Math.Round((imageWidthPx * scaleX) / 10.0, 2);
        double heightCm = Math.Round((imageHeightPx * scaleY) / 10.0, 2);

        return new CalibrationProfile
        {
            Mode = CalibrationMode.ResolutionDpi,
            ScaleXMmPerPx = scaleX,
            ScaleYMmPerPx = scaleY,
            CellWidthCm = 5.0,
            CellHeightCm = 5.0,
            GridColumns = Math.Max(1, (int)Math.Round(widthCm / 5.0)),
            GridRows = Math.Max(1, (int)Math.Round(heightCm / 5.0)),
            ActiveAreaWidthCm = widthCm,
            ActiveAreaHeightCm = heightCm,
            ImageWidthPx = imageWidthPx,
            ImageHeightPx = imageHeightPx,
            CropBox = new CropBoxPixels(0, 0, imageWidthPx, imageHeightPx)
        };
    }

    public bool ValidateCalibration(CalibrationProfile? profile, out string validationMessage)
    {
        if (profile is null)
        {
            validationMessage = "Calibration profile is null.";
            return false;
        }

        if (!profile.IsValid)
        {
            validationMessage = "Calibration scale values (mm/px) are invalid or non-positive.";
            return false;
        }

        if (profile.ScaleXMmPerPx < 0.005 || profile.ScaleXMmPerPx > 50.0 ||
            profile.ScaleYMmPerPx < 0.005 || profile.ScaleYMmPerPx > 50.0)
        {
            validationMessage = $"Calibration scale ({profile.ScaleXMmPerPx:F4}, {profile.ScaleYMmPerPx:F4} mm/px) is outside physical bounds.";
            return false;
        }

        if (profile.ActiveAreaWidthCm <= 0 || profile.ActiveAreaHeightCm <= 0)
        {
            validationMessage = "Active calibration area dimensions must be positive.";
            return false;
        }

        validationMessage = "Calibration profile is valid.";
        return true;
    }

    public void SaveToFile(CalibrationProfile profile, string filePath)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string fullPath = Path.GetFullPath(filePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(fullPath, json);
    }

    public CalibrationProfile LoadFromFile(string filePath)
    {
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Calibration file not found: {fullPath}", fullPath);
        }

        string json = File.ReadAllText(fullPath);
        var profile = JsonSerializer.Deserialize<CalibrationProfile>(json, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize calibration profile.");
        return profile;
    }

    private static (List<double> Lines, double PitchPx, int CellCount, double MeanErrPx, double MaxErrPx, int LeadingBoundaryCells, int TrailingBoundaryCells) DetectGridAxisLines(
        RawImageBuffer img,
        CropBoxPixels crop,
        bool isVerticalLines)
    {
        int w = img.Width;
        int h = img.Height;
        int stride = img.Stride;
        byte[] pixels = img.BgraPixels;

        int primaryStart = isVerticalLines ? crop.StartX : crop.StartY;
        int primaryStop = isVerticalLines ? crop.StopX : crop.StopY;
        int secondaryStart = isVerticalLines ? crop.StartY : crop.StartX;
        int secondaryStop = isVerticalLines ? crop.StopY : crop.StopX;

        // Exclude outermost 4% margin on secondary axis to avoid magnets/frame on edges
        int secMargin = Math.Max(4, (int)((secondaryStop - secondaryStart) * 0.08));
        int s0 = secondaryStart + secMargin;
        int s1 = Math.Max(s0 + 4, secondaryStop - secMargin);
        int step = Math.Max(1, (s1 - s0) / 450);

        double[] signal = new double[ isVerticalLines ? w : h ];
        for (int p = primaryStart; p < primaryStop; p++)
        {
            double sum = 0;
            int count = 0;
            for (int s = s0; s < s1; s += step)
            {
                int x = isVerticalLines ? p : s;
                int y = isVerticalLines ? s : p;
                int idx = (y * stride) + (x * 4);
                double g = (pixels[idx] + pixels[idx + 1] + pixels[idx + 2]) / 3.0;
                sum += g;
                count++;
            }

            signal[p] = count > 0 ? sum / count : 128.0;
        }

        int span = primaryStop - primaryStart;
        int windowRadius = Math.Clamp(span / 90, 8, 36);
        int minPeakSpacing = Math.Clamp(span / 45, 16, 75);

        var candidates = new List<double>();
        int edgeGuard = Math.Max(8, (int)(span * 0.04));

        for (int p = primaryStart + edgeGuard; p < primaryStop - edgeGuard; p++)
        {
            double localSum = 0;
            int localCnt = 0;
            for (int k = -windowRadius; k <= windowRadius; k++)
            {
                int idx = p + k;
                if (idx >= primaryStart && idx < primaryStop)
                {
                    localSum += signal[idx];
                    localCnt++;
                }
            }

            double localMean = localSum / Math.Max(1, localCnt);
            double dip = localMean - signal[p];
            if (dip > 3.2)
            {
                bool isLocalMin = true;
                int checkRad = Math.Max(6, minPeakSpacing / 2);
                for (int k = -checkRad; k <= checkRad; k++)
                {
                    int idx = p + k;
                    if (idx >= primaryStart && idx < primaryStop && signal[idx] < signal[p])
                    {
                        isLocalMin = false;
                        break;
                    }
                }

                if (isLocalMin)
                {
                    // Parabolic sub-pixel refinement
                    double subPos = p;
                    if (p > primaryStart + 1 && p < primaryStop - 2)
                    {
                        double yLeft = signal[p - 1];
                        double yMid = signal[p];
                        double yRight = signal[p + 1];
                        double denom = 2.0 * (yLeft - (2.0 * yMid) + yRight);
                        if (Math.Abs(denom) > 1e-5)
                        {
                            subPos = p + Math.Clamp((yLeft - yRight) / denom, -0.5, 0.5);
                        }
                    }

                    candidates.Add(Math.Round(subPos, 2));
                    p += minPeakSpacing;
                }
            }
        }

        if (candidates.Count >= 3)
        {
            var diffs = new List<double>();
            for (int i = 1; i < candidates.Count; i++)
            {
                diffs.Add(candidates[i] - candidates[i - 1]);
            }

            diffs.Sort();
            double medianDiff = diffs[diffs.Count / 2];

            // Filter diffs within +-20% of medianDiff to get accurate grid pitch
            var inlierDiffs = diffs.Where(d => Math.Abs(d - medianDiff) <= medianDiff * 0.20).ToList();
            double pitch = inlierDiffs.Count > 0 ? inlierDiffs.Average() : medianDiff;

            // Compute residual errors against ideal lattice spacing
            double firstLine = candidates[0];
            var errors = new List<double>();
            foreach (double linePos in candidates)
            {
                double nearestIndex = Math.Round((linePos - firstLine) / pitch);
                double idealPos = firstLine + (nearestIndex * pitch);
                errors.Add(Math.Abs(linePos - idealPos));
            }

            double meanErr = errors.Count > 0 ? errors.Average() : 0.0;
            double maxErr = errors.Count > 0 ? errors.Max() : 0.0;

            // Compute total grid cells from detected lattice intervals and active board boundary margins
            // without any hardcoded image resolution or cell counts.
            int intervalsBetweenLines = Math.Max(1, (int)Math.Round((candidates[^1] - candidates[0]) / pitch));
            double leadingMarginRatio = (candidates[0] - primaryStart) / pitch;
            double trailingMarginRatio = (primaryStop - candidates[^1]) / pitch;
            int axisFullLength = isVerticalLines ? w : h;

            int leadingBoundaryCells = leadingMarginRatio >= 1.30
                ? (int)Math.Floor(leadingMarginRatio - 0.30)
                : (leadingMarginRatio >= 0.75 && primaryStart > 0 ? 1 : 0);

            int trailingBoundaryCells = trailingMarginRatio >= 1.30
                ? (int)Math.Floor(trailingMarginRatio - 0.30)
                : (trailingMarginRatio >= 0.75 && primaryStop < axisFullLength ? 1 : 0);

            int estimatedCells = Math.Max(1, intervalsBetweenLines + leadingBoundaryCells + trailingBoundaryCells);

            return (candidates, pitch, estimatedCells, meanErr, maxErr, leadingBoundaryCells, trailingBoundaryCells);
        }

        throw new InvalidOperationException(
            $"Unable to detect a reliable {(isVerticalLines ? "vertical" : "horizontal")} grid lattice. " +
            "Calibration was not fabricated from a fixed cell count.");
    }

    private static (List<ImagePixelPoint> Intersections, List<double> ResidualsMm, double MeanErrorMm, double MaxErrorMm)
        RefineGridIntersections2D(
            RawImageBuffer img,
            List<double> vLines,
            List<double> hLines,
            double scaleXMmPerPx,
            double scaleYMmPerPx)
    {
        int w = img.Width;
        int h = img.Height;
        int stride = img.Stride;
        byte[] pixels = img.BgraPixels;

        int numCols = vLines.Count;
        int numRows = hLines.Count;
        if (numCols < 2 || numRows < 2)
        {
            return ([], [], 0.0, 0.0);
        }

        var refinedPoints = new List<ImagePixelPoint>(numCols * numRows);
        var residualsMm = new List<double>(numCols * numRows);

        // The photographed grid is perspective-distorted: a line can be many
        // pixels away from its image-wide average at the top or bottom.  The old
        // fixed 9px patch/5px centroid could therefore never reach the real line.
        // Keep the search safely below half a cell so it cannot jump to a neighbour.
        double localPitchX = MedianPitch(vLines);
        double localPitchY = MedianPitch(hLines);
        int R = Math.Clamp((int)Math.Round(Math.Min(localPitchX, localPitchY) * 0.35), 12, 32);
        int centreSearchRadius = Math.Max(6, R - 4);

        for (int r = 0; r < numRows; r++)
        {
            double hy = hLines[r];
            for (int c = 0; c < numCols; c++)
            {
                double vx = vLines[c];
                int cx = (int)Math.Round(vx);
                int cy = (int)Math.Round(hy);

                if (cx < R + 2 || cx >= w - R - 2 || cy < R + 2 || cy >= h - R - 2)
                {
                    refinedPoints.Add(new ImagePixelPoint(vx, hy));
                    continue;
                }

                double[] xSignal = new double[(2 * R) + 1];
                for (int dx = -R; dx <= R; dx++)
                {
                    double sum = 0;
                    int sampleCount = 0;
                    // Never estimate the vertical-line centre from pixels on the
                    // horizontal line itself.  A dark, asymmetric crossing or a
                    // stain there biases the one-dimensional centroid away from
                    // the actual vertical-line centre.
                    for (int dy = -R; dy <= R; dy++)
                    {
                        if (Math.Abs(dy) <= 3)
                        {
                            continue;
                        }

                        int px = cx + dx;
                        int py = cy + dy;
                        int idx = (py * stride) + (px * 4);
                        sum += (pixels[idx] + pixels[idx + 1] + pixels[idx + 2]) / 3.0;
                        sampleCount++;
                    }
                    xSignal[dx + R] = sum / sampleCount;
                }

                double subX = cx + RefineDarkLineCenter(xSignal, R, centreSearchRadius);

                double[] ySignal = new double[(2 * R) + 1];
                for (int dy = -R; dy <= R; dy++)
                {
                    double sum = 0;
                    int sampleCount = 0;
                    // Symmetric rule for the horizontal-line centre: exclude the
                    // vertical-line crossing rather than letting its local ink
                    // distribution choose the Y coordinate.
                    for (int dx = -R; dx <= R; dx++)
                    {
                        if (Math.Abs(dx) <= 3)
                        {
                            continue;
                        }

                        int px = cx + dx;
                        int py = cy + dy;
                        int idx = (py * stride) + (px * 4);
                        sum += (pixels[idx] + pixels[idx + 1] + pixels[idx + 2]) / 3.0;
                        sampleCount++;
                    }
                    ySignal[dy + R] = sum / sampleCount;
                }

                double subY = cy + RefineDarkLineCenter(ySignal, R, centreSearchRadius);
                double maxShift = R - 3.0;
                double finalX = Math.Abs(subX - vx) <= maxShift ? subX : vx;
                double finalY = Math.Abs(subY - hy) <= maxShift ? subY : hy;
                refinedPoints.Add(new ImagePixelPoint(Math.Round(finalX, 2), Math.Round(finalY, 2)));
            }
        }

        // Fit 2D affine lattice model and calculate per-intersection residuals
        double firstVx = vLines[0], pitchX = numCols > 1 ? (vLines[^1] - vLines[0]) / (numCols - 1) : 100.0;
        double firstHy = hLines[0], pitchY = numRows > 1 ? (hLines[^1] - hLines[0]) / (numRows - 1) : 100.0;

        for (int r = 0; r < numRows; r++)
        {
            for (int c = 0; c < numCols; c++)
            {
                int k = (r * numCols) + c;
                ImagePixelPoint pt = refinedPoints[k];
                double modelX = firstVx + (c * pitchX);
                double modelY = firstHy + (r * pitchY);
                double errX = (pt.X - modelX) * scaleXMmPerPx;
                double errY = (pt.Y - modelY) * scaleYMmPerPx;
                double errMm = Math.Sqrt((errX * errX) + (errY * errY));
                residualsMm.Add(Math.Round(errMm, 3));
            }
        }

        double meanErr = residualsMm.Count > 0 ? residualsMm.Average() : 0.0;
        double maxErr = residualsMm.Count > 0 ? residualsMm.Max() : 0.0;
        return (refinedPoints, residualsMm, Math.Round(meanErr, 3), Math.Round(maxErr, 3));
    }

    private static double MedianPitch(IReadOnlyList<double> lines)
    {
        if (lines.Count < 2)
        {
            return 100.0;
        }

        var intervals = new List<double>(lines.Count - 1);
        for (int i = 1; i < lines.Count; i++)
        {
            intervals.Add(lines[i] - lines[i - 1]);
        }
        intervals.Sort();
        return intervals[intervals.Count / 2];
    }

    private static double RefineDarkLineCenter(IReadOnlyList<double> signal, int centerIndex, int searchRadius)
    {
        double baseline = signal.Max();
        double minimum = signal.Min();
        double contrast = baseline - minimum;
        if (contrast < 1e-6)
        {
            return 0.0;
        }

        // Restrict the centroid to the expected line neighbourhood. The global
        // 1D detector already supplies the line position; this local step only
        // estimates its sub-pixel centre and must not jump to nearby shadows.
        int radius = Math.Min(searchRadius, centerIndex);
        double threshold = minimum + (contrast * 0.55);
        double weightedPosition = 0.0;
        double totalWeight = 0.0;
        for (int i = centerIndex - radius; i <= centerIndex + radius; i++)
        {
            if (signal[i] > threshold)
            {
                continue;
            }

            double weight = baseline - signal[i];
            weightedPosition += (i - centerIndex) * weight;
            totalWeight += weight;
        }

        return totalWeight > 1e-9
            ? Math.Clamp(weightedPosition / totalWeight, -radius, radius)
            : 0.0;
    }

    private static (List<double> PixelToPhysical, List<double> PhysicalToPixel, List<double> ResidualsMm)
        FitProjectiveGridMapping(
            IReadOnlyList<ImagePixelPoint> intersections,
            int numCols,
            int numRows,
            double cellWidthMm,
            double cellHeightMm,
            int leadingColumns,
            int leadingRows)
    {
        if (numCols < 2 || numRows < 2 || intersections.Count != numCols * numRows)
        {
            return ([], [], []);
        }

        var pixelSamples = new List<(double X, double Y)>(intersections.Count);
        var physicalSamples = new List<(double X, double Y)>(intersections.Count);
        for (int r = 0; r < numRows; r++)
        {
            for (int c = 0; c < numCols; c++)
            {
                ImagePixelPoint p = intersections[(r * numCols) + c];
                pixelSamples.Add((p.X, p.Y));
                physicalSamples.Add(((c + leadingColumns) * cellWidthMm, (r + leadingRows) * cellHeightMm));
            }
        }

        List<double> p2m = SolveProjective(pixelSamples, physicalSamples);
        List<double> m2p = InvertProjective(p2m);
        if (p2m.Count != 9 || m2p.Count != 9)
        {
            return ([], [], []);
        }

        var residuals = new List<double>(intersections.Count);
        for (int i = 0; i < intersections.Count; i++)
        {
            (double expectedX, double expectedY) = physicalSamples[i];
            if (!ApplyProjective(p2m, pixelSamples[i].X, pixelSamples[i].Y, out double actualX, out double actualY))
            {
                return ([], [], []);
            }

            double dx = actualX - expectedX;
            double dy = actualY - expectedY;
            residuals.Add(Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), 3));
        }

        return (p2m, m2p, residuals);
    }

    private static (List<double> Warp, List<double> ResidualsMm) FitQuadraticGridWarp(
        IReadOnlyList<ImagePixelPoint> intersections,
        int numCols,
        int numRows,
        double cellWidthMm,
        double cellHeightMm,
        int leadingColumns,
        int leadingRows)
    {
        if (numCols < 3 || numRows < 3 || intersections.Count != numCols * numRows)
        {
            return ([], []);
        }

        var source = intersections.Select(p => (p.X, p.Y)).ToList();
        var target = new List<(double X, double Y)>(intersections.Count);
        for (int r = 0; r < numRows; r++)
        {
            for (int c = 0; c < numCols; c++)
            {
                target.Add(((c + leadingColumns) * cellWidthMm, (r + leadingRows) * cellHeightMm));
            }
        }

        List<double> warp = FitPolynomialWarpSamples(source, target, cubic: false);
        if (warp.Count != 18)
        {
            return ([], []);
        }

        return (warp, ComputeWarpResiduals(warp, source, target));
    }

    private static (List<double> Warp, List<double> ResidualsMm) FitCubicGridWarp(
        IReadOnlyList<ImagePixelPoint> intersections,
        int numCols,
        int numRows,
        double cellWidthMm,
        double cellHeightMm,
        int leadingColumns,
        int leadingRows)
    {
        if (numCols < 4 || numRows < 4 || intersections.Count != numCols * numRows)
        {
            return ([], []);
        }

        var source = intersections.Select(p => (p.X, p.Y)).ToList();
        var target = new List<(double X, double Y)>(intersections.Count);
        for (int r = 0; r < numRows; r++)
        {
            for (int c = 0; c < numCols; c++)
            {
                target.Add(((c + leadingColumns) * cellWidthMm, (r + leadingRows) * cellHeightMm));
            }
        }

        List<double> warp = FitPolynomialWarpSamples(source, target, cubic: true);
        return warp.Count == 26 ? (warp, ComputeWarpResiduals(warp, source, target)) : ([], []);
    }

    private static List<double> ComputePolynomialHoldoutResiduals(
        IReadOnlyList<ImagePixelPoint> intersections,
        int numCols,
        int numRows,
        double cellWidthMm,
        double cellHeightMm,
        int leadingColumns,
        int leadingRows,
        bool cubic)
    {
        var trainingSource = new List<(double X, double Y)>();
        var trainingTarget = new List<(double X, double Y)>();
        var validationSource = new List<(double X, double Y)>();
        var validationTarget = new List<(double X, double Y)>();
        for (int r = 0; r < numRows; r++)
        {
            for (int c = 0; c < numCols; c++)
            {
                int index = (r * numCols) + c;
                if (index >= intersections.Count)
                {
                    return [];
                }

                var source = (intersections[index].X, intersections[index].Y);
                var target = ((c + leadingColumns) * cellWidthMm, (r + leadingRows) * cellHeightMm);
                // Deterministic spatial holdout distributed across every row.
                if (((r * 3) + c) % 5 == 0)
                {
                    validationSource.Add(source);
                    validationTarget.Add(target);
                }
                else
                {
                    trainingSource.Add(source);
                    trainingTarget.Add(target);
                }
            }
        }

        List<double> warp = FitPolynomialWarpSamples(trainingSource, trainingTarget, cubic);
        int expectedCount = cubic ? 26 : 18;
        return warp.Count == expectedCount ? ComputeWarpResiduals(warp, validationSource, validationTarget) : [];
    }

    private static List<double> FitPolynomialWarpSamples(
        IReadOnlyList<(double X, double Y)> source,
        IReadOnlyList<(double X, double Y)> target,
        bool cubic)
    {
        if (source.Count != target.Count || source.Count < 12)
        {
            return [];
        }

        (double sourceMeanX, double sourceMeanY, double sourceScale) = ComputeNormalization(source);
        (double targetMeanX, double targetMeanY, double targetScale) = ComputeNormalization(target);
        int termCount = cubic ? 10 : 6;
        var normal = new double[termCount, termCount];
        var rhsX = new double[termCount];
        var rhsY = new double[termCount];
        for (int idx = 0; idx < source.Count; idx++)
        {
            double x = (source[idx].X - sourceMeanX) * sourceScale;
            double y = (source[idx].Y - sourceMeanY) * sourceScale;
            double[] row = BuildPolynomialBasis(x, y, cubic);
            double u = (target[idx].X - targetMeanX) * targetScale;
            double v = (target[idx].Y - targetMeanY) * targetScale;
            AccumulateNormalEquation(normal, rhsX, row, u);
            for (int k = 0; k < row.Length; k++)
            {
                rhsY[k] += row[k] * v;
            }
        }

        double[]? coeffX = SolveLinearSystem(normal, rhsX);
        double[]? coeffY = SolveLinearSystem(normal, rhsY);
        if (coeffX is null || coeffY is null)
        {
            return [];
        }

        return
        [
            sourceMeanX, sourceMeanY, sourceScale,
            targetMeanX, targetMeanY, targetScale,
            .. coeffX,
            .. coeffY
        ];
    }

    private static List<double> ComputeWarpResiduals(
        IReadOnlyList<double> warp,
        IReadOnlyList<(double X, double Y)> source,
        IReadOnlyList<(double X, double Y)> target)
    {
        var residuals = new List<double>(source.Count);
        for (int idx = 0; idx < source.Count; idx++)
        {
            ApplyPolynomialWarp(warp, source[idx].X, source[idx].Y, out double u, out double v);
            double dx = u - target[idx].X;
            double dy = v - target[idx].Y;
            residuals.Add(Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), 3));
        }
        return residuals;
    }

    private static void ApplyPolynomialWarp(IReadOnlyList<double> warp, double x, double y, out double u, out double v)
    {
        int termCount = (warp.Count - 6) / 2;
        bool cubic = termCount == 10;
        double nx = (x - warp[0]) * warp[2];
        double ny = (y - warp[1]) * warp[2];
        double[] basis = BuildPolynomialBasis(nx, ny, cubic);
        double nu = 0;
        double nv = 0;
        for (int k = 0; k < termCount; k++)
        {
            nu += warp[6 + k] * basis[k];
            nv += warp[6 + termCount + k] * basis[k];
        }
        u = (nu / warp[5]) + warp[3];
        v = (nv / warp[5]) + warp[4];
    }

    private static double[] BuildPolynomialBasis(double x, double y, bool cubic) => cubic
        ? [1.0, x, y, x * x, x * y, y * y, x * x * x, x * x * y, x * y * y, y * y * y]
        : [1.0, x, y, x * x, x * y, y * y];

    private static List<double> InvertProjective(IReadOnlyList<double> m)
    {
        if (m.Count != 9)
        {
            return [];
        }

        double a = m[0], b = m[1], c = m[2];
        double d = m[3], e = m[4], f = m[5];
        double g = m[6], h = m[7], i = m[8];
        double det = a * ((e * i) - (f * h))
                   - b * ((d * i) - (f * g))
                   + c * ((d * h) - (e * g));
        if (Math.Abs(det) < 1e-14)
        {
            return [];
        }

        return
        [
            ((e * i) - (f * h)) / det,
            ((c * h) - (b * i)) / det,
            ((b * f) - (c * e)) / det,
            ((f * g) - (d * i)) / det,
            ((a * i) - (c * g)) / det,
            ((c * d) - (a * f)) / det,
            ((d * h) - (e * g)) / det,
            ((b * g) - (a * h)) / det,
            ((a * e) - (b * d)) / det
        ];
    }

    private static List<double> SolveProjective(
        IReadOnlyList<(double X, double Y)> source,
        IReadOnlyList<(double X, double Y)> target)
    {
        if (source.Count != target.Count || source.Count < 4)
        {
            return [];
        }

        (double sourceMeanX, double sourceMeanY, double sourceScale) = ComputeNormalization(source);
        (double targetMeanX, double targetMeanY, double targetScale) = ComputeNormalization(target);

        var normal = new double[8, 8];
        var rhs = new double[8];
        for (int i = 0; i < source.Count; i++)
        {
            double x = (source[i].X - sourceMeanX) * sourceScale;
            double y = (source[i].Y - sourceMeanY) * sourceScale;
            double u = (target[i].X - targetMeanX) * targetScale;
            double v = (target[i].Y - targetMeanY) * targetScale;
            double[] rowU = [x, y, 1, 0, 0, 0, -u * x, -u * y];
            double[] rowV = [0, 0, 0, x, y, 1, -v * x, -v * y];
            AccumulateNormalEquation(normal, rhs, rowU, u);
            AccumulateNormalEquation(normal, rhs, rowV, v);
        }

        double[]? h = SolveLinearSystem(normal, rhs);
        if (h is null)
        {
            return [];
        }

        double[,] normalized =
        {
            { h[0], h[1], h[2] },
            { h[3], h[4], h[5] },
            { h[6], h[7], 1.0 }
        };
        double[,] sourceNormalization =
        {
            { sourceScale, 0, -sourceScale * sourceMeanX },
            { 0, sourceScale, -sourceScale * sourceMeanY },
            { 0, 0, 1 }
        };
        double inverseTargetScale = 1.0 / targetScale;
        double[,] inverseTargetNormalization =
        {
            { inverseTargetScale, 0, targetMeanX },
            { 0, inverseTargetScale, targetMeanY },
            { 0, 0, 1 }
        };

        double[,] denormalized = Multiply3x3(Multiply3x3(inverseTargetNormalization, normalized), sourceNormalization);
        double divisor = denormalized[2, 2];
        if (Math.Abs(divisor) < 1e-12)
        {
            return [];
        }

        return
        [
            denormalized[0, 0] / divisor, denormalized[0, 1] / divisor, denormalized[0, 2] / divisor,
            denormalized[1, 0] / divisor, denormalized[1, 1] / divisor, denormalized[1, 2] / divisor,
            denormalized[2, 0] / divisor, denormalized[2, 1] / divisor, 1.0
        ];
    }

    private static (double MeanX, double MeanY, double Scale) ComputeNormalization(
        IReadOnlyList<(double X, double Y)> points)
    {
        double meanX = points.Average(p => p.X);
        double meanY = points.Average(p => p.Y);
        double meanDistance = points.Average(p => Math.Sqrt(
            ((p.X - meanX) * (p.X - meanX)) + ((p.Y - meanY) * (p.Y - meanY))));
        double scale = meanDistance > 1e-12 ? Math.Sqrt(2.0) / meanDistance : 1.0;
        return (meanX, meanY, scale);
    }

    private static double[,] Multiply3x3(double[,] left, double[,] right)
    {
        var result = new double[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                for (int k = 0; k < 3; k++)
                {
                    result[r, c] += left[r, k] * right[k, c];
                }
            }
        }
        return result;
    }

    private static void AccumulateNormalEquation(double[,] normal, double[] rhs, double[] row, double value)
    {
        int n = row.Length;
        for (int i = 0; i < n; i++)
        {
            rhs[i] += row[i] * value;
            for (int j = 0; j < n; j++)
            {
                normal[i, j] += row[i] * row[j];
            }
        }
    }

    private static double[]? SolveLinearSystem(double[,] matrix, double[] values)
    {
        int N = values.Length;
        var a = new double[N, N + 1];
        for (int r = 0; r < N; r++)
        {
            for (int c = 0; c < N; c++)
            {
                a[r, c] = matrix[r, c];
            }
            a[r, N] = values[r];
        }

        for (int pivot = 0; pivot < N; pivot++)
        {
            int best = pivot;
            for (int r = pivot + 1; r < N; r++)
            {
                if (Math.Abs(a[r, pivot]) > Math.Abs(a[best, pivot]))
                {
                    best = r;
                }
            }

            if (Math.Abs(a[best, pivot]) < 1e-12)
            {
                return null;
            }

            if (best != pivot)
            {
                for (int c = pivot; c <= N; c++)
                {
                    (a[pivot, c], a[best, c]) = (a[best, c], a[pivot, c]);
                }
            }

            double divisor = a[pivot, pivot];
            for (int c = pivot; c <= N; c++)
            {
                a[pivot, c] /= divisor;
            }

            for (int r = 0; r < N; r++)
            {
                if (r == pivot)
                {
                    continue;
                }

                double factor = a[r, pivot];
                for (int c = pivot; c <= N; c++)
                {
                    a[r, c] -= factor * a[pivot, c];
                }
            }
        }

        var result = new double[N];
        for (int i = 0; i < N; i++)
        {
            result[i] = a[i, N];
        }
        return result;
    }

    private static bool ApplyProjective(IReadOnlyList<double> m, double x, double y, out double tx, out double ty)
    {
        tx = 0;
        ty = 0;
        if (m.Count != 9)
        {
            return false;
        }

        double w = (m[6] * x) + (m[7] * y) + m[8];
        if (Math.Abs(w) < 1e-10)
        {
            return false;
        }

        tx = ((m[0] * x) + (m[1] * y) + m[2]) / w;
        ty = ((m[3] * x) + (m[4] * y) + m[5]) / w;
        return double.IsFinite(tx) && double.IsFinite(ty);
    }
}
