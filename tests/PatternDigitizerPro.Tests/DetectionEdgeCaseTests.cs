using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using Xunit;

namespace PatternDigitizerPro.Tests;

public sealed class DetectionEdgeCaseTests
{
    [Fact]
    public void Detection_PieceThreePixelsFromImageEdge_IsNotRejectedAsBorderArtifact()
    {
        const int width = 1000;
        const int height = 700;
        RawImageBuffer image = CreateSolidImage(width, height, 88);

        // Large valid paper pattern close to the left edge. Add a clipped
        // chamfer so this is not a special-case perfect rectangle fixture.
        FillPolygon(image,
        [
            new ImagePixelPoint(3, 125),
            new ImagePixelPoint(42, 82),
            new ImagePixelPoint(305, 96),
            new ImagePixelPoint(330, 585),
            new ImagePixelPoint(18, 612),
            new ImagePixelPoint(3, 570)
        ], 232);

        CalibrationProfile calibration = new CalibrationEngine().CalibrateModeBManualRectangle(
            new CropBoxPixels(0, 0, width, height),
            physicalWidthCm: 100,
            physicalHeightCm: 70,
            imageWidthPx: width,
            imageHeightPx: height);

        DetectionPipelineResult result = new DetectionEngine().DetectPatterns(image, calibration);

        PatternPiece piece = Assert.Single(result.Pieces);
        Assert.DoesNotContain(result.RejectedComponents, rejected =>
            rejected.Reason is "BorderFrameOrGlare" or "BottomEdgeArtifact");
        Assert.True(piece.AreaMm2 > 120_000, $"Unexpected area: {piece.AreaMm2:F1} mm2");
        Assert.True(piece.BoundingBoxMm.Left < 10, $"Piece was moved away from edge: {piece.BoundingBoxMm.Left:F1} mm");
        Assert.True(piece.Vertices.Count >= 4);
    }

    [Fact]
    public void Detection_DigitizeRoi_RestrictsTracingWithoutChangingGlobalCalibrationCoordinates()
    {
        const int width = 1000;
        const int height = 700;
        RawImageBuffer image = CreateSolidImage(width, height, 88);

        // Two intentionally separate photographed paper pieces.  The frame
        // surrounds only the left one; the right one must not leak into the
        // trace just because it remains in the source image.
        FillPolygon(image,
        [
            new ImagePixelPoint(100, 110), new ImagePixelPoint(370, 110),
            new ImagePixelPoint(370, 530), new ImagePixelPoint(100, 530)
        ], 232);
        FillPolygon(image,
        [
            new ImagePixelPoint(620, 105), new ImagePixelPoint(910, 105),
            new ImagePixelPoint(910, 545), new ImagePixelPoint(620, 545)
        ], 232);

        CalibrationProfile calibration = new CalibrationEngine().CalibrateModeBManualRectangle(
            new CropBoxPixels(0, 0, width, height),
            physicalWidthCm: 100,
            physicalHeightCm: 70,
            imageWidthPx: width,
            imageHeightPx: height);
        var engine = new DetectionEngine();
        DetectionPipelineResult wholeImage = engine.DetectPatterns(image, calibration);
        Assert.Equal(2, wholeImage.Pieces.Count);

        CalibrationProfile digitizeFrameCalibration = calibration.Clone();
        digitizeFrameCalibration.CropBox = new CropBoxPixels(60, 70, 420, 570);
        digitizeFrameCalibration.RestrictDetectionToCropBox = true;
        DetectionPipelineResult framed = engine.DetectPatterns(image, digitizeFrameCalibration);

        PatternPiece piece = Assert.Single(framed.Pieces);
        // The result remains in the original calibrated physical plane, not in
        // a new ROI-local coordinate system (100 px == 100 mm on this fixture).
        Assert.InRange(piece.BoundingBoxMm.Left, 99.0, 101.5);
        Assert.InRange(piece.BoundingBoxMm.Right, 369.0, 371.5);
    }

    [Fact]
    public void Detection_TightFrameAroundWarmPattern_UsesGlobalBoardContrastWhenLocalGridIsCovered()
    {
        const int width = 1200;
        const int height = 900;
        RawImageBuffer image = CreateSolidImage(width, height, 88);

        // A warm/cardboard pattern has a strong colour distance from the board,
        // but a tight user frame means central local-background cells contain
        // only the pattern.  Detection must not lose that central region merely
        // because local contrast tends to zero there.
        FillPolygon(image,
        [
            new ImagePixelPoint(180, 178), new ImagePixelPoint(997, 192),
            new ImagePixelPoint(1008, 686), new ImagePixelPoint(164, 705)
        ], r: 198, g: 145, b: 72);

        CalibrationProfile calibration = new CalibrationEngine().CalibrateModeBManualRectangle(
            new CropBoxPixels(150, 160, 1030, 725),
            physicalWidthCm: 88,
            physicalHeightCm: 56.5,
            imageWidthPx: width,
            imageHeightPx: height);
        calibration.RestrictDetectionToCropBox = true;

        PatternPiece piece = Assert.Single(new DetectionEngine().DetectPatterns(image, calibration).Pieces);
        Assert.InRange(piece.BoundingBoxMm.Width, 790, 850);
        Assert.InRange(piece.BoundingBoxMm.Height, 490, 550);
        Assert.True(piece.AreaMm2 > 300_000, $"Tight-frame foreground collapsed: {piece.AreaMm2:F1} mm²");
    }

    [Fact]
    public void Detection_TightFrameAroundNeutralPaper_UsesFramePriorWithoutTreatingItAsBoard()
    {
        const int width = 1200;
        const int height = 900;
        RawImageBuffer image = CreateSolidImage(width, height, 88);
        FillPolygon(image,
        [
            new ImagePixelPoint(180, 178), new ImagePixelPoint(997, 192),
            new ImagePixelPoint(1008, 686), new ImagePixelPoint(164, 705)
        ], 190);

        CalibrationProfile calibration = new CalibrationEngine().CalibrateModeBManualRectangle(
            new CropBoxPixels(150, 160, 1030, 725),
            physicalWidthCm: 88,
            physicalHeightCm: 56.5,
            imageWidthPx: width,
            imageHeightPx: height);
        calibration.RestrictDetectionToCropBox = true;

        PatternPiece piece = Assert.Single(new DetectionEngine().DetectPatterns(image, calibration).Pieces);
        Assert.True(piece.AreaMm2 > 300_000, $"Tight-frame neutral paper was lost: {piece.AreaMm2:F1} mm²");
    }

    private static RawImageBuffer CreateSolidImage(int width, int height, byte gray)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = gray;
            pixels[i + 1] = gray;
            pixels[i + 2] = gray;
            pixels[i + 3] = 255;
        }

        return new RawImageBuffer
        {
            Width = width,
            Height = height,
            Stride = width * 4,
            BgraPixels = pixels,
            FilePath = "synthetic-edge-fixture"
        };
    }

    private static void FillPolygon(RawImageBuffer image, IReadOnlyList<ImagePixelPoint> polygon, byte gray)
        => FillPolygon(image, polygon, gray, gray, gray);

    private static void FillPolygon(RawImageBuffer image, IReadOnlyList<ImagePixelPoint> polygon, byte r, byte g, byte b)
    {
        int minY = Math.Max(0, (int)Math.Floor(polygon.Min(p => p.Y)));
        int maxY = Math.Min(image.Height - 1, (int)Math.Ceiling(polygon.Max(p => p.Y)));
        for (int y = minY; y <= maxY; y++)
        {
            var intersections = new List<double>();
            for (int i = 0; i < polygon.Count; i++)
            {
                ImagePixelPoint a = polygon[i];
                ImagePixelPoint endpoint = polygon[(i + 1) % polygon.Count];
                if ((a.Y <= y && endpoint.Y > y) || (endpoint.Y <= y && a.Y > y))
                {
                    intersections.Add(a.X + (((y - a.Y) / (endpoint.Y - a.Y)) * (endpoint.X - a.X)));
                }
            }

            intersections.Sort();
            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                int x0 = Math.Max(0, (int)Math.Ceiling(intersections[i]));
                int x1 = Math.Min(image.Width - 1, (int)Math.Floor(intersections[i + 1]));
                for (int x = x0; x <= x1; x++)
                {
                    image.SetPixel(x, y, r, g, b);
                }
            }
        }
    }
}
