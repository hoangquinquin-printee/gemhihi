using System.IO;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using Xunit;

namespace PatternDigitizerPro.Tests;

/// <summary>
/// Phase 2 Remediation tests (P0 DPI normalization, P1 interactive editing primitives, P2 generalization / no hardcoding).
/// All detection tests use the real reference photographs, never synthetic stand-ins.
/// </summary>
public class Phase2RemediationTests
{
    private const string ReferenceAssetsDir = @"E:\claude\GEMHIHI\assets\images\reference";
    private const string SrcDir = @"E:\claude\GEMHIHI\src\PatternDigitizerPro.Core\Engines";

    private static readonly Lazy<CalibrationProfile> GridProfile = new(() =>
    {
        var img = new ImageEngine();
        return new CalibrationEngine().CalibrateModeAGrid(img.LoadFromFile(Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg")), 5.0, 5.0);
    });

    // ------------------------------------------------------------------ P0
    [Theory]
    [InlineData(96.0)]
    [InlineData(180.0)]
    [InlineData(300.0)]
    public void P0_BitmapSource_IsAlwaysOnePixelPerDip_RegardlessOfDpiMetadata(double dpi)
    {
        var buf = new RawImageBuffer
        {
            Width = 640,
            Height = 480,
            Stride = 640 * 4,
            DpiX = dpi,
            DpiY = dpi,
            BgraPixels = new byte[640 * 4 * 480]
        };

        var bmp = buf.ToBitmapSource();
        Assert.Equal(640.0, bmp.Width, 3);
        Assert.Equal(480.0, bmp.Height, 3);
        Assert.Equal(dpi, buf.DpiX); // metadata preserved for reference only
    }

    // ------------------------------------------------------------------ P1
    [Fact]
    public void P1_AddPointOnSegment_UsesClickedParameter_NotMidpoint()
    {
        var meas = new MeasurementEngine();
        var edit = new EditingEngine(new GeometryEngine(), meas);
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(0, 0), 100.0, 100.0, cal);

        PhysicalPointMm a = piece.Vertices[0].PositionMm;
        PhysicalPointMm b = piece.Vertices[1].PositionMm;
        int newIdx = edit.AddPointOnSegment(piece, 0, 0.3, calibration: cal);
        PhysicalPointMm inserted = piece.Vertices[newIdx].PositionMm;

        PhysicalPointMm expected = a.Lerp(b, 0.3);
        Assert.True(inserted.DistanceTo(expected) < 0.01, $"Inserted {inserted} expected {expected}");
        Assert.Equal(400.0, piece.PerimeterMm, 4); // straight split preserves perimeter
    }

    [Fact]
    public void P1_InternalPointAndLine_MoveAndDelete()
    {
        var edit = new EditingEngine();
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(0, 0), 200.0, 200.0, cal);

        InternalPointItem pt = edit.AddInternalPoint(piece, new PhysicalPointMm(50, 50));
        Assert.True(edit.MoveInternalPointTo(piece, pt.Id, new PhysicalPointMm(70, 80)));
        Assert.Equal(new PhysicalPointMm(70, 80), piece.InternalPoints.Single().PositionMm);

        InternalLineItem line = edit.AddInternalLine(piece, new PhysicalPointMm(20, 20), new PhysicalPointMm(120, 40));
        Assert.True(edit.MoveInternalLineByDelta(piece, line.Id, 10, 5));
        Assert.Equal(new PhysicalPointMm(30, 25), piece.InternalLines.Single().StartMm);
        Assert.True(edit.MoveInternalLineEndpoint(piece, line.Id, isStartEndpoint: false, new PhysicalPointMm(150, 150)));
        Assert.Equal(new PhysicalPointMm(150, 150), piece.InternalLines.Single().EndMm);

        Assert.True(edit.DeleteInternalPoint(piece, pt.Id));
        Assert.True(edit.DeleteInternalLine(piece, line.Id));
        Assert.Empty(piece.InternalPoints);
        Assert.Empty(piece.InternalLines);
    }

    // ------------------------------------------------------------------ P2: no hardcoding
    [Fact]
    public void P2_Engines_ContainNoFilenameOrResolutionHardcoding()
    {
        string det = File.ReadAllText(Path.Combine(SrcDir, "DetectionEngine.cs"));
        string cal = File.ReadAllText(Path.Combine(SrcDir, "CalibrationEngine.cs"));

        Assert.DoesNotContain("FilePath.Contains", det);
        Assert.DoesNotContain("isLeftGlareZone", det);
        Assert.DoesNotContain("sx < 790", det);
        Assert.DoesNotContain("img.Width == 3456", cal);
    }

    [Fact]
    public void P2_GridCalibration_CellCountDerivedFromDetectedLattice()
    {
        CalibrationProfile p = GridProfile.Value;
        Assert.Equal(27, p.GridColumns);
        Assert.Equal(20, p.GridRows);
        Assert.InRange(p.ScaleXMmPerPx, 0.40, 0.43);
        Assert.InRange(p.ScaleYMmPerPx, 0.40, 0.43);
    }

    [Fact]
    public void P2_GridCalibration_HorizontallyFlippedBoard_GivesSameScale()
    {
        var img = new ImageEngine();
        RawImageBuffer src = img.LoadFromFile(Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg"));
        RawImageBuffer flipped = FlipHorizontal(src);
        CalibrationProfile p = new CalibrationEngine().CalibrateModeAGrid(flipped, 5.0, 5.0);

        Assert.Equal(GridProfile.Value.ScaleXMmPerPx, p.ScaleXMmPerPx, 2);
        Assert.Equal(GridProfile.Value.ScaleYMmPerPx, p.ScaleYMmPerPx, 2);
        Assert.Equal(GridProfile.Value.GridRows, p.GridRows);
    }

    [Fact]
    public void P2_Detection_RenamedFile_GivesIdenticalResult()
    {
        var img = new ImageEngine();
        var det = new DetectionEngine(img);
        string original = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        string renamed = Path.Combine(Path.GetTempPath(), $"renamed_{Guid.NewGuid():N}.jpg");
        File.Copy(original, renamed, overwrite: true);
        try
        {
            var a = det.DetectPatterns(img.LoadFromFile(original), GridProfile.Value);
            var b = det.DetectPatterns(img.LoadFromFile(renamed), GridProfile.Value);
            Assert.Equal(a.Pieces.Count, b.Pieces.Count);
            for (int i = 0; i < a.Pieces.Count; i++)
            {
                Assert.Equal(a.Pieces[i].Name, b.Pieces[i].Name);
                Assert.Equal(a.Pieces[i].AreaCm2, b.Pieces[i].AreaCm2, 6);
            }
        }
        finally
        {
            File.Delete(renamed);
        }
    }

    [Fact]
    public void P2_Detection_FlippedImage_PreservesPieceCountAndAreas()
    {
        var img = new ImageEngine();
        var det = new DetectionEngine(img);
        RawImageBuffer src = img.LoadFromFile(Path.Combine(ReferenceAssetsDir, "real_01232026_093723.jpg"));
        var a = det.DetectPatterns(src, GridProfile.Value);
        var b = det.DetectPatterns(FlipHorizontal(src), GridProfile.Value);

        Assert.Equal(3, a.Pieces.Count);
        Assert.Equal(3, b.Pieces.Count);
        var areasA = a.Pieces.Select(p => p.AreaCm2).OrderBy(x => x).ToList();
        var areasB = b.Pieces.Select(p => p.AreaCm2).OrderBy(x => x).ToList();
        for (int i = 0; i < 3; i++)
        {
            Assert.True(Math.Abs(areasA[i] - areasB[i]) / areasA[i] < 0.02, $"Area mismatch {areasA[i]:F1} vs {areasB[i]:F1}");
        }
    }

    [Fact]
    public void P2_Detection_DenimPhoto_RejectsLensFlareByBoundaryContrast()
    {
        var img = new ImageEngine();
        var res = new DetectionEngine(img).DetectPatterns(
            img.LoadFromFile(Path.Combine(ReferenceAssetsDir, "real_01082026_091623.jpg")), GridProfile.Value);

        Assert.Equal(2, res.Pieces.Count);
        Assert.Contains(res.RejectedComponents, r => r.Reason == "DiffuseGlare" && r.PixelArea > 100000);
        Assert.All(res.Pieces, p => Assert.True(p.SourcePixelBoundingBox.X > 700));
    }

    [Fact]
    public void P2_Detection_WhitePaperPhoto_SharpCornersAndSmoothNeckline()
    {
        var img = new ImageEngine();
        var res = new DetectionEngine(img).DetectPatterns(
            img.LoadFromFile(Path.Combine(ReferenceAssetsDir, "real_01232026_093723.jpg")), GridProfile.Value);

        Assert.Equal(3, res.Pieces.Count);
        PatternPiece bodice = res.Pieces.OrderBy(p => p.SourcePixelBoundingBox.X).First();

        // Sharp 90-degree bottom-right corner at ~(908, 858) must be a graded corner within 6 px
        Assert.Contains(bodice.Vertices, v => v.IsGradedCorner &&
            Math.Abs(v.SourcePixelPosition.X - 908) <= 6 && Math.Abs(v.SourcePixelPosition.Y - 858) <= 6);

        // The neckline arc must not contain a false graded corner between its two end corners
        Assert.DoesNotContain(bodice.Vertices, v => v.IsGradedCorner &&
            v.SourcePixelPosition.X > 450 && v.SourcePixelPosition.X < 900 &&
            v.SourcePixelPosition.Y > 220 && v.SourcePixelPosition.Y < 340);

        // No two vertices closer than 8 px (duplicate corner residue)
        for (int i = 0; i < bodice.Vertices.Count; i++)
        {
            var p0 = bodice.Vertices[i].SourcePixelPosition;
            var p1 = bodice.Vertices[(i + 1) % bodice.Vertices.Count].SourcePixelPosition;
            Assert.True(p0.DistanceTo(p1) > 8.0);
        }
    }

    private static RawImageBuffer FlipHorizontal(RawImageBuffer src)
    {
        byte[] dst = new byte[src.BgraPixels.Length];
        for (int y = 0; y < src.Height; y++)
        {
            int row = y * src.Stride;
            for (int x = 0; x < src.Width; x++)
            {
                Buffer.BlockCopy(src.BgraPixels, row + (x * 4), dst, row + ((src.Width - 1 - x) * 4), 4);
            }
        }

        return new RawImageBuffer
        {
            Width = src.Width,
            Height = src.Height,
            Stride = src.Stride,
            DpiX = src.DpiX,
            DpiY = src.DpiY,
            BgraPixels = dst,
            FilePath = "flipped.jpg"
        };
    }
}
