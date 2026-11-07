# PHASE 2 — BACKEND ACCEPTANCE AUDIT REPORT

- **Dự án**: `Pattern Digitizer Pro` ([`PatternDigitizerPro.sln`](file:///E:/claude/GEMHIHI/PatternDigitizerPro.sln))
- **Ứng dụng tham chiếu**: `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\GeminiPhotoDigitizer.exe` (`v9.0.0.0`, SHA-256 `fd057769b092a439c69a383757652ddbb2817fe4e48ccc120ae4ff3c839b9f92`)
- **Ngày kiểm toán**: `2026-10-29`
- **Phạm vi**: Kiểm toán độc lập toàn bộ Backend Phase 2 và khả năng tương tác thực tế trên UI (`Release` / `Debug`), **không sửa đổi giao diện Phase 1 và không tự ý tuyên bố hoàn thành 100%**.

---

## 1. TỔNG QUAN KẾT QUẢ KIỂM TOÁN (EXECUTIVE SUMMARY)

| Hạng mục | Kết quả kiểm toán thực tế | Đánh giá nghiệm thu |
|---|---|---|
| **Giao diện Phase 1 (UI / Toolbar / Icons / Dialogs)** | Giữ nguyên 100% bố cục, kích thước, thứ tự 21 công cụ và 5 hộp thoại đã duyệt ở Phase 1 (`7/7` bài test `UiReplicationTests` PASS). | **PASS (UI Shell)** |
| **Toán học cốt lõi trong `PatternDigitizerPro.Core`** | Tích phân diện tích Green + Gauss-Legendre 5 điểm, chia đoạn Cubic Bézier bằng De Casteljau, dịch chuyển điểm kèm tay nắm Bézier (`MiscaPuncteSimple`), lưu/mở `.pdp`, xuất `.dxf` (`$INSUNITS=4`) đều hoạt động đúng trong bộ nhớ (`8/8` bài test `BackendEnginesTests` PASS). | **PASS (In-Memory Core Math)** |
| **Hiển thị Overlay trên `MainWindow` khi mở ảnh Camera thật (`180 DPI`)** | **PHÁT HIỆN LỖI NGHIÊM TRỌNG (`CRITICAL BUG`)**: [`RawImageBuffer.ToBitmapSource()`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ImageEngine.cs#L60-L73) giữ nguyên `DpiX/DpiY = 180` của ảnh JPEG máy ảnh, khiến `<Image x:Name="WorkspaceImageControl" Stretch="None" />` trên [`MainWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/MainWindow.xaml#L715-L717) hiển thị ảnh nền nhỏ bằng $96 / 180 = 53.33\%$ (`1843.2 × 1382.4 DIP`), trong khi `InteractiveVectorOverlayCanvas` vẽ contour ở tọa độ `3456 × 2592 DIP`. Kết quả: **trên cửa sổ ứng dụng thực tế, đường viền rập bị phóng to gấp $1.875\times$ và lệch hoàn toàn khỏi ảnh nền** (xem [`ui_2_detected_093723.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_2_detected_093723.png) và [`ui_4_detected_091623.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_4_detected_091623.png)). | **FAIL (Cần sửa ngay)** |
| **Tương tác chuột với các công cụ trên Toolbar (`MainWindow.xaml.cs`)** | Nhiều nút trên Toolbar (`Add Point`, `Add Notch`, `Add Internal Point`, `Add Internal Line`, `Line to Curve`, `Curve to Line`) đang thực thi **hành động tức thời lên đoạn/điểm mặc định ngay khi bấm nút trên Toolbar** thay vì chuyển sang chế độ công cụ để người dùng click lên vị trí mong muốn trên rập; các đối tượng `Notch`, `Internal Point`, `Internal Line` vẽ trên canvas chưa gắn sự kiện chuột để chọn/di chuyển/xóa trực tiếp. | **PARTIAL / FAIL (Trên UI thật)** |
| **Hiệu chuẩn Mode A & Mode B** | **Mode A**: Phát hiện đường lưới 1D hoạt động, nhưng đang gán cứng `27 × 20` ô khi ảnh có kích thước `3456 × 2592` ([`CalibrationEngine.cs:379-382`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs#L379-L382)) và chưa vẽ lưới/giao điểm lên `CalibrationWindow`.<br>**Mode B**: Toán học tính đúng `ScaleX`/`ScaleY` độc lập và lọc ROI trong code, nhưng **trên UI chưa có thao tác kéo chuột vẽ khung chữ nhật Mode B** và **chưa có nút/checkbox bật `RestrictDetectionToManualBox` trên hộp thoại Settings**. | **PARTIAL / FAIL (Thiếu tương tác UI)** |
| **Nhận diện rập (`DetectionEngine.cs`)** | Đúng số lượng mảnh trên 3 ảnh thật (`3`, `2`, `3`), loại bỏ được nam châm rời và ghế vàng, nhưng còn dùng vùng tọa độ gán cứng (`isLeftGlareZone: sx < 790 && sy > 1150`) và tên file `"111128"` để sắp thứ tự mảnh; một số góc vuông trên `01232026 093723.jpg` bị bo tròn do `MorphologicalClose(radius: 7)`. | **PARTIAL (Cần tổng quát hóa)** |

---

## 2. KIỂM TOÁN BẰNG CHỨNG REA + GHIDRA ĐỐI CHIẾU VỚI MÃ C# ĐÃ VIẾT

Dữ liệu đối chiếu được trích xuất trực tiếp từ các hồ sơ REA + Ghidra 12.1.4 đã lưu trữ:
- [`REA_GEMINI_DEEP_ANALYSIS.md`](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/REA_GEMINI_DEEP_ANALYSIS.md)
- [`GEMINI_TOOL_FUNCTIONAL_AUDIT.md`](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_TOOL_FUNCTIONAL_AUDIT.md)
- [`GEMINI_CALIBRATION_REVERSE_ENGINEERING.md`](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_CALIBRATION_REVERSE_ENGINEERING.md)
- [`GEMINI_DETECTION_PARITY_ANALYSIS.md`](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_DETECTION_PARITY_ANALYSIS.md)
- [`GEMINI_FULL_REPLICATION_TRACKER.md`](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_FULL_REPLICATION_TRACKER.md)

### Bảng Đối Chiếu Toàn Bộ Công Cụ Backend Với Bằng Chứng REA

| Tool | REA Evidence | Gemini Behavior | PDP Implementation | Test Result | Match Status |
|---|---|---|---|---|---|
| **1. Load Picture (`LoadPictureAction`)** | **VERIFIED (DFM)**: `TMAINFRM` action `LoadPictureActionExecute`; hỗ trợ `.jpg`, `.bmp`, `.png`. | Mở hộp thoại chọn file ảnh, nạp bitmap vào `TImagine`, cập nhật thanh tiêu đề và độ phân giải. | [`ImageEngine.LoadFromFile`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ImageEngine.cs#L83-L130) & [`MainWindow.LoadPicture_Click`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/MainWindow.xaml.cs#L689-L716). Tuy nhiên `ToBitmapSource()` giữ nguyên `DpiX/DpiY` gốc của file làm lệch tỷ lệ hiển thị WPF khi `Dpi != 96`. | Unit Test: **PASS**<br>UI Test (180 DPI JPG): **FAIL (Lệch tỷ lệ overlay)** | **PARTIAL** |
| **2. Capture / Acquire (`CaptureAction`, `AquireImageAction`)** | **VERIFIED (DFM)**: `CaptureActionExecute`, `AquireImageActionExecute`, `TwainPRO1PostScan`. | Chụp ảnh từ camera DirectShow/Canon SDK hoặc máy quét TWAIN. | Hiển thị hộp thoại thông báo không có thiết bị phần cứng tại [`MainWindow.UnimplementedAcquire_Click`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/MainWindow.xaml.cs#L1265-L1274). | UI Test: Hiển thị thông báo rõ ràng. | **BLOCKED (Thiếu phần cứng)** |
| **3. Calibrate — Mode A Grid (`CalibrateAction`)** | **VERIFIED (DFM)**: `TCALIBRATIONFRM` (`SquareDimEN`, `GS1X..GS2Y`, `CalibrateBtnClick`).<br>**UNKNOWN (Native Math)**: Không giải mã được hàm xử lý ma trận nội suy lưới hoặc `fdpc.dat` (`442,833` bytes). | Cắt vùng `Cropping box`, phát hiện lưới ô vuông (mặc định `5×5 cm`), tính vùng hoạt động (`135×100 cm`) và khử méo ảnh (`2918×2214`). | **INFERRED**: [`CalibrationEngine.CalibrateModeAGrid`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs#L24-L89) chiếu sáng 1D tìm rãnh tối theo trục X/Y. Có nhánh gán cứng `27×20` cho ảnh `3456×2592` ở dòng `379-382`. Không khử méo phối cảnh/thấu kính. | Unit Test: **PASS**<br>UI Test: **PARTIAL** (Chưa kéo được khung crop bằng chuột). | **INFERRED / PARTIAL** |
| **4. Calibrate — Mode B Manual X/Y (`TSettingsFrm`)** | **VERIFIED (DFM)**: `TSettingsFrm` có `UseGridCalibrationCheck`, `CalibByDimRadio` (`GridHorizSizeBox`, `GridVertSizeBox`), `CalibByResRadio`. | Cho phép nhập kích thước ngang/dọc (`cm`) hoặc độ phân giải (`PPI`) khi bỏ chọn `Use grid calibration`. | [`CalibrationEngine.CalibrateModeBManualRectangle`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs#L97-L147) & [`SettingsWindow.OkBtn_Click`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/SettingsWindow.xaml.cs#L65-L118). Thiếu thao tác vẽ khung ROI bằng chuột và thiếu checkbox bật giới hạn ROI trên UI. | Unit Test: **PASS**<br>UI Test: **FAIL (Không vẽ được khung ROI bằng chuột)** | **PARTIAL** |
| **5. Detect Pieces (`SnakeAction`)** | **VERIFIED (Entry)**: `SnakeActionExecute` tại `0x0041B860` (`6,602` bytes, gọi `0x00418A60` xóa trạng thái cũ) + nạp `SDEngine.dll` (`minunata_functie_1..7` tại `0x435a7c`).<br>**UNKNOWN (Core CV)**: 28 lệnh gọi gián tiếp chưa giải mã được thuật toán ngưỡng/Snake cụ thể. | Tách nền bảng, tìm biên chi tiết rập, loại bỏ nam châm giữ rập, khớp đỉnh góc (`Graded point`) và đường cong Bézier. | **INFERRED (Clean-room)**: [`DetectionEngine.DetectPatterns`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs#L52-L393) gồm 8 bước. Hoạt động trên 3 ảnh thật nhưng còn dùng tọa độ gán cứng `isLeftGlareZone` (`sx < 790 && sy > 1150`) và kiểm tra tên file `"111128"`. | Real Image Test: **PASS số mảnh (`3, 2, 3`)**, **PARTIAL độ chính xác biên** (xem Mục 3). | **INFERRED / PARTIAL** |
| **6. Select / Move Point & Piece (`ProceseazaMouseUpPuncte`, `DoMovePuncte`)** | **VERIFIED**: `@TImagine@DoMovePuncte` (`0x5ea5ac`) → `MiscaPunct` (`0x5ea460`) → `MiscaPuncteSimple` (`0x622274`) → `FUN_004b8b1c` cộng cùng vector $(\Delta X, \Delta Y)$ vào cả điểm đỉnh và 2 tay nắm Bézier. | Kéo điểm hoặc nhập tọa độ `Move to` / `Move with` sẽ dịch chuyển cả đỉnh lẫn 2 điểm điều khiển Bézier đi kèm, bảo toàn hình dạng đường cong lân cận. | [`EditingEngine.MoveVertexByDelta`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L99-L122), [`MoveVertexToAbsolute`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L124-L138), [`MovePieceByDelta`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L140-L170). | Unit Test: **PASS**<br>UI Drag Test: **PASS** (sau khi chuyển sang `RealImageCanvas`). | **ALGORITHM ALIGNED / PARTIAL** |
| **7. Add Point on Curve (`AddPointAction`)** | **VERIFIED**: `AddPointActionExecute` (`0x422938`) → `@TImagine@SplitCurve$qv` (`0x5d53a0`) → `FUN_004bdc70` (`0x4bdc70`) thực hiện chia đôi đường cong Cubic Bézier bằng thuật toán **De Casteljau** tại tham số $t$. | Chèn điểm mới lên cạnh thẳng hoặc đường cong tại vị trí chọn mà không làm biến dạng đường cong ban đầu. | [`CubicBezierSegment.SplitAt`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Models/DomainGeometryModels.cs#L287-L313) & [`EditingEngine.AddPointOnSegment`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L172-L234) thực hiện đúng De Casteljau.<br>**Lỗi UI**: Nút `AddPoint_Click` trên Toolbar luôn chèn cứng ở điểm giữa $t = 0.5$ của đoạn đang chọn, chưa cho click vị trí bất kỳ trên đường viền. | Unit Test: **PASS (Sai số diện tích/chu vi `< 0.0001 mm`)**<br>UI Test: **PARTIAL (Chỉ chèn tại $t=0.5$)** | **ALGORITHM ALIGNED / UI PARTIAL** |
| **8. Delete Point (`DeletePointsAction`)** | **VERIFIED (Entry)**: `DeletePointsActionExecute` (`0x422988`) → `@TImagine@StergePuncte$qv` (`0x5d57c8`) → `TObiect__StergePunct` (`0x506854`).<br>**UNKNOWN**: Điều kiện số đỉnh tối thiểu của Gemini chưa được chứng minh hoàn toàn (nhánh `> 2` gắn với Undo). | Xóa điểm đang chọn khỏi đường bao chi tiết và tạo bản ghi Undo. | [`EditingEngine.DeleteVertex`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L251-L286) xóa đỉnh đang chọn và giữ điều kiện tối thiểu $\ge 3$ đỉnh cho đa giác khép kín. | Unit Test: **PASS**<br>UI Test: **PASS** | **PARTIAL** |
| **9. Smart Corner (`SmartCornerAction`)** | **VERIFIED (DFM)**: `SmartCornerActionExecute`, hint *"Detect corner in the selected area"*.<br>**UNKNOWN (Native body)**: Chưa xác định được địa chỉ hàm native. | Khoanh vùng trên ảnh để tự động dò góc sắc của rập trong vùng chọn. | **SAI LỆCH HÀNH VI**: [`MainWindowViewModel.ExecutePromoteToCorner`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/ViewModels/MainWindowViewModel.cs#L1131-L1146) chỉ đổi thuộc tính đỉnh có sẵn thành `IsGradedCorner = true`, hoàn toàn không dò góc từ ảnh trong vùng chọn. | Unit Test: Đổi cờ `IsGradedCorner = true`.<br>So với Gemini: **Không đúng chức năng Smart Detection**. | **NOT IMPLEMENTED (Aliased to Promote Corner)** |
| **10. Delete Corner (`DeleteCornerAction`)** | **VERIFIED (Pointer)**: Method-table pointer `0x41ed80`.<br>**UNKNOWN (Body)**: Ghidra chưa giải mã được thân hàm tại `0x41ed80`. | Xóa góc hoặc hạ cấp điểm góc (`Graded point`) xuống điểm cong (`Curve point`). | [`MainWindowViewModel.ExecuteDemoteCornerToCurvePoint`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/ViewModels/MainWindowViewModel.cs#L1148-L1163) đổi `IsGradedCorner = false` (tương đương chọn radio `curve point`). | Unit Test: **PASS**<br>UI Test: **PASS** | **INFERRED / NOT VERIFIED** |
| **11. Line to Curve (`Line2Curve`)** | **VERIFIED**: `Line2CurveExecute` (`0x428f68`) → `@TImagine@ToCurve$qv` (`0x5d3f08`) → `FUN_00506650`. | Chuyển đoạn thẳng thành đoạn cong Cubic Bézier và khởi tạo 2 điểm điều khiển. | [`EditingEngine.ConvertLineToCurve`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L298-L343).<br>**Lỗi nhỏ trên UI**: Bấm nút trên Toolbar lập tức biến đổi luôn đoạn đang chọn trước khi người dùng click cạnh trên canvas. | Unit Test: **PASS**<br>UI Test: **PARTIAL** | **PARTIAL** |
| **12. Curve to Line (`Curve2Line`)** | **VERIFIED**: `Curve2LineExecute` (`0x428fb8`) → `@TImagine@ToLine$qv` (`0x5d461c`). | Chuyển đoạn cong Cubic Bézier về đoạn thẳng. | [`EditingEngine.ConvertCurveToLine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L349-L368). | Unit Test: **PASS**<br>UI Test: **PARTIAL** | **PARTIAL** |
| **13. Smart Notch / Add Notch / Delete Notch** | **VERIFIED (DFM)**: `SmartNotchActionExecute`, `AddNotchActionExecute`, `DeleteNotchActionExecute`.<br>**UNKNOWN**: Chưa ánh xạ được hàm native xử lý Notch. | `Smart Notch`: Dò dấu bấm trên ảnh.<br>`Add Notch`: Click lên đường bao để thêm dấu bấm.<br>`Delete Notch`: Xóa dấu bấm đang chọn. | [`EditingEngine.AddNotch`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L408-L435), [`DeleteNotch`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L437-L446).<br>**Lỗi UI**: `SmartNotch` bị gán chung vào `ExecuteAddNotch`; bấm nút trên Toolbar tự động thêm ngay 1 notch ở giữa cạnh đang chọn; không click chọn được notch đã vẽ trên canvas. | Unit Test: **PASS**<br>UI Test: **PARTIAL / FAIL (`SmartNotch` giả lập)** | **INFERRED / PARTIAL** |
| **14. Smart / Add / Delete Internal Point** | **VERIFIED (DFM)**: `SmartInternalPointActionExecute`, `AddInternalPointActionExecute`, `DeleteInternalPointActionExecute`.<br>**INFERRED**: Ứng viên `@TImagine@CreazaPunctAuxiliar` (`0x628b54`). | Thêm/xóa điểm khoan nội vi (`Drill hole`) hoặc tự động dò trong vùng ảnh chọn (`Smart`). | [`EditingEngine.AddInternalPoint`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L448-L460), [`DeleteInternalPoint`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L462-L466).<br>**Lỗi UI**: `SmartInternalPoint` gán chung vào `ExecuteAddInternalPoint`; bấm nút trên Toolbar tự sinh ngay 1 điểm ở tâm mảnh rập; không click chọn được điểm nội vi trên canvas. | Unit Test: **PASS**<br>UI Test: **PARTIAL / FAIL (`Smart` giả lập)** | **INFERRED / PARTIAL** |
| **15. Smart / Add / Delete Internal Line & Grain Axis** | **VERIFIED (DFM)**: `SmartInternalLineActionExecute`, `AddInternalLineActionExecute`, `DeleteInternalLineActionExecute`.<br>**INFERRED**: `@TImagine@CreazaDreaptaReper$qv` (`0x5dee20`), `@TImagine@AliniereFirDrept` (`0x5ecb44`). | Vẽ đường nội vi hoặc đường canh sợi (`Grain axis`), đổi chiều (`Flip`), hoặc tự động dò từ ảnh (`Smart`). | [`EditingEngine.AddInternalLine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L468-L490).<br>**Lỗi UI**: Không hỗ trợ click 2 điểm đầu-cuối để vẽ đường nội vi mà tự sinh đoạn thẳng ngang cố định `80 mm` tại điểm click ([`MainWindow.xaml.cs:585-590`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/MainWindow.xaml.cs#L585-L590)). | Unit Test: **PASS**<br>UI Test: **PARTIAL (Không vẽ 2 điểm tự do được)** | **INFERRED / PARTIAL** |
| **16. Add / Delete Auxiliary Piece (`AddFigAux`, `DelFigAux`)** | **VERIFIED (DFM)**: `AddFigAux`, `DelFigAux` (`Visible = False` mặc định trên DFM gốc).<br>**UNKNOWN**: Chưa xác minh hàm native. | Tạo/xóa chi tiết phụ trợ. | [`EditingEngine.CreateAuxiliaryRectanglePiece`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L498-L541) tạo hình chữ nhật `200×150 mm`; `ExecuteDeleteSelectedPiece` xóa mảnh đang chọn. | Unit Test: **PASS**<br>UI Test: **PASS** | **INFERRED / NOT VERIFIED** |
| **17. Measure on Perimeter (`ComputePerimeterDistanceAction`)** | **VERIFIED**: `@TImagine@PaintMasurareDistantaPeContur` (`0x5be434`), `CalculeazaMasuratoare` (`0x64c584`) → `0x64c5d0`. | Chọn 2 điểm trên đường bao, tô sáng phần đường bao được đo và vẽ nhãn chiều dài trực tiếp lên canvas. | [`MeasurementEngine.ComputeContourDistanceBetweenVerticesMm`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/MeasurementEngine.cs#L140-L169) tính chính xác chiều dài cung Gauss-Legendre giữa 2 đỉnh và hiện lên `StatusBar`. **Chưa vẽ đường tô sáng và nhãn số đo lên canvas**. | Unit Test: **PASS**<br>UI Test: **PARTIAL (Chỉ hiện ở StatusBar)** | **PARTIAL** |
| **18. Measure Direct Distance (`ComputeDirectDistanceAction`)** | **VERIFIED**: `@TImagine@PaintMasurareDistantaDirecta` (`0x5bf46c`), `CalculeazaMasuratoare` (`0x64c584`) → `0x64cf2c`. | Chọn 2 điểm bất kỳ, vẽ đoạn thẳng nối 2 điểm kèm nhãn khoảng cách Euclid trên canvas. | [`MeasurementEngine.ComputeStraightDistanceCm`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/MeasurementEngine.cs#L43-L46) tính khoảng cách khi click 2 điểm bên trong mảnh rập và hiện lên `StatusBar`. **Chưa vẽ đoạn thẳng đo lên canvas và không bắt sự kiện click ngoài mảnh rập**. | Unit Test: **PASS**<br>UI Test: **PARTIAL** | **PARTIAL** |
| **19. Undo / Redo (`UndoAction`, `RedoAction`)** | **VERIFIED**: `UndoActionExecute` (`0x41e704`) và `RedoActionExecute` (`0x41e940`) cùng gọi `@TImagine@DoUndo$qp10TStivaUndo` (`0x5d62d8`), kết hợp `CreateUndoPoint` (`0x5d61d0`) và `ClearStivaRedo` (`0x5d656c`). | Lưu snapshot toàn bộ trạng thái mô hình trước mỗi thao tác sửa đổi, xóa ngăn xếp Redo khi có chỉnh sửa mới, hoán đổi trạng thái khi Undo/Redo. | [`EditingEngine.PushUndoSnapshot`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L36-L53), [`Undo`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L55-L71), [`Redo`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/EditingEngine.cs#L73-L89). | Unit Test: **PASS**<br>UI Test: **PASS** | **ALGORITHM ALIGNED / PARTIAL** |
| **20. Save / Open Project (`SaveGEMAction`)** | **VERIFIED (DFM)**: `SaveGEMActionExecute`, `SaveAsActionExecute`.<br>**UNKNOWN**: Cấu trúc nhị phân `.gem` độc quyền của Gemini. | Lưu/mở dự án số hóa. | [`FileEngine.SaveProject`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs#L68-L93) & [`LoadProject`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs#L95-L119) lưu định dạng JSON `.pdp`/`.gem` độc lập, bảo toàn 100% hình học và hiệu chuẩn của PDP (không tương thích nhị phân `.gem` gốc). | Unit Test: **PASS**<br>UI Test: **PASS** | **PARTIAL (Định dạng riêng của PDP)** |
| **21. Export DXF (`ExportDxfExecute`)** | **VERIFIED (DFM & Profiles)**: `TEXPORTDXFFrm`, 6 file cấu hình tại `Profiles\DXF\Export\*.txt` (Layers `1, 2, 4, 7, 8, 13, 19`). | Xuất file CAD/AAMA DXF theo đơn vị vật lý đã hiệu chuẩn. | [`FileEngine.ExportDxf`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs#L159-L296) xuất chuẩn AC1009, `$INSUNITS=4` (`mm`), đầy đủ các lớp `1, 2, 4, 7, 8, 13, 19`. | Unit Test & Independent Parser: **PASS (Sai số `< 0.001 mm` cho cạnh thẳng, `0.057 mm` cho cung tròn Bézier)** | **PARTIAL (Chưa so sánh byte-level với DXF xuất từ Gemini gốc)** |

---

## 3. KIỂM TOÁN NHẬN DIỆN RẬP TRÊN 3 ẢNH THẬT (CONTOUR DEVIATION & ERROR ANALYSIS)

> **Lưu ý về khả năng chạy Gemini gốc trực tiếp**: Khi khởi chạy `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\GeminiPhotoDigitizer.exe` trên máy hiện tại (không có khóa cứng HASP/Sentinel vật lý), tiến trình chỉ hiện `TSplashScreen` rồi thoát trước khi mở `TMainFrm`. Do đó:
> - Với ảnh 1 (`03172025 111128.jpg`), chúng tôi đối chiếu trực tiếp với **ảnh chụp màn hình Gemini gốc đang mở chính ảnh này** do người dùng cung cấp (`Ảnh 1` & `Ảnh 2`).
> - Với ảnh 2 (`01082026 091623.jpg`) và ảnh 3 (`01232026 093723.jpg`), việc chạy Gemini gốc trực tiếp bị **BLOCKED bởi khóa cứng HASP**, nên chúng tôi đo độ lệch pixel trực tiếp giữa đường bao thô trích xuất từ biên ảnh (`Stage 6 Outer Contours` — màu xanh cyan) và đường bao vector cuối cùng của PDP (`Stage 8 Final Overlay` — màu đỏ), đồng thời kiểm tra bằng mắt thường từng góc, đường cong, vòng nam châm và vùng nhiễu.

### 3.1. Ảnh 1: [`sample_03172025_111128.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/sample_03172025_111128.jpg) (`3456 × 2592 px`)

- **File ảnh kết quả kiểm toán**:
  - Overlay cuối cùng: [`artifacts/acceptance-audit/sample_03172025_111128/8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/sample_03172025_111128/8_final_overlay.png)
  - Bản đồ đối chiếu biên Cyan (Raw) vs Đỏ (Fitted Vector): [`artifacts/acceptance-audit/sample_03172025_111128_audit_comparison.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/sample_03172025_111128_audit_comparison.png)

| Chi tiết | Số đỉnh (Graded / Curve) | Chu vi / Diện tích (ở lưới 5 cm) | Độ lệch Contour (Mean / RMS / P95 / Max px) | Tọa độ lệch lớn nhất `(X, Y)` | Nguyên nhân gây lệch & So sánh với Gemini gốc (`Ảnh 1–2`) |
|---|---|---|---|---|---|
| **Piece 1** (Trái trên) | `4` (`4 / 0`) | `101.15 cm` (`21.14 × 29.42 cm`) / `622.41 cm²` | `4.20` / `4.76` / `8.23` / **`10.93 px`** (`4.53 mm`) | `(1153, 139)` | Vòng nam châm nằm sát góc trên-phải `(1135, 110)` và góc dưới-trái `(715, 760)` làm biên nhị phân thô bị lẹm nhẹ, trong khi `TryFitConvexQuadrilateralCorners` cắt bỏ 20% hai đầu cạnh để hồi phục đường thẳng thực của mép giấy.<br>**Khác biệt với Gemini gốc (`Ảnh 1`)**: Trong Gemini gốc, ảnh đã được cắt/khử méo về `2918 × 2214` nên góc trên cùng của `Piece 1` bị cắt cụt bởi mép trên khung ảnh; còn PDP xử lý trực tiếp trên ảnh gốc `3456 × 2592` nên giữ nguyên đủ 4 góc. |
| **Piece 2** (Phải dưới) | `4` (`4 / 0`) | `100.20 cm` (`20.79 × 29.31 cm`) / `609.28 cm²` | `4.12` / `4.68` / `8.15` / **`9.09 px`** (`3.77 mm`) | `(3340, 1556)` | Nam châm góc trên-phải `(3288, 1445)` và góc dưới-phải `(3225, 2005)` nằm sát mép giấy; đường thẳng khớp qua phần giữa cạnh giúp bỏ qua phần bóng đổ của nam châm. |
| **Piece 3** (Giữa) | `4` (`4 / 0`) | `100.32 cm` (`20.85 × 29.31 cm`) / `611.20 cm²` | `4.13` / `4.69` / `8.14` / **`9.14 px`** (`3.79 mm`) | `(1669, 1715)` | Nam châm giữa nằm hoàn toàn bên trong mảnh rập được lấp kín 100% (`FillInteriorHoles`); nam châm góc trên `(1848, 975)` và góc dưới-trái `(1552, 1502)` không làm sinh thêm đỉnh giả. |

> **Phát hiện kiểm toán mã nguồn trên Ảnh 1**: Tại [`DetectionEngine.cs:352-364`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs#L352-L364) có đoạn kiểm tra `sourceImage.FilePath.Contains("111128")` để hoán đổi thứ tự `Piece 2` và `Piece 3` cho trùng với thứ tự của `Ảnh 1`. Đây là xử lý phụ thuộc tên file, cần thay bằng quy tắc sắp xếp hình học tổng quát.

---

### 3.2. Ảnh 2: [`real_01082026_091623.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/real_01082026_091623.jpg) (`3456 × 2592 px` — 2 mảnh rập vải bò xanh)

- **File ảnh kết quả kiểm toán**:
  - Overlay cuối cùng: [`artifacts/acceptance-audit/real_01082026_091623/8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01082026_091623/8_final_overlay.png)
  - Bản đồ đối chiếu biên Cyan (Raw) vs Đỏ (Fitted Vector): [`artifacts/acceptance-audit/real_01082026_091623_audit_comparison.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01082026_091623_audit_comparison.png)
  - Ảnh phân loại vùng nhiễu bị loại bỏ (`12` vùng nhiễu): [`artifacts/acceptance-audit/real_01082026_091623/5_rejected_components.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01082026_091623/5_rejected_components.png)

| Chi tiết | Số đỉnh (Graded / Curve) | Số đoạn (Line / Bézier) | Chu vi / Diện tích (ở lưới 5 cm) | Độ lệch Contour vs Raw Mask (Mean / RMS / P95 / Max px) | Các vị trí sai lệch thực tế trên ảnh (Đã đánh dấu) |
|---|---|---|---|---|---|
| **Piece 1** (Mảnh vải bò bên trái) | `14` (`7 / 7`) | `5 Line` / `9 Bézier` | `105.15 cm` / `700.85 cm²` | `1.87` / `2.20` / `4.09` / **`8.03 px`** (`3.33 mm`) tại `(1125, 1600)` | 1. **Góc dưới cùng `(1075, 1612)` và `(1125, 1600)`**: Mép vải gập tạo bóng tối khiến đường bao bị vát thành đoạn ngắn `0.83 cm` với 2 điểm sát nhau.<br>2. **Cạnh trên cùng `(956, 714) -> (1465, 895)`**: Đường cong lưng quần bị chia nhỏ thành 3 đoạn nối tiếp với 2 điểm xanh (`Curve point`) ở giữa thay vì 1 đường cong Cubic Bézier duy nhất.<br>3. **Tem nhãn trắng nhỏ ở mép dưới phải `(1415, 1420)`**: Làm đường bao lồi nhẹ ra ngoài ~`4 px`. |
| **Piece 2** (Mảnh vải bò bên phải) | `11` (`8 / 3`) | `4 Line` / `7 Bézier` | `100.81 cm` / `622.16 cm²` | `2.05` / `2.35` / `4.12` / **`7.39 px`** (`3.07 mm`) tại `(1670, 1388)` | 1. **LỖI VÒNG NAM CHÂM DÍNH VÀO BIÊN CONG LÕM TẠI `(1700, 1350)`**: Ở hõm đáy quần bên trái của `Piece 2`, cục nam châm tròn màu trắng-xám đặt sát mép vải. Do Stage 2 lấy cả pixel sáng (`lumDiff >= 88.0`) lẫn pixel màu xanh (`blueRedShift >= 40.0`), cục nam châm này bị gộp thẳng vào mặt nạ của mảnh vải xanh! Đường biên đỏ vì thế **vòng qua mép ngoài của cục nam châm** (lệch ~`38–42 px` $\approx 16\text{ mm}$ so với mép vải thật nằm dưới nam châm).<br>2. **Tem nhãn trắng ở mép dưới phải `(2025, 1492)`**: Tạo vết lồi nhỏ trên đường biên. |

> **Phát hiện kiểm toán mã nguồn trên Ảnh 2**: Để chặn vệt lóa sáng lớn ở góc dưới bên trái bảng, [`DetectionEngine.cs:128`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs#L128) đang dùng điều kiện tọa độ cứng `bool isLeftGlareZone = (fullW >= 2500) && (sx < 790) && (sy > 1150);`. Nếu đặt một mảnh rập tối màu vào góc dưới bên trái bảng (`X < 790, Y > 1150`), mảnh rập đó sẽ không được nhận diện.

---

### 3.3. Ảnh 3: [`real_01232026_093723.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/real_01232026_093723.jpg) (`3456 × 2592 px` — 3 mảnh rập giấy trắng + 11 nam châm rời + ghế vàng)

- **File ảnh kết quả kiểm toán**:
  - Overlay cuối cùng: [`artifacts/acceptance-audit/real_01232026_093723/8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01232026_093723/8_final_overlay.png)
  - Bản đồ đối chiếu biên Cyan (Raw) vs Đỏ (Fitted Vector): [`artifacts/acceptance-audit/real_01232026_093723_audit_comparison.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01232026_093723_audit_comparison.png)
  - Ảnh phân loại vùng nhiễu bị loại bỏ (`14` vùng nhiễu gồm 11 nam châm rời và ghế vàng): [`artifacts/acceptance-audit/real_01232026_093723/5_rejected_components.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01232026_093723/5_rejected_components.png)

| Chi tiết | Số đỉnh (Graded / Curve) | Số đoạn (Line / Bézier) | Chu vi / Diện tích (ở lưới 5 cm) | Độ lệch Contour vs Raw Mask (Mean / RMS / P95 / Max px) | Các vị trí sai lệch thực tế trên ảnh (Đã đánh dấu) |
|---|---|---|---|---|---|
| **Piece 1** (Mảnh trắng lớn bên trái) | `9` (`5 / 4`) | `6 Line` / `3 Bézier` | `104.99 cm` / `639.47 cm²` | `3.93` / `4.79` / `9.02` / **`12.75 px`** (`5.29 mm`) tại `(249, 233)` | 1. **Đường cong cổ áo phía trên `(412, 178) -> (938, 350)`**: Bị chia thành 4 đoạn nhỏ với **1 điểm góc đỏ (`Graded point`) nằm ngay giữa đường cong trơn tại `(538, 318)`** và 2 điểm xanh (`Curve point`) lân cận, thay vì 1 cung Cubic Bézier duy nhất nối 2 đầu góc cổ.<br>2. **Góc dưới-phải `(908, 858)`**: Bị vát chéo (`1.53 cm`) thành 1 điểm xanh + 1 điểm đỏ do phép đóng hình thái học `MorphologicalClose(radius: 7)` làm tròn góc nhọn trước khi rút gọn đa giác. |
| **Piece 2** (Mảnh trắng ở giữa) | `10` (`5 / 5`) | `7 Line` / `3 Bézier` | `93.19 cm` / `494.91 cm²` | `2.30` / `2.64` / `4.75` / **`6.59 px`** (`2.73 mm`) tại `(1427, 507)` | 1. **Góc vuông dưới-phải `(1666, 1044)` bị nhận nhầm thành `Curve point` (chấm xanh)**: Do góc giấy bị làm trơn bởi bước đóng hình thái học và làm mượt biên, góc rẽ tại đỉnh nhỏ hơn ngưỡng `30°` nên không được gắn cờ `Graded point` (chấm đỏ).<br>2. **Góc trên-trái `(1035, 528)`**: Sinh ra 2 đỉnh sát nhau cách nhau `0.69 cm`.<br>3. **Các dấu bấm chữ T màu đỏ vẽ bằng bút trên mép giấy**: Không được trích xuất tự động thành `NotchItem`. |
| **Piece 3** (Dải chữ nhật đứng bên phải) | `4` (`4 / 0`) | `4 Line` / `0 Bézier` | `102.69 cm` (`43.05 × 8.81 cm`) / `374.17 cm²` | `5.85` / `6.91` / `12.10` / **`13.33 px`** (`5.53 mm`) tại `(1981, 776)` | Nhận diện đúng 4 góc nhọn (`Graded point`). Độ lệch `Mean = 5.85 px` giữa cạnh phải và biên cyan là do mép phải của dải giấy dài bị uốn cong nhẹ trên bảng, trong khi `TryFitConvexQuadrilateralCorners` ép 4 cạnh thành 4 đoạn thẳng tuyệt đối. |

---

## 4. KIỂM TOÁN HIỆU CHUẨN MODE A VÀ MODE B

### 4.1. Kiểm toán Mode A (Grid Calibration trên ảnh bảng thật [`cal_grid_117cm_20o.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/cal_grid_117cm_20o.jpg))

- **Dữ liệu đo thực nghiệm từ [`CalibrationEngine.CalibrateModeAGrid`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs#L24-L89)**:
  - Kích thước ảnh bảng lưới: `3456 × 2592 px`.
  - Số đường kẻ dọc phát hiện được (`VerticalGridLinesPx`): **`26` đường** tại tọa độ pixel: `[249.4, 361.9, 484.0, 605.4, 724.2, 844.0, 975.5, 1094.6, 1214.8, 1324.7, 1453.3, 1572.9, 1688.1, 1810.4, 1939.7, 2059.6, 2179.3, 2297.8, 2419.1, 2539.4, 2659.9, 2780.3, 2900.6, 3020.7, 3140.9, 3261.8]`.
  - Bước lưới ngang (`Pitch X`): Trung bình **`120.49 px/ô`** (`min = 109.90 px`, `max = 131.52 px` do méo quang học của ống kính góc rộng và góc nghiêng camera).
  - Số đường kẻ ngang phát hiện được (`HorizontalGridLinesPx`): **`20` đường** tại tọa độ pixel: `[173.5, 297.3, 419.9, 543.8, 656.5, 776.6, 892.0, 1013.1, 1131.5, 1255.0, 1380.0, 1501.8, 1623.9, 1744.9, 1867.8, 1971.6, 2091.8, 2213.2, 2334.6, 2455.9]`.
  - Bước lưới dọc (`Pitch Y`): Trung bình **`120.13 px/ô`** (`min = 103.76 px`, `max = 124.97 px`).
  - **Với ô lưới `5.0 × 5.0 cm`**:
    - `ScaleX = 0.414957 mm/px`, `ScaleY = 0.416227 mm/px`
    - `Active area = 135 cm × 100 cm` (`27 × 20` ô)
    - Sai số dư lưới (`Residual Error` so với bước lưới đều lý tưởng): **`Mean = 1.513 mm`, `Max = 5.225 mm`**.
  - **Với ô lưới `5.85 × 5.85 cm` (theo tên ảnh gốc `117cm--20o.jpg` / `5.85x-y.jpg`)**:
    - `ScaleX = 0.485500 mm/px`, `ScaleY = 0.486985 mm/px`
    - `Active area = 157.95 cm × 117.00 cm` (`27 × 20` ô)
    - Sai số dư lưới: **`Mean = 1.770 mm`, `Max = 6.114 mm`**.
- **Các tồn tại kỹ thuật của Mode A được phát hiện qua kiểm toán**:
  1. **Chỉ mới phát hiện đường thẳng 1D, chưa phát hiện giao điểm 2D (`Grid Intersections`)**: `DetectGridAxisLines` lấy trung bình độ sáng theo toàn bộ cột/hàng để tìm tọa độ $X_i$ và $Y_j$ độc lập. Vì bước lưới thực tế trên ảnh `cal_grid_117cm_20o.jpg` biến thiên từ `103.8 px` đến `131.5 px` giữa tâm ảnh và rìa ảnh (do méo thấu kính và phối cảnh), việc dùng một hệ số tỷ lệ tuyến tính duy nhất (`ScaleX`, `ScaleY`) tạo ra sai số cục bộ tối đa tới **`5.23 mm` (`~12.6 px`)** ở rìa bảng.
  2. **Gán cứng số ô `27 × 20` cho ảnh `3456 × 2592`**: Tại [`CalibrationEngine.cs:379-382`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs#L379-L382), khi `img.Width == 3456 && img.Height == 2592 && crop.Width > 3200`, hàm ghi đè `estimatedCells = isVerticalLines ? 27 : 20;` thay vì lấy trực tiếp từ số khoảng lưới thực tế phát hiện được (`26` đường dọc = `25` khoảng nội bộ + 2 khoảng biên).
  3. **Chưa hiển thị trực quan lưới/giao điểm và khung Crop trên `CalibrationWindow`**: Trên [`CalibrationWindow.xaml.cs`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/CalibrationWindow.xaml.cs), người dùng chỉ nhập số vào `Start X/Y`, `Stop X/Y` chứ không kéo chuột vẽ khung Crop trên ảnh được, và sau khi bấm `Calibrate` không có lớp phủ vẽ các giao điểm lưới tìm được lên ảnh.
  4. **Sai số trên vật chuẩn độc lập**: Chưa có số đo thước vật lý độc lập của 3 tờ giấy trắng trong ảnh `03172025 111128.jpg` (khi áp tỷ lệ lưới `5.0 cm`, 3 tờ giấy có kích thước đo được là `21.14 × 29.42 cm`, `20.79 × 29.31 cm`, `20.85 × 29.31 cm` — rất sát khổ giấy chuẩn **A4 `21.0 × 29.7 cm`**, tức lệch khoảng `1.4 mm` theo chiều rộng và `3.8 mm` theo chiều dài so với khổ A4 tiêu chuẩn!).

---

### 4.2. Kiểm toán Mode B (Manual X/Y Rectangle Calibration)

| Yêu cầu kiểm thử Mode B | Trạng thái trong Engine (`Core`) | Trạng thái trên Giao diện (`UI`) | Kết luận |
|---|---|---|---|
| **1. Vẽ hình chữ nhật bằng chuột** | `CropBoxPixels` nhận tọa độ `(StartX, StartY, StopX, StopY)` bất kỳ. | **CHƯA HOẠT ĐỘNG (`FAIL`)**: Không có sự kiện chuột nào trên `MainWindow` hoặc `CalibrationWindow` cho phép kéo vẽ khung hình chữ nhật Mode B; `MainWindowViewModel._manualCalibrationBoxPx` đang giữ giá trị khởi tạo cố định `(180, 140, 3276, 2452)`. | **FAIL (Trên UI)** |
| **2. Nhập X/Y & Apply Calibration** | [`CalibrateModeBManualRectangle`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/CalibrationEngine.cs#L97-L147) tính đúng `ScaleX = (WidthCm * 10) / RectWidthPx` và `ScaleY = (HeightCm * 10) / RectHeightPx` độc lập. | Trong `Settings -> Camera settings / Calibration`, khi bỏ tích `Use grid calibration`, chọn `Calibrate by dimensions` và nhập `Grid horizontal size (cm)` / `Grid vertical size (cm)` rồi bấm `OK`, [`SettingsWindow.OkBtn_Click`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/SettingsWindow.xaml.cs#L72-L95) áp dụng đúng `ScaleX`/`ScaleY` vào `ActiveCalibration`. | **PASS** |
| **3. Kiểm tra Pan/Zoom vẫn hoạt động** | [`ViewportEngine`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ViewportEngine.cs) độc lập với `CalibrationProfile`. | Sau khi áp dụng Mode B, lăn chuột Zoom và kéo chuột Pan trên `MainWindow` vẫn hoạt động bình thường. | **PASS** |
| **4. Detect chỉ trong ROI** | Khi `RestrictDetectionToCropBox = true`, [`DetectionEngine.cs:103`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs#L103) chỉ quét pixel nằm trong `CropBox`: thử nghiệm đặt ROI `(1000, 700 -> 1950, 1650)` quanh mảnh giữa của ảnh 1 chỉ phát hiện đúng `1` mảnh ([`artifacts/acceptance-audit/modeB_roi_only/8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/modeB_roi_only/8_final_overlay.png)). | **CHƯA CÓ NÚT BẬT TRÊN UI (`FAIL`)**: Biến `_viewModel.RestrictDetectionToManualBox` mặc định bằng `false` và không có CheckBox nào trên `SettingsWindow` hay `MainWindow` để người dùng bật tính năng giới hạn nhận diện trong ROI. | **PASS (Core) / FAIL (UI)** |
| **5. Kiểm tra kích thước thực sau số hóa** | Mảnh rập `400 × 300 px` bên trong khung chuẩn `2000 × 1000 px` (`100 × 60 cm`) có kích thước đúng `20.0 × 18.0 cm` (không bị ép thành `100 × 60 cm`). | Hiển thị đúng `Area` và `Perimeter` theo tỷ lệ `ScaleX`, `ScaleY` mới trên bảng bên phải. | **PASS** |

---

## 5. KIỂM TOÁN TOÀN BỘ 21 CÔNG CỤ TRÊN TOOLBAR & TƯƠNG TÁC UI

> **Trạng thái kiểm thử tự động hoá chuột trên màn hình Desktop**: Công cụ môi trường hiện tại chạy lệnh qua PowerShell và khởi tạo trực tiếp cửa sổ WPF thực trên luồng STA (chụp ảnh trực tiếp buffer hiển thị của cửa sổ qua `RenderTargetBitmap`). Đối với thao tác click chuột vật lý của người dùng cuối trên cửa sổ `PatternDigitizerPro.UI.exe` (`Release`, đang mở sẵn trên màn hình Desktop), chúng tôi đã kiểm tra toàn bộ hàm xử lý sự kiện chuột trong [`MainWindow.xaml.cs`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/MainWindow.xaml.cs) và phân loại trung thực từng công cụ dưới đây, kèm quy trình kiểm thử từng bước cho người dùng.

### 5.1. Hai Lỗi Nền Tảng Trên Giao Diện Tương Tác (`MainWindow`) Cần Lưu Ý Ngay

1. **Bẫy trạng thái Demo khi mới khởi động ứng dụng (`IsDemoReferenceLayoutActive = true`)**:
   - Khi vừa mở `PatternDigitizerPro.UI.exe`, ứng dụng hiển thị `LiveV9ReferenceCanvas` (chứa ảnh mẫu tĩnh và 3 đa giác tĩnh để khớp 1:1 với `Ảnh 1` của Phase 1). Lúc này `DomainPieces.Count == 0` và `RealImageCanvas` đang `Collapsed`.
   - Nếu người dùng click chuột ngay vào mảnh rập trên màn hình mà chưa bấm nút **Detect pieces (Tam giác xanh)** hoặc **Load picture**, **sẽ không chọn hay kéo được điểm nào**.
2. **Lỗi lệch tỷ lệ hiển thị giữa Ảnh nền (`180 DPI`) và Lớp Vector (`96 DPI`) trên `RealImageCanvas`**:
   - Khi mở ảnh chụp từ máy ảnh (`real_01082026_091623.jpg`, `real_01232026_093723.jpg`, `sample_03172025_111128.jpg` đều có metadata `180 DPI`), [`RawImageBuffer.ToBitmapSource()`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ImageEngine.cs#L60-L73) truyền `DpiX = 180, DpiY = 180` vào `BitmapSource.Create`.
   - Trong WPF, thẻ `<Image x:Name="WorkspaceImageControl" Stretch="None" />` vẽ bức ảnh `3456 × 2592 px` ở kích thước $3456 \times \frac{96}{180} = 1843.2 \times 1382.4\text{ DIP}$, trong khi `InteractiveVectorOverlayCanvas` vẽ các mảnh rập ở tọa độ $3456 \times 2592\text{ DIP}$ (xem ảnh chụp thực tế [`ui_2_detected_093723.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_2_detected_093723.png) và [`ui_4_detected_091623.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_4_detected_091623.png)).

### 5.2. Bảng Kiểm Toán Chi Tiết Từng Công Cụ Trên Toolbar (Click -> Thao tác -> Undo/Redo -> Save/Reopen)

| # | Công cụ trên Toolbar | Click Toolbar & Thao tác chuột thực tế trên UI | Hình học thay đổi | Undo / Redo | Save / Reopen (`.pdp`) | Đánh giá thực tế |
|---:|---|---|---|---|---|---|
| 1 | **Load picture** (`Ctrl+O` + Dropdown) | Mở `OpenFileDialog`, chọn ảnh `.jpg/.png/.bmp` hoặc `.pdp`. Có menu con nạp nhanh ảnh mẫu. | Nạp ảnh mới, xóa các mảnh cũ. | Không lưu ảnh trước đó vào Undo stack khi đổi ảnh. | Lưu đường dẫn ảnh trong `.pdp`. | **WORKING** (Nhưng bị lỗi DPI hiển thị khi ảnh $\ne 96$ DPI) |
| 2 | **Detect pieces** (Tam giác xanh) | Chạy `DetectionEngine.DetectPatterns` trên ảnh hiện tại, chuyển sang `RealImageCanvas`, tạo thẻ ở `Stock Area`. | Sinh các `PatternPiece` từ ảnh thật. | Xóa lịch sử Undo cũ khi Detect mới. | Bảo toàn 100% sau Save/Reopen. | **WORKING** (Cần sửa lỗi DPI hiển thị trên `MainWindow`) |
| 3 | **Save / Export** (`Ctrl+S` + Dropdown) | Nút chính mở `SaveFileDialog` lưu `.pdp`/`.gem`. Menu con mở `Export DXF...`, `Export Investronica DB...` (chỉ có UI), `Export Other Format...`. | Không làm đổi hình học. | N/A | Đọc lại `.pdp` khớp 100%. | **WORKING** (Riêng Investronica DB chỉ là hộp thoại UI Phase 1) |
| 4 | **Undo** (`Ctrl+Z`) | Khôi phục snapshot `DomainPieces` ngay trước đó. | Khôi phục chính xác đỉnh, đường cong, notch, internal line, diện tích, chu vi. | Hoạt động đa bước. | N/A | **WORKING** |
| 5 | **Redo** (`Ctrl+Y`) | Áp dụng lại snapshot vừa Undo. | Khôi phục chính xác trạng thái sau chỉnh sửa. | Hoạt động đa bước; tự xóa khi có chỉnh sửa mới. | N/A | **WORKING** |
| 6 | **Show / Hide Grid** (`Alt+G` + Dropdown) | Bật/tắt lưới đỏ nét đứt (`Alt+G`), ảnh nền (`Alt+H`), dấu bấm (`Alt+N`), đường nội vi (`Alt+L`), điểm nội vi (`Alt+P`). | Chỉ ẩn/hiện lớp hiển thị, không làm đổi tọa độ hình học. | N/A | N/A | **WORKING** |
| 7 | **Zoom to selected area** | Bấm nút bật `ActiveTool = ZoomArea`. **Lỗi**: Khi click chuột trái lên canvas chỉ gọi `ZoomIn()` (phóng to cố định 25%), **không kéo vẽ được khung chữ nhật để zoom vào vùng chọn**. | Không đổi hình học. | N/A | N/A | **PARTIAL / DEFECT** |
| 8 | **Zoom to selected pieces** (`F4`) | Phóng to và cuộn khung nhìn tới bao hình chữ nhật của mảnh rập đang chọn (`ZoomToImageRect`). | Không đổi hình học. | N/A | N/A | **WORKING** |
| 9 | **Zoom to entire picture** (`F5`) | Thu phóng vừa khít khung nhìn (`ZoomFit`). | Không đổi hình học. | N/A | N/A | **WORKING** |
| 10 | **Transform line to curve** | **Lỗi tương tác**: Vừa bấm nút trên Toolbar là biến đổi ngay cạnh đang chọn thành đường cong Cubic Bézier (hiện 2 tay nắm màu cyan kéo được bằng chuột), đồng thời bật mode `LineToCurve` để click cạnh khác. | Cạnh thẳng chuyển thành Cubic Bézier; kéo 2 tay nắm `C1`, `C2` cập nhật trực tiếp diện tích & chu vi. | **PASS** | **PASS** | **PARTIAL (Tự chạy ngay khi bấm nút Toolbar)** |
| 11 | **Transform curve to line** | Vừa bấm nút trên Toolbar là chuyển ngay cạnh cong đang chọn về đoạn thẳng, đồng thời bật mode `CurveToLine`. | Đoạn cong chuyển về đoạn thẳng; diện tích & chu vi cập nhật lại. | **PASS** | **PASS** | **PARTIAL (Tự chạy ngay khi bấm nút Toolbar)** |
| 12 | **Add point** | **Lỗi tương tác**: Bấm nút trên Toolbar lập tức chèn điểm mới vào **chính giữa ($t = 0.5$)** của cạnh đang chọn bằng De Casteljau. **Không thể click chuột lên một vị trí bất kỳ trên cạnh để chèn điểm tại chỗ click**. | Số đỉnh tăng 1; hình dạng đường cong trước và sau khi chèn giữ nguyên 100%. | **PASS** | **PASS** | **PARTIAL (Chỉ chèn ở $t=0.5$, thiếu click trên cạnh)** |
| 13 | **Delete point** | Bấm nút trên Toolbar xóa ngay đỉnh đang chọn (`SelectedVertexIndex`), chặn xóa nếu mảnh chỉ còn 3 đỉnh. | Số đỉnh giảm 1; nối thẳng/cong lại cạnh liền kề. | **PASS** | **PASS** | **WORKING (Thao tác trên đỉnh đang chọn)** |
| 14 | **Smart corner** | **Chức năng giả lập**: Không dò góc từ ảnh trong vùng chọn mà chỉ đổi đỉnh đang chọn/được click thành `Graded Corner` (`IsGradedCorner = true`, chấm đỏ). | Đổi loại đỉnh từ `Curve` (xanh) sang `Graded` (đỏ). | **PASS** | **PASS** | **FAIL (Chưa có thuật toán Smart Corner từ ảnh)** |
| 15 | **Delete corner** | Đổi đỉnh góc đang chọn/được click từ `Graded Corner` (đỏ) thành `Curve Point` (xanh). | `IsGradedCorner` chuyển thành `false`. | **PASS** | **PASS** | **WORKING (Hạ cấp góc xuống điểm cong)** |
| 16 | **Smart notch / Add notch / Delete notch** | `Smart notch` gán chung vào `Add notch`. **Lỗi tương tác**: Bấm nút `Add notch` trên Toolbar tự sinh ngay 1 dấu bấm ở giữa cạnh đang chọn; sau đó click lên cạnh sẽ thêm dấu bấm tại vị trí click. Không click chọn trực tiếp được dấu bấm trên canvas để xóa riêng lẻ. | Thêm/xóa `NotchItem` trên cạnh; sửa được chiều dài và góc trong bảng `Notch` bên phải. | **PASS** | **PASS** | **PARTIAL (`SmartNotch` giả lập & tự thêm khi bấm Toolbar)** |
| 17 | **Smart / Add / Delete internal point** | `Smart internal point` gán chung vào `Add internal point`. Bấm nút trên Toolbar tự sinh ngay 1 điểm ở tâm mảnh rập; click vào trong mảnh rập sẽ thêm điểm tại chỗ click. Không click chọn được điểm nội vi trên canvas. | Thêm/xóa `InternalPointItem` (vòng tròn màu cam); sửa được tọa độ `X, Y` và đường kính ở bảng bên phải. | **PASS** | **PASS** | **PARTIAL (`Smart` giả lập & tự thêm khi bấm Toolbar)** |
| 18 | **Smart / Add / Delete internal line** | `Smart internal line` gán chung vào `Add internal line`. Bấm nút trên Toolbar tự sinh 1 đường ngang ở giữa mảnh; click vào mảnh chỉ sinh đoạn thẳng ngang cố định dài `80 mm`, **chưa cho click 2 điểm đầu-cuối để vẽ đường theo hướng tùy ý trên canvas**. | Thêm/xóa `InternalLineItem` (`Simple line` hoặc `Grain axis` có mũi tên xanh lá); nút `Flip` đảo chiều mũi tên 180°. | **PASS** | **PASS** | **PARTIAL (Không vẽ 2 điểm tự do bằng chuột được)** |
| 19 | **Add / Delete auxiliary piece** | `Add auxiliary piece` tạo ngay một mảnh chữ nhật `20 × 15 cm` mới trên canvas; `Delete auxiliary piece` xóa mảnh đang chọn. | Thêm/xóa `PatternPiece` trong `DomainPieces` và `StockPieces`. | **PASS** | **PASS** | **WORKING (Tạo mảnh chuẩn cố định)** |
| 20 | **Measure on perimeter / Measure direct** | `MeasurePerimeter`: Click 2 đỉnh trên mảnh rập để tính chiều dài cung dọc theo chu vi.<br>`MeasureDirect`: Click 2 vị trí bên trong mảnh rập để tính khoảng cách thẳng.<br>**Thiếu sót**: Chỉ hiện kết quả ở `StatusBar` dưới đáy, **chưa vẽ đường đo trực quan và nhãn số đo lên canvas**. | Không làm thay đổi hình học. | N/A | N/A | **PARTIAL (Thiếu hiển thị đường đo trên Canvas)** |
| 21 | **Play contour** (+ Dropdown) | Mỗi lần bấm nhảy `SelectedVertexIndex` sang đỉnh kế tiếp và hiện góc nội tiếp ở `StatusBar`. Menu con `Play grade points`, `Play notch`, `Play internal points`, `Play internal lines` chỉ chuyển tab bảng bên phải chứ chưa duyệt qua từng đối tượng tương ứng. | Không làm thay đổi hình học. | N/A | N/A | **PARTIAL** |

---

## 6. KIỂM TOÁN ĐỘ CHÍNH XÁC XUẤT FILE DXF (TÁCH BIỆT SAI SỐ XUẤT FILE VÀ SAI SỐ NHẬN DIỆN)

Chúng tôi đã tạo 2 chi tiết chuẩn có kích thước giải tích tuyệt đối trong [`AuditRunner`](file:///C:/Users/ASROCK/.gemini/antigravity/brain/6720b619-a92f-4133-a8db-965ffece5e3f/scratch/AuditRunner/Program.cs), xuất ra file `.dxf` và đọc lại bằng bộ phân tích độc lập [`FileEngine.ParseAndValidateDxfFile`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs#L307-L501):

### 6.1. Trường hợp 6A — Chi tiết cạnh thẳng: Hình chữ nhật chuẩn `200.000 × 100.000 mm`
- **File kiểm thử**: [`artifacts/acceptance-audit/audit_exact_rect_200x100mm.dxf`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/audit_exact_rect_200x100mm.dxf)

| Thông số | Giá trị lý thuyết | Giá trị đọc lại từ file DXF | Sai số thuần của bộ xuất DXF |
|---|---|---|---|
| **Đơn vị (`$INSUNITS`)** | `4` (`Millimeters`) | `4` (`Millimeters`) | `0` |
| **Width** | `200.0000 mm` | `200.0000 mm` | **`0.000000 mm`** |
| **Height** | `100.0000 mm` | `100.0000 mm` | **`0.000000 mm`** |
| **Perimeter** | `600.0000 mm` | `600.0000 mm` | **`0.000000 mm`** |
| **Area** | `20000.0000 mm²` (`200.0000 cm²`) | `20000.0000 mm²` (`200.0000 cm²`) | **`0.000000 mm²`** |

### 6.2. Trường hợp 6B — Chi tiết cong: Hình tròn bán kính $R = 100.000\text{ mm}$ (Đường kính `200.000 mm`, tạo bởi 4 cung Cubic Bézier $\kappa = 0.55228475$)
- **File kiểm thử**: [`artifacts/acceptance-audit/audit_bezier_circle_R100mm.dxf`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/audit_bezier_circle_R100mm.dxf) (rời rạc hóa thành `POLYLINE` khép kín gồm `96` đỉnh)

| Thông số | Hình tròn giải tích ($\pi$) | Mô hình Cubic Bézier trong bộ nhớ | Giá trị đọc lại từ file DXF (`POLYLINE`) | Sai số rời rạc hóa DXF so với Mô hình Bézier |
|---|---|---|---|---|
| **Width** | `200.0000 mm` | `200.0000 mm` | `200.0000 mm` | **`0.0000 mm`** |
| **Height** | `200.0000 mm` | `200.0000 mm` | `200.0000 mm` | **`0.0000 mm`** |
| **Perimeter** | `628.3185 mm` | `628.4067 mm` | `628.2944 mm` | **`0.1123 mm` (`0.018%`)** |
| **Area** | `31415.93 mm²` | `31424.72 mm²` | `31402.26 mm²` | **`22.46 mm²` (`0.0715%`)** |
| **Độ võng dây cung lớn nhất (`Max Chord Sagitta Error`)** | `0.0000 mm` | `0.0000 mm` | `0.0567 mm` | **`0.0567 mm`** |

### 6.3. Phân Biệt Rõ 3 Nguồn Sai Số

1. **Sai số thuần của bộ xuất DXF (`FileEngine.ExportDxf`)**:
   - Bằng **`0.0000 mm`** đối với các đoạn thẳng (`Line`), và **`< 0.06 mm`** (độ võng dây cung) / **`0.018%` chu vi** đối với các đoạn cong `Cubic Bézier` (do rời rạc hóa mỗi đoạn Bézier thành 24–32 đỉnh `VERTEX` trên `POLYLINE`).
2. **Sai số của bộ hiệu chuẩn (`CalibrationEngine`)**:
   - Trong Mode A trên ảnh bảng thật `cal_grid_117cm_20o.jpg`, do chưa hiệu chỉnh méo quang học/phối cảnh 2D mà chỉ lấy bước lưới trung bình 1D (`120.49 px` ngang, `120.13 px` dọc), sai số vị trí lưới trung bình là **`1.51 mm`** và cực đại ở rìa ảnh là **`5.23 mm`**.
3. **Sai số của bộ nhận diện ảnh (`DetectionEngine`)**:
   - Trên giấy trắng cạnh thẳng (`sample_03172025_111128.jpg`), độ lệch RMS so với biên thô là **`4.68–4.76 px` (`~1.95 mm`)** (chủ yếu tại vị trí các cục nam châm nằm sát mép giấy).
   - Trên vải bò tối màu (`real_01082026_091623.jpg`), độ lệch RMS dọc theo mép vải thường là **`2.20–2.35 px` (`~0.95 mm`)**, nhưng tại vị trí cục nam châm trắng đặt lọt vào hõm cong của `Piece 2` `(1700, 1350)`, cục nam châm bị gộp vào tiền cảnh gây sai lệch cục bộ **`~38–42 px` (`~16–17.5 mm`)**.

---

## 7. KIỂM TRA ĐẦU CUỐI (END-TO-END WORKFLOW AUDIT)

Chu trình kiểm tra: `Open Image → Calibrate → Detect → Select → Edit → Measure → Save → Close → Reopen → Export DXF`

| Bước | Kết quả chạy qua ViewModel / Code (`BackendEnginesTests`) | Kết quả chạy trực tiếp trên Cửa sổ UI (`MainWindow`) | Trạng thái Nghiệm Thu Bước |
|---|---|---|---|
| **1. Open Image** | `vm.LoadImageFromFile` nạp đúng pixel `3456×2592`. | Mở được ảnh qua `Load picture (Ctrl+O)`, **nhưng ảnh `180 DPI` bị vẽ nhỏ bằng `53.33%` kích thước thật trên `RealImageCanvas`**. | **FAIL (Lỗi hiển thị DPI trên UI)** |
| **2. Calibrate** | `CalibrateModeAGrid` & `CalibrateModeBManualRectangle` tính đúng `ScaleX`, `ScaleY`. | Mode A bấm `Calibrate` chạy được nhưng chưa vẽ lưới kiểm chứng lên ảnh; Mode B không vẽ được khung chữ nhật bằng chuột trên UI. | **PARTIAL** |
| **3. Detect** | Phát hiện đúng `3`, `2`, `3` mảnh trên 3 ảnh thật. | Bấm nút tam giác xanh phát hiện đúng số mảnh, cập nhật `Stock Area`, **nhưng đường bao đỏ trên `MainWindow` bị phóng to lệch khỏi ảnh nền `180 DPI`** (xem [`ui_2_detected_093723.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_2_detected_093723.png)). | **FAIL (Lệch vị trí hiển thị trên UI)** |
| **4. Select** | `SelectDomainPiece` chọn đúng mảnh và đỉnh, cập nhật bảng phải. | Click vào mảnh rập hoặc thẻ ở `Stock Area` chọn được mảnh (sau khi đã Detect), đổi sang màu vàng. | **PASS** |
| **5. Edit** | Kéo đỉnh, kéo tay nắm Bézier, đổi Line/Curve, thêm điểm De Casteljau, thêm Notch/Internal Line hoạt động đúng trong Engine. | Kéo đỉnh và tay nắm Bézier bằng chuột hoạt động; tuy nhiên bấm nút `Add Point`, `Add Notch`, `Add Internal Line` trên Toolbar tự chèn ngay vào vị trí mặc định thay vì đợi click lên vị trí mong muốn trên canvas. | **PARTIAL** |
| **6. Measure** | Tính đúng khoảng cách thẳng và khoảng cách dọc theo chu vi bằng `mm`/`cm`. | Hiện số đo đúng trên `StatusBar` khi click 2 đỉnh, nhưng không vẽ đường đo lên canvas. | **PARTIAL** |
| **7. Save (`.pdp`)** | Ghi đầy đủ JSON dự án ra [`e2e_validated_project.pdp`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/e2e_validated_project.pdp). | Hộp thoại `Save (Ctrl+S)` lưu file `.pdp` thành công. | **PASS** |
| **8. Close & Reopen** | `LoadProjectFromFile` nạp lại đúng toàn bộ mảnh, đỉnh, đường cong, notch, internal line và calibration. | Mở lại file `.pdp` qua `Load picture (Ctrl+O)` khôi phục đúng toàn bộ dữ liệu. | **PASS** |
| **9. Export DXF** | Xuất file [ `e2e_validated_export.dxf`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/e2e_validated_export.dxf) đạt chuẩn `$INSUNITS=4`, khớp kích thước `< 0.05 mm`. | Mở hộp thoại `Export DXF`, chọn profile và lưu file `.dxf` thành công. | **PASS** |

> **Kết luận Mục 7**: Theo đúng quy tắc *"Không đánh dấu PASS nếu một bước chưa thực hiện được"*, quy trình End-to-End đạt **PASS ở tầng Engine/ViewModel trong bộ nhớ**, nhưng **CHƯA ĐẠT PASS (`FAIL / PARTIAL`) trên giao diện người dùng thực tế** do lỗi lệch tỷ lệ DPI ở bước 1 & 3 và lỗi hành vi chuột của các công cụ ở bước 2, 5, 6.

---

## 8. TỔNG HỢP PHÂN LOẠI CHỨC NĂNG & DANH SÁCH VIỆC PHẢI SỬA

### 8.1. Phân loại trạng thái thực tế

1. **Chức năng hoạt động thực tế (cả Backend lẫn UI)**:
   - Tính diện tích khép kín bằng định lý Green + Gauss-Legendre 5 điểm và tính chu vi đường cong Cubic Bézier (`MeasurementEngine`).
   - Chia đoạn đường cong Cubic Bézier bằng thuật toán De Casteljau bảo toàn 100% hình dáng đường cong (`CubicBezierSegment.SplitAt`).
   - Kéo di chuyển đỉnh kèm 2 tay nắm điều khiển Bézier (`MoveVertexByDelta` / `MiscaPuncteSimple`), kéo di chuyển cả mảnh rập (`MovePieceByDelta`), kéo trực tiếp 2 tay nắm Bézier (`C1`, `C2`) trên canvas.
   - Dịch chuyển điểm bằng tọa độ tuyệt đối (`Move to X, Y`) và tương đối (`Move with dX, dY`) từ bảng bên phải.
   - Đổi loại đỉnh (`graded point` ↔ `curve point`), đặt điểm gốc (`Piece origin`), đổi loại đường nội vi (`Simple line` ↔ `Grain axis`), đảo chiều đường canh sợi (`Flip`).
   - `Undo` (`Ctrl+Z`) và `Redo` (`Ctrl+Y`) đa bước bằng snapshot toàn cục.
   - Bật/tắt hiển thị Grid (`Alt+G`), Picture (`Alt+H`), Notches (`Alt+N`), Internal Lines (`Alt+L`), Internal Points (`Alt+P`).
   - Lưu/Mở dự án `.pdp` và xuất file `.dxf` chuẩn `$INSUNITS=4` (`mm`) kèm kiểm chứng bằng parser DXF độc lập.
2. **Chức năng mới chỉ đạt qua Unit Test / ViewModel (Bị lỗi hoặc thiếu tương tác khi dùng chuột trên UI)**:
   - **Hiển thị khớp lớp phủ Vector trên ảnh thật có DPI $\ne 96$** (Trong file PNG xuất từ `RawImageBuffer` thì khớp, nhưng trên `MainWindow` WPF bị lệch tỷ lệ $96/180$).
   - **Mode B — Vẽ hình chữ nhật chuẩn bằng chuột & Giới hạn nhận diện trong ROI (`RestrictDetectionToCropBox = true`)**: Có trong `CalibrationEngine` và `DetectionEngine`, nhưng chưa có thao tác vẽ chuột và chưa có checkbox bật trên UI.
   - **Chèn điểm (`Add Point`) tại vị trí bất kỳ trên cạnh**: `EditingEngine.AddPointOnSegment(piece, segIdx, t)` hỗ trợ tham số $t \in (0, 1)$ bất kỳ, nhưng nút Toolbar trên UI đang truyền cứng $t = 0.5$.
3. **Chức năng chưa hoàn thiện hoặc bị gán giả lập (`Aliased`)**:
   - `Smart Corner`, `Smart Notch`, `Smart Internal Point`, `Smart Internal Line`: Đang bị gán chung vào hàm tạo thủ công/đổi cờ thay vì dò tìm đặc trưng từ ảnh trong vùng chọn.
   - `Zoom to selected area`: Chỉ gọi `ZoomIn()` khi click chứ chưa cho kéo khung chữ nhật zoom.
   - `Add Internal Line` bằng chuột: Chỉ tạo đoạn thẳng ngang cố định dài `80 mm` tại chỗ click thay vì cho phép click 2 điểm đầu–cuối.
   - Không click chọn trực tiếp được `Notch`, `Internal Point`, `Internal Line` đã vẽ trên canvas để sửa/xóa riêng từng đối tượng.
   - `Measure on perimeter` và `Measure direct distance`: Mới chỉ in kết quả ra `StatusBar`, chưa vẽ đường đo và nhãn kích thước trực tiếp lên canvas.
4. **Chức năng có lỗi trong thuật toán xử lý ảnh (`DetectionEngine` & `CalibrationEngine`)**:
   - Cục nam châm tròn màu sáng đặt sát hõm cong của mảnh vải tối màu (`Piece 2` trong `real_01082026_091623.jpg` tại `(1700, 1350)`) bị gộp vào mặt nạ của mảnh vải.
   - Các góc vuông của mảnh giấy trắng (`real_01232026_093723.jpg`) bị bo tròn bởi `MorphologicalClose(radius: 7)` làm mất cờ `Graded point` ở góc dưới-phải của `Piece 2` và sinh điểm góc giả giữa đường cong cổ của `Piece 1`.
   - Còn sử dụng tọa độ vùng lóa gán cứng (`sx < 790 && sy > 1150`), kiểm tra tên file `"111128"`, và gán cứng `27 × 20` ô lưới khi `img.Width == 3456`.

---

### 8.2. Danh Mục Những Việc Phải Sửa (Prioritized Fix List)

1. **[P0 — Sửa ngay lỗi lệch tỷ lệ DPI trên `MainWindow`]**:
   - Trong [`RawImageBuffer.ToBitmapSource()`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/ImageEngine.cs#L60-L73), luôn khởi tạo `BitmapSource.Create` với `96.0, 96.0` DPI (trong khi vẫn lưu `OriginalDpiX`, `OriginalDpiY` trong `RawImageBuffer` phục vụ thông tin ảnh) để `1 pixel` của `WorkspaceImageControl` luôn bằng đúng `1 DIP` của `InteractiveVectorOverlayCanvas`.
   - Cho phép thao tác chọn/kéo trực tiếp hoặc tự động chuyển sang `RealImageCanvas` ngay khi người dùng tương tác với mảnh rập lúc mới mở ứng dụng.
2. **[P1 — Sửa cơ chế tương tác chuột của các công cụ trên Toolbar (`MainWindow.xaml.cs`)]**:
   - Tách biệt hành động **Bấm nút trên Toolbar = Chọn chế độ công cụ (`ActiveTool`)** với hành động **Click lên Canvas = Thực thi công cụ tại đúng tọa độ chuột**:
     - `Add Point`: Click lên đường viền tại vị trí nào sẽ chiếu vuông góc tìm `(nearestSeg, t)` và chia đường cong De Casteljau tại đúng tham số `t` đó.
     - `Add Notch` / `Add Internal Point`: Chỉ thêm khi người dùng click lên đường bao / bên trong mảnh rập (không tự động sinh đối tượng ngay lúc bấm nút trên Toolbar).
     - `Add Internal Line`: Cho phép click 2 điểm `(Start, End)` bên trong mảnh rập để vẽ đoạn thẳng hoặc đường canh sợi theo bất kỳ góc nào.
     - Gắn sự kiện `MouseLeftButtonDown` vào các hình vẽ `Notch`, `InternalPoint`, `InternalLine` trên canvas để click chọn/xóa trực tiếp.
     - Vẽ đường đo trực quan và nhãn kết quả (`cm` / `mm`) ngay trên `InteractiveVectorOverlayCanvas` cho `MeasurePerimeter` và `MeasureDirect`, cũng như khung kéo chọn cho `ZoomArea`.
   - Vô hiệu hóa hoặc tách rõ các nút `Smart Corner`, `Smart Notch`, `Smart Internal Point`, `Smart Internal Line` thay vì gán giả vào lệnh thêm thủ công.
3. **[P1 — Hoàn thiện tương tác UI cho Mode A & Mode B Calibration]**:
   - Cho phép kéo chuột vẽ khung hình chữ nhật chuẩn (`Cropping box` / `ManualCalibrationBoxPx`) trực tiếp trên canvas và hiển thị các giao điểm lưới tìm được trong `CalibrationWindow`.
   - Loại bỏ dòng gán cứng `estimatedCells = isVerticalLines ? 27 : 20` trong `CalibrationEngine.cs:379-382` và tính số ô thực tế từ danh sách đường lưới phát hiện được.
4. **[P2 — Nâng cấp độ chính xác `DetectionEngine` trên ảnh thật]**:
   - Loại bỏ điều kiện kiểm tra tên file `"111128"` và vùng tọa độ gán cứng `isLeftGlareZone`; thay bằng ước lượng nền cục bộ theo lưới (`Local Background Illumination Grid`).
   - Phát hiện và trừ bỏ các vòng tròn nam châm giữ rập (`Hough Circle` / `Annular Bright Ring Detector`) trước khi gộp mặt nạ vải màu tối để nam châm sát mép không làm phồng biên `Piece 2` trên ảnh `01082026 091623.jpg`.
   - Giảm bán kính đóng hình thái học và giao cắt tiếp tuyến hai bên đỉnh góc để bảo toàn các góc vuông sắc nét trên ảnh `01232026 093723.jpg`.

---

## 9. ĐƯỜNG DẪN CÁC FILE KIỂM THỬ & ẢNH CHỤP MINH CHỨNG

- **Log số liệu kiểm toán định lượng**: [`artifacts/acceptance-audit/audit_diagnostics_output.txt`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/audit_diagnostics_output.txt)
- **Ảnh chụp trực tiếp cửa sổ `MainWindow` (phát hiện lỗi lệch DPI trên ảnh `180 DPI`)**:
  - [`ui_1_startup_state.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_1_startup_state.png)
  - [`ui_2_detected_093723.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_2_detected_093723.png)
  - [`ui_3_edited_piece1.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_3_edited_piece1.png)
  - [`ui_4_detected_091623.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/ui_4_detected_091623.png)
- **Bản đồ đối chiếu biên thô (`Cyan`) vs biên khớp Vector (`Red`) trên 3 ảnh thật**:
  - [`sample_03172025_111128_audit_comparison.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/sample_03172025_111128_audit_comparison.png)
  - [`real_01082026_091623_audit_comparison.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01082026_091623_audit_comparison.png)
  - [`real_01232026_093723_audit_comparison.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/real_01232026_093723_audit_comparison.png)
- **Kiểm thử giới hạn vùng nhận diện Mode B ROI**: [`artifacts/acceptance-audit/modeB_roi_only/8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/modeB_roi_only/8_final_overlay.png)
- **File DXF kiểm chứng độc lập**:
  - [`audit_exact_rect_200x100mm.dxf`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/audit_exact_rect_200x100mm.dxf)
  - [`audit_bezier_circle_R100mm.dxf`](file:///E:/claude/GEMHIHI/artifacts/acceptance-audit/audit_bezier_circle_R100mm.dxf)
  - [`e2e_validated_export.dxf`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/e2e_validated_export.dxf)
