using System.IO;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using Xunit;

namespace PatternDigitizerPro.Tests;

/// <summary>
/// Regression and parity verification tests for:
/// TASK 1: Multi-Point Selection + Delete + Undo/Redo
/// TASK 2: Line <-> Curve Conversion + Bézier Handles + Persistence
/// TASK 3: Pan / Move Workspace Geometry Invariance
/// TASK 4: Grid Calibration 2D Sub-Pixel Intersection Accuracy
/// </summary>
public class GeminiParityFixTests
{
    private const string ReferenceAssetsDir = @"E:\claude\GEMHIHI\assets\images\reference";

    // =======================================================================
    // TASK 1: MULTI-POINT SELECTION & DELETE
    // =======================================================================

    [Fact]
    public void Task1_MultiPointSelection_DeletesOnlySelectedPoints_AndPreservesClosedContour()
    {
        var edit = new EditingEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        // Create an 8-vertex polygon
        PatternPiece piece = new PatternPiece { Index = 1, Name = "Piece 1" };
        for (int i = 0; i < 8; i++)
        {
            double angle = i * (Math.PI / 4.0);
            double x = 100.0 + (50.0 * Math.Cos(angle));
            double y = 100.0 + (50.0 * Math.Sin(angle));
            piece.Vertices.Add(new ContourVertex
            {
                PositionMm = new PhysicalPointMm(x, y),
                IsGradedCorner = (i % 2 == 0),
                OutgoingIsCurve = false
            });
        }
        Assert.Equal(8, piece.Vertices.Count);

        // Select and delete vertices at indices 2 and 5
        int deleted = edit.DeleteMultipleVertices(piece, [2, 5]);
        Assert.Equal(2, deleted);
        Assert.Equal(6, piece.Vertices.Count);

        // Piece must not be destroyed when points are deleted
        Assert.False(string.IsNullOrEmpty(piece.Name));
        Assert.True(piece.Vertices.Count >= 3);
    }

    [Fact]
    public void Task1_MultiPointDelete_EnforcesMinimumThreeVerticesInvariant()
    {
        var edit = new EditingEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(0, 0), 100.0, 100.0, cal);
        Assert.Equal(4, piece.Vertices.Count);

        // Try to delete all 4 vertices
        int deleted = edit.DeleteMultipleVertices(piece, [0, 1, 2, 3]);
        // Must delete at most 1 vertex so 3 remain
        Assert.Equal(1, deleted);
        Assert.Equal(3, piece.Vertices.Count);
    }

    [Fact]
    public void Task1_MultiPointDelete_UndoRedo_RestoresExactVerticesAndHandles()
    {
        var edit = new EditingEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(0, 0), 100.0, 100.0, cal);
        edit.AddPointOnSegment(piece, 0, 0.4, calibration: cal);
        edit.AddPointOnSegment(piece, 2, 0.6, calibration: cal);
        Assert.Equal(6, piece.Vertices.Count);

        List<PhysicalPointMm> originalPositions = piece.Vertices.Select(v => v.PositionMm).ToList();

        // Push snapshot and delete 2 vertices
        var pieceList = new List<PatternPiece> { piece };
        edit.PushUndoSnapshot(pieceList, "Delete 2 points");
        edit.DeleteMultipleVertices(piece, [1, 3]);
        Assert.Equal(4, piece.Vertices.Count);

        // Undo
        var (undoSuccess, restored, _) = edit.Undo(pieceList);
        Assert.True(undoSuccess);
        Assert.Single(restored);
        Assert.Equal(6, restored[0].Vertices.Count);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(originalPositions[i].XMm, restored[0].Vertices[i].PositionMm.XMm, precision: 4);
            Assert.Equal(originalPositions[i].YMm, restored[0].Vertices[i].PositionMm.YMm, precision: 4);
        }

        // Redo
        var (redoSuccess, redoRestored, _) = edit.Redo(restored);
        Assert.True(redoSuccess);
        Assert.Equal(4, redoRestored[0].Vertices.Count);
    }

    // =======================================================================
    // TASK 2: LINE <-> CURVE CONVERSION & BÉZIER HANDLES
    // =======================================================================

    [Fact]
    public void Task2_LineToCurve_ConvertsTargetSegmentToCubicBezier_WithEditableHandles()
    {
        var edit = new EditingEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(0, 0), 200.0, 100.0, cal);
        Assert.False(piece.Vertices[0].OutgoingIsCurve);

        // Convert segment 0 (top horizontal line from (0,0) to (200,0)) to curve
        edit.ConvertLineToCurve(piece, 0);
        Assert.True(piece.Vertices[0].OutgoingIsCurve);
        Assert.False(piece.Vertices[1].OutgoingIsCurve); // other segments unchanged

        CubicBezierSegment seg = piece.GetSegment(0);
        Assert.True(seg.IsCurve);

        // Edit Bézier handle C1
        var newC1 = new PhysicalPointMm(60.0, -35.0);
        edit.MoveBezierControlHandle(piece, 0, isStartHandleC1: true, newC1);
        Assert.Equal(newC1.XMm, piece.Vertices[0].OutControlHandleMm.XMm, precision: 4);
        Assert.Equal(newC1.YMm, piece.Vertices[0].OutControlHandleMm.YMm, precision: 4);

        // Convert curve back to line
        edit.ConvertCurveToLine(piece, 0);
        Assert.False(piece.Vertices[0].OutgoingIsCurve);
        CubicBezierSegment backToLine = piece.GetSegment(0);
        Assert.False(backToLine.IsCurve);
    }

    [Fact]
    public void Task2_LineToCurve_ProjectSaveAndLoad_PreservesCurveTypeAndHandles()
    {
        var edit = new EditingEngine();
        var fileEng = new FileEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(10, 10), 150.0, 80.0, cal);
        edit.ConvertLineToCurve(piece, 1);
        var customC1 = new PhysicalPointMm(180.0, 40.0);
        var customC2 = new PhysicalPointMm(170.0, 70.0);
        piece.Vertices[1].OutControlHandleMm = customC1;
        piece.Vertices[2].InControlHandleMm = customC2;

        string tempProj = Path.Combine(Path.GetTempPath(), $"pdp_curve_test_{Guid.NewGuid():N}.pdp");
        try
        {
            var doc = new DigitizerProjectDocument
            {
                Calibration = cal,
                Pieces = [piece]
            };
            fileEng.SaveProject(doc, tempProj);

            DigitizerProjectDocument loaded = fileEng.LoadProject(tempProj);
            Assert.Single(loaded.Pieces);
            PatternPiece p = loaded.Pieces[0];
            Assert.True(p.Vertices[1].OutgoingIsCurve);
            Assert.Equal(customC1.XMm, p.Vertices[1].OutControlHandleMm.XMm, precision: 3);
            Assert.Equal(customC1.YMm, p.Vertices[1].OutControlHandleMm.YMm, precision: 3);
            Assert.Equal(customC2.XMm, p.Vertices[2].InControlHandleMm.XMm, precision: 3);
            Assert.Equal(customC2.YMm, p.Vertices[2].InControlHandleMm.YMm, precision: 3);
        }
        finally
        {
            if (File.Exists(tempProj)) File.Delete(tempProj);
        }
    }

    // =======================================================================
    // TASK 3: PAN / MOVE WORKSPACE GEOMETRY INVARIANCE
    // =======================================================================

    [Theory]
    [InlineData(150.0, -80.0)]
    [InlineData(-200.0, 300.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(500.0, 500.0)]
    public void Task3_Pan_StrictlyPreservesPatternPieceCoordinates(double panX, double panY)
    {
        var vp = new ViewportEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        var edit = new EditingEngine();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(50, 50), 200.0, 120.0, cal);

        // Record initial geometry
        List<PhysicalPointMm> initialMm = piece.Vertices.Select(v => v.PositionMm).ToList();
        List<ImagePixelPoint> initialPx = piece.Vertices.Select(v => v.SourcePixelPosition).ToList();

        // Perform pan
        vp.SetPanOffset(panX, panY);
        vp.PanBy(45.0, -25.0);

        // Verification: Geometry in piece must be strictly identical (0.0000 mm deviation)
        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            Assert.Equal(initialMm[i].XMm, piece.Vertices[i].PositionMm.XMm);
            Assert.Equal(initialMm[i].YMm, piece.Vertices[i].PositionMm.YMm);
            Assert.Equal(initialPx[i].X, piece.Vertices[i].SourcePixelPosition.X);
            Assert.Equal(initialPx[i].Y, piece.Vertices[i].SourcePixelPosition.Y);
        }

        // Relative coordinate mapping from Image to Viewport shifts by exact pan offset
        ImagePixelPoint imgPt = new(100, 200);
        ViewportPoint vpPt = vp.ImageToViewport(imgPt);
        Assert.Equal(vp.PanOffsetX + (100 * vp.ZoomScale), vpPt.X, precision: 5);
        Assert.Equal(vp.PanOffsetY + (200 * vp.ZoomScale), vpPt.Y, precision: 5);

        // Round-trip invertibility
        ImagePixelPoint backToImg = vp.ViewportToImage(vpPt);
        Assert.Equal(imgPt.X, backToImg.X, precision: 5);
        Assert.Equal(imgPt.Y, backToImg.Y, precision: 5);
    }

    [Fact]
    public void Task3_ZoomAfterPan_And_PanAfterZoom_PreservesGeometryInvarianceAcrossDpis()
    {
        var vp = new ViewportEngine();
        double[] testDpis = [96.0, 180.0, 300.0];

        foreach (double dpi in testDpis)
        {
            var raw = new RawImageBuffer
            {
                Width = 2918, Height = 2214, Stride = 2918 * 4,
                DpiX = dpi, DpiY = dpi, BgraPixels = new byte[2918 * 4 * 2214]
            };
            var bmp = raw.ToBitmapSource();
            Assert.Equal(2918.0, bmp.Width);
            Assert.Equal(2214.0, bmp.Height);

            // Pan then Zoom
            vp.SetPanOffset(250.0, -120.0);
            vp.ZoomIn(new ViewportPoint(500, 400));
            Assert.True(vp.ZoomPercent > 100.0);

            // Zoom then Pan
            vp.ZoomOut();
            vp.PanBy(-100.0, 80.0);

            // Coordinate transform integrity
            ImagePixelPoint testPx = new(500, 600);
            ViewportPoint screenPt = vp.ImageToViewport(testPx);
            ImagePixelPoint restoredPx = vp.ViewportToImage(screenPt);
            Assert.Equal(testPx.X, restoredPx.X, precision: 4);
            Assert.Equal(testPx.Y, restoredPx.Y, precision: 4);
        }
    }

    // =======================================================================
    // TASK 4: GRID CALIBRATION 2D INTERSECTION ACCURACY
    // =======================================================================

    [Fact]
    public void Task4_GridCalibration_DetectsRefined2DIntersections_AndComputesMeasurableResiduals()
    {
        string gridPhotoPath = Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg");
        Assert.True(File.Exists(gridPhotoPath), "Calibration photo cal_grid_117cm_20o.jpg must exist.");

        var imgEng = new ImageEngine();
        var calEng = new CalibrationEngine();
        RawImageBuffer img = imgEng.LoadFromFile(gridPhotoPath);

        CalibrationProfile profile = calEng.CalibrateModeAGrid(img, cellWidthCm: 5.0, cellHeightCm: 5.0);

        Assert.Equal(CalibrationMode.GridModeA, profile.Mode);
        Assert.Equal(27, profile.GridColumns);
        Assert.Equal(20, profile.GridRows);

        // 2D Intersections must be populated
        Assert.NotEmpty(profile.DetectedIntersections);
        Assert.Equal(profile.VerticalGridLinesPx.Count * profile.HorizontalGridLinesPx.Count, profile.DetectedIntersections.Count);
        Assert.Equal(profile.GridColumns,
            (profile.VerticalGridLinesPx.Count - 1) + profile.GridLeadingBoundaryColumns + profile.GridTrailingBoundaryColumns);
        Assert.Equal(profile.GridRows,
            (profile.HorizontalGridLinesPx.Count - 1) + profile.GridLeadingBoundaryRows + profile.GridTrailingBoundaryRows);

        PhysicalPointMm firstIntersectionMm = profile.PixelToPhysicalMm(profile.DetectedIntersections[0]);
        var expectedFirstIntersectionMm = new PhysicalPointMm(
            profile.GridLeadingBoundaryColumns * profile.CellWidthCm * 10.0,
            profile.GridLeadingBoundaryRows * profile.CellHeightCm * 10.0);
        Assert.InRange(firstIntersectionMm.DistanceTo(expectedFirstIntersectionMm), 0.0, 0.001);
        Assert.True(firstIntersectionMm.XMm >= 40.0, "First visible vertical grid line must retain the hidden leading-cell offset.");
        Assert.True(firstIntersectionMm.YMm >= 40.0, "First visible horizontal grid line must retain the hidden leading-cell offset.");

        // Per-intersection residuals must be populated and finite
        Assert.NotEmpty(profile.IntersectionResidualsMm);
        Assert.Equal(profile.DetectedIntersections.Count, profile.IntersectionResidualsMm.Count);
        Assert.All(profile.IntersectionResidualsMm, err => Assert.InRange(err, 0.0, 20.0));

        // Mean residual error must be reasonable and measurable
        Assert.InRange(profile.MeanResidualErrorMm, 0.1, 2.5);
        Assert.InRange(profile.MaxResidualErrorMm, 0.5, 8.0);

        // Mode A must apply the measured 2D perspective model, not merely keep
        // the intersections as a diagnostic overlay while using x*scale/y*scale.
        Assert.Equal(9, profile.PixelToPhysicalTransform.Count);
        Assert.Equal(9, profile.PhysicalToPixelTransform.Count);
        Assert.Contains(profile.PixelToPhysicalWarp.Count, new[] { 18, 26 });
        Assert.True(profile.ProjectiveMeanResidualErrorMm > 0.0);
        Assert.True(profile.ProjectiveMaxResidualErrorMm >= profile.ProjectiveMeanResidualErrorMm);
        Assert.InRange(profile.ProjectiveMeanResidualErrorMm, 0.1, 2.4);
        Assert.InRange(profile.ProjectiveMaxResidualErrorMm, 0.5, 6.0);
        Assert.InRange(profile.HoldoutMeanResidualErrorMm, 0.1, 2.4);
        Assert.InRange(profile.HoldoutMaxResidualErrorMm, 0.5, 5.6);

        for (int index = 0; index < profile.DetectedIntersections.Count; index++)
        {
            ImagePixelPoint sample = profile.DetectedIntersections[index];
            int row = index / profile.VerticalGridLinesPx.Count;
            int column = index % profile.VerticalGridLinesPx.Count;
            PhysicalPointMm mm = profile.PixelToPhysicalMm(sample);
            var expectedMm = new PhysicalPointMm(
                (profile.GridLeadingBoundaryColumns + column) * profile.CellWidthCm * 10.0,
                (profile.GridLeadingBoundaryRows + row) * profile.CellHeightCm * 10.0);
            Assert.InRange(mm.DistanceTo(expectedMm), 0.0, 0.001);
            ImagePixelPoint roundTrip = profile.PhysicalMmToPixel(mm);
            Assert.InRange(roundTrip.DistanceTo(sample), 0.0, 0.02);
        }

        ImagePixelPoint dragStart = profile.DetectedIntersections[0];
        ImagePixelPoint dragEnd = new(dragStart.X + 80.0, dragStart.Y + 35.0);
        PhysicalPointMm expectedDragStart = profile.PixelToPhysicalMm(dragStart);
        PhysicalPointMm expectedDragEnd = profile.PixelToPhysicalMm(dragEnd);
        PhysicalPointMm dragDelta = profile.PixelDragDeltaToPhysicalMm(dragStart, dragEnd);
        Assert.InRange(dragDelta.DistanceTo(new PhysicalPointMm(
            expectedDragEnd.XMm - expectedDragStart.XMm,
            expectedDragEnd.YMm - expectedDragStart.YMm)), 0.0, 1e-9);
        double constantScaleDeltaMm = Math.Sqrt(
            Math.Pow(80.0 * profile.ScaleXMmPerPx, 2) +
            Math.Pow(35.0 * profile.ScaleYMmPerPx, 2));
        Assert.True(Math.Abs(dragDelta.DistanceTo(default) - constantScaleDeltaMm) > 0.01,
            "Projective/polynomial drag must not silently fall back to a constant global scale.");

        string calibrationPath = Path.Combine(Path.GetTempPath(), $"pdp-projective-{Guid.NewGuid():N}.json");
        try
        {
            calEng.SaveToFile(profile, calibrationPath);
            CalibrationProfile reopened = calEng.LoadFromFile(calibrationPath);
            Assert.Equal(profile.PixelToPhysicalTransform, reopened.PixelToPhysicalTransform);
            Assert.Equal(profile.PhysicalToPixelTransform, reopened.PhysicalToPixelTransform);
            Assert.Equal(profile.PixelToPhysicalWarp, reopened.PixelToPhysicalWarp);
            Assert.Equal(profile.ProjectiveMeanResidualErrorMm, reopened.ProjectiveMeanResidualErrorMm);
            Assert.Equal(profile.HoldoutMeanResidualErrorMm, reopened.HoldoutMeanResidualErrorMm);
            Assert.Equal(profile.HoldoutMaxResidualErrorMm, reopened.HoldoutMaxResidualErrorMm);
            Assert.Equal(profile.GridLeadingBoundaryColumns, reopened.GridLeadingBoundaryColumns);
            Assert.Equal(profile.GridTrailingBoundaryColumns, reopened.GridTrailingBoundaryColumns);
            Assert.Equal(profile.GridLeadingBoundaryRows, reopened.GridLeadingBoundaryRows);
            Assert.Equal(profile.GridTrailingBoundaryRows, reopened.GridTrailingBoundaryRows);
        }
        finally
        {
            if (File.Exists(calibrationPath))
            {
                File.Delete(calibrationPath);
            }
        }

        // Verify each intersection is inside image bounds
        foreach (ImagePixelPoint pt in profile.DetectedIntersections)
        {
            Assert.InRange(pt.X, 0.0, (double)img.Width);
            Assert.InRange(pt.Y, 0.0, (double)img.Height);
        }
    }

    [Fact]
    public void Task4_GridCalibration_IntersectionInkDoesNotPullCentersOffTheirLines()
    {
        const int width = 900;
        const int height = 700;
        var image = new RawImageBuffer
        {
            Width = width,
            Height = height,
            Stride = width * 4,
            BgraPixels = Enumerable.Repeat((byte)220, width * height * 4).ToArray()
        };

        int[] xs = [100, 200, 300, 400, 500, 600, 700, 800];
        int[] ys = [100, 200, 300, 400, 500, 600];
        foreach (int x in xs)
        {
            for (int y = 0; y < height; y++)
            {
                for (int dx = -1; dx <= 1; dx++) image.SetPixel(x + dx, y, 30, 30, 30);
            }
        }
        foreach (int y in ys)
        {
            for (int x = 0; x < width; x++)
            {
                for (int dy = -1; dy <= 1; dy++) image.SetPixel(x, y + dy, 30, 30, 30);
            }
        }

        // Deliberately place an extra-dark defect beside each true crossing.  It
        // models the uneven ink/shadow that must not drag a centre mark away from
        // the crossing of the two grid lines.
        foreach (int x in xs)
        {
            foreach (int y in ys)
            {
                for (int dx = 3; dx <= 5; dx++)
                {
                    for (int dy = 2; dy <= 4; dy++) image.SetPixel(x + dx, y + dy, 0, 0, 0);
                }
            }
        }

        CalibrationProfile profile = new CalibrationEngine().CalibrateModeAGrid(image, 5.0, 5.0);
        Assert.Equal(xs.Length * ys.Length, profile.DetectedIntersections.Count);
        for (int row = 0; row < ys.Length; row++)
        {
            for (int column = 0; column < xs.Length; column++)
            {
                ImagePixelPoint point = profile.DetectedIntersections[(row * xs.Length) + column];
                Assert.InRange(point.X, xs[column] - 0.6, xs[column] + 0.6);
                Assert.InRange(point.Y, ys[row] - 0.6, ys[row] + 0.6);
            }
        }
    }

    [Fact]
    public void Task4_GridCalibration_PerspectiveSlantUsesEachLocalCrossingNotGlobalLineAverage()
    {
        const int width = 900;
        const int height = 700;
        var image = new RawImageBuffer
        {
            Width = width,
            Height = height,
            Stride = width * 4,
            BgraPixels = Enumerable.Repeat((byte)220, width * height * 4).ToArray()
        };

        int[] bases = [100, 200, 300, 400, 500, 600, 700, 800];
        int[] ys = [100, 200, 300, 400, 500, 600];
        static int XAt(int baseX, int y) => baseX + (int)Math.Round((y - 350) * 0.05);

        foreach (int baseX in bases)
        {
            for (int y = 0; y < height; y++)
            {
                int x = XAt(baseX, y);
                for (int dx = -4; dx <= 4; dx++) image.SetPixel(x + dx, y, 25, 25, 25);
            }
        }
        foreach (int y in ys)
        {
            for (int x = 0; x < width; x++)
            {
                for (int dy = -1; dy <= 1; dy++) image.SetPixel(x, y + dy, 25, 25, 25);
            }
        }

        CalibrationProfile profile = new CalibrationEngine().CalibrateModeAGrid(image, 5.0, 5.0);
        Assert.Equal(bases.Length * ys.Length, profile.DetectedIntersections.Count);
        for (int row = 0; row < ys.Length; row++)
        {
            for (int column = 0; column < bases.Length; column++)
            {
                ImagePixelPoint point = profile.DetectedIntersections[(row * bases.Length) + column];
                Assert.InRange(point.X, XAt(bases[column], ys[row]) - 1.0, XAt(bases[column], ys[row]) + 1.0);
                Assert.InRange(point.Y, ys[row] - 1.0, ys[row] + 1.0);
            }
        }
    }
}
