# REA & Ghidra Research Index — Gemini Photo Digitizer (`v9.0.0.0`)

Tài liệu này lập chỉ mục toàn bộ kết quả khảo sát trực tiếp ứng dụng gốc, các báo cáo REA + Ghidra đã lưu trữ, tài nguyên DFM nhị phân đã giải mã và ảnh chụp tham chiếu dùng cho **Pattern Digitizer Pro — Phase 1 (1:1 Gemini UI Replication)**.

> **Nguyên tắc bảo toàn**: Toàn bộ file tại `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer` và kho nghiên cứu cũ `E:\claude\GEMHIHI\Projects\PatternDigitizer_REA_REPORTS_KEEP_20261029\docs` chỉ được đọc (read-only), không sửa đổi hoặc xóa bất kỳ file nào.

---

## 1. Hạn chế khi mở trực tiếp `GeminiPhotoDigitizer.exe` & Phương pháp xác minh bổ sung

Thực hiện đúng Mục 4 của Master Prompt:
- Khi khởi chạy trực tiếp `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\GeminiPhotoDigitizer.exe` trên máy hiện tại (không cắm khóa cứng HASP/Sentinel vật lý hoặc cấu hình license đầy đủ), tiến trình tạo các cửa sổ Win32 sau (xác minh bằng `EnumWindows` / `GetWindowText` / `IsWindowVisible`):
  - `TSplashScreen` (`HWND`, `Visible = True`, kích thước `466 × 231`) hiển thị màn hình khởi động.
  - `TMainFrm` (`Caption = "GeminiPhotoDigitizer"`, `Visible = False`), `TSettingsFrm` (`Visible = False`), `TSelectedPointsPropertiesFrm` (`Visible = False`), `TNotchPropertiesFrm` (`Visible = False`), `TInternalPointPropertiesFrm` (`Visible = False`), `TInternalLinePropertiesFrm` (`Visible = False`) được khởi tạo ngầm trong bộ nhớ rồi tiến trình tự thoát sau bước kiểm tra khóa tại `TSplashScreen`.
- Do `TMainFrm` không chuyển sang trạng thái `Visible = True` khi thiếu khóa cứng, chúng tôi **báo rõ hạn chế này** và kết hợp **3 nguồn bằng chứng xác thực 100%** (không suy đoán bất kỳ thành phần nào):
  1. **Ảnh chụp màn hình thực tế của Gemini Photo Digitizer khi đang chạy** (đã lưu từ các phiên khảo sát trước và trong User Manual chính thức đi kèm bộ cài).
  2. **Giải mã nhị phân hoàn chỉnh 45/45 biểu mẫu VCL DFM (`TPF0`)** bên trong `GeminiPhotoDigitizer.exe` sang cây thuộc tính JSON chính xác tuyệt đối (bao gồm cả cờ boolean `Visible = True/False`, `Enabled = True/False`, `Left`, `Top`, `Width`, `Height`, `ImageIndex`, `ShortCut`, `Hint`, `TabOrder`).
  3. **Tài liệu hướng dẫn gốc 54 trang** (`C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\Gemini Photo Digitizer-User Manual.pdf`) chứa bảng giải thích từng icon trên thanh công cụ (trang 4–5) và ảnh chụp mọi hộp thoại, menu, trạng thái chọn công cụ (trang 12–54).

---

## 2. Ứng dụng gốc & Tài liệu gốc tại `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer`

| STT | Đường dẫn | Loại | Tình trạng | Nội dung sử dụng cho Phase 1 |
|---|---|---|---|---|
| 1 | `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\GeminiPhotoDigitizer.exe` | PE32 Borland C++Builder 6 VCL (`6,342,144` bytes) | Đã xác minh (Read-Only) | Chứa 45 form DFM (`TMainFrm`, `TCalibrationFrm`, `TSettingsFrm`, `TExportDXFFrm`, `TExportDXFStructuraFrm`, các panel thuộc tính...) |
| 2 | `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\Gemini Photo Digitizer-User Manual.pdf` | PDF (54 trang) | Đã đọc toàn bộ 54 trang | Trang 4–5: Bảng icon Toolbar đầy đủ; Trang 12–19: Calibration & Settings; Trang 20–43: Main Window, Menu, Toolbar, Properties panels; Trang 44–54: Export DXF & Structure Settings |
| 3 | `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\Help\GeminiPhotoDigitizerEn.chm` | HTML Help (`1,532,331` bytes) | Đã kiểm tra | Nội dung tương đương User Manual PDF |
| 4 | `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\Profiles\DXF\Export\*.txt` | 6 file cấu hình DXF Profile | Đã xác minh | Danh sách profile thực tế trong combobox `DXF Profile`: `General`, `Gemini Cut Plan`, `Gerber Accumark`, `Gerber TMP`, `Lectra Modaris`, `Lectraiba` |
| 5 | `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\Grid\grid_5_cm.PLT` | File lưới chuẩn | Đã xác minh | Lưới tham chiếu 5 cm đi kèm bộ cài |

---

## 3. Danh mục tài liệu REA + Ghidra cũ đã lưu trữ (`E:\claude\GEMHIHI\Projects\PatternDigitizer_REA_REPORTS_KEEP_20261029\docs`)

| STT | Tên tài liệu | Đường dẫn | Tình trạng | Mức độ ưu tiên & Nội dung chính |
|---|---|---|---|---|
| 1 | `GEMINI_UI_INVENTORY.md` | [GEMINI_UI_INVENTORY.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_UI_INVENTORY.md) | Đã xác minh | **Ưu tiên 1** — Kiểm kê các form DFM, cấu trúc `TMainFrm`, `TCalibrationFrm`, `TSettingsFrm`, `TExportDXFFrm` |
| 2 | `GEMINI_UI_COMPARISON.md` | [GEMINI_UI_COMPARISON.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_UI_COMPARISON.md) | Đã xác minh | **Ưu tiên 1** — Đối chiếu giao diện Gemini gốc và các sai khác cần tránh |
| 3 | `GEMINI_TOOL_FUNCTIONAL_AUDIT.md` | [GEMINI_TOOL_FUNCTIONAL_AUDIT.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_TOOL_FUNCTIONAL_AUDIT.md) | Đã xác minh | **Ưu tiên 1** — Phân tích 32 action trên `MainActionList`, phím tắt, gợi ý (Hint), các nút ẩn (`Visible = False`) |
| 4 | `GEMINI_FULL_REPLICATION_TRACKER.md` | [GEMINI_FULL_REPLICATION_TRACKER.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_FULL_REPLICATION_TRACKER.md) | Đã xác minh | **Ưu tiên 1** — Bảng theo dõi các hạng mục tái tạo toàn bộ |
| 5 | `REA_GEMINI_DEEP_ANALYSIS.md` | [REA_GEMINI_DEEP_ANALYSIS.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/REA_GEMINI_DEEP_ANALYSIS.md) | Đã xác minh | **Ưu tiên 1** — Phân tích chuyên sâu kiến trúc Borland VCL, `TImagine`, hệ thống panel dock |
| 6 | `REA_ANALYSIS.md` | [REA_ANALYSIS.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/REA_ANALYSIS.md) | Đã xác minh | Tổng quan phân tích nhị phân PE32, danh sách 45 DFM resources |
| 7 | `GEMINI_CALIBRATION_REVERSE_ENGINEERING.md` | [GEMINI_CALIBRATION_REVERSE_ENGINEERING.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_CALIBRATION_REVERSE_ENGINEERING.md) | Đã xác minh | Chi tiết giao diện `TCalibrationFrm` và tab `Camera settings / Calibration` |
| 8 | `GEMINI_TOOL_REVERSE_ENGINEERING.md` | [GEMINI_TOOL_REVERSE_ENGINEERING.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_TOOL_REVERSE_ENGINEERING.md) | Đã xác minh | Hành vi giao diện của từng công cụ trên thanh `MainTB` |
| 9 | `GEMINI_TOOL_COMPARISON.md` | [GEMINI_TOOL_COMPARISON.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_TOOL_COMPARISON.md) | Đã xác minh | Bảng đối chiếu trạng thái các công cụ trên Toolbar |
| 10 | `GEMINI_BEHAVIOR_COMPARISON.md` | [GEMINI_BEHAVIOR_COMPARISON.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/GEMINI_BEHAVIOR_COMPARISON.md) | Đã xác minh | Đối chiếu trạng thái giao diện, panel thuộc tính ngữ cảnh |
| 11 | `FEATURE_COMPARISON.md` | [FEATURE_COMPARISON.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/FEATURE_COMPARISON.md) | Đã xác minh | So sánh danh mục tính năng |
| 12 | `REA_GHIDRA_NATIVE_ANALYSIS.md` | [REA_GHIDRA_NATIVE_ANALYSIS.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/REA_GHIDRA_NATIVE_ANALYSIS.md) | Đã xác minh | Phân tích Ghidra các hàm sự kiện `TMainFrm`, `TCalibrationFrm`, `TSettingsFrm` |
| 13 | `REA_GHIDRA_READINESS.md` | [REA_GHIDRA_READINESS.md](file:///E:/claude/GEMHIHI/Projects/PatternDigitizer_REA_REPORTS_KEEP_20261029/docs/REA_GHIDRA_READINESS.md) | Đã xác minh | Trạng thái dữ liệu Ghidra |
| 14 | `REA_GHIDRA_EVIDENCE/*.json` | `E:\claude\GEMHIHI\Projects\PatternDigitizer_REA_REPORTS_KEEP_20261029\docs\REA_GHIDRA_EVIDENCE\` | Đã xác minh | 11 file JSON (`TMAINFRM_strings.txt`, `TCALIBRATIONFRM_strings.txt`, `TSETTINGSFRM_strings.txt`, `decompiled_Batch1..3.json`, `functions_categorized.json`...) |

---

## 4. Ảnh chụp Gemini gốc dùng làm chuẩn đối chiếu trực quan

Đã sao chép bản gốc vào [assets/images/reference](file:///E:/claude/GEMHIHI/assets/images/reference) và [artifacts/ui-comparison/reference](file:///E:/claude/GEMHIHI/artifacts/ui-comparison/reference):

| STT | Tên file | Kích thước | Nội dung hiển thị |
|---|---|---|---|
| 1 | [gemini_main_window.jpg](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_main_window.jpg) | `762 × 550` | Cửa sổ chính `GeminiPhotoDigitizer` sau khi nhận diện 4 chi tiết rập (`Piece 1`..`Piece 4`), hiển thị đầy đủ Menu bar, Toolbar, Conversion Area (nền xám, lưới đỏ nét đứt, biên dạng xanh lá, điểm nhảy cỡ/đường cong), Stock Area (4 thẻ rập nền trắng), Right Panel (`Detected pieces: 4`, `ObjectPropertiesPanel`, `Points` dock panel, `Magnifier` với nút `+`/`-`), và `StatusBar` (`Done detecting pieces!`) |
| 2 | [gemini_calibration_screen.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_calibration_screen.png) | `996 × 700` | Hộp thoại `Gemini PhotoDigitizer calibration` (`TCalibrationFrm`) với toolbar 5 nút, vùng ảnh lưới có thanh cuộn, cột thông số bên phải (`Cell width: 10 cm`, `Cell height: 10 cm`, `Croping box`, `Active area width: 120 cm`, `Active area height: 90 cm`, nút `Calibrate` và `Close`) |
| 3 | [gemini_full_ui.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_full_ui.png) | `544 × 390` | Hộp thoại `Settings Gemini Photo Digitizer` (`TSettingsFrm`) đang mở tab `Camera settings / Calibration` |
| 4 | [gemini_digitizer_workflow.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_digitizer_workflow.png) | `544 × 390` | Bản chụp thứ hai của hộp thoại `Settings Gemini Photo Digitizer` |
| 5 | [gemini_properties_panel.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_properties_panel.png) | `573 × 421` | Ảnh chụp cận cảnh vùng Canvas, thanh cuộn, Stock Area, vùng `Points` properties (`Name`, `Curve point`, `Graded point`, bảng `Size/dX/dY`, `Angle`), `Magnifier` (`+`/`-`) và `StatusBar` |
| 6 | [gemini_screen_full.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_screen_full.png) | `573 × 421` | Ảnh chụp cận cảnh vùng làm việc và bảng thuộc tính bên phải |
| 7 | [toolbar_strip_reference.png](file:///E:/claude/GEMHIHI/assets/images/reference/icons/toolbar_strip_reference.png) | `762 × 28` | Dải Toolbar cắt trực tiếp từ `gemini_main_window.jpg` dùng để so sánh vị trí, khoảng cách và hình dáng từng icon |

---

## 5. Bộ 45 Form DFM giải mã nhị phân hoàn chỉnh (`TPF0` -> JSON)

Được trích xuất bằng script giải mã DFM nhị phân từ `GeminiPhotoDigitizer.exe` (lưu tại thư mục scratch của phiên làm việc để tra cứu toạ độ pixel chính xác tuyệt đối):
- Biểu mẫu chính: `TMainFrm_MainFrm.json` (2,935 dòng)
- Các bảng thuộc tính ngữ cảnh (dock vào `PropertiesDockSite`):
  - `TSelectedPointsPropertiesFrm_SelectedPointsPropertiesFrm.json`
  - `TNotchPropertiesFrm_NotchPropertiesFrm.json`
  - `TInternalPointPropertiesFrm_InternalPointPropertiesFrm.json`
  - `TInternalLinePropertiesFrm_InternalLinePropertiesFrm.json`
- Các hộp thoại chính:
  - `TCalibrationFrm_CalibrationFrm.json`
  - `TSettingsFrm_SettingsFrm.json`
  - `TExportDXFFrm_ExportDXFFrm.json`
  - `TExportDXFStructuraFrm_ExportDXFStructuraFrm.json`
  - `TExportInvestronicaDBFrm_ExportInvestronicaDBFrm.json`
  - `TAboutBox_AboutBox.json`
  - `TRegisterFrm_RegisterFrm.json`
  - `TSplashScreen_SplashScreen.json`
  - Cùng 32 form Canon/WIA/TWAIN/Log/Update phụ trợ khác.
