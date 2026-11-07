using System.IO;
using System.Windows;
using PatternDigitizerPro.Core.Engines;
using PatternDigitizerPro.Core.Models;
using Xunit;

namespace PatternDigitizerPro.Tests;

public sealed class BackendEnginesTests
{
    private const string ReferenceAssetsDir = @"E:\claude\GEMHIHI\assets\images\reference";
    private const string DebugArtifactsRoot = @"E:\claude\GEMHIHI\artifacts\detection-debug";
    private const string DxfArtifactsRoot = @"E:\claude\GEMHIHI\artifacts\dxf-validation";

    // =======================================================================
    // MILESTONE 1: IMAGE ENGINE & VIEWPORT COORDINATE SYSTEM TESTS
    // =======================================================================

    [Fact]
    public void ViewportEngine_CoordinateRoundTrip_And_PanZoomNeverMutateGeometry()
    {
        var viewport = new ViewportEngine();
        var calibration = CalibrationProfile.CreateDefaultV9GridProfile(3456, 2592);
        var editEngine = new EditingEngine();

        PatternPiece piece = editEngine.CreateAuxiliaryRectanglePiece(
            1,
            new PhysicalPointMm(100.0, 200.0),
            300.0,
            150.0,
            calibration);

        double initialAreaMm2 = piece.AreaMm2;
        double initialPerimMm = piece.PerimeterMm;
        PhysicalPointMm initialV0 = piece.Vertices[0].PositionMm;

        // Perform ZoomIn, ZoomAtCursor, PanBy, ZoomToFit
        var cursorScreen = new ViewportPoint(480.0, 320.0);
        ImagePixelPoint anchorPixelBefore = viewport.ViewportToImage(cursorScreen);

        viewport.ZoomAtCursor(1.5, cursorScreen);
        ImagePixelPoint anchorPixelAfter = viewport.ViewportToImage(cursorScreen);

        // Pixel under cursor must remain stationary after ZoomAtCursor
        Assert.Equal(anchorPixelBefore.X, anchorPixelAfter.X, precision: 5);
        Assert.Equal(anchorPixelBefore.Y, anchorPixelAfter.Y, precision: 5);

        viewport.PanBy(125.0, -80.0);

        // Round-trip: Physical mm -> Image px -> Viewport screen -> Image px -> Physical mm
        var testMm = new PhysicalPointMm(425.6, 318.4);
        ViewportPoint vpPt = viewport.PhysicalMmToViewport(testMm, calibration);
        PhysicalPointMm roundTripMm = viewport.ViewportToPhysicalMm(vpPt, calibration);

        Assert.Equal(testMm.XMm, roundTripMm.XMm, precision: 5);
        Assert.Equal(testMm.YMm, roundTripMm.YMm, precision: 5);

        // Invariant: Pan and Zoom NEVER mutate pattern piece geometry
        Assert.Equal(initialAreaMm2, piece.AreaMm2, precision: 6);
        Assert.Equal(initialPerimMm, piece.PerimeterMm, precision: 6);
        Assert.Equal(initialV0.XMm, piece.Vertices[0].PositionMm.XMm, precision: 6);
        Assert.Equal(initialV0.YMm, piece.Vertices[0].PositionMm.YMm, precision: 6);
    }

    [Fact]
    public void ImageEngine_LoadsRealPhoto_And_CropsRoiCorrectly()
    {
        var imgEngine = new ImageEngine();
        string samplePath = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        Assert.True(File.Exists(samplePath));

        RawImageBuffer raw = imgEngine.LoadFromFile(samplePath);
        Assert.Equal(3456, raw.Width);
        Assert.Equal(2592, raw.Height);

        var cropBox = new CropBoxPixels(200, 150, 1200, 950);
        RawImageBuffer cropped = imgEngine.Crop(raw, cropBox);
        Assert.Equal(1000, cropped.Width);
        Assert.Equal(800, cropped.Height);

        var pOrig = raw.GetPixel(250, 200);
        var pCrop = cropped.GetPixel(50, 50);
        Assert.Equal(pOrig.R, pCrop.R);
        Assert.Equal(pOrig.G, pCrop.G);
        Assert.Equal(pOrig.B, pCrop.B);
    }

    // =======================================================================
    // MILESTONE 2: CALIBRATION ENGINE (MODE A GRID & MODE B MANUAL X/Y) TESTS
    // =======================================================================

    [Fact]
    public void CalibrationEngine_ModeAGrid_RealBoardPhoto_Detects27x20GridAndAccurateScale()
    {
        var imgEngine = new ImageEngine();
        var calEngine = new CalibrationEngine();
        string gridPath = Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg");
        Assert.True(File.Exists(gridPath));

        RawImageBuffer gridImg = imgEngine.LoadFromFile(gridPath);

        // Mode A1: Default 5x5 cm cell size (matches Ảnh 1-5: 27x20 cells = 135cm x 100cm)
        CalibrationProfile prof5Cm = calEngine.CalibrateModeAGrid(gridImg, cellWidthCm: 5.0, cellHeightCm: 5.0);
        Assert.True(calEngine.ValidateCalibration(prof5Cm, out _));
        Assert.Equal(CalibrationMode.GridModeA, prof5Cm.Mode);
        Assert.Equal(27, prof5Cm.GridColumns);
        Assert.Equal(20, prof5Cm.GridRows);
        Assert.Equal(135.0, prof5Cm.ActiveAreaWidthCm, precision: 1);
        Assert.Equal(100.0, prof5Cm.ActiveAreaHeightCm, precision: 1);
        Assert.InRange(prof5Cm.ScaleXMmPerPx, 0.405, 0.425);
        Assert.InRange(prof5Cm.ScaleYMmPerPx, 0.405, 0.425);
        Assert.True(prof5Cm.MeanResidualErrorMm < 2.5, $"Mean residual error too high: {prof5Cm.MeanResidualErrorMm} mm");

        // Mode A2: 5.85x5.85 cm cell size (matches 117cm--20o.jpg / 5.85x-y.jpg: 20 rows * 5.85cm = 117cm)
        CalibrationProfile prof585 = calEngine.CalibrateModeAGrid(gridImg, cellWidthCm: 5.85, cellHeightCm: 5.85);
        Assert.Equal(117.0, prof585.ActiveAreaHeightCm, precision: 1);
        Assert.Equal(157.95, prof585.ActiveAreaWidthCm, precision: 2);
    }

    [Fact]
    public void CalibrationEngine_ModeBManualRectangle_AnisotropicScale_And_PersistenceRoundTrip()
    {
        var calEngine = new CalibrationEngine();
        var rectPx = new CropBoxPixels(200, 150, 2200, 1150); // Width = 2000 px, Height = 1000 px

        // Physical X = 100 cm (1000 mm), Physical Y = 60 cm (600 mm)
        // -> ScaleX = 0.50 mm/px, ScaleY = 0.60 mm/px (independent X/Y scale!)
        CalibrationProfile modeB = calEngine.CalibrateModeBManualRectangle(
            rectPx,
            physicalWidthCm: 100.0,
            physicalHeightCm: 60.0,
            imageWidthPx: 3456,
            imageHeightPx: 2592,
            restrictDetectionToRoi: true);

        Assert.True(calEngine.ValidateCalibration(modeB, out _));
        Assert.Equal(CalibrationMode.ManualRectangleModeB, modeB.Mode);
        Assert.Equal(0.50, modeB.ScaleXMmPerPx, precision: 6);
        Assert.Equal(0.60, modeB.ScaleYMmPerPx, precision: 6);
        Assert.True(modeB.RestrictDetectionToCropBox);

        // Verify a 400px x 300px piece inside the calibrated space gets size 200mm x 180mm (NOT forced to 100cm x 60cm!)
        PhysicalPointMm p0 = modeB.PixelToPhysicalMm(new ImagePixelPoint(500, 400));
        PhysicalPointMm p1 = modeB.PixelToPhysicalMm(new ImagePixelPoint(900, 700));
        Assert.Equal(200.0, p1.XMm - p0.XMm, precision: 5);
        Assert.Equal(180.0, p1.YMm - p0.YMm, precision: 5);

        // Reject invalid inputs
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calEngine.CalibrateModeBManualRectangle(rectPx, 0, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calEngine.CalibrateModeBManualRectangle(rectPx, -10, 50));

        // Persistence round-trip
        string tempCalPath = Path.Combine(Path.GetTempPath(), $"pdp_cal_{Guid.NewGuid():N}.json");
        try
        {
            calEngine.SaveToFile(modeB, tempCalPath);
            CalibrationProfile loaded = calEngine.LoadFromFile(tempCalPath);
            Assert.Equal(modeB.ScaleXMmPerPx, loaded.ScaleXMmPerPx, precision: 6);
            Assert.Equal(modeB.ScaleYMmPerPx, loaded.ScaleYMmPerPx, precision: 6);
            Assert.Equal(modeB.ActiveAreaWidthCm, loaded.ActiveAreaWidthCm, precision: 4);
            Assert.Equal(modeB.ActiveAreaHeightCm, loaded.ActiveAreaHeightCm, precision: 4);
        }
        finally
        {
            if (File.Exists(tempCalPath)) File.Delete(tempCalPath);
        }
    }

    // =======================================================================
    // MILESTONE 3: PATTERN DETECTION ON ALL 3 REAL PHOTOGRAPHS + DEBUG IMAGES
    // =======================================================================

    [Fact]
    public void DetectionEngine_RealPhotos_DetectsExactPieces_RejectsMagnetsAndGlare_Writes8DebugStages()
    {
        var imgEngine = new ImageEngine();
        var calEngine = new CalibrationEngine();
        var detEngine = new DetectionEngine(imgEngine);

        RawImageBuffer gridImg = imgEngine.LoadFromFile(Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg"));
        CalibrationProfile calProfile = calEngine.CalibrateModeAGrid(gridImg, 5.0, 5.0);

        // 1. Photo 1: sample_03172025_111128.jpg (3 white rectangular pieces with magnets)
        string p1Path = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        string dbg1Dir = Path.Combine(DebugArtifactsRoot, "sample_03172025_111128");
        DetectionPipelineResult res1 = detEngine.DetectPatterns(imgEngine.LoadFromFile(p1Path), calProfile, dbg1Dir);

        Assert.Equal(3, res1.Pieces.Count);
        Assert.Equal(8, res1.SavedDebugStageFiles.Count);
        foreach (PatternPiece piece in res1.Pieces)
        {
            Assert.Equal(4, piece.Vertices.Count);
            Assert.Equal(4, piece.GradedPointsCount);
            Assert.True(piece.AreaCm2 > 200.0 && piece.AreaCm2 < 850.0, $"Unexpected piece area: {piece.AreaCm2} cm^2");
        }

        // 2. Photo 2: real_01082026_091623.jpg (2 blue denim shorts pattern pieces + left glare + right magnets)
        string p2Path = Path.Combine(ReferenceAssetsDir, "real_01082026_091623.jpg");
        string dbg2Dir = Path.Combine(DebugArtifactsRoot, "real_01082026_091623");
        DetectionPipelineResult res2 = detEngine.DetectPatterns(imgEngine.LoadFromFile(p2Path), calProfile, dbg2Dir);

        Assert.Equal(2, res2.Pieces.Count);
        Assert.Equal(8, res2.SavedDebugStageFiles.Count);
        foreach (PatternPiece piece in res2.Pieces)
        {
            Assert.True(piece.Vertices.Count >= 4);
            Assert.True(piece.AreaCm2 > 150.0, $"Unexpected denim piece area: {piece.AreaCm2} cm^2");
        }

        // 3. Photo 3: real_01232026_093723.jpg (3 white paper pieces + 11 loose round magnets + yellow chair at bottom)
        string p3Path = Path.Combine(ReferenceAssetsDir, "real_01232026_093723.jpg");
        string dbg3Dir = Path.Combine(DebugArtifactsRoot, "real_01232026_093723");
        DetectionPipelineResult res3 = detEngine.DetectPatterns(imgEngine.LoadFromFile(p3Path), calProfile, dbg3Dir);

        Assert.Equal(3, res3.Pieces.Count);
        Assert.Equal(8, res3.SavedDebugStageFiles.Count);
        // Verify that loose round magnets and bottom chair were explicitly rejected!
        Assert.Contains(res3.RejectedComponents, r => r.Reason == "HoldingMagnet");
        Assert.Contains(res3.RejectedComponents, r => r.Reason == "BottomEdgeArtifact");
    }

    // =======================================================================
    // MILESTONE 4 & 5: GEOMETRY EDITING, DE CASTELJAU SPLIT, UNDO/REDO, MEASUREMENT & TOOLS
    // =======================================================================

    [Fact]
    public void EditingEngine_DeCasteljauSplit_PreservesCurveShape_And_UndoRedoRestoresExactState()
    {
        var meas = new MeasurementEngine();
        var geom = new GeometryEngine();
        var edit = new EditingEngine(geom, meas);
        var cal = CalibrationProfile.CreateDefaultV9GridProfile();

        // Create a 100mm x 100mm square piece (= 100.0 cm^2, 40.0 cm perimeter)
        PatternPiece piece = edit.CreateAuxiliaryRectanglePiece(1, new PhysicalPointMm(0, 0), 100.0, 100.0, cal);
        Assert.Equal(400.0, piece.PerimeterMm, precision: 5);
        Assert.Equal(10000.0, piece.AreaMm2, precision: 5);
        Assert.Equal(90.0, meas.ComputeVertexAngleDegrees(piece, 0), precision: 4);

        var pieces = new List<PatternPiece> { piece };

        // 1. Convert top edge (segment 0) from Line to Curve and bow its handles outward
        edit.PushUndoSnapshot(pieces, "Line to Curve");
        edit.ConvertLineToCurve(piece, 0);
        edit.MoveBezierControlHandle(piece, 0, isStartHandleC1: true, new PhysicalPointMm(33.0, -30.0));
        edit.MoveBezierControlHandle(piece, 0, isStartHandleC1: false, new PhysicalPointMm(67.0, -30.0));

        double curvedPerimBeforeSplit = piece.PerimeterMm;
        double curvedAreaBeforeSplit = piece.AreaMm2;
        Assert.True(curvedPerimBeforeSplit > 400.0);
        Assert.True(curvedAreaBeforeSplit > 10000.0);

        // 2. Add Point on the curved segment (t = 0.42) using De Casteljau subdivision
        edit.PushUndoSnapshot(pieces, "Add Point on Curve");
        int newIdx = edit.AddPointOnSegment(piece, segmentIndex: 0, parameterT: 0.42, isGradedCorner: false, cal);
        Assert.Equal(1, newIdx);
        Assert.Equal(5, piece.Vertices.Count);

        // De Casteljau split MUST preserve exact curve perimeter and area!
        Assert.Equal(curvedPerimBeforeSplit, piece.PerimeterMm, precision: 2);
        Assert.Equal(curvedAreaBeforeSplit, piece.AreaMm2, precision: 1);

        // 3. Move vertex and verify attached Bezier handles translate by the exact same delta (MiscaPuncteSimple)
        edit.PushUndoSnapshot(pieces, "Move Vertex");
        PhysicalPointMm oldIn = piece.Vertices[newIdx].InControlHandleMm;
        PhysicalPointMm oldOut = piece.Vertices[newIdx].OutControlHandleMm;
        edit.MoveVertexByDelta(piece, newIdx, 12.5, -8.0, cal);
        Assert.Equal(oldIn.XMm + 12.5, piece.Vertices[newIdx].InControlHandleMm.XMm, precision: 5);
        Assert.Equal(oldIn.YMm - 8.0, piece.Vertices[newIdx].InControlHandleMm.YMm, precision: 5);
        Assert.Equal(oldOut.XMm + 12.5, piece.Vertices[newIdx].OutControlHandleMm.XMm, precision: 5);
        Assert.Equal(oldOut.YMm - 8.0, piece.Vertices[newIdx].OutControlHandleMm.YMm, precision: 5);

        // 4. Add Notch, Internal Point, and Grain Line
        edit.PushUndoSnapshot(pieces, "Add Pattern Features");
        NotchItem notch = edit.AddNotch(piece, new PhysicalPointMm(100.0, 45.0), lengthMm: 6.0, angleDegrees: 90.0);
        Assert.Single(piece.Notches);
        Assert.Equal(45.0, notch.DistanceFromPrevGradedPointMm, precision: 2);

        InternalPointItem drill = edit.AddInternalPoint(piece, new PhysicalPointMm(50.0, 50.0), lengthMm: 5.0);
        Assert.Single(piece.InternalPoints);

        InternalLineItem grain = edit.AddInternalLine(
            piece,
            new PhysicalPointMm(20.0, 50.0),
            new PhysicalPointMm(80.0, 50.0),
            InternalLineType.GrainAxis);
        Assert.NotNull(piece.GrainLine);
        Assert.Equal(60.0, grain.LengthMm, precision: 5);

        // 5. Undo and Redo verification
        var undo1 = edit.Undo(pieces);
        Assert.True(undo1.Success);
        pieces = undo1.RestoredPieces;
        Assert.Empty(pieces[0].Notches);

        var redo1 = edit.Redo(pieces);
        Assert.True(redo1.Success);
        pieces = redo1.RestoredPieces;
        Assert.Single(pieces[0].Notches);
        Assert.Single(pieces[0].InternalPoints);
        Assert.NotNull(pieces[0].GrainLine);
    }

    // =======================================================================
    // MILESTONE 6 & 7: END-TO-END WORKFLOW, PROJECT SAVE/OPEN & DXF ACCURACY VALIDATION
    // =======================================================================

    [Fact]
    public void EndToEndWorkflow_OpenCalibrateDetectEditMeasureSaveReopenExportDxf_ValidatesWithParser()
    {
        var imgEngine = new ImageEngine();
        var calEngine = new CalibrationEngine();
        var geomEngine = new GeometryEngine();
        var measEngine = new MeasurementEngine();
        var detEngine = new DetectionEngine(imgEngine, geomEngine, measEngine);
        var editEngine = new EditingEngine(geomEngine, measEngine);
        var fileEngine = new FileEngine(measEngine);

        // Step 1 & 2: Load calibration grid photo & calibrate Mode A (5x5 cm)
        RawImageBuffer gridImg = imgEngine.LoadFromFile(Path.Combine(ReferenceAssetsDir, "cal_grid_117cm_20o.jpg"));
        CalibrationProfile calibration = calEngine.CalibrateModeAGrid(gridImg, 5.0, 5.0);
        Assert.True(calibration.IsValid);

        // Step 3: Load pattern photo & detect patterns
        string photoPath = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        RawImageBuffer patternImg = imgEngine.LoadFromFile(photoPath);
        DetectionPipelineResult detected = detEngine.DetectPatterns(patternImg, calibration);
        Assert.Equal(3, detected.Pieces.Count);

        // Step 4 & 5: Select Piece 1, add a Notch, Internal Point, and Grain Line, and measure contour distance
        PatternPiece piece1 = detected.Pieces[0];
        Rect bbox1 = piece1.BoundingBoxMm;
        PhysicalPointMm center1 = new(bbox1.X + (bbox1.Width * 0.5), bbox1.Y + (bbox1.Height * 0.5));

        editEngine.AddNotch(piece1, piece1.Vertices[0].PositionMm.Lerp(piece1.Vertices[1].PositionMm, 0.5), 6.0, 90.0);
        editEngine.AddInternalPoint(piece1, center1, 5.0);
        editEngine.AddInternalLine(
            piece1,
            new PhysicalPointMm(center1.XMm - 40.0, center1.YMm),
            new PhysicalPointMm(center1.XMm + 40.0, center1.YMm),
            InternalLineType.GrainAxis);

        var (shortestDistMm, _, _) = measEngine.ComputeContourDistanceBetweenVerticesMm(piece1, 0, 1);
        double directEdgeMm = measEngine.ComputeStraightDistanceMm(piece1.Vertices[0].PositionMm, piece1.Vertices[1].PositionMm);
        Assert.Equal(directEdgeMm, shortestDistMm, precision: 3);

        // Step 6: Save Project (.pdp) and Reopen Project
        Directory.CreateDirectory(DxfArtifactsRoot);
        string projectPath = Path.Combine(DxfArtifactsRoot, "e2e_validated_project.pdp");
        var projectDoc = new DigitizerProjectDocument
        {
            ImageFilePath = photoPath,
            ImageWidthPx = patternImg.Width,
            ImageHeightPx = patternImg.Height,
            Calibration = calibration,
            Pieces = detected.Pieces
        };

        fileEngine.SaveProject(projectDoc, projectPath);
        DigitizerProjectDocument reopenedDoc = fileEngine.LoadProject(projectPath);

        Assert.Equal(3, reopenedDoc.Pieces.Count);
        Assert.Single(reopenedDoc.Pieces[0].Notches);
        Assert.Single(reopenedDoc.Pieces[0].InternalPoints);
        Assert.NotNull(reopenedDoc.Pieces[0].GrainLine);
        Assert.Equal(piece1.AreaMm2, reopenedDoc.Pieces[0].AreaMm2, precision: 4);
        Assert.Equal(piece1.PerimeterMm, reopenedDoc.Pieces[0].PerimeterMm, precision: 4);

        // Step 7: Export DXF and run independent DXF Parser Validator
        string dxfPath = Path.Combine(DxfArtifactsRoot, "e2e_validated_export.dxf");
        fileEngine.ExportDxf(reopenedDoc.Pieces, dxfPath, profileName: "Gemini Cut Plan");

        DxfValidationReport dxfReport = fileEngine.ParseAndValidateDxfFile(dxfPath, reopenedDoc.Pieces);
        Assert.True(dxfReport.IsValid, string.Join(" | ", dxfReport.Errors));
        Assert.Equal(4, dxfReport.InsUnitsCode); // 4 = Millimeters
        Assert.Equal(3, dxfReport.ParsedPieces.Count);
        Assert.Equal(1, dxfReport.NotchLineCount);
        Assert.Equal(1, dxfReport.GrainLineCount);
        Assert.Equal(1, dxfReport.DrillPointCount);
        Assert.True(dxfReport.MaxDimensionErrorMm < 0.05, $"DXF dimension error {dxfReport.MaxDimensionErrorMm} mm >= 0.05 mm");
        Assert.True(dxfReport.MaxPerimeterErrorMm < 0.05, $"DXF perimeter error {dxfReport.MaxPerimeterErrorMm} mm >= 0.05 mm");
    }

    [Fact]
    public void MainWindowViewModel_UiIntegration_AllToolsAndUndoRedoAndProjectDxfRoundTrip()
    {
        var vm = new PatternDigitizerPro.UI.ViewModels.MainWindowViewModel();

        // 1. Load real image
        string photoPath = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        Assert.True(vm.LoadImageFromFile(photoPath));
        Assert.True(vm.HasImageLoaded);

        // 2. Run real pattern detection
        DetectionPipelineResult? det = vm.RunDetectPatterns();
        Assert.NotNull(det);
        Assert.Equal(3, vm.DomainPieces.Count);
        Assert.Equal(3, vm.DetectedPiecesCount);
        Assert.Equal(3, vm.StockPieces.Count);

        // 3. Select Piece 1 and test editing tools through ViewModel commands
        vm.SelectDomainPiece(vm.DomainPieces[0], 0);
        Assert.True(vm.IsPieceSelected);
        Assert.Equal("Piece 1", vm.PieceName);

        // Add Point -> Line to Curve -> Curve to Line -> Smart Corner -> Delete Corner -> Delete Point
        vm.ExecuteAddPointOnActiveSegment();
        Assert.Equal(5, vm.SelectedDomainPiece!.Vertices.Count);

        vm.ExecuteLineToCurve();
        Assert.True(vm.SelectedDomainPiece.Vertices[vm.SelectedSegmentIndex].OutgoingIsCurve);

        vm.ExecuteCurveToLine();
        Assert.False(vm.SelectedDomainPiece.Vertices[vm.SelectedSegmentIndex].OutgoingIsCurve);

        vm.ExecutePromoteToCorner();
        Assert.True(vm.SelectedDomainPiece.Vertices[vm.SelectedVertexIndex].IsGradedCorner);

        vm.ExecuteDemoteCornerToCurvePoint();
        Assert.False(vm.SelectedDomainPiece.Vertices[vm.SelectedVertexIndex].IsGradedCorner);

        vm.ExecuteDeleteSelectedPoint();
        Assert.Equal(4, vm.SelectedDomainPiece.Vertices.Count);

        // 4. Add Notch, Internal Point, and Grain Axis Line
        vm.ExecuteAddNotch();
        Assert.Single(vm.SelectedDomainPiece.Notches);

        vm.ExecuteAddInternalPoint();
        Assert.Single(vm.SelectedDomainPiece.InternalPoints);

        vm.InternalLineTypeIndex = 2; // Grain axis (Gemini dock order: drawing, cutting, grain, symmetry)
        vm.ExecuteAddInternalLine();
        Assert.Single(vm.SelectedDomainPiece.InternalLines);
        Assert.NotNull(vm.SelectedDomainPiece.GrainLine);
        vm.ExecuteFlipInternalLine();

        // Playback dropdowns must select/cycle real objects, not merely switch docks.
        vm.StepPlayNextPoint(PropertiesDockMode.Notch);
        Assert.Equal(PropertiesDockMode.Notch, vm.ActivePropertiesDockMode);
        Assert.Equal(vm.SelectedDomainPiece.Notches[0].Id, vm.SelectedNotchId);
        vm.StepPlayNextPoint(PropertiesDockMode.InternalPoint);
        Assert.Equal(PropertiesDockMode.InternalPoint, vm.ActivePropertiesDockMode);
        Assert.Equal(vm.SelectedDomainPiece.InternalPoints[0].Id, vm.SelectedInternalPointId);
        vm.StepPlayNextPoint(PropertiesDockMode.InternalLine);
        Assert.Equal(PropertiesDockMode.InternalLine, vm.ActivePropertiesDockMode);
        Assert.Equal(vm.SelectedDomainPiece.InternalLines[0].Id, vm.SelectedInternalLineId);

        foreach (ContourVertex vertex in vm.SelectedDomainPiece.Vertices)
        {
            vertex.IsGradedCorner = false;
        }
        vm.SelectedDomainPiece.Vertices[0].IsGradedCorner = true;
        vm.SelectedDomainPiece.Vertices[2].IsGradedCorner = true;
        vm.SelectDomainPiece(vm.SelectedDomainPiece, 1);
        vm.StepPlayNextGradedPoint();
        Assert.Equal(0, vm.SelectedVertexIndex);
        vm.StepPlayNextGradedPoint();
        Assert.Equal(2, vm.SelectedVertexIndex);

        // 5. Undo and Redo via ViewModel
        vm.ExecuteUndo();
        vm.ExecuteRedo();
        Assert.Single(vm.SelectedDomainPiece.InternalLines);

        // 6. Save Project -> Reopen Project -> Export DXF via ViewModel
        Directory.CreateDirectory(DxfArtifactsRoot);
        string vmProjPath = Path.Combine(DxfArtifactsRoot, "vm_ui_roundtrip.pdp");
        string vmDxfPath = Path.Combine(DxfArtifactsRoot, "vm_ui_export.dxf");

        vm.SaveProjectToFile(vmProjPath);
        Assert.True(vm.LoadProjectFromFile(vmProjPath));
        Assert.Equal(3, vm.DomainPieces.Count);

        DxfValidationReport dxfReport = vm.ExportDxfToFile(vmDxfPath, "Gerber Accumark");
        Assert.True(dxfReport.IsValid, string.Join(" | ", dxfReport.Errors));
        Assert.Equal(3, dxfReport.ParsedPieces.Count);
    }

    [Fact]
    public void CurrentSelection_DeleteTargetsOnlyTheVisibleSelectedObject_AndClearsStaleMultiPointSelection()
    {
        var vm = new PatternDigitizerPro.UI.ViewModels.MainWindowViewModel();
        PatternPiece piece = vm.EditingEngine.CreateAuxiliaryRectanglePiece(
            1, new PhysicalPointMm(0, 0), 200, 150, vm.ActiveCalibration);
        vm.DomainPieces.Add(piece);
        vm.SelectDomainPiece(piece, 0);

        vm.ExecuteAddNotch();
        vm.ExecuteAddInternalPoint();
        vm.ExecuteAddInternalLine();
        Assert.Single(piece.Notches);
        Assert.Single(piece.InternalPoints);
        Assert.Single(piece.InternalLines);
        Assert.Equal(4, piece.Vertices.Count);

        vm.SelectMultipleVertices(piece, [0, 1]);
        vm.SelectDomainPiece(piece, 2);
        Assert.Empty(vm.SelectedVertexIndices);

        vm.SelectMultipleVertices(piece, [0, 1]);
        vm.SelectNotchOnPiece(piece, piece.Notches[0].Id);
        Assert.Empty(vm.SelectedVertexIndices);
        Assert.Equal(-1, vm.SelectedVertexIndex);
        vm.ExecuteDeleteCurrentSelection();
        Assert.Empty(piece.Notches);
        Assert.Equal(4, piece.Vertices.Count);
        Assert.Single(piece.InternalPoints);
        Assert.Single(piece.InternalLines);

        vm.SelectInternalPointOnPiece(piece, piece.InternalPoints[0].Id);
        vm.ExecuteDeleteCurrentSelection();
        Assert.Empty(piece.InternalPoints);
        Assert.Equal(4, piece.Vertices.Count);
        Assert.Single(piece.InternalLines);

        vm.SelectInternalLineOnPiece(piece, piece.InternalLines[0].Id);
        vm.ExecuteDeleteCurrentSelection();
        Assert.Empty(piece.InternalLines);
        Assert.Equal(4, piece.Vertices.Count);

        vm.SelectMultipleVertices(piece, [0, 1]);
        vm.ExecuteDeleteCurrentSelection();
        Assert.Equal(3, piece.Vertices.Count);
        Assert.Contains("Deleted 1 points", vm.StatusLeftText, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadProject_WithMissingSourceImage_ClearsPreviousRasterInsteadOfMixingProjects()
    {
        var vm = new PatternDigitizerPro.UI.ViewModels.MainWindowViewModel();
        string existingPhoto = Path.Combine(ReferenceAssetsDir, "sample_03172025_111128.jpg");
        Assert.True(vm.LoadImageFromFile(existingPhoto));
        Assert.True(vm.HasImageLoaded);

        string projectPath = Path.Combine(Path.GetTempPath(), $"pdp_missing_image_{Guid.NewGuid():N}.pdp");
        string missingImagePath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png");
        PatternPiece piece = vm.EditingEngine.CreateAuxiliaryRectanglePiece(
            1, new PhysicalPointMm(10, 20), 200, 150, vm.ActiveCalibration);
        var document = new DigitizerProjectDocument
        {
            ImageFilePath = missingImagePath,
            ImageWidthPx = 800,
            ImageHeightPx = 600,
            Calibration = vm.ActiveCalibration.Clone(),
            Pieces = [piece]
        };

        try
        {
            vm.FileEngine.SaveProject(document, projectPath);
            Assert.True(vm.LoadProjectFromFile(projectPath));
            Assert.False(vm.HasImageLoaded);
            Assert.Null(vm.LoadedBitmap);
            Assert.Null(vm.CurrentRawImage);
            Assert.Equal(missingImagePath, vm.LoadedImagePath);
            Assert.Single(vm.DomainPieces);
            Assert.Equal(piece.Id, vm.DomainPieces[0].Id);
            Assert.Contains("source image is unavailable", vm.StatusLeftText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(projectPath))
            {
                File.Delete(projectPath);
            }
        }
    }

    [Fact]
    public void DxfExport_HighCurvatureLargeBezier_MeetsPhysicalPerimeterTolerance()
    {
        var measurement = new MeasurementEngine();
        var editing = new EditingEngine(new GeometryEngine(), measurement);
        var files = new FileEngine(measurement);
        PatternPiece piece = editing.CreateAuxiliaryRectanglePiece(
            1, new PhysicalPointMm(0, 0), 1000, 500, CalibrationProfile.CreateDefaultV9GridProfile());
        editing.ConvertLineToCurve(piece, 0);
        editing.MoveBezierControlHandle(piece, 0, true, new PhysicalPointMm(0, 1000));
        editing.MoveBezierControlHandle(piece, 0, false, new PhysicalPointMm(1000, -1000));
        measurement.RecalculatePieceMetrics(piece);

        string dxfPath = Path.Combine(Path.GetTempPath(), $"pdp_high_curve_{Guid.NewGuid():N}.dxf");
        try
        {
            files.ExportDxf([piece], dxfPath);
            DxfValidationReport report = files.ParseAndValidateDxfFile(dxfPath, [piece]);
            Assert.True(report.IsValid, string.Join(" | ", report.Errors));
            Assert.InRange(report.MaxPerimeterErrorMm, 0, 0.05);
            Assert.InRange(report.MaxDimensionErrorMm, 0, 0.05);
        }
        finally
        {
            if (File.Exists(dxfPath))
            {
                File.Delete(dxfPath);
            }
        }
    }

    [Fact]
    public void AamaDxfExport_WritesObservedReferenceTablesAndMetadata()
    {
        var measurement = new MeasurementEngine();
        var editing = new EditingEngine(new GeometryEngine(), measurement);
        var files = new FileEngine(measurement);
        PatternPiece piece = editing.CreateAuxiliaryRectanglePiece(
            1, new PhysicalPointMm(0, 0), 120, 80, CalibrationProfile.CreateDefaultV9GridProfile());
        editing.ConvertLineToCurve(piece, 0);
        editing.MoveBezierControlHandle(piece, 0, true, new PhysicalPointMm(30, -40));
        editing.MoveBezierControlHandle(piece, 0, false, new PhysicalPointMm(90, -40));
        editing.AddNotch(piece, new PhysicalPointMm(60, 0), lengthMm: 7, angleDegrees: 35, isRelativeAngle: false);
        editing.AddInternalLine(piece, new PhysicalPointMm(20, 20), new PhysicalPointMm(100, 20));
        editing.AddInternalPoint(piece, new PhysicalPointMm(60, 40), lengthMm: 4);
        string dxfPath = Path.Combine(Path.GetTempPath(), $"pdp_aama_{Guid.NewGuid():N}.dxf");

        try
        {
            AamaDxfExportResult result = files.ExportAamaDxf([piece], dxfPath);
            Assert.Equal(dxfPath, result.DxfPath);
            Assert.True(File.Exists(dxfPath));
            string dxf = File.ReadAllText(dxfPath);
            Assert.Contains("\r\nSTYLE\r\n2\r\nSTANDARD\r\n", dxf);
            Assert.Contains("\r\nLAYER\r\n2\r\n19\r\n", dxf);
            Assert.Contains("\r\nBLOCKS\r\n", dxf);
            Assert.Contains("\r\nENTITIES\r\n", dxf);
            Assert.DoesNotContain("EXCHANGE PROFILE:", dxf);
            Assert.Contains("\r\nTEXT\r\n8\r\n4\r\n", dxf);
            Assert.Contains("\r\nPOINT\r\n8\r\n4\r\n", dxf);
            Assert.Contains("\r\n30\r\n15\r\n50\r\n35\r\n", dxf);
            Assert.DoesNotContain("\r\nLINE\r\n8\r\n4\r\n", dxf);
            Assert.Contains("\r\nLINE\r\n8\r\n8\r\n", dxf);
            Assert.Contains("\r\nPOINT\r\n8\r\n13\r\n", dxf);

            int polylineStart = dxf.IndexOf("\r\nPOLYLINE\r\n", StringComparison.Ordinal);
            int sequenceEnd = dxf.IndexOf("\r\nSEQEND\r\n", polylineStart, StringComparison.Ordinal);
            Assert.True(polylineStart >= 0 && sequenceEnd > polylineStart);
            string perimeterPayload = dxf[polylineStart..sequenceEnd];
            Assert.DoesNotContain("\r\nPOINT\r\n", perimeterPayload);
            Assert.DoesNotContain("\r\nTEXT\r\n", perimeterPayload);
            Assert.True(perimeterPayload.Split("\r\nVERTEX\r\n", StringSplitOptions.None).Length - 1 > piece.Vertices.Count,
                "A curved AAMA perimeter must contain sampled vertices, not only the original curve endpoints.");

        }
        finally
        {
            if (File.Exists(dxfPath)) File.Delete(dxfPath);
        }
    }

    [Fact]
    public void RotatedFramePixels_PreservesRigidLocalDimensionsAndImageContainment()
    {
        var frame = new RotatedFramePixels(500, 400, 300, 120, 27);

        Assert.True(frame.IsValid);
        Assert.True(frame.Contains(500, 400));
        Assert.False(frame.Contains(100, 100));
        Assert.True(frame.IsInsideImage(1200, 900));

        IReadOnlyList<ImagePixelPoint> corners = frame.Corners;
        Assert.Equal(4, corners.Count);
        Assert.Equal(300, corners[0].DistanceTo(corners[1]), precision: 8);
        Assert.Equal(120, corners[1].DistanceTo(corners[2]), precision: 8);
        Assert.True(frame.BoundingBox.Width > 300);
        Assert.True(frame.BoundingBox.Height > 120);
    }

    [Fact]
    public void MoveVertexByDelta_ChangesOnlyTheSelectedVertexAndItsOwnHandles()
    {
        var measurement = new MeasurementEngine();
        var editing = new EditingEngine(new GeometryEngine(), measurement);
        PatternPiece piece = editing.CreateAuxiliaryRectanglePiece(
            1, new PhysicalPointMm(0, 0), 100, 60, CalibrationProfile.CreateDefaultV9GridProfile());
        ContourVertex unaffected = piece.Vertices[2].Clone();
        ContourVertex movedBefore = piece.Vertices[1].Clone();

        editing.MoveVertexByDelta(piece, 1, 15, -7);

        Assert.Equal(unaffected.PositionMm, piece.Vertices[2].PositionMm);
        Assert.Equal(unaffected.InControlHandleMm, piece.Vertices[2].InControlHandleMm);
        Assert.Equal(unaffected.OutControlHandleMm, piece.Vertices[2].OutControlHandleMm);
        Assert.Equal(movedBefore.PositionMm.XMm + 15, piece.Vertices[1].PositionMm.XMm, precision: 8);
        Assert.Equal(movedBefore.PositionMm.YMm - 7, piece.Vertices[1].PositionMm.YMm, precision: 8);
    }

    [Fact]
    public void InternalCurve_ExportsAsLayer8PolylineSegments()
    {
        var measurement = new MeasurementEngine();
        var editing = new EditingEngine(new GeometryEngine(), measurement);
        PatternPiece piece = editing.CreateAuxiliaryRectanglePiece(
            1, new PhysicalPointMm(0, 0), 100, 60, CalibrationProfile.CreateDefaultV9GridProfile());
        editing.AddInternalCurve(piece, new PhysicalPointMm(10, 10), new PhysicalPointMm(50, 45), new PhysicalPointMm(90, 10));
        string output = Path.Combine(Path.GetTempPath(), $"internal_curve_{Guid.NewGuid():N}.dxf");
        try
        {
            new FileEngine(measurement).ExportDxf([piece], output);
            string dxf = File.ReadAllText(output);
            int layerEightLines = dxf.Split("\r\nLINE\r\n8\r\n8\r\n", StringSplitOptions.None).Length - 1;
            Assert.True(layerEightLines >= 16, "A curved internal line must be tessellated into editable DXF layer-8 segments.");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

}

