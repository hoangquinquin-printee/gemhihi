using System.IO;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using PatternDigitizerPro.UI.ViewModels;
using Xunit;

namespace PatternDigitizerPro.Tests;

public sealed class UiReplicationTests
{
    private const string ReferenceAssetsDir = @"E:\claude\GEMHIHI\assets\images\reference";

    [Fact]
    public void LiveV9ReferenceState_MatchesUserUploadedScreenshots1To5()
    {
        var vm = new MainWindowViewModel();
        vm.ApplyLiveV9ReferenceState();

        Assert.True(vm.IsDemoReferenceLayoutActive);
        Assert.True(vm.IsV9ThreePieceDemoActive);
        Assert.Equal(@"gemhihi - [D:\pictures\03172025 111128.jpg]", vm.WindowTitle);
        Assert.Equal(3, vm.DetectedPiecesCount);
        Assert.Equal("Detected patterns: 3", vm.DetectedPiecesText);
        Assert.Equal("Picture resolution: 2918 x 2214    Grid dimensions: 135cm x 100cm", vm.PictureResolutionText);
        Assert.False(vm.IsPieceSelected);
        Assert.Equal(string.Empty, vm.PieceName);
        Assert.Equal(PropertiesDockMode.NothingSelected, vm.ActivePropertiesDockMode);
        Assert.True(vm.IsNothingSelectedDockVisible);
        Assert.Equal(3, vm.StockPieces.Count);
        Assert.Equal("Done detecting patterns!", vm.StatusRightText);
    }

    [Fact]
    public void ManualFourPieceDemoState_MatchesGeminiManualScreenshot()
    {
        var vm = new MainWindowViewModel();
        vm.ApplyReferenceDemoState();

        Assert.True(vm.IsDemoReferenceLayoutActive);
        Assert.False(vm.IsV9ThreePieceDemoActive);
        Assert.Equal(@"gemhihi - [D:\LOREDANA\foto digi\fotografii\Traian\Picture1.jpg]", vm.WindowTitle);
        Assert.Equal(4, vm.DetectedPiecesCount);
        Assert.Equal("Detected patterns: 4", vm.DetectedPiecesText);
        Assert.Equal("Picture resolution: 2592 x 1944    Grid dimensions: 110cm x 80cm", vm.PictureResolutionText);
        Assert.True(vm.IsPieceSelected);
        Assert.Equal("Piece 3", vm.PieceName);
        Assert.Equal("1213,75", vm.PieceAreaCm2);
        Assert.Equal("167,43", vm.PiecePerimeterCm);
        Assert.Equal("6 / 0 / 0", vm.NotchesSummary);
        Assert.Equal("5 / 5", vm.CornersSummary);
        Assert.Equal(PropertiesDockMode.Points, vm.ActivePropertiesDockMode);
        Assert.True(vm.IsPointsDockVisible);
        Assert.Equal(4, vm.StockPieces.Count);
    }

    [Fact]
    public void CleanStartupState_ClearsDemoDataAndDisplaysEmptyState()
    {
        var vm = new MainWindowViewModel();

        Assert.False(vm.IsDemoReferenceLayoutActive);
        Assert.False(vm.HasImageLoaded);
        Assert.Null(vm.LoadedBitmap);
        Assert.Equal("gemhihi", vm.WindowTitle);
        Assert.Equal(0, vm.DetectedPiecesCount);
        Assert.False(vm.IsPieceSelected);
        Assert.Equal(PropertiesDockMode.NothingSelected, vm.ActivePropertiesDockMode);
        Assert.True(vm.IsNothingSelectedDockVisible);
        Assert.Empty(vm.StockPieces);
        Assert.Null(vm.RunDetectPatterns());
        Assert.Equal("No image loaded to detect patterns.", vm.StatusLeftText);
    }

    [Fact]
    public void ViewportZoomAndLayerToggles_OperateCorrectly()
    {
        var vm = new MainWindowViewModel();

        Assert.Equal(100.0, vm.ZoomPercent);
        Assert.Equal(1.0, vm.ZoomScale);

        vm.ZoomPercent = 200.0;
        Assert.Equal(2.0, vm.ZoomScale);

        vm.ZoomSliderPosition = 100;
        Assert.Equal(180.0, vm.ZoomPercent);

        vm.ShowGrid = true;
        Assert.True(vm.ShowGrid);

        vm.ShowPicture = false;
        Assert.False(vm.ShowPicture);
    }

    [Fact]
    public void ContextualPropertiesDockModes_SwitchMutuallyExclusively()
    {
        var vm = new MainWindowViewModel();

        vm.SetDigitizerToolWithPhase1Notice(GeminiToolMode.SmartNotch, "Detect notch", PropertiesDockMode.Notch);
        Assert.Equal(GeminiToolMode.SmartNotch, vm.ActiveTool);
        Assert.True(vm.IsNotchDockVisible);
        Assert.False(vm.IsPointsDockVisible);

        vm.SetDigitizerToolWithPhase1Notice(GeminiToolMode.AddInternalPoint, "Create new internal point", PropertiesDockMode.InternalPoint);
        Assert.True(vm.IsInternalPointDockVisible);

        vm.SetDigitizerToolWithPhase1Notice(GeminiToolMode.AddInternalLine, "Create new internal line", PropertiesDockMode.InternalLine);
        Assert.True(vm.IsInternalLineDockVisible);
    }

    [Fact]
    public void LoadImageFromFile_LoadsRealBitmapAndUpdatesResolution()
    {
        string refImg = @"E:\claude\GEMHIHI\assets\images\reference\gemini_main_window.jpg";
        if (!File.Exists(refImg))
        {
            return;
        }

        var vm = new MainWindowViewModel();
        bool loaded = vm.LoadImageFromFile(refImg);

        Assert.True(loaded);
        Assert.True(vm.HasImageLoaded);
        Assert.NotNull(vm.LoadedBitmap);
        Assert.Contains("766 x 555", vm.PictureResolutionText);
    }

    [Fact]
    public void DigitizeFrame_ExplicitPixelInputs_AreExactAndRejectOutOfImageValues()
    {
        string refImg = @"E:\claude\GEMHIHI\assets\images\reference\gemini_main_window.jpg";
        if (!File.Exists(refImg))
        {
            return;
        }

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadImageFromFile(refImg));

        Assert.True(vm.TrySetDigitizeRoi(20, 30, 400, 300, out string success));
        Assert.True(vm.HasDigitizeRoi);
        Assert.Equal(new CropBoxPixels(20, 30, 420, 330), vm.DigitizeRoiBoxPx);
        Assert.False(vm.IsDigitizeRoiCalibrated);
        Assert.Contains("centimetres", success);

        Assert.True(vm.TryCalibrateDigitizeRoi(80, 60, out string calibrated));
        Assert.True(vm.IsDigitizeRoiCalibrated);
        Assert.NotNull(vm.DigitizeFrameCalibration);
        Assert.Equal(80, vm.DigitizeFrameCalibration!.ActiveAreaWidthCm);
        Assert.Equal(60, vm.DigitizeFrameCalibration.ActiveAreaHeightCm);
        Assert.Equal(2.0, vm.DigitizeFrameCalibration.ScaleXMmPerPx, precision: 6);
        Assert.Equal(2.0, vm.DigitizeFrameCalibration.ScaleYMmPerPx, precision: 6);
        Assert.Contains("separately", calibrated);
        PhysicalPointMm frameTopLeft = vm.DigitizeFrameCalibration.PixelToPhysicalMm(new ImagePixelPoint(20, 30));
        PhysicalPointMm frameBottomRight = vm.DigitizeFrameCalibration.PixelToPhysicalMm(new ImagePixelPoint(420, 330));
        Assert.Equal(800.0, frameBottomRight.XMm - frameTopLeft.XMm, precision: 6);
        Assert.Equal(600.0, frameBottomRight.YMm - frameTopLeft.YMm, precision: 6);

        Assert.False(vm.TrySetDigitizeRoi(700, 30, 100, 300, out string error));
        Assert.Contains("inside the image", error);
        Assert.Equal(new CropBoxPixels(20, 30, 420, 330), vm.DigitizeRoiBoxPx);
    }

    [Fact]
    public void SuccessfulDetect_FromCalibratedDigitizeFrame_ClearsFrameAndReturnsToSelect()
    {
        string refImg = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        if (!File.Exists(refImg))
        {
            return;
        }

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadImageFromFile(refImg));
        Assert.NotNull(vm.CurrentRawImage);
        Assert.True(vm.TrySetDigitizeRoi(0, 0, vm.CurrentRawImage!.Width, vm.CurrentRawImage.Height, out _));
        Assert.True(vm.TryCalibrateDigitizeRoi(98.5, 32.5, out _));

        DetectionPipelineResult? result = vm.RunDetectPatterns();

        Assert.NotNull(result);
        Assert.Single(vm.DomainPieces); // A frame digitizes one chosen detail.
        Assert.True(vm.DomainPieces[0].HasLockedDigitizeFrame);
        Assert.Equal(new CropBoxPixels(0, 0, vm.CurrentRawImage.Width, vm.CurrentRawImage.Height), vm.DomainPieces[0].LockedDigitizeFramePixels);
        Assert.Equal(98.5, vm.DomainPieces[0].LockedDigitizeFrameWidthCm, precision: 6);
        Assert.Equal(32.5, vm.DomainPieces[0].LockedDigitizeFrameHeightCm, precision: 6);
        CalibrationProfile frameCalibration = vm.DomainPieces[0].DigitizeFrameCalibration!;
        PhysicalPointMm frameTopLeft = frameCalibration.PixelToPhysicalMm(new ImagePixelPoint(0, 0));
        PhysicalPointMm frameBottomRight = frameCalibration.PixelToPhysicalMm(new ImagePixelPoint(vm.CurrentRawImage.Width, vm.CurrentRawImage.Height));
        Assert.Equal(985.0, frameBottomRight.XMm - frameTopLeft.XMm, precision: 6);
        Assert.Equal(325.0, frameBottomRight.YMm - frameTopLeft.YMm, precision: 6);
        foreach (ContourVertex vertex in vm.DomainPieces[0].Vertices)
        {
            ImagePixelPoint renderedPixel = frameCalibration.PhysicalMmToPixel(vertex.PositionMm);
            Assert.Equal(vertex.SourcePixelPosition.X, renderedPixel.X, precision: 3);
            Assert.Equal(vertex.SourcePixelPosition.Y, renderedPixel.Y, precision: 3);
        }

        // The frame fixes calibration scale but never clips correction points:
        // a photographed contour may legitimately extend beyond a rectangular
        // width/height reference.
        PatternPiece lockedPiece = vm.DomainPieces[0];
        vm.SelectDomainPiece(lockedPiece, 0);
        vm.PointMoveToX = "9999";
        vm.PointMoveToY = "9999";
        vm.ExecuteMovePointToInputs();
        ImagePixelPoint moved = frameCalibration.PhysicalMmToPixel(lockedPiece.Vertices[0].PositionMm);
        Assert.True(moved.X > lockedPiece.LockedDigitizeFramePixels.StopX || moved.Y > lockedPiece.LockedDigitizeFramePixels.StopY);
        Assert.False(vm.HasDigitizeRoi);
        Assert.False(vm.IsDigitizeRoiCalibrated);
        Assert.Equal(GeminiToolMode.SelectDefault, vm.ActiveTool);
    }

    [Fact]
    public void FramedDetect_AppendsOneSelectedDetail_AndKeepsPreviouslyDigitizedPieces()
    {
        string refImg = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        if (!File.Exists(refImg))
        {
            return;
        }

        var vm = new MainWindowViewModel();
        Assert.True(vm.LoadImageFromFile(refImg));
        Assert.NotNull(vm.RunDetectPatterns());
        int existingPieceCount = vm.DomainPieces.Count;
        Assert.True(existingPieceCount > 0);

        Assert.NotNull(vm.CurrentRawImage);
        Assert.True(vm.TrySetDigitizeRoi(0, 0, vm.CurrentRawImage!.Width, vm.CurrentRawImage.Height, out _));
        Assert.True(vm.TryCalibrateDigitizeRoi(135, 100, out _));

        Assert.NotNull(vm.RunDetectPatterns());

        Assert.Equal(existingPieceCount + 1, vm.DomainPieces.Count);
        Assert.Equal(existingPieceCount + 1, vm.DetectedPiecesCount);
        Assert.Equal($"Piece {existingPieceCount + 1}", vm.DomainPieces[^1].Name);
        Assert.Same(vm.DomainPieces[^1], vm.SelectedDomainPiece);
        Assert.False(vm.HasDigitizeRoi);
    }

    [Fact]
    public void DxfProfiles_MatchOriginalGeminiInstallationProfiles()
    {
        var profiles = GeminiReferenceDemoData.GetDxfProfiles();
        Assert.Equal(6, profiles.Count);
        Assert.Contains("General", profiles);
        Assert.Contains("Gemini Cut Plan", profiles);
        Assert.Contains("Gerber Accumark", profiles);
        Assert.Contains("Lectra Modaris", profiles);
    }
}
