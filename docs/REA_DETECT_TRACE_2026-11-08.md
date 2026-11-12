# Gemini Detect trace — 2026-11-08

## Scope

Read-only REA/Ghidra analysis of the installed Gemini Photo Digitizer binaries. No Gemini or Gemhihi binary/source was modified.

## Artifacts

- `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\GeminiPhotoDigitizer.exe`
  - SHA-256: `fd057769b092a439c69a383757652ddbb2817fe4e48ccc120ae4ff3c839b9f92`
- `C:\Program Files (x86)\Gemini\Gemini Photo Digitizer\SDEngine.dll`
  - SHA-256: `7587e6f0d90e67520c6f72d5838dbdcea8250b722cae642ea2e3ba9da646f9ca`

## Confirmed static evidence

`SDEngine.dll` is a native x86 engine. Its imports include GDI bitmap/polygon operations (`CreatePolygonRgn`, `FillRgn`, `StretchBlt`) and geometry/math operations from MSVCR80 (`qsort`, `atan2`, `acos`, `sqrt`, `pow`, `fmod`). This supports a bitmap-to-contour pipeline, but does not by itself prove the full Detect entry.

The largest export is `minunata_functie_1` at `0x10007a40` (64 basic blocks, 18 resolved callees, 9,359 pseudocode units). It calls `0x100033e0` and `0x100070d0`.

`0x100033e0` has resolved callers `0x10002f30` and `0x10007a40`. Its pseudocode visibly:

1. iterates a point record array with a `0x90`-byte stride;
2. marks candidate points using angle/neighbor predicates;
3. copies coordinates to an output point array;
4. marks neighboring points around accepted points;
5. removes candidates when angle relationships fail thresholds;
6. emits the filtered contour structure.

`0x100070d0` has resolved callers `0x10007a40` and `0x100080b0`; it contains a large geometry/dataflow body (31 blocks, 4,461 pseudocode units).

`procedure_callers` for `0x10007a40` returned no internal caller in the DLL, which is expected for an exported entry reached through a dynamic EXE/DLL boundary. This is not proof that the export is the only Detect entry.

## Evidence IDs

- Call graph from `0x10007a40`: `ev_0625b5fe62fbf41dc8e7d067aa8b1d5bc7f80f6cd7cb03df64577c2ff4b5b665`
- `0x100033e0` callers: `ev_4d299ccf0a3076f261e9f8d2f484c94841e2f939c903c6dcd1b1becb4d01c0c4`
- `0x100070d0` callers: `ev_d50ba0edf57cb64b1eea9243c3c879f0aa6d44dd1d210ef90c346cdaba19d6fb`
- Complete `0x100033e0` dossier: `ev_999be1f21c50b145743c332c522ed55439e99194ff154f39bff38ef19b154750`
- SDEngine PE imports: `ev_e7abd88d3833c7f099da24e4bf3205c6ef0c1bf203445780973c373b2f8cc14e`

## Still unresolved

- The exact Detect-triggering caller among the many callers of the loader thunks is still unresolved.
- Runtime confirmation that `minunata_functie_1` is the Detect command rather than another contour operation.
- Image preprocessing, segmentation thresholds, rejection/magnet/glare filters, and exact point-reduction parameters.

Large whole-EXE xref/feature scans exceeded the configured Ghidra provider response window. Runtime UI tracing was also unavailable because Gemini's process had no exposed Windows window/UIA handle.

## Newly confirmed EXE → DLL bridge

REA decompiled `FUN_00435a7c` at `0x435a7c` completely. It performs:

1. `LoadLibraryA("SDEngine.dll")`;
2. `GetProcAddress` for `minunata_functie_3`, `_4`, `_2`, `_5`, `_6`, `_7`, and `_1`;
3. stores the seven function pointers in globals `0x00a5637c` through `0x00a56394`;
4. keeps the module only when all pointers are non-null, otherwise calls `FreeLibrary`.

This proves the EXE-to-DLL export bridge and the complete export set used by the application. Evidence: `ev_17938e776e2cee3947ba4ccfa0ae6d51cc6d0b7dab0e45e88abac3d4c75fcfc4`.

The loader thunk for `GetProcAddress` is at `0x00a465ec`; the thunk for `LoadLibraryA` is at `0x00a466e2`. Evidence: `ev_cdb52fc856e2bba01c567081ffa88823e285d4f2d8dc28ae29cc7029d245270e` and `ev_f27aa73a5550eaccbd9bc6f4f3ffe932df48e59a02ca8833a68c694653610a78`.

## Next probe

Use targeted address-range analysis around the EXE's dynamic loader references, then correlate one controlled Detect action with native calls if a runtime trace provider becomes available. Do not change Gemhihi until those unknowns are resolved.

## User-supplied Gemini output fixture (2026-11-08)

Files supplied for format comparison:

- `D:\pictures\2.gem` — 1,910 bytes, a ZIP container with one entry named `2.gem` (PK header), not an image file.
- `D:\pictures\2.dxf` — 21,215 bytes, ASCII DXF.

Observed DXF structure from the actual file:

- `POLYLINE`/`VERTEX` outer geometry on layer `1` (one polyline, 156 vertices).
- `POINT` entities: 166 total; layer `2` has 17, layer `3` has 148, layer `4` has 1.
- `LINE` entities: 5 total; layer `7` has 1 and layer `8` has 4.
- One `INSERT` on layer `1`.
- 31 `TEXT` entities and metadata strings `PIECE NAME: Piece 1`, `AUTHOR: GEMINI CAD SYSTEMS`, `PRODUCT: GEMINI PATTERN EDITOR`.

These observations are file evidence only. They confirm that Gemini's saved project is a separate ZIP-based GEM artifact and that its DXF carries outer contour, point entities, and internal line entities in distinct layers. They do not yet prove the exact binary GEM schema or the semantic meaning of every DXF layer.

## Runtime Detect trace (2026-11-08)

REA/Frida attached to the user-owned Gemini process `GeminiPhotoDigitizer.exe` PID `15992` and observed `SDEngine.dll` at runtime base `0x6fd0000`. All seven exports were hookable. After one user-triggered Detect action, the observed order was:

1. `minunata_functie_4` (`0x6fe9200`) — returned `0x0`, then later returned `0x1` on a second call.
2. `minunata_functie_3` (`0x6fe84c0`) — returned `0x19c61e01`, elapsed about 555 ms.
3. `minunata_functie_5` (`0x6fe92b0`) — returned pointer `0x19f88b00`.
4. `minunata_functie_1` (`0x6fd7a40`) — returned pointer `0x19c61fb8`, elapsed about 26 ms.
5. `minunata_functie_6` (`0x6fe9310`) — returned `1`.
6. `minunata_functie_2` (`0x6fd8330`) — returned `1`.
7. `minunata_functie_4` (`0x6fe9200`) — returned `1`.

This is the first direct runtime evidence tying Detect to the DLL. The 555 ms call to export 3 precedes allocation/initialization (export 5) and the known contour point-reduction export 1; therefore export 3 is the strongest candidate for an upstream image/contour preparation stage, while export 1 remains the observed contour simplification stage. The call sequence alone does not prove the exact pixel threshold or segmentation algorithm. Evidence: `ev_19b49a9894809eae6536ed83b689b037ddcb8852d9538bd9c0935686c72bd3f7`; hook installation: `ev_6ab05c52c4b0b04c3cc70480b92fb3f0fdbb260cc23b700382a5fd8ca35c03fd`.

### Deep buffer observation

The second controlled Detect run used read-only Frida hooks on exports 3, 5 and 1. Export 3 received two data pointers (`0x53917e0`, `0x539d398`) plus the same callback/function pointer (`0x907270`) in arguments 3 and 4; it returned `0x19c61f01` after about 561 ms. Export 5 then received `0x430` and `0x16` as scalar arguments around two pointers and returned `0x19f88b00`. Export 1 received that returned object pointer as its first argument and a second data pointer `0x53ffc18`, then returned `0x19c61df0` after about 49 ms. The captured object headers and byte ranges were read-only snapshots; they are not yet decoded into image dimensions or point counts. Evidence: `ev_a04a9bcddecb7c6b3c05568b605466e4d69c19b2c5152679a82df61e0cd5e111`; hook installation: `ev_6a64c5b809680d12265457452cfa714019b1169381d34da045b7095ea26d2d0d`.

The runtime session and script were unloaded after capture. REA reported the final target state as unknown, so no claim is made about Gemini's post-detach process state.

### Multi-piece Detect trace

A later controlled run after loading a new image showed one call to export 3 followed by six repeated `export 5 -> export 1` pairs. Export 3 ran for about 2.8 seconds with arguments `(0x53917e0, 0x539d398, 0x907270, 0x907270)`. The six export-5 scalar pairs were `(0x4e5,0x18)`, `(0x322,0x0c)`, `(0x524,0x14)`, `(0x320,0x0d)`, `(0x3c9,0x0a)`, and `(0x510,0x13)`; each was immediately followed by export 1 with the returned object and pattern index `0` through `5` as its fourth argument. This is strong runtime evidence that export 3 produces a shared detection result and export 5 creates one intermediate pattern object per detected component before export 1 reduces/fits each component independently. The scalar meanings are not yet proven to be pixel dimensions or point counts. Evidence: `ev_e266f11a9ef2f0f9a6f09ca3d004bc7b699b86779774bc1620838c6a698c94f7`.
### Current controlled Detect capture (2026-11-08, user-triggered)

The active Gemini process was hooked without changing its files. The hook retained 47 Frida messages and reported no provider-side truncation. Export 3 was entered with two data pointers (`0x53917e0`, `0x539d398`) and the same callback pointer (`0x907270`) in arguments 3 and 4; it returned `0x19c61e01` after about 2.39 seconds. The follow-on trace again contained repeated export-5/export-1 calls: export 5 received a scalar pair plus a shared data pointer and returned an intermediate object; export 1 consumed that object together with a shared second data pointer and a changing component index. Read-only snapshots show structured records and floating-point fields, but no field schema or pixel-threshold value is established.

This capture strengthens the observed boundary: export 3 is the shared upstream detection/preparation stage, export 5 constructs per-component intermediate data, and export 1 performs per-component contour fitting/reduction. It does not establish the exact segmentation, glare/magnet rejection, or point-sampling parameters. Evidence: `ev_3264e04492b86db6210456f58f6894c45641ad905760e911c8fb7535512ff4bd`; summary view: `ev_e2ba4ec1241df590e2c6175ac59ebc18f8cf3308cbbbd300d166249bd2d56829`.
### Deep static follow-up on export 3

REA reopened the shipped `SDEngine.dll` and analyzed export 3 at `0x100184c0`: 19,236 pseudocode units, 251 basic blocks, 28 resolved callees, 546 outgoing references, and two observed parameters. Its resolved callees include `GDI32.DLL::DeleteObject`, `malloc/calloc/free`, and large internal routines at `0x10017600`, `0x100132f0`, `0x100128f0`, and `0x1000d6a0`. This confirms that export 3 performs substantial native image/geometry preparation and allocation, but the available summary does not identify thresholds or semantic field names.

The largest internal callee examined, `0x10017600`, has 15,543 pseudocode units, 130 basic blocks, one caller, 12 resolved callees, and complete (not truncated) value-flow availability. These are useful targets for the next exact pseudocode/assembly inspection. No Gemhihi source was changed. Evidence: export 3 `ev_c73b01cddcc6cdf2fba5e4ab4d972615766fad3356cee98d9df2e569f7a122c4`; export-3 callee page `ev_9966770abfa23043d1c48a46d9adb2bf534ae6068b8c382d901dd3d544ed48ae`; callee `0x10017600` `ev_c586147d69d2ed7d6740bd26685b4a011df0c67809d553469a0570629091a2ea`.
### Evidence-derived Gemhihi change (2026-11-08)

The contour stage now includes a bounded response-map refinement derived from the observed `FUN_10017600` structure: a +/-3-pixel normal candidate window, response comparison, a minimum improvement guard, and one circular smoothing pass. It is applied only to already accepted component boundaries, so it does not alter connected-component selection, calibration, editing, or export. This is an evidence-derived approximation of the native refinement stage, not a claim of binary equivalence. Build/regression result: 57 tests passed.
### Segmentation correction derived from REA evidence (2026-11-08)

The real-photo failure was located before contour tracing: in an explicit digitize frame containing a pale background and warm brown/cardboard pattern paper, the generic bright-paper rule classified the pale background as foreground and connected it to the pattern. The detector now estimates a warm-material prior inside the user frame and disables that generic bright-paper branch when the prior is present, while retaining the warm material branch. This change is isolated to framed detection; full-photo detection, editing, calibration, and export are unchanged. It is a source-level reconstruction guided by the observed native preprocessing boundary, not a claim of exact Gemini threshold parity.
### Additional preprocessing evidence from export 3 (2026-11-08)

REA identified `FUN_10001710` as a direct export-3 callee with a concrete bitmap boundary: it calls `GDI32.CreateCompatibleBitmap`, allocates/copies 24-bit pixel buffers, constructs edge/corner color bands, and returns a new `HBITMAP`. It computes cropped dimensions and only continues when both dimensions are positive and one normalized dimension is at least 0.25 of the corresponding source dimension. The function writes the resulting output dimensions through its two pointer parameters. This is the first direct evidence of the image-buffer/crop preparation layer before contour work; it is not a generic “brown versus white” threshold.

Evidence: `FUN_10001710` summary `ev_9b8ce32ce9c8f91e8ef4a574a0828725e72b63cf968012c0ef2a80db2e6aaf9`; callees `GDI32.CreateCompatibleBitmap` and buffer helpers are retained in that dossier; pseudocode pages show the bitmap allocation and row copies.
### Helper-level preprocessing and crop evidence (2026-11-08)

REA resolved the helper chain used by `FUN_10001710`:

- `FUN_10001040` calls `GetObjectW` on the bitmap and records width, signed height, bit depth, byte stride and pixel-buffer origin. It accounts for bottom-up DIBs by reversing the stride when the height sign is positive.
- `FUN_10001130` frees and reallocates a two-dimensional zeroed byte-buffer table: one row-pointer array and one zeroed buffer per row.
- `FUN_100012D0`, `FUN_10001400`, and `FUN_10001520` scan rows/columns against their channel mean. Each pixel must stay within an absolute per-channel deviation of `10`; the first failing row/column stops the crop scan. This is the concrete crop criterion, not a heuristic material/color test.
- `FUN_10001710` then builds a compatible bitmap, paints the detected border bands with sampled RGB values, copies the interior rows, and returns the cropped dimensions through its output pointers.

This explains the edge bands: they are sampled-color border fills used while constructing the cropped bitmap. The crop is selected by uniform-color runs at the image edges using the per-channel `<=10` rule. Evidence: `FUN_10001040` `ev_31c48a43176cb75674a289298bf98445de8434178857251f9e2a5f42f4b92a66`; `FUN_10001130` `ev_03677333cad43f8009b1d14ac8dc170d3afdb240981b2973af70999a4f758294`; `FUN_100012D0` `ev_da326fcd060294f676b7f52e0744cd62ce17cc8b1b26f2fcea98113c6de9f225`; `FUN_10001400` `ev_cfa19f20c2dc44d30177e74871c00b593e9e9956e39f0e7d5550a98aae1ac7f6`; `FUN_10001520` `ev_f308dd871befb8113074cd572adc75b195d80e921ca826930da279d1aa1188cb`.
### Deep helper follow-up (2026-11-08)

The requested helper addresses were traced from retained native dossiers. FUN_10002ed0 expands 2-float source records into 0x90-byte records, writes coordinate doubles, and initializes per-record flags; it is record initialization, not pixel segmentation. FUN_10002e30 maintains a capacity-backed count*0x18 scratch buffer, calls FUN_10002670, selects one of two record sets by mode, stores count/last-index state, then calls FUN_10006250. FUN_10002670 stages four cyclic neighbouring coordinate pairs per point, with forward and reverse traversal modes. Evidence: ev_be70cf6de1d574e81ee020a099942f2f78c56fcb25763cbf4a2fa9c05835c80c, ev_93698744f6ecc4fad34fc55bbb51388d1bd77b4c7546e110d03f9128e11689f0, ev_122022d28faae7e5f374357cc5c11d7829d5f3e46f487b62d852f63c10e5dc0e.

FUN_100033e0 is the point acceptance/reduction stage. It evaluates 0x90-byte candidates through FUN_10003cd0 and FUN_10003b20, marks accepted records at +0x89, propagates marks to cyclic predecessor/successor, copies compact 0x0c-byte output records, runs post-processing helpers, and removes candidates using angle guards including 0.15, 0.667, and 3.92699-radian branches. Evidence: ev_6e74d28dfdb24ad2b9d04e83633227ed36bfdf571cb860d7a126705265a0615c.

FUN_100070d0 is a cyclic path/segment walker. It grows working arrays at +0x1bc and +0x1c8, tracks start/end indices at +0x1a4/+0x1ac, calls FUN_10005fc0 to test spans, bisects rejected spans by midpoint, copies a 0xc0-byte candidate state, and advances through hierarchy helpers FUN_10007340, FUN_10007470, FUN_10007510, and FUN_10007630. This is geometry/path subdivision, not the image segmentation threshold itself. Evidence: ev_f8efc5b8267f36b5356d0f9d24487da8fdf6d80acf96c58a667fb918a30dd8c.

These helpers separate into record expansion (0x2ed0), cyclic neighbour staging and point reduction (0x2e30/0x2670/0x33e0), and path subdivision (0x70d0). The upstream pixel classification and contour extractor remain in export 3 and its bitmap/preprocessing callees.
### Export-3 refinement detail (2026-11-08)

Further paging of FUN_10017600 shows the native refinement is not a generic contour smoothing pass. For each candidate segment it builds directional windows with offsets around +/-3 pixels, reads 4-byte-stride pixel records from the prepared row buffers at param+0xb0, accumulates local channel values, computes an absolute per-channel/Manhattan difference against the local reference, and keeps the candidate with the minimum cost. A fallback branch preserves the current point when no candidate wins; a later pass averages seven neighbouring samples. This is direct static evidence for a bounded local pixel-cost search and explains why simple global thresholding cannot match Gemini on shadows and paper edges. Evidence: ev_c586147d69d2ed7d6740bd26685b4a011df0c67809d553469a0570629091a2ea, parent ev_b1bc4df684279977e9b3c20f40dc9ccad0af8bb4e771a54be021a64263dbddc3.
### Export-3 orchestration follow-up (2026-11-08)

Additional callee inspection separates orchestration from pixel work. FUN_100102a0 is a progress/state driver: it posts UI progress messages, iterates detected components, invokes contour post-processing and calls FUN_10010a30. FUN_10010a30 groups/splits contour candidates using coordinate continuity tests with absolute gaps below 11 units and angle/spacing checks; it does not read image pixels. FUN_100106f0 is a geometric distance helper returning ABS(param_3 * 0.5) in its fallback path. Therefore these functions cannot explain the white-background false foreground. The remaining pixel-semantic boundary is inside the export-3 bitmap/row-buffer pipeline and the byte-reading loops already observed in FUN_10017600; static analysis still does not expose a named standalone mask function or its complete threshold table. Evidence: FUN_100102a0 parent ev_c1ec757eae8e8edd84f6b458e4e74a777d364ce7d231ef2917ca454b6f2e7d4e; FUN_10010a30 parent ev_1612d231adfe23d0ab8b32a8c31bcb3d354a0d49c5ef2b497c10b2404a9ffff0; FUN_100106f0 parent ev_3989325288a327cf56de26cefb5d6a1f5fc0e93836948cec8926a339e4b40cf6.
### Export-3 input-to-contour chain (2026-11-08)

The export-3 pseudocode now gives a concrete order: FUN_10001710 creates the bitmap, then FUN_100128f0 initializes working buffers; FUN_10015c40 computes component bounds; FUN_10011250 calls FUN_10001040 and FUN_10001130, expands each component crop by 14 pixels on every side, records the cropped width/height and copies the 24-bit bitmap rows into the row buffers. FUN_10012510 initializes the path buffers, calls FUN_10011c70 and FUN_100121e0, then iterates FUN_10012720; only after these stages does export 3 call FUN_10017600, followed by FUN_1000d6a0. FUN_100121e0 computes central differences of neighbouring coordinate fields (factor 0.5), and FUN_10012720 reads the generated path/gradient records and evaluates geometric continuity. This proves that the missing mask/edge decision occurs before the path records consumed by FUN_100121e0/12720; those two helpers are derivative/path processing, not the original image segmentation. Evidence: export-3 parent ev_0707005867a94806d8a3a13867bbaac5d53d922cb2c58f5297362bd0ffdb3c4d; FUN_10011250 ev_36ff527456125afdfc3faf194e0570160dd95460b1fa590deb98fcd5a60c7317; FUN_100121e0 ev_ae194030e415d3abaa5d7ffc38c8b2fdc8e6aedad6b190358f2c22294eb33137; FUN_10012720 ev_38a4c9cbff4ad443bdcecf5ad36a3693da57ce27027ec4299b83120d55c0a313.
### Pixel-mask cost helper recovered (2026-11-08)

The missing pixel-semantic stage is now partially resolved. FUN_10011c70 walks the prepared component grid and calls FUN_10011aa0. FUN_10011aa0 samples a single pixel or a bounded window and delegates to FUN_1000e000. FUN_1000e000 checks the pixel flag byte at BGRA record offset +3 against mask bits (param_3 | 2); unvisited pixels are added to the current region, then FUN_1000e2e0 evaluates a +/-3 neighbourhood and stores the resulting cost field. FUN_1000e2e0 is now explicit: it sums B, G, and R bytes over the requested rectangle, computes the rectangle mean for each channel, sums absolute per-pixel deviations from those three means, and returns the average combined deviation. FUN_10011c70 uses the resulting costs with observed thresholds `local_38 < 10` and `10 < local_38` while constructing/marking path records. This is the first direct evidence of Gemini's local uniformity/edge cost before contour refinement; it is not a global brightness threshold. Evidence: FUN_10011c70 ev_af3b56f6775ef00bc6576a8baa0df2eca6425fe48a7ed43e89acf5bf6443bf84; FUN_10011aa0 ev_9a15384cf7f35df67f0625c3a917f1d84326fa1f729b9387847cdd7c5628f625; FUN_1000e000 ev_b7b6cc6025b5dc89f6d4ae81bc1d2b825a9921fd2347fb85915a1928ee2d6b92; FUN_1000e2e0 ev_e99715f336c3205475516919164ac1159cc90c4522dde6490eabfca2decb6426.
