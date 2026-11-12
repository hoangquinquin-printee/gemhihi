# REA evidence manifest

This manifest lists the REA documents committed for handoff. The native target was the installed Gemini `SDEngine.dll`; the executable itself is not redistributed.

## Included documents

- `REA_RESEARCH_INDEX.md` — index of prior REA/Ghidra investigations.
- `REA_DETECT_TRACE_2026-11-08.md` — runtime call order, export-3 pipeline, helper traces, pixel-cost findings, and limitations.

## Main evidence areas

- EXE to DLL export bridge and Detect call order.
- Bitmap/DIB extraction and edge crop helpers.
- Component crop expansion by 14 pixels.
- BGRA flag byte at record offset `+3`.
- Local BGR uniformity cost in `FUN_1000e2e0`.
- Path/gradient processing and contour refinement.
- Point reduction and cyclic neighbour staging.

## Scope and limitations

Evidence IDs in the report are retained REA references, not portable copies of Gemini binaries or private data. The report distinguishes observed static facts from inference and unresolved runtime semantics. The complete standalone mask decision table and all indirect call targets remain unresolved.
