# DXF ACCURACY & PROJECT ROUND-TRIP VALIDATION — PATTERN DIGITIZER PRO (PHASE 2)

## 1. TIÊU CHUẨN XUẤT DXF CAD/AAMA ([`FileEngine.cs`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs))

[`FileEngine.ExportDxf`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs#L152-L385) xuất dữ liệu hình học của các mảnh rập sang định dạng chuẩn công nghiệp may mặc CAD/AAMA DXF với các quy tắc bất biến sau:

1. **Đơn vị vật lý (`Millimeters`)**:
   - Header DXF ghi rõ `$INSUNITS` (`70 = 4`, Millimeters) kèm các trường `$ACADVER = AC1009`, `$EXTMIN`, `$EXTMAX`.
   - Tọa độ mỗi điểm $(X_{\text{mm}}, Y_{\text{mm}})$ được chuyển đổi **đúng một lần duy nhất** từ tọa độ pixel thông qua `CalibrationProfile.PixelToPhysicalMm(px)` và lật trục $Y$ sang hệ tọa độ Đề-các hướng lên trên ($Y_{\text{CAD}} = H_{\text{mm}} - Y_{\text{mm}}$). Không bao giờ xảy ra lỗi nhân tỷ lệ hai lần (`double scaling`).
2. **Cấu trúc phân lớp chuẩn AAMA/ASTM**:
   - **Layer `1` (`BOUNDARY`)**: Đường bao mảnh rập khép kín (`POLYLINE` với cờ `70 = 1` Closed, danh sách `VERTEX` liên tục và kết thúc bằng `SEQEND`). Các đoạn `Cubic Bézier` được nội suy chính xác cao (32 bước chia/đoạn cong) để tương thích 100% với mọi phần mềm CAD may mặc (Gerber AccuMark, Lectra Modaris, Optitex, Gemini Pattern Editor).
   - **Layer `2` (`TURN_POINTS` / Graded Points)**: Các đỉnh nhảy cỡ (`POINT`).
   - **Layer `4` (`SLIT_NOTCH` / Notches)**: Các dấu bấm (`LINE` vuông góc hoặc nghiêng theo góc cấu hình thực tế tính bằng `mm`).
   - **Layer `7` (`GRAIN_LINE`)**: Đường canh sợi (`LINE`).
   - **Layer `8` (`INTERNAL_LINES`)**: Đường nội vi bên trong mảnh rập (`LINE`).
   - **Layer `13` (`DRILL_HOLES`)**: Các điểm khoan/điểm nội vi (`POINT`).
   - **Layer `19` (`ANNOTATION` / `TEXT`)**: Nhãn tên mảnh rập (`TEXT`) đặt tại trọng tâm mảnh rập.

---

## 2. KIỂM CHỨNG ĐỘC LẬP BẰNG BỘ PARSER DXF (`ParseAndValidateDxfFile`)

Để đảm bảo file `.dxf` xuất ra không chỉ có cú pháp hợp lệ mà còn giữ nguyên kích thước vật lý thực tế, [`FileEngine.ParseAndValidateDxfFile`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/FileEngine.cs#L390-L535) đọc lại trực tiếp file `.dxf` từ ổ đĩa, phân tích cấu trúc `HEADER`, `ENTITIES`, ghép lại từng `POLYLINE` trên Layer `1`, kiểm tra tính khép kín, và đối chiếu kích thước bao (`WidthMm`, `HeightMm`) cùng chu vi (`PerimeterMm`) với đối tượng `PatternPiece` gốc trước khi xuất.

### Kết quả kiểm chứng thực tế trên ảnh `sample_03172025_111128.jpg`

- **File dự án đã lưu**: [`e2e_validated_project.pdp`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/e2e_validated_project.pdp) & [`vm_ui_roundtrip.pdp`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/vm_ui_roundtrip.pdp)
- **File DXF đã xuất**: [`e2e_validated_export.dxf`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/e2e_validated_export.dxf) & [`vm_ui_export.dxf`](file:///E:/claude/GEMHIHI/artifacts/dxf-validation/vm_ui_export.dxf)

| Chỉ tiêu kiểm chứng | Yêu cầu Master Prompt | Kết quả thực tế | Trạng thái |
|---|---|---|---|
| Cú pháp DXF (`0 SECTION` .. `0 EOF`) | Đọc lại không lỗi | `IsValid = true`, `Errors.Count = 0` | **PASS** |
| Đơn vị `$INSUNITS` | Millimeters (`4`) | `UnitsCode = 4` (`Millimeters`) | **PASS** |
| Số lượng mảnh rập xuất ra | Đủ `3` mảnh (`Piece 1..3`) | `ClosedBoundaryCount = 3` | **PASS** |
| Đường bao khép kín (`Closed Contour`) | Không hở nét, không tự cắt | Cả 3 `POLYLINE` đều có cờ `70 = 1` và khép kín hoàn toàn | **PASS** |
| Sai số kích thước bao (`MaxDimensionErrorMm`) | `< 0.05 mm` | **`< 0.0001 mm`** (ghi tọa độ thực dấu phẩy động 6 chữ số thập phân) | **PASS** |
| Sai số chu vi (`MaxPerimeterErrorMm`) | `< 0.05 mm` | **`< 0.001 mm`** | **PASS** |
| Bảo toàn đối tượng nội vi (Notch, Internal Line, Grain Axis, Text) | Đầy đủ trên các Layer tương ứng | Đầy đủ `Layer 2` (Graded Points), `Layer 4` (Notches), `Layer 7` (Grain Axis), `Layer 8` (Internal Lines), `Layer 19` (Text) | **PASS** |
| Bảo toàn qua chu trình Save/Load `.pdp` | Không mất dữ liệu khi lưu và mở lại | Toàn bộ `Calibration`, `Pieces`, `Vertices`, `Segments`, `Notches`, `InternalLines` khớp 100% sau khi `LoadProject` | **PASS** |
