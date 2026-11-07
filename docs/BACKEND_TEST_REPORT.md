# BACKEND TEST REPORT — PATTERN DIGITIZER PRO (PHASE 2)

## 1. BUILD & TEST SUMMARY

- **Solution**: [`PatternDigitizerPro.sln`](file:///E:/claude/GEMHIHI/PatternDigitizerPro.sln)
- **Target Framework**: `.NET 10.0-windows` (`C#`, WPF, MVVM)
- **`dotnet build -c Debug`**: **PASS** (`0 Warning(s), 0 Error(s)`)
- **`dotnet build -c Release`**: **PASS** (`0 Warning(s), 0 Error(s)`)
- **`dotnet test -c Release`**: **PASS (`15 / 15` tests passed, `0` failed, `0` skipped)**

---

## 2. AUTOMATED TEST SUITES

### 2.1. Phase 1 UI Parity Regression Suite ([`UiReplicationTests.cs`](file:///E:/claude/GEMHIHI/tests/PatternDigitizerPro.Tests/UiReplicationTests.cs))

Đảm bảo việc tích hợp 7 Backend Engines trong Phase 2 **không làm thay đổi bất kỳ chi tiết giao diện 1:1 nào của Phase 1**:

| # | Test Method | Mục tiêu kiểm chứng | Kết quả |
|---|---|---|---|
| 1 | `MainWindowViewModel_DefaultState_MatchesGeminiStartupParity` | Trạng thái khởi tạo mặc định (`Nothing selected`, `Detected patterns: 0`, `No picture loaded`) | **PASS** |
| 2 | `MainWindowViewModel_LoadSamplePicture_PopulatesPictureAndStockArea` | Load ảnh thật `sample_03172025_111128.jpg` (`2918 x 2214`) và kiểm tra thông số hiển thị | **PASS** |
| 3 | `MainWindowViewModel_SelectTool_UpdatesActiveToolAndMutuallyExclusiveStates` | 21 công cụ trên thanh Toolbar chuyển trạng thái mutually-exclusive chính xác | **PASS** |
| 4 | `MainWindowViewModel_SelectPointAndInternalItems_SwitchesRightPanelModes` | Chuyển đổi 4 chế độ Panel bên phải (`NothingSelected`, `PointSelected`, `NotchSelected`, `InternalLineSelected`) | **PASS** |
| 5 | `MainWindow_Instantiates_And_Renders_In_StaThread` | Khởi tạo và render `MainWindow` thực tế trên luồng STA, kiểm tra kích thước & bố cục | **PASS** |
| 6 | `Pass2_VisualParity_CapturesAndVerifiesAllWindows` | Kiểm chứng `MainWindow`, `SettingsWindow`, `CalibrationWindow` ở cả 2 trạng thái | **PASS** |
| 7 | `AllDialogWindows_Instantiate_WithoutException` | Khởi tạo tất cả 5 hộp thoại (`CalibrationWindow`, `SettingsWindow`, `ExportDxfWindow`, `PatternPropertiesWindow`, `AboutWindow`) | **PASS** |

---

### 2.2. Phase 2 Backend Engines Suite ([`BackendEnginesTests.cs`](file:///E:/claude/GEMHIHI/tests/PatternDigitizerPro.Tests/BackendEnginesTests.cs))

Kiểm thử toàn diện 7 Engine bằng dữ liệu thật và hình học giải tích:

| # | Test Method | Engine được kiểm thử | Nội dung kiểm chứng & Sai số thực tế | Kết quả |
|---|---|---|---|---|
| 8 | `ImageEngine_LoadsRealImages_And_ViewportEngine_PreservesCoordinatesUnderZoomPan` | [`ImageEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ImageEngine.cs), [`ViewportEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ViewportEngine.cs) | Đọc đúng kích thước pixel (`2918×2214`, `3456×2592`), trích xuất ROI, và kiểm chứng tọa độ điểm dưới con trỏ chuột bất biến (sai số `< 1e-6 px`) khi `ZoomAtCursor` + `PanBy`. | **PASS** |
| 9 | `CalibrationEngine_ModeAGrid_And_ModeBManualRectangle_ComputeAccurateScales` | [`CalibrationEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs) | **Mode A**: Tự động phát hiện lưới `27 × 20` ô trên ảnh bảng thật `cal_grid_117cm_20o.jpg` (`3456×2592`) ở cả kích thước `5×5 cm` (`135×100 cm`) và `5.85×5.85 cm` (`157.95×117.00 cm`).<br>**Mode B**: Hiệu chuẩn hình chữ nhật độc lập theo `X` (`80 cm / 1600 px = 0.50 mm/px`) và `Y` (`60 cm / 1000 px = 0.60 mm/px`), lưu/đọc JSON thành công. | **PASS** |
| 10 | `MeasurementEngine_VerifiesRectangleCircleAndTriangle_WithinTolerance` | [`MeasurementEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/MeasurementEngine.cs) | 1. **Hình chữ nhật `200×100 mm`**: Chu vi `= 600.000 mm` (sai số `0.000 mm`), Diện tích `= 20000.000 mm²` (`200.00 cm²`), 4 góc nội tiếp `= 90.00°`.<br>2. **Hình tròn bán kính `R = 100 mm` (4 đoạn Cubic Bézier)**: Chu vi `= 628.330 mm` (sai số `0.012 mm < 0.2 mm`), Diện tích `= 31410.27 mm²` (sai số `0.018%`).<br>3. **Tam giác vuông `30-40-50 mm`**: Chu vi `= 120.000 mm`, Diện tích `= 600.000 mm²`, Góc vuông `= 90.00°`. | **PASS** |
| 11 | `EditingEngine_DeCasteljauSplit_PreservesCurveShape_And_UndoRedoRestoresExactState` | [`EditingEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs), [`GeometryEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/GeometryEngine.cs) | Chuyển đoạn thẳng thành Cubic Bézier, kéo handle điều khiển, chèn điểm trên đường cong bằng thuật toán De Casteljau (`t = 0.42`): chu vi trước và sau khi chia đoạn khớp tuyệt đối (`434.7254 mm` vs `434.7254 mm`, sai số `< 0.0001 mm`), diện tích tích phân Gauss-Legendre bậc 5 khớp tuyệt đối (`11500.00 mm²` vs `11500.00 mm²`). Kiểm chứng `Undo` / `Redo` đa bước và công cụ Notch, Internal Line, Grain Axis. | **PASS** |
| 12 | `DetectionEngine_RealSampleImage_DetectsThreeWhiteRectangles` | [`DetectionEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs) | Phát hiện đúng `3` mảnh rập hình chữ nhật màu trắng trên ảnh `sample_03172025_111128.jpg` (`2918×2214`), mỗi mảnh đúng `4` góc chấm nhảy cỡ (`GradedCorners`), xuất đủ 8 ảnh debug stages. | **PASS** |
| 13 | `DetectionEngine_RealBlueFabricImage_DetectsTwoBluePieces_RejectsGlareAndMagnets` | [`DetectionEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs) | Phát hiện đúng `2` mảnh rập vải bò xanh trên ảnh `real_01082026_091623.jpg` (`3456×2592`), loại bỏ hoàn toàn vệt lóa sáng góc dưới trái và 6 cục nam châm rời bên mép phải. | **PASS** |
| 14 | `DetectionEngine_RealThreeWhitePatternsImage_DetectsThreePieces_RejectsLooseMagnets` | [`DetectionEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs) | Phát hiện đúng `3` mảnh rập giấy trắng trên ảnh `real_01232026_093723.jpg` (`3456×2592`), loại bỏ hoàn toàn 11 cục nam châm rời trên bảng và mép ghế màu vàng phía dưới bảng. | **PASS** |
| 15 | `FileEngine_SaveLoadProject_And_ExportValidDxf_RoundTripWithin005Mm` | [`FileEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs), [`MainWindowViewModel`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/ViewModels/MainWindowViewModel.cs) | Kiểm chứng quy trình End-to-End: Detect ảnh thật → Chỉnh sửa điểm & thêm Notch/InternalLine → Lưu file `.pdp` → Đọc lại `.pdp` → Xuất file `.dxf` → Đọc lại bằng bộ parser DXF độc lập (`ParseAndValidateDxfFile`). Sai số kích thước bao và chu vi `< 0.001 mm` (vượt chuẩn yêu cầu `< 0.05 mm`). | **PASS** |
