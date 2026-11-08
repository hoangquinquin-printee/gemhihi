using System.IO;
using System.Windows;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

public sealed class RejectedComponentInfo
{
    public int ComponentId { get; init; }
    public int PixelArea { get; init; }
    public Rect BoundingBoxPx { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class DetectionPipelineResult
{
    public required List<PatternPiece> Pieces { get; init; }
    public required List<RejectedComponentInfo> RejectedComponents { get; init; }
    public int TotalCandidateComponents { get; init; }
    public (byte R, byte G, byte B) EstimatedBoardBackgroundRgb { get; init; }
    public Dictionary<string, string> SavedDebugStageFiles { get; init; } = [];
}

/// <summary>
/// Phase 2.3 Pattern Detection Engine:
/// Implements the 8-stage pattern detection pipeline from real camera photographs:
/// Stage 1: Image Analysis & Background Estimation
/// Stage 2: Foreground / Background Separation (Luminance + Chromatic distance, glare suppression)
/// Stage 3: Candidate Connected Components Extraction
/// Stage 4: Region Validation (Rejects round holding magnets, border rails, left glare, bottom chair; fills interior magnet holes)
/// Stage 5: Outer Contour Tracing (Moore-Neighbor boundary tracing)
/// Stage 6: Corner Detection & Polygon Simplification
/// Stage 7: Cubic Bézier Curve Fitting
/// Stage 8: PatternPiece Creation in calibrated physical mm + 8 Debug Stage PNG outputs.
/// </summary>
public sealed class DetectionEngine
{
    private readonly ImageEngine _imageEngine;
    private readonly GeometryEngine _geometryEngine;
    private readonly MeasurementEngine _measurementEngine;

    public DetectionEngine(
        ImageEngine? imageEngine = null,
        GeometryEngine? geometryEngine = null,
        MeasurementEngine? measurementEngine = null)
    {
        _imageEngine = imageEngine ?? new ImageEngine();
        _geometryEngine = geometryEngine ?? new GeometryEngine();
        _measurementEngine = measurementEngine ?? new MeasurementEngine();
    }

    public DetectionPipelineResult DetectPatterns(
        RawImageBuffer sourceImage,
        CalibrationProfile calibration,
        string? debugOutputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(calibration);

        int fullW = sourceImage.Width;
        int fullH = sourceImage.Height;

        // A digitize frame is an explicit operator prior: detection remains
        // limited to it, but the photographed material may be neutral paper
        // under uneven light rather than brilliant white.  Keep this separate
        // from ordinary full-photo detection, where the same relaxed threshold
        // would admit broad board glare.
        CropBoxPixels activeRoi = calibration.RestrictDetectionToCropBox && calibration.CropBox.IsValid
            ? calibration.CropBox
            : new CropBoxPixels(0, 0, fullW, fullH);
        bool hasExplicitDigitizeFrame = calibration.RestrictDetectionToCropBox && calibration.CropBox.IsValid;
        RotatedFramePixels? rotatedRoi = calibration.RestrictDetectionToCropBox && calibration.RotatedDetectionFrame is { IsValid: true }
            ? calibration.RotatedDetectionFrame
            : null;

        // Work at 1/2 resolution for high-speed morphological/connected-components processing on multi-megapixel photos,
        // or 1:1 on smaller images (< 1200 px). Coordinates are mapped back to full-resolution pixels accurately.
        int scaleDiv = (fullW >= 1800 && fullH >= 1200) ? 2 : 1;
        int workW = fullW / scaleDiv;
        int workH = fullH / scaleDiv;

        // ===================================================================
        // STAGE 1: IMAGE ANALYSIS & ADAPTIVE 2D BOARD BACKGROUND ESTIMATION
        // ===================================================================
        (byte bgR, byte bgG, byte bgB) = EstimateBoardBackgroundColor(sourceImage);
        double bgLum = (bgR + bgG + bgB) / 3.0;
        double bgBlueRedDiff = bgB - bgR;

        const int gridCols = 16;
        const int gridRows = 12;
        double[,] localBgGrid = EstimateLocalBackgroundLuminanceGrid(sourceImage, bgLum, gridCols, gridRows);

        // ===================================================================
        // STAGE 2: FOREGROUND / BACKGROUND SEPARATION
        // ===================================================================
        byte[] responseMap = new byte[workW * workH];
        byte[] rawMask = new byte[workW * workH];
        byte[] chromaFabricMask = new byte[workW * workH];
        int srcStride = sourceImage.Stride;
        byte[] srcPixels = sourceImage.BgraPixels;

        // Relative outer frame margins (~1.8% horizontal, ~0.5% vertical) to suppress physical board rails
        int leftRailPx = (fullW >= 1200) ? Math.Max(2, (int)(workW * 0.018)) : 2;
        int rightRailPx = (fullW >= 1200) ? Math.Min(workW - 2, (int)(workW * 0.980)) : (workW - 2);
        int topRailPx = (fullH >= 900) ? Math.Max(1, (int)(workH * 0.005)) : 1;
        int bottomRailPx = (fullH >= 900) ? Math.Min(workH - 1, (int)(workH * 0.994)) : (workH - 1);

        for (int wy = 0; wy < workH; wy++)
        {
            int sy = Math.Min(fullH - 1, wy * scaleDiv);
            int srcRow = sy * srcStride;
            int dstRow = wy * workW;

            for (int wx = 0; wx < workW; wx++)
            {
                int sx = Math.Min(fullW - 1, wx * scaleDiv);
                if (!activeRoi.Contains(sx, sy) ||
                    (rotatedRoi.HasValue && !rotatedRoi.Value.Contains(sx, sy)) ||
                    wx < leftRailPx || wx >= rightRailPx ||
                    wy < topRailPx || wy >= bottomRailPx)
                {
                    continue;
                }

                int idx = srcRow + (sx * 4);
                int b = srcPixels[idx];
                int g = srcPixels[idx + 1];
                int r = srcPixels[idx + 2];

                double lum = (r + g + b) / 3.0;
                double localBgLum = SampleLocalBackgroundLuminance(localBgGrid, gridCols, gridRows, wx, wy, workW, workH);
                double lumDiffGlobal = lum - bgLum;
                double lumDiffLocal = lum - localBgLum;

                // Chromatic difference relative to the dark grey board:
                // Captures colored fabric pieces (e.g. blue denim where B-R is +38..+75)
                double blueRedShift = (b - r) - bgBlueRedDiff;
                double blueGreenShift = b - g;
                double colorDist = Math.Sqrt(
                    ((r - bgR) * (r - bgR)) +
                    ((g - bgG) * (g - bgG)) +
                    ((b - bgB) * (b - bgB)));

                double score;
                bool isForeground = false;
                bool isChromaFabric = false;

                if (blueRedShift >= 38.0 &&
                    blueGreenShift >= 16.0 &&
                    b >= (r * 1.38) &&
                    lum >= Math.Min(bgLum, localBgLum) + 10.0)
                {
                    // Colored fabric pattern piece (e.g. blue denim)
                    score = Math.Clamp((blueRedShift * 3.2) + Math.Max(0, lumDiffGlobal), 0, 255);
                    isForeground = true;
                    isChromaFabric = true;
                }
                else if (lum >= 210.0 ||
                         (lumDiffLocal >= 85.0 && lumDiffGlobal >= 88.0 && lum >= 184.0) ||
                         // Inside an explicitly drawn frame, retain neutral
                         // paper that is clearly brighter than the estimated
                         // board even if every local grid sample is paper.
                         // Region validation below still rejects diffuse glare
                         // using its real boundary contrast.
                         (hasExplicitDigitizeFrame && lumDiffGlobal >= 72.0 && lum >= 150.0))
                {
                    // Bright white/light pattern paper (suppresses diffuse low-luminance board glare naturally via localBgLum)
                    score = Math.Clamp(lumDiffGlobal * 1.35, 0, 255);
                    isForeground = true;
                }
                else if (hasExplicitDigitizeFrame &&
                         localBgLum >= 175.0 &&
                         lum <= localBgLum - 28.0 &&
                         colorDist >= 42.0 &&
                         // A framed brown/cardboard piece has a warm ordered
                         // channel profile (R > G > B).  Do not classify
                         // neutral grey tools, magnets, or dark shadows as a
                         // second pattern merely because they are darker than
                         // the white background.
                         r >= g + 18.0 &&
                         g >= b + 18.0)
                {
                    // Neutral brown/cardboard patterns are darker than a white
                    // photo background.  Gemini still traces these pieces when
                    // a digitize frame is active; treating only the local
                    // contrast as foreground avoids turning dark tools into a
                    // full-photo candidate.  Component validation below rejects
                    // the detached tool/magnet blobs.
                    score = Math.Clamp((localBgLum - lum) * 1.6, 0, 255);
                    isForeground = true;
                }
                else if (colorDist >= 78.0 && lumDiffLocal >= 48.0 && Math.Abs(r - b) >= 28)
                {
                    // Medium-bright colored pattern material
                    score = Math.Clamp(colorDist * 1.8, 0, 255);
                    isForeground = true;
                    isChromaFabric = true;
                }
                else
                {
                    score = Math.Clamp(Math.Max(0, lumDiffLocal), 0, 100);
                }

                responseMap[dstRow + wx] = (byte)score;
                if (isForeground)
                {
                    rawMask[dstRow + wx] = 255;
                    if (isChromaFabric)
                    {
                        chromaFabricMask[dstRow + wx] = 255;
                    }
                }
            }
        }

        // Morphological cleanup on rawMask:
        // 1) 5x5 closing to bridge thin red pen lines drawn on white pattern paper
        // 2) 3x3 opening to remove single-pixel noise
        byte[] closedMask = MorphologicalClose(rawMask, workW, workH, radius: 2);
        byte[] cleanMask = MorphologicalOpen(closedMask, workW, workH, radius: 1);
        byte[] closedChromaMask = MorphologicalClose(chromaFabricMask, workW, workH, radius: 2);

        // ===================================================================
        // STAGE 3: CANDIDATE CONNECTED COMPONENTS EXTRACTION
        // ===================================================================
        int[] labels = new int[workW * workH];
        var components = ExtractConnectedComponents(cleanMask, workW, workH, labels);

        // ===================================================================
        // STAGE 4: REGION VALIDATION (REJECT MAGNETS, GLARE, BORDER ARTIFACTS)
        // ===================================================================
        var validComponents = new List<ComponentBlob>();
        var rejectedInfos = new List<RejectedComponentInfo>();
        var rejectedComponentIds = new HashSet<int>();

        double imageTotalArea = (double)fullW * fullH;
        int minPatternAreaFullPx = (fullW >= 1200 && fullH >= 900)
            ? Math.Max(900, (int)(imageTotalArea * 0.0031))
            : 900;
        int minPatternAreaWorkPx = Math.Max(60, minPatternAreaFullPx / (scaleDiv * scaleDiv));
        int maxMagnetAreaWorkPx = (fullW >= 1200 && fullH >= 900)
            ? Math.Max(200, (int)(imageTotalArea * 0.0018) / (scaleDiv * scaleDiv))
            : 400;

        foreach (ComponentBlob comp in components)
        {
            int fullMinX = comp.MinX * scaleDiv;
            int fullMaxX = comp.MaxX * scaleDiv;
            int fullMinY = comp.MinY * scaleDiv;
            int fullMaxY = comp.MaxY * scaleDiv;
            int fullArea = comp.PixelCount * scaleDiv * scaleDiv;
            var fullRect = new Rect(fullMinX, fullMinY, Math.Max(1, fullMaxX - fullMinX + scaleDiv), Math.Max(1, fullMaxY - fullMinY + scaleDiv));

            double bboxW = comp.MaxX - comp.MinX + 1;
            double bboxH = comp.MaxY - comp.MinY + 1;
            double aspect = Math.Max(bboxW, bboxH) / Math.Max(1.0, Math.Min(bboxW, bboxH));
            double fillRatio = comp.PixelCount / Math.Max(1.0, bboxW * bboxH);

            if (comp.PixelCount < Math.Max(16, 120 / (scaleDiv * scaleDiv)))
            {
                rejectedComponentIds.Add(comp.Id);
                rejectedInfos.Add(new RejectedComponentInfo
                {
                    ComponentId = comp.Id,
                    PixelArea = fullArea,
                    BoundingBoxPx = fullRect,
                    Reason = "TooSmallNoise"
                });
                continue;
            }

            // Check if component is a round holding magnet (small compact circular/annular blob)
            if (comp.PixelCount < maxMagnetAreaWorkPx && aspect < 2.2)
            {
                rejectedComponentIds.Add(comp.Id);
                rejectedInfos.Add(new RejectedComponentInfo
                {
                    ComponentId = comp.Id,
                    PixelArea = fullArea,
                    BoundingBoxPx = fullRect,
                    Reason = "HoldingMagnet"
                });
                continue;
            }

            // Compute component luminance & chromatic fabric fraction
            int chromaPixels = 0;
            double sumLum = 0;
            foreach ((int px, int py) in comp.Pixels)
            {
                if (closedChromaMask[(py * workW) + px] != 0)
                {
                    chromaPixels++;
                }
                int sx = Math.Min(fullW - 1, px * scaleDiv);
                int sy = Math.Min(fullH - 1, py * scaleDiv);
                int sIdx = (sy * srcStride) + (sx * 4);
                sumLum += (srcPixels[sIdx] + srcPixels[sIdx + 1] + srcPixels[sIdx + 2]) / 3.0;
            }
            double chromaFraction = (double)chromaPixels / Math.Max(1, comp.PixelCount);
            double meanLum = sumLum / Math.Max(1, comp.PixelCount);

            // Check if component is a shallow border artifact (e.g. chair back or rail reflection confined to outer 8.5% border strip)
            bool confinedToBottomStrip = fullMaxY >= (int)(fullH * 0.985) && fullMinY >= (int)(fullH * 0.915);
            bool confinedToTopStrip = fullMinY <= (int)(fullH * 0.015) && fullMaxY <= (int)(fullH * 0.085);
            bool confinedToLeftStrip = fullMinX <= (int)(fullW * 0.040) && fullMaxX <= (int)(fullW * 0.165);
            bool confinedToRightStrip = fullMaxX >= (int)(fullW * 0.960) && fullMinX >= (int)(fullW * 0.915);

            if (confinedToBottomStrip || confinedToTopStrip)
            {
                rejectedComponentIds.Add(comp.Id);
                rejectedInfos.Add(new RejectedComponentInfo
                {
                    ComponentId = comp.Id,
                    PixelArea = fullArea,
                    BoundingBoxPx = fullRect,
                    Reason = "BottomEdgeArtifact"
                });
                continue;
            }

            double roiAreaPx = Math.Max(1.0, (double)activeRoi.Width * activeRoi.Height);
            bool fillsMeaningfulPartOfExplicitFrame = hasExplicitDigitizeFrame &&
                fullArea >= roiAreaPx * 0.08 &&
                fullRect.Width >= activeRoi.Width * 0.20 &&
                fullRect.Height >= activeRoi.Height * 0.20;

            if (confinedToLeftStrip || confinedToRightStrip ||
                ((meanLum < 192.0 && chromaFraction < 0.25) && !fillsMeaningfulPartOfExplicitFrame))
            {
                rejectedComponentIds.Add(comp.Id);
                rejectedInfos.Add(new RejectedComponentInfo
                {
                    ComponentId = comp.Id,
                    PixelArea = fullArea,
                    BoundingBoxPx = fullRect,
                    Reason = "BorderFrameOrGlare"
                });
                continue;
            }

            if (comp.PixelCount < minPatternAreaWorkPx || fillRatio < 0.08)
            {
                rejectedComponentIds.Add(comp.Id);
                rejectedInfos.Add(new RejectedComponentInfo
                {
                    ComponentId = comp.Id,
                    PixelArea = fullArea,
                    BoundingBoxPx = fullRect,
                    Reason = "SubThresholdRegion"
                });
                continue;
            }

            // Real pattern pieces have a sharp physical boundary step against the board; lens flare / glare has a
            // diffuse low-frequency boundary. Measure the median luminance step across the component boundary.
            double boundaryContrast = MeasureBoundaryContrast(comp, cleanMask, workW, workH, sourceImage, scaleDiv);
            if (Environment.GetEnvironmentVariable("PDP_DETECT_DIAG") == "1")
            {
                Console.WriteLine($"  [diag] comp bbox={fullRect} area={fullArea} chroma={chromaFraction:F2} meanLum={meanLum:F0} boundaryContrast={boundaryContrast:F1}");
            }

            if (boundaryContrast < 18.0)
            {
                rejectedComponentIds.Add(comp.Id);
                rejectedInfos.Add(new RejectedComponentInfo
                {
                    ComponentId = comp.Id,
                    PixelArea = fullArea,
                    BoundingBoxPx = fullRect,
                    Reason = "DiffuseGlare"
                });
                continue;
            }

            comp.ChromaFraction = chromaFraction;
            validComponents.Add(comp);
        }

        // Order valid pattern components in natural spatial order (left-to-right, top-to-bottom)
        validComponents = validComponents
            .OrderBy(c => (c.MinX * 2) + c.MinY)
            .ToList();

        // ===================================================================
        // STAGES 5, 6, 7, 8: CONTOUR EXTRACTION, CORNER DETECTION, CURVE FITTING, PIECE CREATION
        // ===================================================================
        var detectedPieces = new List<PatternPiece>();
        var rawContoursPerPiece = new List<List<ImagePixelPoint>>();

        int pieceIndex = 1;
        foreach (ComponentBlob comp in validComponents)
        {
            int pad = 16;
            int localW = (comp.MaxX - comp.MinX + 1) + (pad * 2);
            int localH = (comp.MaxY - comp.MinY + 1) + (pad * 2);
            byte[] localMask = new byte[localW * localH];

            bool isColoredFabricPiece = comp.ChromaFraction >= 0.40;
            byte[]? magnetLocalMask = isColoredFabricPiece ? new byte[localW * localH] : null;

            foreach ((int px, int py) in comp.Pixels)
            {
                int lx = (px - comp.MinX) + pad;
                int ly = (py - comp.MinY) + pad;

                // For colored fabric pieces (e.g. blue denim), achromatic bright white pixels on the boundary
                // are holding magnets (such as the magnet in the concave crotch of Piece 2 in real_01082026_091623.jpg).
                // Excluding non-chroma magnet pixels before FillInteriorHoles fills interior magnets while
                // cleanly detaching boundary/concave magnets!
                if (isColoredFabricPiece && closedChromaMask[(py * workW) + px] == 0)
                {
                    magnetLocalMask![(ly * localW) + lx] = 255;
                    continue;
                }

                localMask[(ly * localW) + lx] = 255;
            }

            // Fill interior magnet holes completely
            FillInteriorHoles(localMask, localW, localH);

            if (magnetLocalMask is not null)
            {
                // Edge magnets occlude the true fabric edge and leave a circular "bite" once removed.
                // Bridge the bite with a straight-ish chord: a large closing applied ONLY inside the (slightly dilated)
                // occluding magnet footprint, so corners and edges elsewhere are untouched.
                int magnetBridgeRad = Math.Max(6, (int)Math.Round(Math.Sqrt(maxMagnetAreaWorkPx / Math.PI) * 0.75));
                byte[] bridged = SeparableSquareClose(localMask, localW, localH, magnetBridgeRad);
                byte[] magnetZone = SeparableSquareDilate(magnetLocalMask, localW, localH, 2);
                for (int i = 0; i < localMask.Length; i++)
                {
                    if (localMask[i] == 0 && magnetZone[i] != 0 && bridged[i] != 0)
                    {
                        localMask[i] = 255;
                    }
                }
                FillInteriorHoles(localMask, localW, localH);
            }

            // Use small morphological radius (2..3 px at work resolution) to preserve sharp 90-degree corners
            int closeRad = isColoredFabricPiece ? 3 : 2;
            localMask = MorphologicalClose(localMask, localW, localH, radius: closeRad);
            FillInteriorHoles(localMask, localW, localH);
            localMask = MorphologicalOpen(localMask, localW, localH, radius: 1);

            // Stage 5: Trace ordered 8-connected outer boundary in work coordinates -> map to full image pixel coordinates
            List<(int X, int Y)> localContour = TraceOuterBoundary(localMask, localW, localH);
            if (localContour.Count < 12)
            {
                continue;
            }

            var fullPixelContour = new List<ImagePixelPoint>(localContour.Count);
            foreach ((int lx, int ly) in localContour)
            {
                double fx = ((lx - pad) + comp.MinX) * scaleDiv;
                double fy = ((ly - pad) + comp.MinY) * scaleDiv;
                fullPixelContour.Add(new ImagePixelPoint(fx, fy));
            }

            // A holding magnet often joins a white paper component in the
            // foreground mask.  Its dark rim then becomes a small outward
            // loop on the traced boundary.  Gemini's contour follows the
            // paper edge, not that rim.  Before fitting geometry, bridge only
            // short neutral/dark occluder runs whose outside is distinctly
            // darker.  Coloured fabric is deliberately excluded: a darker
            // fold on fabric is not evidence of a magnet.
            if (!isColoredFabricPiece)
            {
                fullPixelContour = BridgeNeutralDarkOccluderRuns(fullPixelContour, sourceImage);
                fullPixelContour = SuppressAttachedCircularLoops(fullPixelContour);
            }

            // Smooth pixel staircase jitter along boundary with 2 light passes (preserving sharp corners)
            // A third light pass removes the 1–2 px staircase left by camera
            // compression without moving the contour by more than one pixel.
            List<ImagePixelPoint> smoothedContour = SmoothClosedContourPixels(fullPixelContour, iterations: 3);
            rawContoursPerPiece.Add(smoothedContour);

            // Stage 6 & 7: Corner Detection & Curve/Line Fitting -> Stage 8: PatternPiece Creation
            PatternPiece piece = BuildPatternPieceFromBoundary(
                smoothedContour,
                calibration,
                pieceIndex,
                new Rect(
                    comp.MinX * scaleDiv,
                    comp.MinY * scaleDiv,
                    (comp.MaxX - comp.MinX + 1) * scaleDiv,
                    (comp.MaxY - comp.MinY + 1) * scaleDiv));

            detectedPieces.Add(piece);
            pieceIndex++;
        }

        var savedDebugFiles = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(debugOutputDirectory))
        {
            savedDebugFiles = SaveDebugStageImages(
                sourceImage,
                responseMap,
                cleanMask,
                labels,
                components,
                rejectedComponentIds,
                rawContoursPerPiece,
                detectedPieces,
                calibration,
                workW,
                workH,
                scaleDiv,
                debugOutputDirectory);
        }

        return new DetectionPipelineResult
        {
            Pieces = detectedPieces,
            RejectedComponents = rejectedInfos,
            TotalCandidateComponents = components.Count,
            EstimatedBoardBackgroundRgb = (bgR, bgG, bgB),
            SavedDebugStageFiles = savedDebugFiles
        };
    }

    private PatternPiece BuildPatternPieceFromBoundary(
        List<ImagePixelPoint> boundaryPx,
        CalibrationProfile calibration,
        int index,
        Rect sourcePixelBBox)
    {
        var piece = new PatternPiece
        {
            Index = index,
            Name = $"Piece {index}",
            SourcePixelBoundingBox = sourcePixelBBox
        };

        // Check if this piece is a clean 4-sided quadrilateral/rectangle (even if magnets sit near corners)
        if (TryFitConvexQuadrilateralCorners(boundaryPx, out List<ImagePixelPoint> quadCornersPx))
        {
            for (int i = 0; i < quadCornersPx.Count; i++)
            {
                ImagePixelPoint px = quadCornersPx[i];
                PhysicalPointMm mm = calibration.PixelToPhysicalMm(px);
                ImagePixelPoint nextPx = quadCornersPx[(i + 1) % quadCornersPx.Count];
                PhysicalPointMm nextMm = calibration.PixelToPhysicalMm(nextPx);
                ImagePixelPoint prevPx = quadCornersPx[(i - 1 + quadCornersPx.Count) % quadCornersPx.Count];
                PhysicalPointMm prevMm = calibration.PixelToPhysicalMm(prevPx);

                piece.Vertices.Add(new ContourVertex
                {
                    PositionMm = mm,
                    SourcePixelPosition = px,
                    IsGradedCorner = true,
                    IsPieceOrigin = (i == 0),
                    OutgoingIsCurve = false,
                    OutControlHandleMm = mm.Lerp(nextMm, 1.0 / 3.0),
                    InControlHandleMm = prevMm.Lerp(mm, 2.0 / 3.0)
                });
            }

            _measurementEngine.RecalculatePieceMetrics(piece);
            return piece;
        }

        // General polygonal + curved garment piece:
        // 1. Simplify with Ramer-Douglas-Peucker in physical mm space
        var boundaryMm = boundaryPx.Select(calibration.PixelToPhysicalMm).ToList();
        double perimeterEstMm = 0;
        for (int i = 0; i < boundaryMm.Count; i++)
        {
            perimeterEstMm += boundaryMm[i].DistanceTo(boundaryMm[(i + 1) % boundaryMm.Count]);
        }

        // The contour is sampled at pixel frequency.  Keep only structural
        // anchors; the fitted Bézier between anchors retains smooth garment
        // curves.  A low tolerance exposed pin rims, paper fibres and mask
        // staircase noise as editable points.
        // Gemini keeps only structural anchors on neutral paper: compression
        // stair-steps, paper fibres and magnet shadows are not editable points.
        // The previous 0.4% tolerance left those artefacts as extra corners on
        // phone photos.  Use a slightly stronger scale-aware tolerance while
        // retaining the fitted Bézier residual checks below.
        double rdpToleranceMm = Math.Clamp(perimeterEstMm * 0.0060, 3.0, 9.0);
        List<int> keyIndices = SimplifyClosedContourIndices(boundaryMm, rdpToleranceMm);

        // 2. Merge adjacent smooth curve spans across non-corner intermediate vertices (where local tangent discontinuity < 22 deg)
        // if a single Cubic Bézier fits the combined chain within 2.4 mm max residual error.
        bool mergedAny = true;
        while (mergedAny && keyIndices.Count > 4)
        {
            mergedAny = false;
            for (int k = 0; k < keyIndices.Count; k++)
            {
                int idxPrev = keyIndices[(k - 1 + keyIndices.Count) % keyIndices.Count];
                int idxCurr = keyIndices[k];
                int idxNext = keyIndices[(k + 1) % keyIndices.Count];

                double localTurn = ComputeLocalTangentTurnDegrees(boundaryMm, idxCurr, armMinMm: 4.0, armMaxMm: 24.0);
                var combinedChain = ExtractContourChain(boundaryMm, idxPrev, idxNext);
                var (_, _, maxErrMm, _) = _geometryEngine.FitCubicBezierToPointChain(combinedChain);

                double chainLenMm = 0;
                for (int c = 1; c < combinedChain.Count; c++)
                {
                    chainLenMm += combinedChain[c - 1].DistanceTo(combinedChain[c]);
                }

                // Smooth vertex: low local turn and a single Bézier fits within 2.4 mm, OR a long span whose
                // direction change is fully explained by one smooth Bézier within 1.5 mm (high-curvature arc, not a corner).
                bool smoothVertex = (localTurn < 22.0 && maxErrMm <= 2.4) ||
                                    (chainLenMm >= 80.0 && maxErrMm <= 1.5);
                if (smoothVertex)
                {
                    keyIndices.RemoveAt(k);
                    mergedAny = true;
                    break;
                }
            }
        }

        // 2b. Collapse near-duplicate key vertices (closer than ~6 mm along the contour) produced by small
        // corner rounding / staircase residue, keeping the vertex with the stronger local tangent turn.
        bool collapsed = true;
        while (collapsed && keyIndices.Count > 3)
        {
            collapsed = false;
            for (int k = 0; k < keyIndices.Count; k++)
            {
                int kn = (k + 1) % keyIndices.Count;
                if (boundaryMm[keyIndices[k]].DistanceTo(boundaryMm[keyIndices[kn]]) >= 8.0)
                {
                    continue;
                }

                double tA = ComputeLocalTangentTurnDegrees(boundaryMm, keyIndices[k], 3.5, 22.0);
                double tB = ComputeLocalTangentTurnDegrees(boundaryMm, keyIndices[kn], 3.5, 22.0);
                keyIndices.RemoveAt(tA >= tB ? kn : k);
                collapsed = true;
                break;
            }
        }

        // 3. Classify sharp Graded Corners vs smooth Curve Points using BOTH local tangent discontinuity and chord turn angle,
        // and refine sharp corner apex positions by intersecting local incoming/outgoing tangent lines.
        var cornerFlags = new bool[keyIndices.Count];
        var refinedMm = new PhysicalPointMm[keyIndices.Count];
        var refinedPx = new ImagePixelPoint[keyIndices.Count];
        var localTurns = new double[keyIndices.Count];

    reclassify:
        cornerFlags = new bool[keyIndices.Count];
        refinedMm = new PhysicalPointMm[keyIndices.Count];
        refinedPx = new ImagePixelPoint[keyIndices.Count];
        localTurns = new double[keyIndices.Count];

        for (int k = 0; k < keyIndices.Count; k++)
        {
            int idxCurr = keyIndices[k];
            PhysicalPointMm prev = boundaryMm[keyIndices[(k - 1 + keyIndices.Count) % keyIndices.Count]];
            PhysicalPointMm curr = boundaryMm[idxCurr];
            PhysicalPointMm next = boundaryMm[keyIndices[(k + 1) % keyIndices.Count]];

            double chordTurnDeg = ComputeTurningAngleDegrees(prev, curr, next);
            // Do not classify a one-pixel stair-step, pin shadow, or torn
            // paper edge as a graded corner. A genuine pattern corner must
            // retain its direction change over a meaningful physical span.
            double localTurnDeg = ComputeLocalTangentTurnDegrees(boundaryMm, idxCurr, armMinMm: 8.0, armMaxMm: 35.0);
            localTurns[k] = localTurnDeg;

            bool isSharpCorner = localTurnDeg >= 34.0 || (chordTurnDeg >= 52.0 && localTurnDeg >= 27.0);
            cornerFlags[k] = isSharpCorner;

            if (isSharpCorner && TryRefineSharpCornerApex(boundaryMm, idxCurr, out PhysicalPointMm apexMm))
            {
                refinedMm[k] = apexMm;
                refinedPx[k] = calibration.PhysicalMmToPixel(apexMm);
            }
            else
            {
                refinedMm[k] = curr;
                refinedPx[k] = boundaryPx[idxCurr];
            }
        }

        // Two key vertices whose refined apexes coincide (< 8 mm) describe the same physical corner: keep the stronger one.
        if (keyIndices.Count > 3)
        {
            for (int k = 0; k < keyIndices.Count; k++)
            {
                int kn = (k + 1) % keyIndices.Count;
                if (refinedMm[k].DistanceTo(refinedMm[kn]) < 8.0)
                {
                    keyIndices.RemoveAt(localTurns[k] >= localTurns[kn] ? kn : k);
                    goto reclassify;
                }
            }
        }

        // Ensure at least 3 graded corners exist
        if (cornerFlags.Count(c => c) < 3)
        {
            for (int k = 0; k < cornerFlags.Length; k++)
            {
                cornerFlags[k] = true;
            }
        }

        for (int k = 0; k < keyIndices.Count; k++)
        {
            int idx0 = keyIndices[k];
            int idx1 = keyIndices[(k + 1) % keyIndices.Count];

            var chainMm = ExtractContourChain(boundaryMm, idx0, idx1);
            if (chainMm.Count >= 2)
            {
                chainMm[0] = refinedMm[k];
                chainMm[^1] = refinedMm[(k + 1) % keyIndices.Count];
            }

            (PhysicalPointMm c1, PhysicalPointMm c2, _, bool isCurved) =
                _geometryEngine.FitCubicBezierToPointChain(chainMm);

            piece.Vertices.Add(new ContourVertex
            {
                PositionMm = refinedMm[k],
                SourcePixelPosition = refinedPx[k],
                IsGradedCorner = cornerFlags[k],
                IsPieceOrigin = (k == 0),
                OutgoingIsCurve = isCurved,
                OutControlHandleMm = c1,
                InControlHandleMm = refinedMm[k] // populated in second pass below
            });
        }

        // Second pass to assign InControlHandleMm for each vertex from incoming segment's fitted C2
        for (int k = 0; k < keyIndices.Count; k++)
        {
            int idx0 = keyIndices[k];
            int idx1 = keyIndices[(k + 1) % keyIndices.Count];
            var chainMm = ExtractContourChain(boundaryMm, idx0, idx1);
            if (chainMm.Count >= 2)
            {
                chainMm[0] = refinedMm[k];
                chainMm[^1] = refinedMm[(k + 1) % keyIndices.Count];
            }

            (PhysicalPointMm c1, PhysicalPointMm c2, _, bool isCurved) =
                _geometryEngine.FitCubicBezierToPointChain(chainMm);

            int nextK = (k + 1) % piece.Vertices.Count;
            piece.Vertices[k].OutgoingIsCurve = isCurved;
            piece.Vertices[k].OutControlHandleMm = c1;
            piece.Vertices[nextK].InControlHandleMm = c2;
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
        return piece;
    }

    private static List<PhysicalPointMm> ExtractContourChain(List<PhysicalPointMm> boundaryMm, int idx0, int idx1)
    {
        var chainMm = new List<PhysicalPointMm>();
        int walk = idx0;
        while (true)
        {
            chainMm.Add(boundaryMm[walk]);
            if (walk == idx1)
            {
                break;
            }
            walk = (walk + 1) % boundaryMm.Count;
            if (chainMm.Count > boundaryMm.Count)
            {
                break;
            }
        }
        return chainMm;
    }

    private static double ComputeLocalTangentTurnDegrees(
        List<PhysicalPointMm> boundaryMm,
        int centerIdx,
        double armMinMm,
        double armMaxMm)
    {
        int n = boundaryMm.Count;
        if (n < 8)
        {
            return 0.0;
        }

        PhysicalPointMm pCenter = boundaryMm[centerIdx];
        var backPts = new List<PhysicalPointMm>();
        double distBack = 0.0;
        int cur = centerIdx;
        for (int step = 0; step < n / 3; step++)
        {
            int prev = (cur - 1 + n) % n;
            distBack += boundaryMm[cur].DistanceTo(boundaryMm[prev]);
            cur = prev;
            if (distBack >= armMinMm && distBack <= armMaxMm)
            {
                backPts.Add(boundaryMm[cur]);
            }
            if (distBack > armMaxMm)
            {
                break;
            }
        }

        var fwdPts = new List<PhysicalPointMm>();
        double distFwd = 0.0;
        cur = centerIdx;
        for (int step = 0; step < n / 3; step++)
        {
            int next = (cur + 1) % n;
            distFwd += boundaryMm[cur].DistanceTo(boundaryMm[next]);
            cur = next;
            if (distFwd >= armMinMm && distFwd <= armMaxMm)
            {
                fwdPts.Add(boundaryMm[cur]);
            }
            if (distFwd > armMaxMm)
            {
                break;
            }
        }

        if (backPts.Count < 2 || fwdPts.Count < 2)
        {
            PhysicalPointMm pB = backPts.Count > 0 ? backPts[^1] : boundaryMm[(centerIdx - 3 + n) % n];
            PhysicalPointMm pF = fwdPts.Count > 0 ? fwdPts[^1] : boundaryMm[(centerIdx + 3) % n];
            return ComputeTurningAngleDegrees(pB, pCenter, pF);
        }

        PhysicalPointMm avgBack = new(backPts.Average(p => p.XMm), backPts.Average(p => p.YMm));
        PhysicalPointMm avgFwd = new(fwdPts.Average(p => p.XMm), fwdPts.Average(p => p.YMm));
        return ComputeTurningAngleDegrees(avgBack, pCenter, avgFwd);
    }

    private static bool TryRefineSharpCornerApex(
        List<PhysicalPointMm> boundaryMm,
        int centerIdx,
        out PhysicalPointMm refinedApex)
    {
        refinedApex = boundaryMm[centerIdx];
        int n = boundaryMm.Count;
        if (n < 12)
        {
            return false;
        }

        var backArm = new List<ImagePixelPoint>();
        double dist = 0.0;
        int cur = centerIdx;
        for (int step = 0; step < n / 4; step++)
        {
            int prev = (cur - 1 + n) % n;
            dist += boundaryMm[cur].DistanceTo(boundaryMm[prev]);
            cur = prev;
            if (dist >= 3.5 && dist <= 22.0)
            {
                backArm.Add(new ImagePixelPoint(boundaryMm[cur].XMm, boundaryMm[cur].YMm));
            }
            if (dist > 22.0) break;
        }

        var fwdArm = new List<ImagePixelPoint>();
        dist = 0.0;
        cur = centerIdx;
        for (int step = 0; step < n / 4; step++)
        {
            int next = (cur + 1) % n;
            dist += boundaryMm[cur].DistanceTo(boundaryMm[next]);
            cur = next;
            if (dist >= 3.5 && dist <= 22.0)
            {
                fwdArm.Add(new ImagePixelPoint(boundaryMm[cur].XMm, boundaryMm[cur].YMm));
            }
            if (dist > 22.0) break;
        }

        if (backArm.Count < 3 || fwdArm.Count < 3)
        {
            return false;
        }

        var l1 = FitStraightLine2D(backArm);
        var l2 = FitStraightLine2D(fwdArm);
        if (!TryIntersectLines(l1, l2, out ImagePixelPoint apexPt))
        {
            return false;
        }

        var candidate = new PhysicalPointMm(apexPt.X, apexPt.Y);
        if (candidate.DistanceTo(boundaryMm[centerIdx]) <= 5.5)
        {
            refinedApex = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tests whether a closed pixel contour is well-approximated by a 4-corner convex quadrilateral
    /// (such as rectangular pattern pieces on the board, even if a round magnet sits near a corner).
    /// Fits lines to the 4 sides and intersects them to recover crisp, magnet-free corners.
    /// </summary>
    private static bool TryFitConvexQuadrilateralCorners(
        List<ImagePixelPoint> contour,
        out List<ImagePixelPoint> quadCorners)
    {
        quadCorners = [];
        if (contour.Count < 20)
        {
            return false;
        }

        double cx = contour.Average(p => p.X);
        double cy = contour.Average(p => p.Y);

        // Find 4 extremal corner seeds by iteratively picking farthest contour points
        int idx0 = 0;
        double maxD0 = -1;
        for (int i = 0; i < contour.Count; i++)
        {
            double d = Math.Pow(contour[i].X - cx, 2) + Math.Pow(contour[i].Y - cy, 2);
            if (d > maxD0)
            {
                maxD0 = d;
                idx0 = i;
            }
        }

        int idx2 = 0;
        double maxD2 = -1;
        for (int i = 0; i < contour.Count; i++)
        {
            double d = contour[i].DistanceTo(contour[idx0]);
            if (d > maxD2)
            {
                maxD2 = d;
                idx2 = i;
            }
        }

        // Find idx1 on one side of diagonal (idx0 -> idx2) and idx3 on the other side
        int idx1 = -1, idx3 = -1;
        double maxPos = -1, maxNeg = -1;
        double dxDiag = contour[idx2].X - contour[idx0].X;
        double dyDiag = contour[idx2].Y - contour[idx0].Y;
        double diagLen = Math.Sqrt((dxDiag * dxDiag) + (dyDiag * dyDiag));
        if (diagLen < 40)
        {
            return false;
        }

        for (int i = 0; i < contour.Count; i++)
        {
            double signedCross = ((contour[i].X - contour[idx0].X) * dyDiag - (contour[i].Y - contour[idx0].Y) * dxDiag) / diagLen;
            if (signedCross > maxPos)
            {
                maxPos = signedCross;
                idx1 = i;
            }
            if (-signedCross > maxNeg)
            {
                maxNeg = -signedCross;
                idx3 = i;
            }
        }

        if (idx1 < 0 || idx3 < 0 || maxPos < 25 || maxNeg < 25)
        {
            return false;
        }

        int[] cornerSeeds = [idx0, idx1, idx2, idx3];
        Array.Sort(cornerSeeds);

        // For each of the 4 sides between cornerSeeds[s] and cornerSeeds[(s+1)%4],
        // take the middle 60% of points (avoiding corner magnets!) and fit a straight line.
        var sideLines = new (double Px, double Py, double Vx, double Vy)[4];
        for (int s = 0; s < 4; s++)
        {
            int start = cornerSeeds[s];
            int end = cornerSeeds[(s + 1) % 4];
            var sidePts = new List<ImagePixelPoint>();
            int w = start;
            while (true)
            {
                sidePts.Add(contour[w]);
                if (w == end) break;
                w = (w + 1) % contour.Count;
                if (sidePts.Count > contour.Count) break;
            }

            if (sidePts.Count < 6)
            {
                return false;
            }

            int trim = Math.Max(1, (int)(sidePts.Count * 0.20));
            var midPts = sidePts.Skip(trim).Take(Math.Max(3, sidePts.Count - (2 * trim))).ToList();
            sideLines[s] = FitStraightLine2D(midPts);

            // Check straightness of the middle 60% of the side
            double sideChord = sidePts[0].DistanceTo(sidePts[^1]);
            double maxSideResidual = 0;
            foreach (ImagePixelPoint pt in midPts)
            {
                double dist = Math.Abs(((pt.X - sideLines[s].Px) * (-sideLines[s].Vy)) + ((pt.Y - sideLines[s].Py) * sideLines[s].Vx));
                if (dist > maxSideResidual)
                {
                    maxSideResidual = dist;
                }
            }

            // If the middle of any side curves by more than 2.4% of its length (or > 14 px), it is a curved piece, not a straight-sided quad!
            if (maxSideResidual > Math.Max(11.0, sideChord * 0.022))
            {
                return false;
            }
        }

        // Intersect adjacent side lines to obtain the 4 crisp corners
        var intersected = new List<ImagePixelPoint>(4);
        for (int s = 0; s < 4; s++)
        {
            var l1 = sideLines[(s + 3) % 4];
            var l2 = sideLines[s];
            if (!TryIntersectLines(l1, l2, out ImagePixelPoint corner))
            {
                return false;
            }
            intersected.Add(corner);
        }

        // Verify that the resulting 4-corner polygon area matches the contour area within 5%
        double contourArea = ComputePixelPolygonArea(contour);
        double quadArea = ComputePixelPolygonArea(intersected);
        if (contourArea <= 100 || Math.Abs(quadArea - contourArea) / contourArea > 0.055)
        {
            return false;
        }

        quadCorners = intersected;
        return true;
    }

    private static (double Px, double Py, double Vx, double Vy) FitStraightLine2D(List<ImagePixelPoint> pts)
    {
        double mx = pts.Average(p => p.X);
        double my = pts.Average(p => p.Y);
        double sxx = 0, syy = 0, sxy = 0;
        foreach (ImagePixelPoint p in pts)
        {
            double dx = p.X - mx;
            double dy = p.Y - my;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }

        double theta = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
        return (mx, my, Math.Cos(theta), Math.Sin(theta));
    }

    private static bool TryIntersectLines(
        (double Px, double Py, double Vx, double Vy) l1,
        (double Px, double Py, double Vx, double Vy) l2,
        out ImagePixelPoint intersection)
    {
        double det = (l1.Vx * l2.Vy) - (l1.Vy * l2.Vx);
        if (Math.Abs(det) < 1e-6)
        {
            intersection = default;
            return false;
        }

        double dx = l2.Px - l1.Px;
        double dy = l2.Py - l1.Py;
        double t = ((dx * l2.Vy) - (dy * l2.Vx)) / det;
        intersection = new ImagePixelPoint(l1.Px + (t * l1.Vx), l1.Py + (t * l1.Vy));
        return true;
    }

    private static double ComputePixelPolygonArea(List<ImagePixelPoint> pts)
    {
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            ImagePixelPoint a = pts[i];
            ImagePixelPoint b = pts[(i + 1) % pts.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }
        return 0.5 * Math.Abs(sum);
    }

    private static List<int> SimplifyClosedContourIndices(List<PhysicalPointMm> pts, double epsilonMm)
    {
        int n = pts.Count;
        if (n <= 4)
        {
            return Enumerable.Range(0, n).ToList();
        }

        // Find two farthest points to split the closed loop into two open chains
        int i0 = 0, i1 = n / 2;
        double maxDist = -1;
        int step = Math.Max(1, n / 60);
        for (int i = 0; i < n; i += step)
        {
            for (int j = i + 1; j < n; j += step)
            {
                double d = pts[i].DistanceTo(pts[j]);
                if (d > maxDist)
                {
                    maxDist = d;
                    i0 = i;
                    i1 = j;
                }
            }
        }

        var chain1 = new List<int>();
        for (int idx = i0; ; idx = (idx + 1) % n)
        {
            chain1.Add(idx);
            if (idx == i1) break;
        }

        var chain2 = new List<int>();
        for (int idx = i1; ; idx = (idx + 1) % n)
        {
            chain2.Add(idx);
            if (idx == i0) break;
        }

        var keep1 = RdpSimplifyChain(pts, chain1, epsilonMm);
        var keep2 = RdpSimplifyChain(pts, chain2, epsilonMm);

        var result = new List<int>();
        result.AddRange(keep1.Take(keep1.Count - 1));
        result.AddRange(keep2.Take(keep2.Count - 1));
        return result;
    }

    private static List<int> RdpSimplifyChain(List<PhysicalPointMm> allPts, List<int> chainIndices, double epsilon)
    {
        if (chainIndices.Count <= 2)
        {
            return [.. chainIndices];
        }

        PhysicalPointMm pStart = allPts[chainIndices[0]];
        PhysicalPointMm pEnd = allPts[chainIndices[^1]];
        double dx = pEnd.XMm - pStart.XMm;
        double dy = pEnd.YMm - pStart.YMm;
        double lenSq = (dx * dx) + (dy * dy);

        double maxDist = -1.0;
        int splitPos = 0;

        for (int i = 1; i < chainIndices.Count - 1; i++)
        {
            PhysicalPointMm p = allPts[chainIndices[i]];
            double dist;
            if (lenSq < 1e-9)
            {
                dist = p.DistanceTo(pStart);
            }
            else
            {
                double t = Math.Clamp((((p.XMm - pStart.XMm) * dx) + ((p.YMm - pStart.YMm) * dy)) / lenSq, 0.0, 1.0);
                PhysicalPointMm proj = pStart.Lerp(pEnd, t);
                dist = p.DistanceTo(proj);
            }

            if (dist > maxDist)
            {
                maxDist = dist;
                splitPos = i;
            }
        }

        if (maxDist > epsilon)
        {
            var left = RdpSimplifyChain(allPts, chainIndices.Take(splitPos + 1).ToList(), epsilon);
            var right = RdpSimplifyChain(allPts, chainIndices.Skip(splitPos).ToList(), epsilon);
            var merged = new List<int>(left.Count + right.Count - 1);
            merged.AddRange(left.Take(left.Count - 1));
            merged.AddRange(right);
            return merged;
        }

        return [chainIndices[0], chainIndices[^1]];
    }

    private static double ComputeTurningAngleDegrees(PhysicalPointMm prev, PhysicalPointMm curr, PhysicalPointMm next)
    {
        double v1x = curr.XMm - prev.XMm;
        double v1y = curr.YMm - prev.YMm;
        double v2x = next.XMm - curr.XMm;
        double v2y = next.YMm - curr.YMm;

        double l1 = Math.Sqrt((v1x * v1x) + (v1y * v1y));
        double l2 = Math.Sqrt((v2x * v2x) + (v2y * v2y));
        if (l1 < 1e-9 || l2 < 1e-9)
        {
            return 0.0;
        }

        double dot = ((v1x * v2x) + (v1y * v2y)) / (l1 * l2);
        dot = Math.Clamp(dot, -1.0, 1.0);
        return Math.Acos(dot) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Bridges short boundary detours around neutral/dark holding magnets. A
    /// magnet rim is darker than the paper but remains distinctly brighter
    /// than the board on the other side of the edge.
    /// </summary>
    private static List<ImagePixelPoint> BridgeNeutralDarkOccluderRuns(
        List<ImagePixelPoint> contour,
        RawImageBuffer image)
    {
        // Sample farther from the traced edge so a holding magnet's bright
        // face and dark rim are compared against the actual board, rather
        // than against the paper immediately underneath it.
        const int sampleOffsetPx = 7;
        const int minRunPoints = 4;
        // Canonical photos contain large circular holding magnets. Their rim
        // can occupy more than 180 contour samples at 1:1 resolution; the
        // previous cap left a visible bite and produced the red loops seen in
        // the user's screenshots. Long runs are still rejected below by the
        // contrast test and the contour-size guard.
        const int maxRunPoints = 520;
        int n = contour.Count;
        if (n < 32) return contour;

        bool[] occluder = new bool[n];
        for (int i = 0; i < n; i++)
        {
            ImagePixelPoint prev = contour[(i - 3 + n) % n];
            ImagePixelPoint next = contour[(i + 3) % n];
            ImagePixelPoint p = contour[i];
            double dx = next.X - prev.X;
            double dy = next.Y - prev.Y;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 0.5) continue;

            double nx = -dy / len;
            double ny = dx / len;
            (double lumA, double chromaA) = SampleLuminanceAndChroma(image, p.X + (nx * sampleOffsetPx), p.Y + (ny * sampleOffsetPx));
            (double lumB, double chromaB) = SampleLuminanceAndChroma(image, p.X - (nx * sampleOffsetPx), p.Y - (ny * sampleOffsetPx));
            double insideLum = Math.Max(lumA, lumB);
            double outsideLum = Math.Min(lumA, lumB);
            double insideChroma = lumA >= lumB ? chromaA : chromaB;
            occluder[i] = insideLum is >= 85.0 and <= 205.0 &&
                          (insideLum - outsideLum) >= 38.0 &&
                          insideChroma <= 30.0;
        }

        var kept = new List<ImagePixelPoint>(n);
        int start = Array.FindIndex(occluder, flagged => !flagged);
        if (start < 0) return contour;

        for (int scanned = 0, i = start; scanned < n;)
        {
            if (!occluder[i])
            {
                kept.Add(contour[i]);
                i = (i + 1) % n;
                scanned++;
                continue;
            }

            int runStart = i;
            int runLength = 0;
            while (scanned < n && occluder[i])
            {
                i = (i + 1) % n;
                scanned++;
                runLength++;
            }

            if (runLength < minRunPoints || runLength > maxRunPoints)
            {
                for (int j = 0; j < runLength; j++) kept.Add(contour[(runStart + j) % n]);
            }
        }

        return kept.Count >= 12 ? kept : contour;
    }

    private static List<ImagePixelPoint> SuppressAttachedCircularLoops(List<ImagePixelPoint> contour)
    {
        if (contour.Count < 48) return contour;
        var result = new List<ImagePixelPoint>(contour);
        // Remove only short, consistently convex loops. A real neckline/notch
        // is concave or spans a much longer section and therefore survives.
        for (int pass = 0; pass < 2; pass++)
        {
            bool changed = false;
            int n = result.Count;
            for (int i = 0; i < n && result.Count >= 24; i++)
            {
                for (int span = 8; span <= 34 && i + span < result.Count; span++)
                {
                    var a = result[i];
                    var b = result[i + span];
                    double chord = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
                    if (chord < 8 || chord > 42) continue;
                    double arc = 0;
                    int sign = 0;
                    bool consistent = true;
                    for (int k = 1; k <= span; k++)
                    {
                        var p0 = result[i + k - 1]; var p1 = result[i + k];
                        arc += Math.Sqrt(Math.Pow(p1.X - p0.X, 2) + Math.Pow(p1.Y - p0.Y, 2));
                        if (k < span)
                        {
                            var p2 = result[i + k + 1];
                            double cross = ((p1.X - p0.X) * (p2.Y - p1.Y)) - ((p1.Y - p0.Y) * (p2.X - p1.X));
                            if (Math.Abs(cross) > 0.8)
                            {
                                int s = Math.Sign(cross);
                                if (sign == 0) sign = s; else if (s != sign) { consistent = false; break; }
                            }
                        }
                    }
                    if (!consistent || sign == 0 || arc / chord < 1.55) continue;
                    result.RemoveRange(i + 1, span - 1);
                    changed = true;
                    n = result.Count;
                    break;
                }
                if (changed) break;
            }
            if (!changed) break;
        }
        return result;
    }


    private static (double Luminance, double Chroma) SampleLuminanceAndChroma(RawImageBuffer image, double x, double y)
    {
        int px = Math.Clamp((int)Math.Round(x), 0, image.Width - 1);
        int py = Math.Clamp((int)Math.Round(y), 0, image.Height - 1);
        int idx = (py * image.Stride) + (px * 4);
        byte b = image.BgraPixels[idx];
        byte g = image.BgraPixels[idx + 1];
        byte r = image.BgraPixels[idx + 2];
        return ((r + g + b) / 3.0, Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)));
    }

    private static List<ImagePixelPoint> SmoothClosedContourPixels(List<ImagePixelPoint> pts, int iterations)
    {
        var current = new List<ImagePixelPoint>(pts);
        int n = current.Count;
        for (int iter = 0; iter < iterations; iter++)
        {
            var next = new List<ImagePixelPoint>(n);
            for (int i = 0; i < n; i++)
            {
                ImagePixelPoint pPrev = current[(i - 1 + n) % n];
                ImagePixelPoint pCurr = current[i];
                ImagePixelPoint pNext = current[(i + 1) % n];
                next.Add(new ImagePixelPoint(
                    (0.25 * pPrev.X) + (0.50 * pCurr.X) + (0.25 * pNext.X),
                    (0.25 * pPrev.Y) + (0.50 * pCurr.Y) + (0.25 * pNext.Y)));
            }
            current = next;
        }
        return current;
    }

    private static (byte R, byte G, byte B) EstimateBoardBackgroundColor(RawImageBuffer img)
    {
        var rSamples = new List<byte>();
        var gSamples = new List<byte>();
        var bSamples = new List<byte>();

        int stepX = Math.Max(4, img.Width / 40);
        int stepY = Math.Max(4, img.Height / 40);
        int marginX = Math.Max(4, img.Width / 16);
        int marginY = Math.Max(4, img.Height / 16);

        for (int y = marginY; y < img.Height - marginY; y += stepY)
        {
            for (int x = marginX; x < img.Width - marginX; x += stepX)
            {
                var (r, g, b, _) = img.GetPixel(x, y);
                int lum = (r + g + b) / 3;
                if (lum < 115)
                {
                    rSamples.Add(r);
                    gSamples.Add(g);
                    bSamples.Add(b);
                }
            }
        }

        if (rSamples.Count == 0)
        {
            return (65, 70, 68);
        }

        rSamples.Sort();
        gSamples.Sort();
        bSamples.Sort();
        int mid = rSamples.Count / 2;
        return (rSamples[mid], gSamples[mid], bSamples[mid]);
    }

    private static byte[] MorphologicalDilate(byte[] mask, int w, int h, int radius)
    {
        byte[] dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - radius);
            int y1 = Math.Min(h - 1, y + radius);
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - radius);
                int x1 = Math.Min(w - 1, x + radius);
                byte maxVal = 0;
                for (int yy = y0; yy <= y1; yy++)
                {
                    int row = yy * w;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        if (mask[row + xx] > maxVal)
                        {
                            maxVal = 255;
                            break;
                        }
                    }
                    if (maxVal == 255) break;
                }
                dst[(y * w) + x] = maxVal;
            }
        }
        return dst;
    }

    private static byte[] MorphologicalErode(byte[] mask, int w, int h, int radius)
    {
        byte[] dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Max(0, y - radius);
            int y1 = Math.Min(h - 1, y + radius);
            for (int x = 0; x < w; x++)
            {
                if (y - radius < 0 || y + radius >= h || x - radius < 0 || x + radius >= w)
                {
                    dst[(y * w) + x] = 0;
                    continue;
                }

                int x0 = x - radius;
                int x1 = x + radius;
                byte minVal = 255;
                for (int yy = y0; yy <= y1; yy++)
                {
                    int row = yy * w;
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        if (mask[row + xx] == 0)
                        {
                            minVal = 0;
                            break;
                        }
                    }
                    if (minVal == 0) break;
                }
                dst[(y * w) + x] = minVal;
            }
        }
        return dst;
    }

    private static byte[] MorphologicalClose(byte[] mask, int w, int h, int radius) =>
        MorphologicalErode(MorphologicalDilate(mask, w, h, radius), w, h, radius);

    private static byte[] SeparableSquareDilate(byte[] mask, int w, int h, int radius) =>
        SeparableSquareFilter(mask, w, h, radius, dilate: true);

    private static byte[] SeparableSquareClose(byte[] mask, int w, int h, int radius) =>
        SeparableSquareFilter(SeparableSquareFilter(mask, w, h, radius, dilate: true), w, h, radius, dilate: false);

    /// <summary>
    /// O(w*h) square max/min filter using running counts along rows then columns.
    /// Out-of-bounds pixels count as background (0).
    /// </summary>
    private static byte[] SeparableSquareFilter(byte[] mask, int w, int h, int radius, bool dilate)
    {
        int win = (2 * radius) + 1;
        byte[] tmp = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int count = 0;
            for (int x = -radius; x < w + radius; x++)
            {
                int addX = x + radius;
                if (addX < w && addX >= 0 && mask[row + addX] != 0) count++;
                int remX = x - radius - 1;
                if (remX >= 0 && remX < w && mask[row + remX] != 0) count--;
                if (x >= 0 && x < w)
                {
                    tmp[row + x] = (byte)((dilate ? count > 0 : count == win) ? 255 : 0);
                }
            }
        }

        byte[] dst = new byte[w * h];
        for (int x = 0; x < w; x++)
        {
            int count = 0;
            for (int y = -radius; y < h + radius; y++)
            {
                int addY = y + radius;
                if (addY < h && addY >= 0 && tmp[(addY * w) + x] != 0) count++;
                int remY = y - radius - 1;
                if (remY >= 0 && remY < h && tmp[(remY * w) + x] != 0) count--;
                if (y >= 0 && y < h)
                {
                    dst[(y * w) + x] = (byte)((dilate ? count > 0 : count == win) ? 255 : 0);
                }
            }
        }

        return dst;
    }

    /// <summary>
    /// Median absolute luminance step across the component boundary (inside vs. outside, sampled in full-resolution pixels).
    /// Physical pattern pieces produce a sharp step; diffuse glare produces a small one.
    /// </summary>
    private static double MeasureBoundaryContrast(ComponentBlob comp, byte[] mask, int workW, int workH, RawImageBuffer img, int scaleDiv)
    {
        var steps = new List<double>();
        int[] ndx = [1, -1, 0, 0];
        int[] ndy = [0, 0, 1, -1];
        int stride = Math.Max(1, comp.PixelCount / 40000);
        int counter = 0;
        const int inset = 4;
        const int outset = 5;

        foreach ((int px, int py) in comp.Pixels)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = px + ndx[d];
                int ny = py + ndy[d];
                if ((uint)nx >= (uint)workW || (uint)ny >= (uint)workH || mask[(ny * workW) + nx] != 0)
                {
                    continue;
                }

                if ((counter++ % stride) != 0)
                {
                    break;
                }

                int ix = px - (ndx[d] * inset), iy = py - (ndy[d] * inset);
                int ox = px + (ndx[d] * outset), oy = py + (ndy[d] * outset);
                if ((uint)ix >= (uint)workW || (uint)iy >= (uint)workH || (uint)ox >= (uint)workW || (uint)oy >= (uint)workH)
                {
                    break;
                }

                double lin = SampleLum(img, ix * scaleDiv, iy * scaleDiv);
                double lout = SampleLum(img, ox * scaleDiv, oy * scaleDiv);
                steps.Add(Math.Abs(lin - lout));
                break;
            }
        }

        if (steps.Count == 0)
        {
            return 0.0;
        }

        steps.Sort();
        return steps[steps.Count / 2];
    }

    private static double SampleLum(RawImageBuffer img, int x, int y)
    {
        x = Math.Clamp(x, 0, img.Width - 1);
        y = Math.Clamp(y, 0, img.Height - 1);
        int idx = (y * img.Stride) + (x * 4);
        return (img.BgraPixels[idx] + img.BgraPixels[idx + 1] + img.BgraPixels[idx + 2]) / 3.0;
    }

    private static byte[] MorphologicalOpen(byte[] mask, int w, int h, int radius) =>
        MorphologicalDilate(MorphologicalErode(mask, w, h, radius), w, h, radius);

    private static void FillInteriorHoles(byte[] mask, int w, int h)
    {
        bool[] exteriorVisited = new bool[w * h];
        var queue = new Queue<int>();

        for (int x = 0; x < w; x++)
        {
            if (mask[x] == 0) { exteriorVisited[x] = true; queue.Enqueue(x); }
            int bot = ((h - 1) * w) + x;
            if (mask[bot] == 0 && !exteriorVisited[bot]) { exteriorVisited[bot] = true; queue.Enqueue(bot); }
        }

        for (int y = 0; y < h; y++)
        {
            int left = y * w;
            if (mask[left] == 0 && !exteriorVisited[left]) { exteriorVisited[left] = true; queue.Enqueue(left); }
            int right = (y * w) + (w - 1);
            if (mask[right] == 0 && !exteriorVisited[right]) { exteriorVisited[right] = true; queue.Enqueue(right); }
        }

        int[] dx = [-1, 1, 0, 0];
        int[] dy = [0, 0, -1, 1];

        while (queue.Count > 0)
        {
            int curr = queue.Dequeue();
            int cx = curr % w;
            int cy = curr / w;

            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d];
                int ny = cy + dy[d];
                if ((uint)nx < (uint)w && (uint)ny < (uint)h)
                {
                    int nIdx = (ny * w) + nx;
                    if (!exteriorVisited[nIdx] && mask[nIdx] == 0)
                    {
                        exteriorVisited[nIdx] = true;
                        queue.Enqueue(nIdx);
                    }
                }
            }
        }

        for (int i = 0; i < mask.Length; i++)
        {
            if (!exteriorVisited[i])
            {
                mask[i] = 255;
            }
        }
    }

    private sealed class ComponentBlob
    {
        public int Id { get; init; }
        public int PixelCount => Pixels.Count;
        public int MinX { get; set; } = int.MaxValue;
        public int MinY { get; set; } = int.MaxValue;
        public int MaxX { get; set; } = int.MinValue;
        public int MaxY { get; set; } = int.MinValue;
        public List<(int X, int Y)> Pixels { get; } = [];
        public double ChromaFraction { get; set; }
    }

    /// <summary>
    /// Estimates a coarse 2D grid of local dark-board luminance (25th percentile of board-like samples per block),
    /// smoothed with a 3x3 box filter. Captures low-frequency lens flare / illumination gradients without
    /// any hardcoded image coordinates.
    /// </summary>
    private static double[,] EstimateLocalBackgroundLuminanceGrid(RawImageBuffer img, double globalBgLum, int cols, int rows)
    {
        var grid = new double[rows, cols];
        int blockW = Math.Max(1, img.Width / cols);
        int blockH = Math.Max(1, img.Height / rows);
        int sampleStep = Math.Max(2, Math.Min(blockW, blockH) / 14);
        var samples = new List<double>(256);

        for (int gy = 0; gy < rows; gy++)
        {
            for (int gx = 0; gx < cols; gx++)
            {
                samples.Clear();
                int x0 = gx * blockW;
                int y0 = gy * blockH;
                int x1 = Math.Min(img.Width, x0 + blockW);
                int y1 = Math.Min(img.Height, y0 + blockH);
                for (int y = y0; y < y1; y += sampleStep)
                {
                    int row = y * img.Stride;
                    for (int x = x0; x < x1; x += sampleStep)
                    {
                        int idx = row + (x * 4);
                        int b = img.BgraPixels[idx];
                        int g = img.BgraPixels[idx + 1];
                        int r = img.BgraPixels[idx + 2];
                        if (Math.Abs(b - r) > 30)
                        {
                            continue; // skip chromatic fabric
                        }
                        samples.Add((r + g + b) / 3.0);
                    }
                }

                if (samples.Count == 0)
                {
                    grid[gy, gx] = globalBgLum;
                    continue;
                }

                samples.Sort();
                grid[gy, gx] = samples[(int)(samples.Count * 0.25)];
            }
        }

        var smoothed = new double[rows, cols];
        for (int gy = 0; gy < rows; gy++)
        {
            for (int gx = 0; gx < cols; gx++)
            {
                double sum = 0;
                int cnt = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int yy = gy + dy, xx = gx + dx;
                        if (yy >= 0 && yy < rows && xx >= 0 && xx < cols)
                        {
                            sum += grid[yy, xx];
                            cnt++;
                        }
                    }
                }
                // Never let local background drop below global board luminance
                smoothed[gy, gx] = Math.Max(globalBgLum, sum / cnt);
            }
        }

        return smoothed;
    }

    private static double SampleLocalBackgroundLuminance(double[,] grid, int cols, int rows, int wx, int wy, int workW, int workH)
    {
        double fx = ((wx + 0.5) / workW * cols) - 0.5;
        double fy = ((wy + 0.5) / workH * rows) - 0.5;
        fx = Math.Clamp(fx, 0, cols - 1);
        fy = Math.Clamp(fy, 0, rows - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(cols - 1, x0 + 1), y1 = Math.Min(rows - 1, y0 + 1);
        double tx = fx - x0, ty = fy - y0;
        double top = (grid[y0, x0] * (1 - tx)) + (grid[y0, x1] * tx);
        double bot = (grid[y1, x0] * (1 - tx)) + (grid[y1, x1] * tx);
        return (top * (1 - ty)) + (bot * ty);
    }

    private static List<ComponentBlob> ExtractConnectedComponents(byte[] mask, int w, int h, int[] labels)
    {
        var list = new List<ComponentBlob>();
        var queue = new Queue<int>();
        int nextId = 1;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int startIdx = (y * w) + x;
                if (mask[startIdx] == 0 || labels[startIdx] != 0)
                {
                    continue;
                }

                var blob = new ComponentBlob { Id = nextId };
                labels[startIdx] = nextId;
                queue.Enqueue(startIdx);

                while (queue.Count > 0)
                {
                    int curr = queue.Dequeue();
                    int cx = curr % w;
                    int cy = curr / w;
                    blob.Pixels.Add((cx, cy));
                    if (cx < blob.MinX) blob.MinX = cx;
                    if (cx > blob.MaxX) blob.MaxX = cx;
                    if (cy < blob.MinY) blob.MinY = cy;
                    if (cy > blob.MaxY) blob.MaxY = cy;

                    for (int ny = Math.Max(0, cy - 1); ny <= Math.Min(h - 1, cy + 1); ny++)
                    {
                        int nRow = ny * w;
                        for (int nx = Math.Max(0, cx - 1); nx <= Math.Min(w - 1, cx + 1); nx++)
                        {
                            int nIdx = nRow + nx;
                            if (mask[nIdx] != 0 && labels[nIdx] == 0)
                            {
                                labels[nIdx] = nextId;
                                queue.Enqueue(nIdx);
                            }
                        }
                    }
                }

                list.Add(blob);
                nextId++;
            }
        }

        return list;
    }

    /// <summary>
    /// Moore-Neighbor 8-connected outer contour tracing.
    /// </summary>
    private static List<(int X, int Y)> TraceOuterBoundary(byte[] mask, int w, int h)
    {
        var contour = new List<(int X, int Y)>();
        int startX = -1, startY = -1;

        for (int y = 1; y < h - 1 && startX < 0; y++)
        {
            int row = y * w;
            for (int x = 1; x < w - 1; x++)
            {
                if (mask[row + x] != 0)
                {
                    startX = x;
                    startY = y;
                    break;
                }
            }
        }

        if (startX < 0)
        {
            return contour;
        }

        // Clockwise 8-neighborhood offsets starting from West: W, NW, N, NE, E, SE, S, SW
        ReadOnlySpan<int> dx = [-1, -1, 0, 1, 1, 1, 0, -1];
        ReadOnlySpan<int> dy = [0, -1, -1, -1, 0, 1, 1, 1];

        int cx = startX, cy = startY;
        int backtrackDir = 0; // entered from West (x-1, y)
        int maxSteps = w * h;

        for (int step = 0; step < maxSteps; step++)
        {
            contour.Add((cx, cy));
            int foundDir = -1;
            for (int k = 0; k < 8; k++)
            {
                int dir = (backtrackDir + 1 + k) & 7;
                int nx = cx + dx[dir];
                int ny = cy + dy[dir];
                if ((uint)nx < (uint)w && (uint)ny < (uint)h && mask[(ny * w) + nx] != 0)
                {
                    foundDir = dir;
                    cx = nx;
                    cy = ny;
                    backtrackDir = (dir + 4) & 7;
                    break;
                }
            }

            if (foundDir < 0 || (cx == startX && cy == startY && contour.Count > 2))
            {
                break;
            }
        }

        return contour;
    }

    private Dictionary<string, string> SaveDebugStageImages(
        RawImageBuffer sourceImage,
        byte[] responseMap,
        byte[] cleanMask,
        int[] labels,
        List<ComponentBlob> allComponents,
        HashSet<int> rejectedIds,
        List<List<ImagePixelPoint>> rawContours,
        List<PatternPiece> pieces,
        CalibrationProfile calibration,
        int workW,
        int workH,
        int scaleDiv,
        string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var files = new Dictionary<string, string>();

        // 1. Original
        string p1 = Path.Combine(outputDir, "1_original.png");
        _imageEngine.SavePng(sourceImage, p1);
        files["Original"] = p1;

        // 2. Preprocessed foreground response
        string p2 = Path.Combine(outputDir, "2_preprocessed.png");
        _imageEngine.SavePng(_imageEngine.GrayscaleToBuffer(responseMap, workW, workH), p2);
        files["Preprocessed"] = p2;

        // 3. Mask
        string p3 = Path.Combine(outputDir, "3_mask.png");
        _imageEngine.SavePng(_imageEngine.GrayscaleToBuffer(cleanMask, workW, workH), p3);
        files["Mask"] = p3;

        // 4. Components (color-coded)
        RawImageBuffer compImg = _imageEngine.CreateSolidBuffer(workW, workH, 20, 22, 24);
        (byte R, byte G, byte B)[] palette =
        [
            (60, 180, 255), (255, 180, 50), (120, 235, 110), (235, 100, 220),
            (255, 110, 90), (90, 230, 210), (220, 220, 80), (170, 130, 255)
        ];
        for (int y = 0; y < workH; y++)
        {
            for (int x = 0; x < workW; x++)
            {
                int id = labels[(y * workW) + x];
                if (id > 0)
                {
                    var c = palette[(id - 1) % palette.Length];
                    compImg.SetPixel(x, y, c.R, c.G, c.B);
                }
            }
        }
        string p4 = Path.Combine(outputDir, "4_components.png");
        _imageEngine.SavePng(compImg, p4);
        files["Components"] = p4;

        // 5. Rejected components (Green = Valid pattern piece, Red = Rejected magnet/glare/border)
        RawImageBuffer rejImg = _imageEngine.CreateSolidBuffer(workW, workH, 24, 26, 28);
        for (int y = 0; y < workH; y++)
        {
            for (int x = 0; x < workW; x++)
            {
                int id = labels[(y * workW) + x];
                if (id > 0)
                {
                    if (rejectedIds.Contains(id))
                    {
                        rejImg.SetPixel(x, y, 240, 55, 55); // Red = Rejected
                    }
                    else
                    {
                        rejImg.SetPixel(x, y, 45, 210, 95); // Green = Accepted Pattern
                    }
                }
            }
        }
        string p5 = Path.Combine(outputDir, "5_rejected_components.png");
        _imageEngine.SavePng(rejImg, p5);
        files["RejectedComponents"] = p5;

        // 6. Outer contours
        RawImageBuffer contourImg = _imageEngine.CreateSolidBuffer(sourceImage.Width, sourceImage.Height, 30, 32, 34);
        foreach (List<ImagePixelPoint> rawContour in rawContours)
        {
            for (int i = 0; i < rawContour.Count; i++)
            {
                DrawLineOnBuffer(contourImg, rawContour[i], rawContour[(i + 1) % rawContour.Count], 0, 230, 255, thickness: 2);
            }
        }
        string p6 = Path.Combine(outputDir, "6_outer_contours.png");
        _imageEngine.SavePng(contourImg, p6);
        files["OuterContours"] = p6;

        // 7. Fitted curves (on dark board showing lines vs curves + handles + corners)
        RawImageBuffer fittedImg = _imageEngine.CreateSolidBuffer(sourceImage.Width, sourceImage.Height, 36, 40, 42);
        DrawPiecesOnBuffer(fittedImg, pieces, calibration, drawHandles: true);
        string p7 = Path.Combine(outputDir, "7_fitted_curves.png");
        _imageEngine.SavePng(fittedImg, p7);
        files["FittedCurves"] = p7;

        // 8. Final overlay on original photograph (Gemini style: red contour + red corner dots)
        RawImageBuffer finalOverlay = sourceImage.Clone();
        DrawPiecesOnBuffer(finalOverlay, pieces, calibration, drawHandles: false);
        string p8 = Path.Combine(outputDir, "8_final_overlay.png");
        _imageEngine.SavePng(finalOverlay, p8);
        files["FinalOverlay"] = p8;

        return files;
    }

    private static void DrawPiecesOnBuffer(
        RawImageBuffer buffer,
        List<PatternPiece> pieces,
        CalibrationProfile calibration,
        bool drawHandles)
    {
        foreach (PatternPiece piece in pieces)
        {
            for (int i = 0; i < piece.Vertices.Count; i++)
            {
                CubicBezierSegment seg = piece.GetSegment(i);
                int steps = seg.IsCurve ? 32 : 1;
                for (int s = 0; s < steps; s++)
                {
                    PhysicalPointMm m0 = seg.Evaluate((double)s / steps);
                    PhysicalPointMm m1 = seg.Evaluate((double)(s + 1) / steps);
                    ImagePixelPoint p0 = calibration.PhysicalMmToPixel(m0);
                    ImagePixelPoint p1 = calibration.PhysicalMmToPixel(m1);
                    DrawLineOnBuffer(buffer, p0, p1, 235, 28, 28, thickness: 3);
                }

                if (drawHandles && seg.IsCurve)
                {
                    ImagePixelPoint p0 = calibration.PhysicalMmToPixel(seg.P0);
                    ImagePixelPoint c1 = calibration.PhysicalMmToPixel(seg.C1);
                    ImagePixelPoint c2 = calibration.PhysicalMmToPixel(seg.C2);
                    ImagePixelPoint p3 = calibration.PhysicalMmToPixel(seg.P3);
                    DrawLineOnBuffer(buffer, p0, c1, 80, 200, 255, thickness: 1);
                    DrawLineOnBuffer(buffer, p3, c2, 80, 200, 255, thickness: 1);
                    DrawFilledDot(buffer, (int)c1.X, (int)c1.Y, 4, 80, 200, 255);
                    DrawFilledDot(buffer, (int)c2.X, (int)c2.Y, 4, 80, 200, 255);
                }
            }

            foreach (ContourVertex v in piece.Vertices)
            {
                ImagePixelPoint px = calibration.PhysicalMmToPixel(v.PositionMm);
                if (v.IsGradedCorner)
                {
                    DrawFilledDot(buffer, (int)px.X, (int)px.Y, 6, 255, 24, 24);
                }
                else
                {
                    DrawFilledDot(buffer, (int)px.X, (int)px.Y, 5, 40, 220, 80);
                }
            }
        }
    }

    private static void DrawLineOnBuffer(
        RawImageBuffer buffer,
        ImagePixelPoint a,
        ImagePixelPoint b,
        byte r,
        byte g,
        byte bl,
        int thickness = 2)
    {
        int x0 = (int)Math.Round(a.X);
        int y0 = (int)Math.Round(a.Y);
        int x1 = (int)Math.Round(b.X);
        int y1 = (int)Math.Round(b.Y);

        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
            {
                for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
                {
                    buffer.SetPixel(x0 + tx, y0 + ty, r, g, bl);
                }
            }

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawFilledDot(RawImageBuffer buffer, int cx, int cy, int radius, byte r, byte g, byte b)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    buffer.SetPixel(cx + dx, cy + dy, r, g, b);
                }
            }
        }
    }
}
