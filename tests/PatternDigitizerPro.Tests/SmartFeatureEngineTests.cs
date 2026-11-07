using System.IO;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.UI.ViewModels;
using Xunit;

namespace PatternDigitizerPro.Tests;

public sealed class SmartFeatureEngineTests
{
    [Fact]
    public void ViewModelSmartTools_MutateOnlyDetectedGeometry_AndRoundTripUndoRedo()
    {
        RawImageBuffer image = CreateImage(240, 180, 190);
        DrawLine(image, 20, 20, 220, 20, 20, 3);
        DrawLine(image, 20, 20, 20, 160, 20, 3);
        DrawDisk(image, 105, 78, 7, 20);
        DrawLine(image, 75, 120, 170, 138, 25, 4);

        string imagePath = Path.Combine(Path.GetTempPath(), $"pdp_smart_{Guid.NewGuid():N}.bmp");
        string blankPath = Path.Combine(Path.GetTempPath(), $"pdp_smart_blank_{Guid.NewGuid():N}.bmp");
        try
        {
            WriteBmp32(image, imagePath);
            var vm = new MainWindowViewModel();
            Assert.True(vm.LoadImageFromFile(imagePath));
            vm.ActiveCalibration = new CalibrationProfile
            {
                Mode = CalibrationMode.ManualRectangleModeB,
                ScaleXMmPerPx = 1.0,
                ScaleYMmPerPx = 1.0,
                ImageWidthPx = image.Width,
                ImageHeightPx = image.Height,
                CropBox = new CropBoxPixels(0, 0, image.Width, image.Height)
            };

            PatternPiece piece = CreatePiece(
                new PhysicalPointMm(20, 20),
                new PhysicalPointMm(95, 20),
                new PhysicalPointMm(110, 38),
                new PhysicalPointMm(125, 20),
                new PhysicalPointMm(220, 20),
                new PhysicalPointMm(220, 160),
                new PhysicalPointMm(20, 160));
            foreach (ContourVertex vertex in piece.Vertices)
            {
                vertex.IsGradedCorner = false;
            }
            vm.DomainPieces.Add(piece);
            vm.SelectDomainPiece(piece, 0);

            vm.ExecuteSmartCornerAt(piece, new PhysicalPointMm(22, 22));
            Assert.True(piece.Vertices[0].IsGradedCorner);
            Assert.Equal(7, piece.Vertices.Count);
            vm.ExecuteUndo();
            piece = vm.DomainPieces[0];
            Assert.All(piece.Vertices, vertex => Assert.False(vertex.IsGradedCorner));
            vm.ExecuteRedo();
            piece = vm.DomainPieces[0];
            Assert.True(piece.Vertices[0].IsGradedCorner);

            vm.ExecuteSmartInternalPointAt(piece, new PhysicalPointMm(105, 78));
            Assert.Single(piece.InternalPoints);
            Assert.InRange(piece.InternalPoints[0].PositionMm.DistanceTo(new PhysicalPointMm(105, 78)), 0, 5);
            vm.ExecuteUndo();
            piece = vm.DomainPieces[0];
            Assert.Empty(piece.InternalPoints);
            vm.ExecuteRedo();
            piece = vm.DomainPieces[0];
            Assert.Single(piece.InternalPoints);

            vm.ExecuteSmartInternalLineAt(piece, new PhysicalPointMm(123, 129));
            Assert.Single(piece.InternalLines);
            Assert.True(piece.InternalLines[0].LengthMm > 70);
            int vertexCountBeforeNotch = piece.Vertices.Count;

            vm.ExecuteSmartNotchAt(piece, new PhysicalPointMm(110, 30));
            Assert.Single(piece.Notches);
            Assert.Equal(vertexCountBeforeNotch, piece.Vertices.Count);

            WriteBmp32(CreateImage(240, 180, 190), blankPath);
            var blankVm = new MainWindowViewModel();
            Assert.True(blankVm.LoadImageFromFile(blankPath));
            blankVm.ActiveCalibration = vm.ActiveCalibration.Clone();
            PatternPiece blankPiece = CreateRectanglePiece(20, 20, 220, 160);
            blankVm.DomainPieces.Add(blankPiece);
            blankVm.SelectDomainPiece(blankPiece, 0);
            int undoCountBeforeRejectedCorner = blankVm.EditingEngine.UndoCount;
            int vertexCountBeforeRejectedCorner = blankPiece.Vertices.Count;
            blankVm.ExecuteSmartCornerAt(blankPiece, new PhysicalPointMm(20, 20));
            Assert.Equal(undoCountBeforeRejectedCorner, blankVm.EditingEngine.UndoCount);
            Assert.Equal(vertexCountBeforeRejectedCorner, blankPiece.Vertices.Count);
            Assert.Contains("no sufficiently strong", blankVm.StatusLeftText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }
            if (File.Exists(blankPath))
            {
                File.Delete(blankPath);
            }
        }
    }

    [Fact]
    public void CleanRoomSmartDetectors_FindCornerPointAndLine_AndRejectBlankArea()
    {
        RawImageBuffer image = CreateImage(240, 180, 190);
        CalibrationProfile calibration = new()
        {
            Mode = CalibrationMode.ManualRectangleModeB,
            ScaleXMmPerPx = 1.0,
            ScaleYMmPerPx = 1.0,
            ImageWidthPx = image.Width,
            ImageHeightPx = image.Height,
            CropBox = new CropBoxPixels(0, 0, image.Width, image.Height)
        };
        PatternPiece piece = CreateRectanglePiece(20, 20, 220, 160);
        var engine = new SmartFeatureEngine(new GeometryEngine());

        DrawLine(image, 20, 20, 220, 20, 20, 3);
        DrawLine(image, 20, 20, 20, 160, 20, 3);
        DrawDisk(image, 105, 78, 7, 20);
        DrawLine(image, 75, 120, 170, 138, 25, 4);

        Assert.True(engine.TryDetectCornerOnContour(
            image, calibration, piece, new ImagePixelPoint(22, 22), 35,
            out PhysicalPointMm corner, out double cornerConfidence));
        Assert.InRange(corner.DistanceTo(new PhysicalPointMm(20, 20)), 0, 8);
        Assert.True(cornerConfidence > 0);

        Assert.True(engine.TryDetectInternalPoint(
            image, calibration, piece, new ImagePixelPoint(105, 78), 25,
            out PhysicalPointMm point, out double pointConfidence));
        Assert.InRange(point.DistanceTo(new PhysicalPointMm(105, 78)), 0, 5);
        Assert.True(pointConfidence >= 0.35);

        Assert.True(engine.TryDetectInternalLine(
            image, calibration, piece, new ImagePixelPoint(123, 129), 65,
            out PhysicalPointMm lineStart, out PhysicalPointMm lineEnd, out double lineConfidence));
        Assert.True(lineStart.DistanceTo(lineEnd) > 70);
        Assert.True(lineConfidence > 0);

        PatternPiece notchedPiece = CreatePiece(
            new PhysicalPointMm(20, 20),
            new PhysicalPointMm(95, 20),
            new PhysicalPointMm(110, 38),
            new PhysicalPointMm(125, 20),
            new PhysicalPointMm(220, 20),
            new PhysicalPointMm(220, 160),
            new PhysicalPointMm(20, 160));
        Assert.True(engine.TryDetectNotchOnContour(
            notchedPiece, new PhysicalPointMm(110, 30), 35,
            out PhysicalPointMm notch, out double notchConfidence));
        Assert.Equal(new PhysicalPointMm(110, 38), notch);
        Assert.True(notchConfidence >= 0.18);
        Assert.False(engine.TryDetectNotchOnContour(
            piece, new PhysicalPointMm(20, 20), 35,
            out _, out _));

        RawImageBuffer blank = CreateImage(240, 180, 190);
        Assert.False(engine.TryDetectInternalPoint(
            blank, calibration, piece, new ImagePixelPoint(105, 78), 25,
            out _, out _));
        Assert.False(engine.TryDetectInternalLine(
            blank, calibration, piece, new ImagePixelPoint(123, 129), 65,
            out _, out _, out _));
    }

    private static RawImageBuffer CreateImage(int width, int height, byte gray)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = gray;
            pixels[i + 1] = gray;
            pixels[i + 2] = gray;
            pixels[i + 3] = 255;
        }
        return new RawImageBuffer { Width = width, Height = height, Stride = width * 4, BgraPixels = pixels };
    }

    private static PatternPiece CreateRectanglePiece(double x0, double y0, double x1, double y1)
        => CreatePiece(
            new PhysicalPointMm(x0, y0), new PhysicalPointMm(x1, y0),
            new PhysicalPointMm(x1, y1), new PhysicalPointMm(x0, y1));

    private static PatternPiece CreatePiece(params PhysicalPointMm[] points)
    {
        var piece = new PatternPiece { Name = "Synthetic Piece" };
        foreach (PhysicalPointMm point in points)
        {
            piece.Vertices.Add(new ContourVertex
            {
                PositionMm = point,
                SourcePixelPosition = new ImagePixelPoint(point.XMm, point.YMm),
                InControlHandleMm = point,
                OutControlHandleMm = point,
                IsGradedCorner = true
            });
        }
        return piece;
    }

    private static void DrawDisk(RawImageBuffer image, int cx, int cy, int radius, byte gray)
    {
        for (int y = cy - radius; y <= cy + radius; y++)
        for (int x = cx - radius; x <= cx + radius; x++)
        {
            if (((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= radius * radius)
            {
                image.SetPixel(x, y, gray, gray, gray);
            }
        }
    }

    private static void DrawLine(RawImageBuffer image, int x0, int y0, int x1, int y1, byte gray, int thickness)
    {
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : i / (double)steps;
            int x = (int)Math.Round(x0 + ((x1 - x0) * t));
            int y = (int)Math.Round(y0 + ((y1 - y0) * t));
            DrawDisk(image, x, y, thickness, gray);
        }
    }

    private static void WriteBmp32(RawImageBuffer image, string path)
    {
        int pixelBytes = image.Width * image.Height * 4;
        byte[] file = new byte[54 + pixelBytes];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        WriteInt(file, 2, file.Length);
        WriteInt(file, 10, 54);
        WriteInt(file, 14, 40);
        WriteInt(file, 18, image.Width);
        WriteInt(file, 22, image.Height);
        file[26] = 1;
        file[28] = 32;
        WriteInt(file, 34, pixelBytes);

        for (int y = 0; y < image.Height; y++)
        {
            int sourceRow = y * image.Stride;
            int targetRow = 54 + ((image.Height - 1 - y) * image.Width * 4);
            Buffer.BlockCopy(image.BgraPixels, sourceRow, file, targetRow, image.Width * 4);
        }
        File.WriteAllBytes(path, file);
    }

    private static void WriteInt(byte[] target, int offset, int value) =>
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, target, offset, 4);
}
