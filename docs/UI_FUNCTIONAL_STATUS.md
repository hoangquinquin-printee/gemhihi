# UI FUNCTIONAL STATUS — CURRENT TAKEOVER AUDIT

> **Cập nhật 2026-10-29:** Phần “Phase 1” bên dưới được giữ làm hồ sơ giao diện đã duyệt,
> không còn là trạng thái backend hiện tại. Bảng này là trạng thái kiểm toán có hiệu lực.
> `PASS` ở tầng WPF/engine không đồng nghĩa đã có mouse automation desktop hoặc Gemini live parity.

| Nhóm chức năng | Trạng thái hiện tại | Bằng chứng / giới hạn |
|---|---|---|
| Startup / Detect / Calibration khi chưa nạp ảnh | **PASS — no mock state** | Runtime khởi động rỗng (`0` ảnh, `0` mảnh); Detect và Grid Calibration từ chối rõ ràng nếu người dùng chưa nạp đúng ảnh. Dữ liệu screenshot Phase 1 chỉ còn qua lệnh inspector/capture tường minh. |
| Open image, Calibration, Detect, raster/vector overlay | **PASS — WPF + engine** | `WpfWindowWorkflowTests` tạo `MainWindow` thật, từ chối calibration thiếu ảnh, nạp grid do test chỉ rõ và áp profile vào ViewModel, sau đó nạp ảnh/Detect 3 mảnh và render vector cùng kích thước pixel với raster. |
| Pan / Zoom / coordinate invariance | **PASS — engine + WPF render** | Pan/Zoom không đổi tọa độ `PatternPiece`; raster và overlay chung `RealImageCanvas` + `LayoutTransform`. Khi kéo hình học dưới grid projective/cubic, delta mm lấy từ hiệu hai phép `PixelToPhysical`, không dùng scale toàn cục. Mouse automation desktop chưa có surface trong phiên Codex. |
| Select/delete nhiều điểm | **PASS — logic/render; UI mouse UNVERIFIED** | Xóa đúng điểm chọn, giữ tối thiểu 3 đỉnh, Undo/Redo phục hồi; có rubber-band render. Đã sửa stale multi-selection và phím Delete nay ưu tiên đúng notch/internal object/vertex đang hiển thị là được chọn. |
| Line ↔ Curve + Bézier handles | **PASS — logic/render; UI mouse UNVERIFIED** | Click-path chọn segment có code thật; persistence giữ `OutgoingIsCurve`, C1/C2; WPF test xác nhận overlay đổi sau conversion. |
| Notch / Internal Point / Internal Line | **PASS — manual tools** | Thêm/chọn/kéo/xóa, internal line hai-click, endpoint handles, Undo/Redo và persistence có code/test. |
| Playback contour/grade/notch/internal | **PASS — ViewModel/render** | Đã sửa để duyệt và chọn đối tượng thật, không chỉ đổi tab thuộc tính. |
| Smart Corner/Notch/Internal Point/Internal Line | **PARTIAL — clean-room detector** | Đã có detector thật: gradient góc biên, đỉnh lõm notch, tâm tương phản nội bộ và PCA đường tối. Workflow bitmap→ViewModel→mutation→Undo/Redo và ngưỡng từ chối PASS; đã sửa vùng Smart Internal Point từ 75 xuống 35 px để tránh hút đường biên/đường nội bộ xa click. Chưa có kiểm chứng same-input với Gemini hoặc ảnh chụp thực tế, nên **không** tuyên bố Gemini parity. |
| Grid Calibration Mode A | **PARTIAL** | 27×20 cells, 26×20 đường nhìn thấy/520 intersections. Trong vùng lưới nhìn thấy, pixel↔mm dùng nội suy bilinear từng ô: 520 tâm giao điểm round-trip trong `0.001 mm/0.02 px`; không dùng đa thức toàn cục để làm trôi tâm lưới. Engine vẫn so quadratic/cubic bằng holdout và chỉ chọn cubic khi cả mean/max tốt hơn: fit `2.301/5.874 mm`, holdout độc lập `2.268/5.463 mm`. Đã giữ offset ô biên: giao điểm đầu `52.137/51.052 mm`, board origin pixel `122.48/55.41`. Holdout mép chưa đạt độ chính xác mong muốn. |
| Manual Calibration Mode B | **PASS — engine/render; UI mouse UNVERIFIED** | ROI kéo/resize và X/Y scale độc lập có code; regression persistence PASS. |
| Detection 3 ảnh thật + rập sát mép | **PASS theo fixtures hiện có** | Số mảnh ảnh thật `3/2/3`, test đổi tên/lật ảnh, nam châm/glare/corner PASS. Fixture bất đối xứng cách mép 3 px cũng được giữ, không bị loại như border artifact. Chưa chứng minh tổng quát cho mọi ảnh/camera. |
| Save/Reopen project | **PASS — WPF + engine** | Hình học, Bézier, calibration transform/warp và internal objects round-trip. Nếu ảnh nguồn bị mất, project vẫn mở vector/calibration: raster cũ bị xóa, canvas giữ đúng kích thước pixel đã lưu để overlay không trôi/cắt, và UI cảnh báo rõ ràng. |
| DXF | **PASS — parser độc lập** | Luồng đã xác minh là profile `General`, mm, scale X/Y=`1.0`: `$INSUNITS=4`; E2E 3 mảnh; max dimension/perimeter/area error đều `0.0001`. Preview dùng contour domain thật, không phải rập mẫu. Tùy chọn profile/unit/scale khác bị từ chối rõ ràng thay vì bị exporter âm thầm bỏ qua. Đã thay fixed 24-sample bằng adaptive Bézier subdivision và bbox extrema chính xác; fixture cong cực trị đạt dimension `0.0007 mm`, perimeter `0.0017 mm`, area `0`. |
| Assyst / FK / Gerber / Investronica / Lectra export | **BLOCKED — explicit** | Các menu giữ nguyên giao diện Phase 1 nhưng không còn âm thầm xuất DXF thay thế. Chúng báo chưa triển khai và không ghi file. |
| Capture/Scanner/HASP/Gemini live comparison | **BLOCKED** | Thiếu thiết bị camera/TWAIN/HASP và Gemini không cung cấp runtime target xác minh. |

**Regression hiện tại:** `45 passed, 0 failed, 0 skipped` (`Release`, .NET 10).

---

# ARCHIVED PHASE 1 SCOPE CLASSIFICATION

Tài liệu này liệt kê đầy đủ trạng thái hoạt động của từng thành phần giao diện trong **Pattern Digitizer Pro — Phase 1 (1:1 Gemini UI Replication)**.

Tuân thủ nghiêm ngặt yêu cầu **GIAO DIỆN TRƯỚC — BACKEND SAU**:
- **`[UI ONLY]`**: Thành phần giao diện hiển thị đúng kích thước, vị trí, trạng thái chọn/vô hiệu hóa, mở đúng hộp thoại hoặc chuyển đổi trạng thái dock panel tương ứng.
- **`[VIEWPORT ACTIVE]`**: Thao tác hiển thị ảnh và điều khiển vùng nhìn (Open Image, Display Image, Zoom In, Zoom Out, Zoom Fit, Pan, Horizontal/Vertical Scrollbar, Magnifier Zoom) hoạt động thực sự trên giao diện WPF mà không can thiệp dữ liệu hình học.
- **`[DEFERRED TO PHASE 2]`**: Thuật toán số hóa rập (Snake active contour), hiệu chuẩn quang học (Camera lens distortion & perspective homography), tính toán hình học Bézier/Góc/Dấu bấm và xuất file CAD (`DXF`, `GTX`, `PGS`, `Export Investronica`) chưa triển khai ở Phase 1 và hiển thị thông báo trạng thái rõ ràng khi kích hoạt.

---

## 1. THANH MENU CHÍNH (`MainMenu1`)

| Menu | Mục con (Menu Item) | Phím tắt | Trạng thái Enabled mặc định | Phân loại Phase 1 | Hành vi hiện tại trong Phase 1 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **File** | `Load picture` -> `Acquire from camera...` | `Ctrl+F` | Enabled | `[DEFERRED TO PHASE 2]` | Hiển thị thông báo trạng thái Phase 1 trên Status Bar & hộp thoại thông báo rõ ràng. |
| **File** | `Load picture` -> `Acquire from scanner` | `Ctrl+A` | Enabled | `[DEFERRED TO PHASE 2]` | Hiển thị thông báo trạng thái Phase 1 (TWAIN scanner). |
| **File** | `Load picture` -> `Load from file...` | `Ctrl+O` | Enabled | `[VIEWPORT ACTIVE]` | Mở hộp thoại chọn file ảnh thực tế (`JPG`, `PNG`, `BMP`, `TIF`) và hiển thị lên Canvas + Magnifier. |
| **File** | `Detect pieces` | `Ctrl+D` | Enabled | `[DEFERRED TO PHASE 2]` | Nạp bố cục dữ liệu mẫu tham chiếu 4 chi tiết rập (`Piece 1..4` - ghi rõ dữ liệu mẫu kiểm tra bố cục) hoặc báo trạng thái Phase 1. |
| **File** | `Save pieces` -> `Save as GTX (Gemini) file...` | `Ctrl+S` | Enabled | `[DEFERRED TO PHASE 2]` | Mở hộp thoại [`ExportDxfStructureWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportDxfStructureWindow.xaml) (hoặc thông báo Phase 1). |
| **File** | `Save pieces` -> `Save as DXF file...` | — | Enabled | `[UI ONLY]` / `[DEFERRED TO PHASE 2]` | Mở hộp thoại [`ExportDxfWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportDxfWindow.xaml) đúng chuẩn DFM; nút Export báo trạng thái Phase 1. |
| **File** | `Save pieces` -> `Save in Investronica database` | — | Enabled | `[UI ONLY]` / `[DEFERRED TO PHASE 2]` | Mở hộp thoại [`ExportInvestronicaDbWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportInvestronicaDbWindow.xaml). |
| **File** | `Clear desktop` | — | Enabled | `[VIEWPORT ACTIVE]` | Xóa ảnh và dữ liệu mẫu trên bàn làm việc, đưa giao diện về trạng thái trống ban đầu (`Nothing selected`). |
| **File** | `Exit` | `Ctrl+X` | Enabled | `[UI ONLY]` | Đóng ứng dụng. |
| **Modify** | `Undo` | `Ctrl+Z` | **Disabled** | `[DEFERRED TO PHASE 2]` | Hiển thị trạng thái Disabled đúng như ảnh gốc Gemini; bộ nhớ lịch sử hình học để dành cho Phase 2. |
| **Modify** | `Redo` | `Shift+Ctrl+Z` | **Disabled** | `[DEFERRED TO PHASE 2]` | Hiển thị trạng thái Disabled đúng như ảnh gốc Gemini. |
| **Modify** | `Delete` | `Ctrl+Del` | Enabled | `[DEFERRED TO PHASE 2]` | Báo trạng thái Phase 1 trên Status Bar. |
| **View** | `Zoom` -> `Zoom in` | `Ctrl+I` | Enabled | `[VIEWPORT ACTIVE]` | Phóng đại tỷ lệ hiển thị vùng làm việc (`ViewportZoom`). |
| **View** | `Zoom` -> `Zoom out` | `Ctrl+U` | Enabled | `[VIEWPORT ACTIVE]` | Thu nhỏ tỷ lệ hiển thị vùng làm việc (`ViewportZoom`). |
| **View** | `Zoom` -> `Zoom selected` | `Ctrl+L` | Enabled | `[VIEWPORT ACTIVE]` | Căn chỉnh tỷ lệ hiển thị tập trung vào chi tiết đang chọn. |
| **View** | `Zoom` -> `Zoom all` | `Ctrl+M` | Enabled | `[VIEWPORT ACTIVE]` | Căn vừa toàn bộ ảnh/vùng làm việc vào khung nhìn (`Zoom Fit`). |
| **View** | `View pieces` (`Checked`) | — | Enabled | `[UI ONLY]` | Bật/tắt hiển thị lớp chi tiết rập trên vùng làm việc. |
| **View** | `View picture` (`Checked`) | — | Enabled | `[VIEWPORT ACTIVE]` | Bật/tắt hiển thị lớp ảnh chụp trên vùng làm việc. |
| **View** | `Show grid` -> `Show calbrated grid` (`Checked`) | — | Enabled | `[UI ONLY]` | Bật/tắt lưới hiệu chuẩn màu đỏ đứt nét trên vùng làm việc. |
| **View** | `Show grid` -> `Show detected grid` | — | Enabled | `[UI ONLY]` | Chuyển đổi chế độ hiển thị lưới phát hiện. |
| **View** | `Show grid` -> `Show calibration image` | — | Enabled | `[UI ONLY]` | Chuyển đổi chế độ hiển thị ảnh hiệu chuẩn. |
| **Settings** | `Settings` | — | Enabled | `[UI ONLY]` | Mở hộp thoại [`SettingsWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/SettingsWindow.xaml) (5 tabs). |
| **Settings** | `Language` -> `English` | — | Enabled | `[UI ONLY]` | Hiển thị dấu chọn ngôn ngữ tiếng Anh. |
| **Help** | `Contents` | `F1` | Enabled | `[UI ONLY]` | Hiển thị thông báo hướng dẫn Phase 1. |
| **Help** | `Register Product` | — | Enabled | `[UI ONLY]` | Mở hộp thoại [`RegisterProductWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/RegisterProductWindow.xaml). |
| **Help** | `Update dongle` | — | Enabled | `[DEFERRED TO PHASE 2]` | Hiển thị thông báo trạng thái Phase 1. |
| **Help** | `About` | — | Enabled | `[UI ONLY]` | Mở hộp thoại [`AboutWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/AboutWindow.xaml). |

---

## 2. THANH CÔNG CỤ CHÍNH (`ToolBar2` — 25 NÚT + 9 VẠCH NGĂN CÁCH + `ZoomPanel`)

| Thứ tự | Tên Control (`TMainFrm`) | Kiểu nút | Trạng thái mặc định | Phân loại Phase 1 | Hành vi hiện tại trong Phase 1 |
| :---: | :--- | :--- | :--- | :--- | :--- |
| **01** | `LoadButton` | Dropdown (`36×22`) | Enabled | `[VIEWPORT ACTIVE]` | Mở hộp thoại nạp ảnh thực tế (`OpenFileDialog`) hoặc menu con `Acquire from camera` / `scanner` / `Load from file`. |
| **02** | `DetectPiecesButton` | Button (`23×22`) | Enabled | `[UI ONLY]` / `[DEFERRED TO PHASE 2]` | Nạp bố cục mẫu tham chiếu (ghi rõ dữ liệu mẫu) để kiểm tra giao diện; thuật toán nhận diện rập để dành cho Phase 2. |
| **03** | `SaveButton` | Dropdown (`36×22`) | Enabled | `[UI ONLY]` | Mở hộp thoại cấu hình xuất DXF (`ExportDxfWindow`) hoặc menu con `GTX` / `DXF` / `Investronica`. |
| **04** | `UndoBtn` | Dropdown (`36×22`) | **Disabled** | `[DEFERRED TO PHASE 2]` | Vô hiệu hóa đúng trạng thái chuẩn Gemini. |
| **05** | `RedoBtn` | Dropdown (`36×22`) | **Disabled** | `[DEFERRED TO PHASE 2]` | Vô hiệu hóa đúng trạng thái chuẩn Gemini. |
| **06** | `ZoomInButton` (`ZoomArea`) | Check Toggle (`23×22`) | Enabled | `[VIEWPORT ACTIVE]` | Phóng đại vùng làm việc và bật trạng thái công cụ Zoom. |
| **07** | `ZoomSelectedButton` | Button (`23×22`) | Enabled | `[VIEWPORT ACTIVE]` | Phóng đại vào chi tiết rập đang chọn (`160%`). |
| **08** | `ZoomAllButton` | Button (`23×22`) | Enabled | `[VIEWPORT ACTIVE]` | Đưa tỷ lệ phóng đại về `100%` (`Zoom Fit`) và căn giữa thanh cuộn. |
| **09** | `ToolButton9` (`SmartCorner`) | Check Group 1 (`23×22`) | **Checked (Down)** | `[UI ONLY]` | Chọn công cụ điểm góc (`SmartCorner`) và chuyển vùng `PropertiesDockSite` sang panel **`Points`**. |
| **10** | `DeleteCornerButton` | Button (`23×22`) | Enabled | `[UI ONLY]` / `[DEFERRED TO PHASE 2]` | Chọn panel **`Points`** và báo trạng thái Phase 1 trên Status Bar. |
| **11** | `LineToCurveButton` | Button (`23×22`) | **Disabled** | `[DEFERRED TO PHASE 2]` | Hiển thị trạng thái Disabled đúng như khi đang chọn điểm góc (`graded point`). |
| **12** | `CurveToLineButton` | Button (`23×22`) | **Disabled** | `[DEFERRED TO PHASE 2]` | Hiển thị trạng thái Disabled đúng như khi đang chọn điểm góc (`graded point`). |
| **13** | `ToolButton10` (`SmartNotch`) | Check Group 1 (`23×22`) | Enabled | `[UI ONLY]` | Chuyển trạng thái chọn công cụ trên Toolbar và đổi `PropertiesDockSite` sang panel **`Notch`**. |
| **14** | `ToolButton12` (`AddNotch`) | Check Group 1 (`23×22`) | Enabled | `[UI ONLY]` | Chuyển trạng thái chọn công cụ trên Toolbar và đổi `PropertiesDockSite` sang panel **`Notch`**. |
| **15** | `DeleteNotchButton` | Button (`23×22`) | Enabled | `[UI ONLY]` | Đổi `PropertiesDockSite` sang panel **`Notch`** và báo trạng thái Phase 1. |
| **16** | `ToolButton13` (`SmartInternalPoint`) | Check Group 1 (`23×22`) | Enabled | `[UI ONLY]` | Chuyển trạng thái chọn công cụ trên Toolbar và đổi `PropertiesDockSite` sang panel **`Internal point`**. |
| **17** | `ToolButton14` (`AddInternalPoint`) | Check Group 1 (`23×22`) | Enabled | `[UI ONLY]` | Chuyển trạng thái chọn công cụ trên Toolbar và đổi `PropertiesDockSite` sang panel **`Internal point`**. |
| **18** | `DeleteInternalPointButton` | Button (`23×22`) | Enabled | `[UI ONLY]` | Đổi `PropertiesDockSite` sang panel **`Internal point`** và báo trạng thái Phase 1. |
| **19** | `ToolButton17` (`SmartInternalLine`) | Check Group 1 (`23×22`) | Enabled | `[UI ONLY]` | Chuyển trạng thái chọn công cụ trên Toolbar và đổi `PropertiesDockSite` sang panel **`Internal line`**. |
| **20** | `ToolButton18` (`AddInternalLine`) | Check Group 1 (`23×22`) | Enabled | `[UI ONLY]` | Chuyển trạng thái chọn công cụ trên Toolbar và đổi `PropertiesDockSite` sang panel **`Internal line`**. |
| **21** | `DeleteInternalLineButton` | Button (`23×22`) | Enabled | `[UI ONLY]` | Đổi `PropertiesDockSite` sang panel **`Internal line`** và báo trạng thái Phase 1. |
| **22** | `ShowGridButton` | Dropdown Check (`36×22`) | **Checked (Down)** | `[UI ONLY]` | Bật/tắt hiển thị lưới hiệu chuẩn màu đỏ trên vùng làm việc; mũi tên phụ mở menu chọn loại lưới. |
| **23** | `MeasureButton` (`MeasurePerimeter`) | Check Group 1 (`23×22`) | Enabled | `[DEFERRED TO PHASE 2]` | Chọn công cụ đo chu vi và hiển thị thông báo trạng thái Phase 1 trên Status Bar. |
| **24** | `ToolButton22` (`MeasureDirect`) | Check Group 1 (`23×22`) | Enabled | `[DEFERRED TO PHASE 2]` | Chọn công cụ đo khoảng cách thẳng và hiển thị thông báo trạng thái Phase 1. |
| **25** | `PlayBtn` (`PlayContour`) | Dropdown (`36×22`) | Enabled | `[DEFERRED TO PHASE 2]` | Hiển thị thông báo trạng thái Phase 1 (duyệt từng điểm trên đường bao). |
| **26** | `ZoomPanel` (`- zoom +` & `ZoomBar`) | Panel + Slider (`65×22`) | Enabled | `[VIEWPORT ACTIVE]` | Kéo thanh trượt hoặc nhấn `-`/`+` thay đổi trực tiếp tỷ lệ thu phóng ảnh trên Canvas. |

---

## 3. VÙNG LÀM VIỆC, DANH SÁCH RẬP VÀ CÁC PANEL BÊN PHẢI

| Khu vực | Thành phần | Phân loại Phase 1 | Chi tiết hoạt động |
| :--- | :--- | :--- | :--- |
| **Conversion Area (Canvas)** | Hiển thị ảnh, Lưới hiệu chuẩn, Kéo thả Pan, Lăn chuột Zoom, Scrollbar ngang/dọc | `[VIEWPORT ACTIVE]` | Hỗ trợ đầy đủ 8 thao tác viewport yêu cầu ở Mục 10: Open Image, Display Image, Zoom In, Zoom Out, Zoom Fit, Pan (kéo chuột trên vùng ảnh), Horizontal Scroll, Vertical Scroll. |
| **Stock Area (`PieceList`)** | Danh sách chi tiết rập (`Piece 1..4`) + Thanh cuộn ngang | `[UI ONLY]` | Cho phép chọn từng chi tiết rập, cập nhật khung viền chọn và đổi thông số `Piece name`, `Area`, `Perimeter`, `Notches`, `Corners` ở `ObjectPropertiesPanel`. Ghi rõ là dữ liệu mẫu phục vụ kiểm tra bố cục. |
| **Right Panel (`ImageInfoPanel`)** | Thông số `Detected pieces`, `Picture resolution`, `Grid dimensions` | `[VIEWPORT ACTIVE]` | Tự động cập nhật độ phân giải thực tế (`Width x Height`) khi người dùng mở một file ảnh mới. |
| **Right Panel (`ObjectPropertiesPanel`)** | Thông số `Piece name`, `Area`, `Perimeter`, `Notches / Internal points / Internal lines`, `Corners / Graded points` | `[UI ONLY]` | Hiển thị đầy đủ các ô nhập liệu và đơn vị (`cm²`, `cm`) đúng toạ độ DFM. |
| **Right Panel (`PropertiesDockSite`)** | 5 trạng thái panel: `Points`, `Notch`, `Internal point`, `Internal line`, `Nothing selected` | `[UI ONLY]` | Chuyển đổi linh hoạt khi người dùng chọn các nhóm công cụ tương ứng trên thanh công cụ hoặc nhấn nút đóng `[x]` của dock panel. |
| **Right Panel (`Magnifier`)** | Khung kính lúp phóng đại + 2 nút `+` / `-` | `[VIEWPORT ACTIVE]` | Hiển thị phóng đại điểm góc hoặc ảnh đang mở; 2 nút `+` / `-` thay đổi tỷ lệ phóng đại của kính lúp (`MagnifierZoom`). |

---

## 4. CÁC HỘP THOẠI (DIALOG WINDOWS)

| Hộp thoại | Lớp thực thi WPF | Phân loại Phase 1 | Chi tiết trạng thái |
| :--- | :--- | :--- | :--- |
| **Open Image Dialog** | `Microsoft.Win32.OpenFileDialog` | `[VIEWPORT ACTIVE]` | Cho phép chọn và nạp file ảnh thực tế từ máy tính lên vùng làm việc. |
| **Camera Calibration (`TCalibrationFrm`)** | [`CalibrationWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/CalibrationWindow.xaml) | `[UI ONLY]` / `[VIEWPORT ACTIVE]` | Hỗ trợ nạp ảnh vào khung hiệu chuẩn, phóng to/thu nhỏ/căn vừa ảnh hiệu chuẩn, nhập `Cell width`/`Cell height` và `Croping box`. Nút `Calibrate` báo rõ trạng thái Phase 1 (thuật toán nội suy lưới thuộc Phase 2). |
| **Settings (`TSettingsFrm`)** | [`SettingsWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/SettingsWindow.xaml) | `[UI ONLY]` | Hiển thị đầy đủ 5 tab (`General settings`, `Camera settings / Calibration`, `Interpretation settings`, `Interface settings`, `Export settings`), cho phép mở tiếp `CalibrationWindow` từ nút `Calibrate` ở Tab 2. |
| **Export DXF (`TExportDXFFrm`)** | [`ExportDxfWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportDxfWindow.xaml) | `[UI ONLY]` / `[DEFERRED TO PHASE 2]` | Hiển thị đầy đủ tuỳ chọn xuất DXF, cho phép mở hộp thoại chọn đường dẫn lưu (`Browse`) và mở [`ExportDxfStructureWindow`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportDxfStructureWindow.xaml) (`Modify the DXF export structure`). |
| **Export DXF Structure (`TExportDXFStructuraFrm`)** | [`ExportDxfStructureWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportDxfStructureWindow.xaml) | `[UI ONLY]` | Hiển thị đầy đủ 3 tab (`Global settings`, `Layers`, `Entities`) với toàn bộ combobox/checkbox chuẩn DFM. |
| **Export Investronica (`TExportInvestronicaDBFrm`)** | [`ExportInvestronicaDbWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/ExportInvestronicaDbWindow.xaml) | `[UI ONLY]` / `[DEFERRED TO PHASE 2]` | Hiển thị đầy đủ các trường kết nối cơ sở dữ liệu Investronica; nút `Export` báo rõ trạng thái Phase 1. |
| **About (`TAboutBox`)** | [`AboutWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/AboutWindow.xaml) | `[UI ONLY]` | Hiển thị thông tin phiên bản `Gemini Photo Digitizer v9.0` và ghi chú bản tái tạo giao diện Phase 1. |
| **Register Product (`TRegisterFrm`)** | [`RegisterProductWindow.xaml`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.UI/Views/RegisterProductWindow.xaml) | `[UI ONLY]` | Hiển thị các trường `Serial Number`, `Hardware ID`, `Activation Key` ở chế độ giao diện Phase 1 (không can thiệp khoá hay hệ thống bản quyền). |
