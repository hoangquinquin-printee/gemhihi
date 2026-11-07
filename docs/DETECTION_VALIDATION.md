# DETECTION VALIDATION REPORT — PATTERN DIGITIZER PRO (PHASE 2)

## 1. KIẾN TRÚC PIPELINE NHẬN DIỆN RẬP 8 GIAI ĐOẠN ([`DetectionEngine.cs`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs))

Mỗi lần chạy nhận diện rập trên ảnh thật, [`DetectionEngine.DetectPatterns`](file:///E:/claude/GEMHIHI/src/PatternDigitizerPro.Core/Engines/DetectionEngine.cs#L43-L320) thực thi 8 giai đoạn xử lý ảnh và hình học độc lập, đồng thời có thể xuất trực tiếp 8 ảnh PNG minh chứng từng giai đoạn:

1. **`1_original.png`**: Ảnh gốc trong vùng ROI hiệu chuẩn.
2. **`2_preprocessed.png`**: Ảnh sau khi làm mượt khử nhiễu bảo toàn biên (`3×3` Gaussian-weighted filter) và phân tích độ tương phản sáng/màu sắc so với nền bảng đen.
3. **`3_binary_mask.png`**: Mặt nạ nhị phân tách tiền cảnh/hậu cảnh kết hợp ngưỡng thích ứng Otsu trên kênh độ sáng (`Luminance`) và độ dịch màu (`Blue-Red Chroma Shift` cho rập vải tối màu/xanh).
4. **`4_cleaned_mask.png`**: Mặt nạ sau khi đóng hình thái học (`Morphological Closing`), lấp đầy các lỗ thủng nội bộ do **nam châm giữ rập đặt bên trong mảnh rập** bằng thuật toán Flood-Fill từ biên nền, và loại bỏ các hạt nhiễu/nam châm rời bằng ngưỡng diện tích tối thiểu và độ đặc (`Solidity` / `Extent`).
5. **`5_raw_contours.png`**: Đường bao ngoài (`Outer Contour`) trích xuất bằng thuật toán Moore-Neighbor Boundary Tracing kèm làm mượt chuỗi điểm pixel.
6. **`6_corners.png`**: Phát hiện các đỉnh góc sắc (`Corner / Graded Point`) bằng phân tích góc lệch dây cung đa tỷ lệ (`Multi-scale Chord Deflection Angle`) kết hợp Non-Maximum Suppression và rút gọn Douglas-Peucker.
7. **`7_fitted_curves.png`**: Khớp đường thẳng (`Line`) cho các cạnh có độ lệch trực giao thấp và khớp đường cong `Cubic Bézier` bình phương tối thiểu (`Least-Squares Chord-Length Parameterization`) cho các cạnh cong.
8. **`8_final_overlay.png`**: Lớp phủ vector hoàn chỉnh gồm đường viền khép kín màu xanh lam, các điểm góc nhảy cỡ màu đỏ, và trục canh sợi (`Grain Axis`) mặc định của từng mảnh rập.

---

## 2. KẾT QUẢ KIỂM CHỨNG TRÊN 3 ẢNH RẬP THẬT

### 2.1. Ảnh 1: [`sample_03172025_111128.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/sample_03172025_111128.jpg) (`2918 × 2214 px`)

- **Đặc điểm ảnh thật**: 3 mảnh rập hình chữ nhật màu trắng đặt nghiêng trên bảng đen, trên mỗi mảnh có từ 2 đến 4 cục nam châm tròn giữ giấy (một số nam châm nằm sát mép làm khuyết nhẹ biên trắng).
- **Kết quả nhận diện**:
  - **Số mảnh phát hiện**: **`3` mảnh** (`Piece 1`, `Piece 2`, `Piece 3`) — khớp 100% với Gemini Photo Digitizer gốc (`Detected patterns: 3`).
  - **Số đỉnh góc (`Graded points`)**: Mỗi mảnh có đúng **`4` đỉnh góc** (`Corners / Graded points: 4 / 4`). Các vết lõm nhỏ do nam châm sát mép được làm phẳng bằng Convex Hull / Douglas-Peucker, không sinh điểm giả.
  - **Loại bỏ nhiễu**: Viền sáng dọc mép trái của khung bảng và các lỗ nam châm bên trong mảnh rập được loại bỏ hoàn toàn.
- **Bộ 8 ảnh minh chứng từng bước**:
  - [`1_original.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/1_original.png)
  - [`2_preprocessed.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/2_preprocessed.png)
  - [`3_binary_mask.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/3_binary_mask.png)
  - [`4_cleaned_mask.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/4_cleaned_mask.png)
  - [`5_raw_contours.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/5_raw_contours.png)
  - [`6_corners.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/6_corners.png)
  - [`7_fitted_curves.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/7_fitted_curves.png)
  - [`8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/sample_03172025_111128/8_final_overlay.png)

---

### 2.2. Ảnh 2: [`real_01082026_091623.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/real_01082026_091623.jpg) (`3456 × 2592 px`)

- **Đặc điểm ảnh thật**: 2 mảnh rập vải bò màu xanh đậm (1 mảnh thân lớn có cạnh cong và 1 mảnh đáp nhỏ bên phải), có vệt lóa sáng chéo rất mạnh ở góc dưới bên trái bảng và 6 cục nam châm trắng xếp dọc thanh biên bên phải.
- **Kết quả nhận diện**:
  - **Số mảnh phát hiện**: **`2` mảnh rập vải xanh** (`Piece 1`, `Piece 2`).
  - **Loại bỏ nhiễu**:
    - Vệt lóa sáng chéo góc dưới bên trái (`Left Glare Zone`) bị loại bỏ hoàn toàn (`0` mảnh giả).
    - 6 cục nam châm trắng dọc mép phải và các cục nam châm đè lên rập được loại bỏ hoàn toàn.
- **Bộ 8 ảnh minh chứng từng bước**:
  - [`1_original.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/1_original.png)
  - [`2_preprocessed.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/2_preprocessed.png)
  - [`3_binary_mask.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/3_binary_mask.png)
  - [`4_cleaned_mask.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/4_cleaned_mask.png)
  - [`5_raw_contours.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082025_091623/5_raw_contours.png) -> [`5_raw_contours.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/5_raw_contours.png)
  - [`6_corners.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/6_corners.png)
  - [`7_fitted_curves.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/7_fitted_curves.png)
  - [`8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01082026_091623/8_final_overlay.png)

---

### 2.3. Ảnh 3: [`real_01232026_093723.jpg`](file:///E:/claude/GEMHIHI/assets/images/reference/real_01232026_093723.jpg) (`3456 × 2592 px`)

- **Đặc điểm ảnh thật**: 3 mảnh rập giấy trắng lớn (thân áo/tay áo có đường cong vòng nách/cổ) đặt trên bảng đen, xung quanh có 11 cục nam châm tròn nằm rời rạc trên mặt bảng và phần ghế màu vàng lộ ra ở sát mép dưới ảnh.
- **Kết quả nhận diện**:
  - **Số mảnh phát hiện**: **`3` mảnh rập giấy trắng** (`Piece 1`, `Piece 2`, `Piece 3`).
  - **Khớp đường cong & góc**: Các cạnh cong vòng nách/sườn được khớp thành các đoạn `Cubic Bézier` trơn mượt, các góc giao cắt được đánh dấu đúng là `Graded Point`.
  - **Loại bỏ nhiễu**: Toàn bộ 11 cục nam châm rời trên mặt bảng và vật thể màu vàng ở mép dưới ảnh bị loại bỏ 100%.
- **Bộ 8 ảnh minh chứng từng bước**:
  - [`1_original.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/1_original.png)
  - [`2_preprocessed.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/2_preprocessed.png)
  - [`3_binary_mask.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/3_binary_mask.png)
  - [`4_cleaned_mask.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/4_cleaned_mask.png)
  - [`5_raw_contours.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/5_raw_contours.png)
  - [`6_corners.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/6_corners.png)
  - [`7_fitted_curves.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/7_fitted_curves.png)
  - [`8_final_overlay.png`](file:///E:/claude/GEMHIHI/artifacts/detection-debug/real_01232026_093723/8_final_overlay.png)
