# Gemini Photo Digitizer — Complete UI Inventory (1:1 Reference Specification)

Tài liệu này liệt kê đầy đủ và chính xác cấu trúc giao diện của **Gemini Photo Digitizer (`v9.0.0.0`)** dựa trên kết quả giải mã nhị phân 45 DFM (`TPF0`), tài liệu hướng dẫn chính thức (54 trang) và ảnh chụp màn hình tham chiếu thực tế ([gemini_main_window.jpg](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_main_window.jpg), [gemini_calibration_screen.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_calibration_screen.png), [gemini_full_ui.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_full_ui.png)).

---

## 1. Main Window (`TMainFrm`)

### 1.1 Thuộc tính cửa sổ
- **Class / Name**: `TMainFrm` / `MainFrm`
- **Caption**: `"GeminiPhotoDigitizer"` (khi mới mở) hoặc `"GeminiPhotoDigitizer - [<FilePath>]"` (khi đã mở ảnh)
- **Kích thước thiết kế (`DFM`)**: `ClientWidth = 1016`, `ClientHeight = 741`, `WindowState = wsMaximized`
- **Kích thước ảnh chụp tham chiếu chuẩn ([gemini_main_window.jpg](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_main_window.jpg))**: `762 × 550` (hoặc toàn màn hình khi chạy thực tế)
- **Màu nền**: `clBtnFace` (`#ECE9D8` / `#D4D0C8` — nền xám kem cổ điển của Windows VCL)
- **Font mặc định**: `MS Sans Serif` (hoặc `Tahoma`), cỡ `8 pt` (`Height = -11`, `TextHeight = 13`)

### 1.2 Cấu trúc phân vùng (`DockPanel` / `Align`)
1. **Top Dock (`TopDock`, `Height = 57`)**:
   - Hàng 0 (`Top = 0, Height = 23`): Thanh Menu chính `MainMenuTB` (`TTBToolbar`, `MenuBar = True`).
   - Hàng 1 (`Top = 23, Height = 34`): Thanh Công cụ chính `MainTB` (`TTBToolbar`, `Images = ImageListMainTB` 24×24).
2. **Bottom Status Bar (`StatusBar`, `Top = 722, Height = 19, Align = alBottom`)**:
   - Panel 0 (`Width = 600`): Hiển thị thông tin đo đạc / gợi ý lệnh.
   - Panel 1 (`Width = 50+`, giãn hết phần còn lại): Hiển thị trạng thái thực thi (ví dụ: `"Done detecting pieces!"`, `"Ready"`).
3. **Right Panel (`MainPanel`, `Width = 363, Align = alRight, BevelInner = bvLowered`)**:
   - `ImageInfoPanel` (`Top = 1, Height = 45, Align = alTop, BevelOuter = bvLowered`):
     - `ObjectsNrL` (`Left = 5, Top = 5`): `"Detected pieces: 4"` (mặc định `"Detected pieces: 0"` khi chưa nhận diện).
     - `ResolutionL` (`Left = 5, Top = 24`): `"Picture resolution: 2592 x 1944    Grid dimensions: 110cm x 80cm"` (hoặc `"Picture resolution: none"` khi chưa mở ảnh).
   - `ObjectPropertiesPanel` (`Top = 46, Height = 131, Align = alTop, BevelOuter = bvLowered`):
     - `ObjectNameL` (`Left = 8, Top = 9`): `"Piece name:"` + `ObjectNameEdit` (`Left = 88, Top = 5, Width = 241, Height = 21`).
     - `ObjectAreaL` (`Left = 8, Top = 35`): `"Area:"` + `ObjectAreaEN` (`Left = 88, Top = 31, Width = 69, Height = 21, ReadOnly = True`) + `ObjectAreaMUL` (`Left = 160, Top = 35`): `"cm²"`.
     - `ObjectPerimeterL` (`Left = 8, Top = 59`): `"Perimeter:"` + `ObjectPerimeterEN` (`Left = 88, Top = 55, Width = 69, Height = 21, ReadOnly = True`) + `ObjectPerimeterMUL` (`Left = 160, Top = 59`): `"cm"`.
     - `NotchesNrL` (`Left = 8, Top = 84`): `"Notches / Internal points / Internal lines:"` + `NotchesNrEN` (`Left = 240, Top = 80, Width = 89, Height = 21, ReadOnly = True`, ví dụ `"6 / 0 / 0"`).
     - `CornersNrL` (`Left = 8, Top = 109`): `"Corners / Graded points:"` + `CornersNrEN` (`Left = 240, Top = 103, Width = 89, Height = 21, ReadOnly = True`, ví dụ `"5 / 5"`).
   - `PropertiesDockSite` (`Top = 177, Height = 244, Align = alTop, BevelOuter = bvLowered`):
     - Khi không chọn đối tượng con: Hiển thị chữ `"Nothing selected"` ở giữa khung.
     - Khi chọn **Point(s)**: Nạp `TSelectedPointsPropertiesFrm` (nhóm `"Points"`, `Name:`, checkbox `Curve point`, checkbox `Graded point`, bảng `ValuesTable` 3 cột `Size | dX (cm) | dY (cm)` với 4 nút `Copy grading`, `Paste grading`, `Flip H`, `Flip V`, và `Angle: ... degrees`).
     - Khi chọn **Notch**: Nạp `TNotchPropertiesFrm` (`Distance notch:` tới hai điểm nhảy cỡ lân cận, loại dấu bấm `V` / `I`, `Angle:`, `Width:`, `Length:`, nút `Flip notch`).
     - Khi chọn **Internal Point**: Nạp `TInternalPointPropertiesFrm` (`Internal point:` `Name:`, bảng nhảy cỡ `Size | dX | dY` và 4 nút thao tác).
     - Khi chọn **Internal Line**: Nạp `TInternalLinePropertiesFrm` (`Internal line type:` радио `For drawing`, `For cutting`, `Grain axis`, `Simetry axis`, checkbox `Symmetrize wait`, `Extend to contour`).
   - `ZoomAreaImagine` (`Top = 421, Height = 214, Align = alClient` — **Magnifier / Loupe**):
     - Khung phóng đại vùng ảnh quanh con trỏ hoặc điểm đang chọn, tâm có dấu chữ thập chéo màu đỏ (`X`).
     - Góc trên bên phải có 2 nút bấm nhỏ: `ZoomIn` (`Caption = "+"`, `Left = 312, Top = 4, Width = 19, Height = 16`) và `ZoomOut` (`Caption = "-"`, `Left = 336, Top = 4, Width = 19, Height = 16`).
4. **Center/Left Workspace (`Panel1`, `Width = 653, Align = alClient`)**:
   - **Conversion Area (`Panel2`, `Align = alClient`)**:
     - Chứa `MainImage` (`TImagine`), nền xám `#808080`, luôn hiển thị thanh cuộn ngang (Horizontal ScrollBar) và thanh cuộn dọc (Vertical ScrollBar) kiểu cổ điển.
   - **Stock Area (`PieceList`, `Height = 110, Align = alBottom`)**:
     - Hiển thị danh sách ngang các thẻ chi tiết rập (`Piece 1`, `Piece 2`, `Piece 3`, `Piece 4`...), mỗi thẻ có nền trắng, viền mảnh, hình thu nhỏ biên dạng màu đen và nhãn tên chi tiết ở góc dưới trái; có thanh cuộn ngang phía dưới.

---

## 2. Menu Bar (`MainMenuTB`) — Đúng Thứ Tự 1:1

Gồm đúng **5 mục cấp cao nhất**: `File`, `Modify`, `View`, `Settings`, `Help`.

### 2.1 Menu `File`
| Thứ tự | Tên DFM | Caption hiển thị | Shortcut | ImageIndex | Trạng thái ban đầu | Ghi chú |
|---|---|---|---|---|---|---|
| 1 | `CapturePictureMenuItem` | `Capture picture` | — | `40` | `Enabled` | Chụp từ máy ảnh |
| 2 | `LoadPictureMenuItem` | `Load picture` | `Ctrl+O` | `39` | `Enabled` | Mở hộp thoại chọn file ảnh |
| — | `OpenGem` | `Open Gemini file` | — | `39` | `Visible = False` | Ẩn trong DFM gốc |
| 3 | `AcquirePictureMenuItem` | `Scan picture` | — | `46` | `Enabled` | Quét ảnh từ máy scan |
| 4 | `ReloadMenuItem` | `Reload` | — | `39` | `Enabled` (Submenu) | Danh sách file gần đây |
| — | `TBSeparatorItem4` | *(Separator)* | — | — | — | Đường kẻ ngang |
| 5 | `DetectPiecesMenuItem` | `Detect pieces` | — | `34` | `Disabled` (bật khi có ảnh) | Nhận diện biên dạng (Phase 2) |
| — | `TBSeparatorItem5` | *(Separator)* | — | — | — | Đường kẻ ngang |
| 6 | `SaveMenuItem` | `Save` | `Ctrl+S` | `47` | `Disabled` | Lưu file `.gem` |
| 7 | `SaveAsMenuItem` | `Save as ...` | — | `47` | `Disabled` | Lưu file `.gem` mới |
| 8 | `ExportMenuItem` | `Export` | — | `47` | `Enabled` (Submenu) | Mở menu con 7 định dạng xuất |
| 8.1 | `ExportAssystMenuItem` | `Export to Assyst (.ZIP)` | — | `-1` | `Disabled` (hoặc mở thông báo Phase 1) | Submenu của `Export` |
| 8.2 | `ExportDxfMenuItem` | `Export to Dxf (.DXF)` | — | `-1` | `Enabled` (mở `TExportDXFFrm`) | Submenu của `Export` |
| 8.3 | `ExportFKMenuItem` | `Export to FK ` | — | `-1` | `Disabled` | Submenu của `Export` |
| 8.4 | `ExportGerberMenuItem` | `Export to Gerber (.TMP)` | — | `-1` | `Disabled` | Submenu của `Export` |
| 8.5 | `ExportInvestronicaMenuItem` | `Export to Investronica (.EXP)` | — | `-1` | `Disabled` | Submenu của `Export` |
| 8.6 | `ExportLectraMenuItem` | `Export to Lectra (.IBA)` | — | `-1` | `Disabled` | Submenu của `Export` |
| 8.7 | `ExportInvestronicaDBMenuItem` | `Export to Investronica database` | — | `-1` | `Enabled` (mở `TExportInvestronicaDBFrm`) | Submenu của `Export` |
| — | `TBSeparatorItem6` | *(Separator)* | — | — | — | Đường kẻ ngang |
| 9 | `ExitMenuItem` | `Exit` | — | `37` | `Enabled` | Đóng ứng dụng |

### 2.2 Menu `Modify`
| Thứ tự | Tên DFM | Caption hiển thị | Submenu Items (Caption / ImageIndex / Action) |
|---|---|---|---|
| 1 | `PointsSubmenuItem` | `Points` | • `Add point` (Img `11`, `AddPointAction`, Disabled)<br>• `Delete points` (Img `12`, `DeletePointsAction`, Disabled) |
| 2 | `CornersSubmenuItem` | `Corners` *(DFM: `Auxiliary pieces` / Manual p.12: `Corners`)* | • `Detect corner` (Img `28`, `SmartCornerAction`, Disabled)<br>• `Delete corner` (Img `19`, `DeleteCornerAction`, Disabled)<br>• `Transform line to curve` (Img `41`, `Line2Curve`, Disabled)<br>• `Transform curve to line` (Img `42`, `Curve2Line`, Disabled) |
| 3 | `NotchesSubmenuItem` | `Notches` | • `Detect notch` (Img `31`, `SmartNotchAction`, Disabled)<br>• `Create notch` (Img `18`, `AddNotchAction`, Disabled)<br>• `Delete notch` (Img `22`, `DeleteNotchAction`, Disabled) |
| 4 | `InternalPointsSubmenuItem` | `Internal points` | • `Detect internal point` (Img `30`, `SmartInternalPointAction`, Disabled)<br>• `Create internal point` (Img `17`, `AddInternalPointAction`, Disabled)<br>• `Delete internal point` (Img `21`, `DeleteInternalPointAction`, Disabled) |
| 5 | `InternalLinesSubmenuItem` | `Internal lines` | • `Detect internal line` (Img `29`, `SmartInternalLineAction`, Disabled)<br>• `Create internal line` (Img `16`, `AddInternalLineAction`, Disabled)<br>• `Delete internal line` (Img `20`, `DeleteInternalLineAction`, Disabled) |
| 6 | `AuxPiecesSubmenuItem` | `Auxiliary pieces` | • `Create auxiliary piece` (Img `50`, `AddFigAux`, Disabled)<br>• `Delete auxiliary piece` (Img `49`, `DelFigAux`, Disabled) |

### 2.3 Menu `View`
| Thứ tự | Tên DFM | Caption hiển thị | Shortcut | ImageIndex | Checked mặc định |
|---|---|---|---|---|---|
| 1 | `ShowGridMenuItem` | `Show grid` | `Alt+G` | `13` | `True` |
| 2 | `ShowPictureMenuItem` | `Show picture` | `Alt+H` | `39` | `True` |
| 3 | `ShowNotchesMenuItem` | `Show notches` | `Alt+N` | `25` | `True` |
| 4 | `ShowInternalLinesMenuItem` | `Show internal lines` | `Alt+L` | `23` | `True` |
| 5 | `ShowInternalPointsMenuItem` | `Show internal points` | `Alt+P` | `24` | `True` |

### 2.4 Menu `Settings`
- Là một mục bấm trực tiếp trên thanh Menu (`SettingsMenuItem` -> `SettingsAction`), khi bấm sẽ mở ngay hộp thoại `Settings Gemini Photo Digitizer` (`TSettingsFrm`).

### 2.5 Menu `Help`
| Thứ tự | Tên DFM | Caption hiển thị | Shortcut | Action |
|---|---|---|---|---|
| 1 | `HelpContentsMenuItem` | `Help` | — | `HelpContentsAction` |
| 2 | `IndexHelpMenuItem` | `Index help` | — | `IndexHelpAction` |
| 3 | `ContextHelpMenuItem` | `What is this ?` | `Shift+F1` | `ContextHelpAction` |
| 4 | `AboutMenuItem` | `About...` | — | `AboutAction` (mở `TAboutBox`) |
| 5 | `TBItem1` | `Register product` | — | `RegisterAction` (mở `TRegisterFrm`) |

---

## 3. Main Toolbar (`MainTB`) — Thứ Tự & Nhóm Công Cụ Chính Xác 1:1

Đối chiếu trực tiếp giữa `TMainFrm_MainFrm.json` và ảnh chụp [gemini_main_window.jpg](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_main_window.jpg) + User Manual trang 4–5:

| Vị trí hiển thị | Tên Control DFM | Loại Control | ImageIndex | Caption / Tooltip (`Hint`) | Trạng thái (`gemini_main_window.jpg`) | Ghi chú |
|---|---|---|---|---|---|---|
| **Nhóm 1: Ảnh, Nhận diện & Lưu** | | | | | | |
| 1 | `FotoTBItem` | Split Button (`DropdownCombo = True`) | `39` | `Load picture (Ctrl+O)` | `Enabled` | Menu xổ xuống: `Capture picture` (40), `Load picture` (39), `Scan picture` (46), `Reload` (39) |
| 2 | `SnakeTBItem` | Button | `34` | `Detect piece contur` | `Enabled` (khi có ảnh) | Hình tam giác Play màu xanh lá |
| 3 | `SaveTBItem` | Split Button (`DropdownCombo = True`) | `47` | `Save (Ctrl+S)` | `Enabled` | Menu xổ xuống: `Save`, Separator, 7 mục `Export to ...` |
| — | `TBSeparatorItem1` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 2: Hoàn tác (Undo / Redo)** | | | | | | |
| 4 | `UndoTBItem` | Split Button (`DropdownCombo = True`) | `14` | `Undo` | `Enabled` / `Disabled` | Mũi tên cong trái màu xanh dương |
| 5 | `RedoTBItem` | Split Button (`DropdownCombo = True`) | `15` | `Redo` | `Disabled` (xám) | Mũi tên cong phải màu xanh dương / xám |
| — | `TBSeparatorItem2` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 3: Phóng đại (Zoom)** | | | | | | |
| 6 | `ZoomModeTBItem` | Toggle Button | `2` | `Zoom to the selected area` | `Enabled` | Kính lúp trên khung chữ nhật chấm đỏ |
| 7 | `ZoomToSelectedTBItem` | Button | `4` | `Zoom to selected pieces (F4)` | `Enabled` | Kính lúp trên chi tiết rập màu vàng |
| *(Ẩn)* | `LastZoomTBItem` | Button (`Visible = False`) | `3` | `Return to previous zoom` | `Hidden` | `Visible = False` trong DFM |
| 8 | `ZoomToAllTBItem` | Button | `5` | `Zoom to entire picture (F5)` | `Enabled` | Kính lúp trên trang giấy xám |
| *(Ẩn)* | `TBSeparatorItem9`, `AddPointTBItem`, `DeletePointsTBItem` | `Visible = False` | `11`, `12` | `Add point`, `Delete points` | `Hidden` | `Visible = False` trong DFM (chỉ nằm trong menu `Modify -> Points`) |
| — | `TBSeparatorItem3` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 4: Góc & Đường cong (Corners & Curves)** | | | | | | |
| 9 | `SmartCornerTBItem` | Button | `28` | `Detect corner in the selected area` | `Enabled` | Góc vàng + ký hiệu góc xanh/đen |
| 10 | `DeleteCornerTBItem` | Button | `19` | `Delete selected corner` | `Enabled` | Góc vàng + dấu `x` đỏ |
| 11 | `TBItem6` (`Line2Curve`) | Button | `41` | `Transform line to curve` | `Enabled` | Cung tròn xanh có điểm điều khiển |
| 12 | `TBItem5` (`Curve2Line`) | Button | `42` | `Transform curve to line` | `Enabled` | Đoạn thẳng chéo có 2 điểm đầu mút |
| — | `TBSeparatorItem11` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 5: Dấu bấm (Notches)** | | | | | | |
| 13 | `SmartNotchTBItem` | Button | `31` | `Detect notch in the selected area` | `Enabled` | Cạnh vàng có dấu bấm + dấu `+` xanh lá |
| 14 | `AddNotchTBItem` | Button | `18` | `Create new notch` | `Enabled` | Cạnh vàng có dấu bấm + dấu `+` đỏ |
| 15 | `DeleteNotchTBItem` | Button | `22` | `Delete selected notch` | `Enabled` | Cạnh vàng có dấu bấm + dấu `-` đỏ |
| — | `TBSeparatorItem12` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 6: Điểm nội vi (Internal Points / Drill Holes)** | | | | | | |
| 16 | `SmartInternalPointTBItem` | Button | `30` | `Detect internal point in the selected area` | `Enabled` | Chi tiết vàng + tâm ngắm + dấu `+` xanh lá |
| 17 | `AddInternalPointTBItem` | Button | `17` | `Create new internal point` | `Enabled` | Chi tiết vàng + tâm ngắm + dấu `+` đỏ |
| 18 | `DeleteInternalPointTBItem` | Button | `21` | `Delete selected internal point` | `Enabled` | Chi tiết vàng + tâm ngắm + dấu `-` đỏ |
| — | `TBSeparatorItem13` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 7: Đường nội vi (Internal Lines)** | | | | | | |
| 19 | `SmartInternalLineTBItem` | Button | `29` | `Detect internal line for the selected piece (for all pieces if none selected)` | `Enabled` | Chi tiết vàng + đường mũi tên + dấu `+` xanh lá |
| 20 | `AddInternalLineTBItem` | Button | `16` | `Create new internal line` | `Enabled` | Chi tiết vàng + đường mũi tên + dấu `+` đỏ |
| 21 | `DeleteInternalLineTBItem` | Button | `20` | `Delete selected internal line` | `Enabled` | Chi tiết vàng + đường mũi tên + dấu `-` đỏ |
| — | `TBSeparatorItem14` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 8: Hiển thị lớp (View Visibility)** | | | | | | |
| 22 | `ShowTBItem` | Split Button (`DropdownCombo = True`) | `13` | `Show grid (Alt+G)` | `Checked / Pressed` | Icon lưới chữ L xanh/xám; menu xổ xuống 5 mục `Show ...` |
| — | `TBSeparatorItem8` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 9: Đo khoảng cách (Measurement)** | | | | | | |
| 23 | `ComputePerimeterDistanceTBItem` | Button | `32` | `Measure on perimeter` | `Enabled` | Chi tiết vàng + mũi tên đỏ chạy dọc chu vi |
| 24 | `ComputeDirectDistanceTBItem` | Button | `33` | `Measure distance between two points` | `Enabled` | Chi tiết vàng + mũi tên đỏ thẳng nối 2 điểm |
| — | `TBSeparatorItem15` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 10: Duyệt kiểm tra (Play / Inspect)** | | | | | | |
| 25 | `TBPlay` | Split Button (`DropdownCombo = True`) | `26` | `Play piece contur` | `Enabled` | Chi tiết vàng + mũi tên phải xanh lá + ô đỏ; menu xổ xuống 5 chế độ Play |
| — | `TBSeparatorItem16` | Separator | — | — | — | Vạch ngăn cách dọc |
| **Nhóm 11: Thanh trượt Zoom (`ZoomPanel`)** | | | | | | |
| 26 | `ZoomPanel` | Panel (`66 × 26`) | — | `Zoom` | `Enabled` | Phía trên: nhãn `"-   zoom   +"`; phía dưới: `TrackBarZoom` (`Min=0, Max=100, Position=50`) |

> **Lưu ý về 2 nút Auxiliary Piece (`TBItem10` / `TBItem9`, Img `50`/`49`)**: Trong DFM có khai báo trước `ShowTBItem`, nhưng trong ảnh chuẩn [gemini_main_window.jpg](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_main_window.jpg) hai nút này ẩn (và xuất hiện trong một số ảnh chụp trang 38–40 của User Manual). Trong Pattern Digitizer Pro, mặc định chúng tôi khớp chính xác 1:1 với [gemini_main_window.jpg](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_main_window.jpg) và cung cấp tuỳ chọn bật/tắt nếu cần kiểm tra.

---

## 4. Các Hộp Thoại (`Dialog Windows`)

### 4.1 Camera Calibration (`TCalibrationFrm`)
- **Caption**: `"Gemini PhotoDigitizer calibration"`
- **Kích thước (`DFM` / Ảnh tham chiếu [gemini_calibration_screen.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_calibration_screen.png))**: `ClientWidth = 992, ClientHeight = 673` (cửa sổ `996 × 700`).
- **Cấu trúc**:
  - Cột trái (`MainPanel`, `Width = 783, Align = alClient`):
    - Thanh công cụ `TBDock1` (`Height = 30`) ở góc trên trái với 5 nút: `Load picture` (Split button, Img 0), Separator, `Zoom in` (Img 3), `Zoom out` (Img 4), `Zoom to width` (Img 1), `Zoom to all` (Img 2).
    - Khung hiển thị ảnh lưới `GridImage` (`TImagine`, nền xám, có thanh cuộn ngang/dọc, hiển thị ảnh tờ lưới chấm và khung chữ nhật giới hạn màu xanh dương kèm đường chéo).
  - Cột phải (`ParamPanel`, `Width = 209, Align = alRight`):
    - `Cell width :` (`10` `cm`), `Cell height :` (`10` `cm`).
    - GroupBox `Croping box` (`Left = 8, Top = 64, Width = 193, Height = 129`): `Start X :`, `Start Y :`, `Stop X :`, `Stop Y :` (kèm các nút `UpDown` tăng giảm).
    - `Active area width:` (`120` `cm`, `Enabled = False`), `Active area height:` (`90` `cm`, `Enabled = False`).
    - Nút `Calibrate` (`Left = 24, Top = 608, Width = 75, Height = 25`) và `Close` (`Left = 104, Top = 608, Width = 75, Height = 25`).
    - Dưới đáy cột phải: `ProgressBarPanel` (`Height = 25`) chứa thanh tiến trình `0%`.

### 4.2 Settings (`TSettingsFrm`)
- **Caption**: `"Settings Gemini Photo Digitizer"`
- **Kích thước (`DFM` / Ảnh tham chiếu [gemini_full_ui.png](file:///E:/claude/GEMHIHI/assets/images/reference/gemini_full_ui.png))**: `ClientWidth = 538, ClientHeight = 362` (cửa sổ `544 × 390`).
- **Cấu trúc**:
  - `SettingsPageControl` (`Left = 0, Top = 0, Width = 537, Height = 313`) gồm **5 tab hiển thị**:
    1. `General settings`: `Measurement Unit` (`mm`, `cm`, `inch`), `Language` (`English`...), `Saving history` (`Keep files`, `Delete after aquired`, `Delete after [0] days`, nút `Delete all now`), `Acquire mode` (`Single` / `Multiple`), checkbox `Save only selected pieces.`.
    2. `Camera settings / Calibration`: GroupBox `Camera` (nút `Camera settings`), GroupBox `Scaner` (nút `Select scaner`, checkbox `Auto pixels per inch`), GroupBox `Calibration` (checkbox `Use grid calibration`, nút `Calibrate`, radio `Calibrate by dimensions` với `Grid horizontal size: 120 cm`, `Grid vertical size: 90 cm`, radio `Calibrate by resolution` với `Horizontal pixels per inch: 0`, `Vertical pixels per inch: 0`).
    3. `Interpretation settings`: GroupBox `Corners settings` (`Minimum distance between two corners: 20 mm`, `Minimum angle: 45 degrees`, checkbox `set corners as graded points`), GroupBox `Interpolation settings` (`Maximum deviation: 1 mm`), GroupBox `Curve to line parameter` (checkbox `Convert curver to lines`, `Approximation: 1 mm`), GroupBox `Smart functions:` (`Autodetect notches`, `Autodetect internal lines`, `Autodetect internal points`), GroupBox `Notches` (`Force notches to 90 degrees`, `If angle under: 0 degrees`, `Auto detect notches length:`, `Default notch length: 0 mm`), GroupBox `Internal Points` (trackbar `Internal point length:`), GroupBox `Internal Lines` (`Internal line origine in ball point`), GroupBox `Detection` (`Safe zone: 0 mm`).
    4. `Interface settings`: 7 ô chọn màu (`Selected elements color`, `Contur color`, `Graded points color`, `Curve points color`, `Notches color`, `Internal points color`, `Internal lines color`), RadioGroup `Cursor color` (`Black cursors`, `White cursors`), checkbox `Line width by zoom`, nút `Reset Colors`.
    5. `Export settings`: GroupBox `Investronica settings:` (`IP:`, `User: sa`, `Password:`, `Remote files path:`, nút `Browse`, `Interpolation Precision : 0,2 mm`), GroupBox `Lectra settings:` (`Interpolation Precision : 0,2 mm`, `Graded point angle : 0`), GroupBox `Gerber settings:` (`Interpolation Precision : 0,2 mm`).
  - Nút `OK` (`Left = 360, Top = 328, Width = 75, Height = 25`) và `Cancel` (`Left = 440, Top = 328, Width = 75, Height = 25`).

### 4.3 Export DXF (`TExportDXFFrm`) & Structure Settings (`TExportDXFStructuraFrm`)
- **`TExportDXFFrm`**:
  - **Caption**: `"Export DXF"`, `ClientWidth = 990, ClientHeight = 570`.
  - Toolbar góc trên trái (`ToolBarExportDXF`, `Width = 109, Height = 28`): 4 nút Zoom (`Zoom in`, `Zoom out`, `Zoom to all`, `Zoom to selection`).
  - Vùng xem trước `ImagineCutPreview` (`Left = 11, Top = 41, Width = 625, Height = 518`, nền trắng/xám).
  - Cột phải (`Left = 648, Width = 336`):
    - GroupBox `General settings` (`Top = 36, Height = 115`): `Measure units` (`mm`, `cm`, `m`, `inch (1/1)`, `inch (1/8)`, `inch (1/10)`, `inch (1/16)`), `Scaling` (`X = 1.0`, `Y = 1.0`).
    - GroupBox `DXF Profile` (`Top = 158, Height = 124`): ComboBox `CBProfil` (`General`, `Gemini Cut Plan`, `Gerber Accumark`, `Gerber TMP`, `Lectra Modaris`, `Lectraiba`), 5 nút `New profile`, `Load profile`, `Edit profile`, `Export profile`, `Delete profile`.
    - 3 nút dưới đáy phải (`Top = 532, Width = 103, Height = 27`): `Apply` (`Left = 651`), `Save` (`Left = 766`), `Close` (`Left = 881`).
- **`TExportDXFStructuraFrm`** (mở khi bấm `Edit profile` hoặc `New profile`):
  - **Caption**: `"Structure settings"`, `ClientWidth = 572, ClientHeight = 506`.
  - `PCSetariExport` (`569 × 472`) gồm 3 tab: `General settings`, `DXF file structure settings`, `DXF layer settings`.
  - Dưới đáy: `Default settings` (`Left = 1, Top = 476, Width = 87, Height = 25`), `Ok` (`Left = 405, Top = 476, Width = 78, Height = 25`), `Cancel` (`Left = 492, Top = 476, Width = 78, Height = 25`).

### 4.4 Các hộp thoại khác
- **`TExportInvestronicaDBFrm`**: Caption `"Export to Investronica database"`, `ClientWidth = 317, ClientHeight = 478`, `Piece List:`, khung trạng thái, nút `OK` và `Cancel`.
- **`TAboutBox`**: Caption `"About"`, `ClientWidth = 298, ClientHeight = 218`, khung sunken `281 × 161` chứa `ProgramIcon`, `Product Name`, `Version`, `Copyright`, `Comments`, nút `OK` (`65 × 33`).
- **`TRegisterFrm`**: Caption `"Inregistrare GEMINI"`, `ClientWidth = 424, ClientHeight = 104`, nhãn `"COD INREGISTRARE:"`, ô nhập `ECod`, nút `"Inregistreaza"` và `"Renunta"`.
- **Open Picture / Save GEM / Save DXF**: Hộp thoại chuẩn của Windows (`OpenFileDialog` / `SaveFileDialog`) với đúng bộ lọc định dạng của Gemini.
