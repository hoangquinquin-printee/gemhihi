using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

public sealed class DigitizerProjectDocument
{
    public string ProjectVersion { get; set; } = "2.0";
    public string ImageFilePath { get; set; } = string.Empty;
    public int ImageWidthPx { get; set; }
    public int ImageHeightPx { get; set; }
    public CalibrationProfile Calibration { get; set; } = CalibrationProfile.CreateDefaultV9GridProfile();
    public List<PatternPiece> Pieces { get; set; } = [];
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DxfParsedPieceSummary
{
    public string PieceName { get; init; } = string.Empty;
    public bool IsClosed { get; init; }
    public int VertexCount { get; init; }
    public Rect BoundingBoxMm { get; init; }
    public double WidthMm => BoundingBoxMm.Width;
    public double HeightMm => BoundingBoxMm.Height;
    public double PerimeterMm { get; init; }
    public double AreaMm2 { get; init; }
    public List<PhysicalPointMm> VerticesMm { get; init; } = [];
}

public sealed class DxfValidationReport
{
    public bool IsValid { get; init; }
    public int InsUnitsCode { get; init; }
    public string UnitsName => InsUnitsCode == 4 ? "Millimeters (mm)" : $"Code {InsUnitsCode}";
    public List<DxfParsedPieceSummary> ParsedPieces { get; init; } = [];
    public int NotchLineCount { get; init; }
    public int GrainLineCount { get; init; }
    public int InternalLineCount { get; init; }
    public int DrillPointCount { get; init; }
    public double MaxDimensionErrorMm { get; init; }
    public double MaxPerimeterErrorMm { get; init; }
    public double MaxAreaErrorCm2 { get; init; }
    public List<string> Errors { get; init; } = [];
}

public sealed class AamaDxfExportResult
{
    public string DxfPath { get; init; } = string.Empty;
    public int PieceCount { get; init; }
    public int NotchCount { get; init; }
    public int GrainLineCount { get; init; }
    public int InternalLineCount { get; init; }
    public int DrillPointCount { get; init; }
}

/// <summary>
/// Phase 2.6 File Engine:
/// Handles Save/Open Project (.pdp / .gem JSON format), CAD/AAMA DXF export in millimeters ($INSUNITS=4),
/// geometry validation, and an independent DXF parser/validator to verify exported CAD geometry.
/// </summary>
public sealed class FileEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly MeasurementEngine _measurementEngine;

    public FileEngine(MeasurementEngine? measurementEngine = null)
    {
        _measurementEngine = measurementEngine ?? new MeasurementEngine();
    }

    // =======================================================================
    // PROJECT SAVE / OPEN (.pdp / .gem)
    // =======================================================================

    public void SaveProject(DigitizerProjectDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Project file path cannot be empty.", nameof(filePath));
        }

        foreach (PatternPiece piece in document.Pieces)
        {
            _measurementEngine.RecalculatePieceMetrics(piece);
        }

        document.SavedAtUtc = DateTime.UtcNow;
        string fullPath = Path.GetFullPath(filePath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(fullPath, json, Encoding.UTF8);
    }

    public DigitizerProjectDocument LoadProject(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Project file path cannot be empty.", nameof(filePath));
        }

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Project file not found: {fullPath}", fullPath);
        }

        string json = File.ReadAllText(fullPath, Encoding.UTF8);
        DigitizerProjectDocument doc = JsonSerializer.Deserialize<DigitizerProjectDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Invalid or empty project file.");

        foreach (PatternPiece piece in doc.Pieces)
        {
            _measurementEngine.RecalculatePieceMetrics(piece);
        }

        return doc;
    }

    // =======================================================================
    // GEOMETRY VALIDATION BEFORE EXPORT
    // =======================================================================

    public bool ValidatePiecesForExport(IReadOnlyList<PatternPiece> pieces, out List<string> validationErrors)
    {
        validationErrors = [];
        if (pieces is null || pieces.Count == 0)
        {
            validationErrors.Add("No pattern pieces available to export.");
            return false;
        }

        foreach (PatternPiece piece in pieces)
        {
            if (piece.Vertices.Count < 3)
            {
                validationErrors.Add($"{piece.Name}: Contour has fewer than 3 vertices ({piece.Vertices.Count}).");
                continue;
            }

            foreach (ContourVertex v in piece.Vertices)
            {
                if (double.IsNaN(v.PositionMm.XMm) || double.IsNaN(v.PositionMm.YMm) ||
                    double.IsInfinity(v.PositionMm.XMm) || double.IsInfinity(v.PositionMm.YMm))
                {
                    validationErrors.Add($"{piece.Name}: Contains NaN or Infinity vertex coordinates.");
                    break;
                }
            }

            _measurementEngine.RecalculatePieceMetrics(piece);
            if (piece.AreaMm2 <= 1.0 || piece.PerimeterMm <= 1.0)
            {
                validationErrors.Add($"{piece.Name}: Degenerate contour area ({piece.AreaMm2:F2} mm^2) or perimeter ({piece.PerimeterMm:F2} mm).");
            }
        }

        return validationErrors.Count == 0;
    }

    // =======================================================================
    // DXF EXPORT (Millimeters, $INSUNITS = 4, Closed Contours + CAD Layers)
    // =======================================================================

    public void ExportDxf(
        IReadOnlyList<PatternPiece> pieces,
        string outputDxfPath,
        string profileName = "General",
        int curveSampleSteps = 24)
    {
        if (!ValidatePiecesForExport(pieces, out List<string> errors))
        {
            throw new InvalidOperationException($"DXF export validation failed: {string.Join("; ", errors)}");
        }

        string fullPath = Path.GetFullPath(outputDxfPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Compute global Y bound so CAD Y-up coordinates stay positive and un-mirrored
        double globalMaxYMm = pieces.SelectMany(p => p.Vertices).Max(v => v.PositionMm.YMm);
        double FlipY(double yMm) => globalMaxYMm - yMm;

        var sb = new StringBuilder(65536);

        // 1. HEADER SECTION ($INSUNITS = 4 -> Millimeters, $MEASUREMENT = 1 -> Metric)
        WriteGroup(sb, 0, "SECTION");
        WriteGroup(sb, 2, "HEADER");
        WriteGroup(sb, 9, "$ACADVER");
        WriteGroup(sb, 1, "AC1009");
        WriteGroup(sb, 9, "$INSUNITS");
        WriteGroup(sb, 70, "4");
        WriteGroup(sb, 9, "$MEASUREMENT");
        WriteGroup(sb, 70, "1");
        WriteGroup(sb, 9, "$PDPRO_PROFILE");
        WriteGroup(sb, 1, profileName);
        WriteGroup(sb, 0, "ENDSEC");

        // 2. ENTITIES SECTION
        WriteGroup(sb, 0, "SECTION");
        WriteGroup(sb, 2, "ENTITIES");

        foreach (PatternPiece piece in pieces)
        {
            _measurementEngine.RecalculatePieceMetrics(piece);

            // Sample closed contour in physical mm (no double scaling!).
            // Curves use adaptive De Casteljau subdivision so CAD accuracy is
            // controlled in millimetres instead of by a fixed vertex count.
            var contourPts = new List<PhysicalPointMm>();
            double curveToleranceMm = 0.05 / Math.Max(8, curveSampleSteps);
            for (int i = 0; i < piece.Vertices.Count; i++)
            {
                CubicBezierSegment seg = piece.GetSegment(i);
                if (!seg.IsCurve)
                {
                    contourPts.Add(new PhysicalPointMm(seg.P0.XMm, FlipY(seg.P0.YMm)));
                    continue;
                }

                List<PhysicalPointMm> sampled = SampleBezierAdaptive(seg, curveToleranceMm);
                for (int s = 0; s < sampled.Count - 1; s++)
                {
                    PhysicalPointMm pt = sampled[s];
                    contourPts.Add(new PhysicalPointMm(pt.XMm, FlipY(pt.YMm)));
                }
            }

            // Write closed POLYLINE on Layer "1" (AAMA/ASTM Outer Boundary)
            WriteGroup(sb, 0, "POLYLINE");
            WriteGroup(sb, 8, "1");
            WriteGroup(sb, 1, piece.Name);
            WriteGroup(sb, 66, "1");
            WriteGroup(sb, 70, "1"); // Bit 1 = Closed Polyline

            foreach (PhysicalPointMm pt in contourPts)
            {
                WriteGroup(sb, 0, "VERTEX");
                WriteGroup(sb, 8, "1");
                WriteGroup(sb, 10, Fmt(pt.XMm));
                WriteGroup(sb, 20, Fmt(pt.YMm));
                WriteGroup(sb, 30, "0.0");
            }

            WriteGroup(sb, 0, "SEQEND");
            WriteGroup(sb, 8, "1");

            // Write Graded Corners on Layer "2" (Turn / Grade Points)
            foreach (ContourVertex v in piece.Vertices.Where(v => v.IsGradedCorner))
            {
                WriteGroup(sb, 0, "POINT");
                WriteGroup(sb, 8, "2");
                WriteGroup(sb, 10, Fmt(v.PositionMm.XMm));
                WriteGroup(sb, 20, Fmt(FlipY(v.PositionMm.YMm)));
                WriteGroup(sb, 30, "0.0");
            }

            // Write Notches on Layer "4" (Notches)
            foreach (NotchItem notch in piece.Notches)
            {
                CubicBezierSegment seg = piece.GetSegment(notch.SegmentIndex);
                PhysicalPointMm pos = seg.Evaluate(notch.ParameterT);
                PhysicalPointMm deriv = seg.Derivative(notch.ParameterT);
                double tangentRad = Math.Atan2(deriv.YMm, deriv.XMm);
                double notchRad = notch.IsRelativeAngle
                    ? tangentRad + (notch.AngleDegrees * Math.PI / 180.0)
                    : (notch.AngleDegrees * Math.PI / 180.0);

                PhysicalPointMm tip = new(
                    pos.XMm + (Math.Cos(notchRad) * notch.LengthMm),
                    pos.YMm + (Math.Sin(notchRad) * notch.LengthMm));

                WriteLineEntity(sb, "4", pos.XMm, FlipY(pos.YMm), tip.XMm, FlipY(tip.YMm));
            }

            // Write Internal Lines ("8") and Grain Line ("7")
            foreach (InternalLineItem line in piece.InternalLines)
            {
                string layer = line.LineType == InternalLineType.GrainAxis ? "7" : "8";
                IReadOnlyList<PhysicalPointMm> points = line.GetExportPoints();
                for (int i = 1; i < points.Count; i++)
                {
                    WriteLineEntity(sb, layer, points[i - 1].XMm, FlipY(points[i - 1].YMm), points[i].XMm, FlipY(points[i].YMm));
                }
            }

            // Write Internal Points / Drill Holes on Layer "13"
            foreach (InternalPointItem ip in piece.InternalPoints)
            {
                WriteGroup(sb, 0, "POINT");
                WriteGroup(sb, 8, "13");
                WriteGroup(sb, 10, Fmt(ip.PositionMm.XMm));
                WriteGroup(sb, 20, Fmt(FlipY(ip.PositionMm.YMm)));
                WriteGroup(sb, 30, "0.0");
            }

            // Write Piece Label TEXT on Layer "19" at piece bounding box center
            Rect bbox = piece.BoundingBoxMm;
            double cx = bbox.X + (bbox.Width * 0.5);
            double cy = FlipY(bbox.Y + (bbox.Height * 0.5));
            WriteGroup(sb, 0, "TEXT");
            WriteGroup(sb, 8, "19");
            WriteGroup(sb, 10, Fmt(cx));
            WriteGroup(sb, 20, Fmt(cy));
            WriteGroup(sb, 30, "0.0");
            WriteGroup(sb, 40, "10.0");
            WriteGroup(sb, 1, piece.Name);
        }

        WriteGroup(sb, 0, "ENDSEC");
        WriteGroup(sb, 0, "EOF");

        File.WriteAllText(fullPath, sb.ToString(), Encoding.ASCII);
    }

    /// <summary>
    /// AAMA DXF exchange export.  The layout is based on the observed
    /// LookStailor DXF-AAMA path; no secondary rule-table or archive format is
    /// emitted as a substitute for this exchange file.
    /// </summary>
    public AamaDxfExportResult ExportAamaDxf(
        IReadOnlyList<PatternPiece> pieces,
        string outputDxfPath)
    {
        if (!ValidatePiecesForExport(pieces, out List<string> errors))
            throw new InvalidOperationException($"AAMA DXF export validation failed: {string.Join("; ", errors)}");

        string dxfPath = Path.GetFullPath(outputDxfPath);
        string? directory = Path.GetDirectoryName(dxfPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string style = Path.GetFileNameWithoutExtension(dxfPath);
        // AAMA stores CAD geometry in a Y-up plane.  Use exactly the same
        // physical transform as the standard DXF writer so perimeter, notches,
        // internal lines, drill points, and labels stay registered together.
        double globalMaxYMm = pieces.SelectMany(piece => piece.Vertices).Max(vertex => vertex.PositionMm.YMm);
        double FlipY(double yMm) => globalMaxYMm - yMm;
        var sb = new StringBuilder(65536);
        WriteGroup(sb, 0, "SECTION"); WriteGroup(sb, 2, "HEADER"); WriteGroup(sb, 9, "$CLAYER"); WriteGroup(sb, 8, "1"); WriteGroup(sb, 0, "ENDSEC");
        WriteGroup(sb, 0, "SECTION"); WriteGroup(sb, 2, "TABLES");
        WriteGroup(sb, 0, "TABLE"); WriteGroup(sb, 2, "LTYPE"); WriteGroup(sb, 70, "1"); WriteGroup(sb, 0, "LTYPE"); WriteGroup(sb, 2, "CONTINUOUS"); WriteGroup(sb, 70, "64"); WriteGroup(sb, 3, "SOLIDLINE"); WriteGroup(sb, 72, "65"); WriteGroup(sb, 73, "0"); WriteGroup(sb, 40, "0.0000"); WriteGroup(sb, 0, "ENDTAB");
        // Keep this table byte-for-byte structurally aligned with the observed
        // AAMA reference: the count is 14 even though its layer records also
        // include layer 19, and the non-default colours are significant to
        // importers that map marker entities by layer.
        WriteGroup(sb, 0, "TABLE"); WriteGroup(sb, 2, "LAYER"); WriteGroup(sb, 70, "14");
        foreach ((int Layer, int Colour) layer in new[]
        {
            (1, 7), (2, 1), (3, 2), (4, 3), (5, 5), (6, 5), (7, 5),
            (8, 4), (9, 5), (10, 5), (11, 4), (12, 6), (13, 5), (14, 7), (19, 5)
        })
        {
            WriteGroup(sb, 0, "LAYER");
            WriteGroup(sb, 2, layer.Layer.ToString(CultureInfo.InvariantCulture));
            WriteGroup(sb, 70, "0");
            WriteGroup(sb, 62, layer.Colour.ToString(CultureInfo.InvariantCulture));
            WriteGroup(sb, 6, "CONTINUOUS");
        }
        WriteGroup(sb, 0, "ENDTAB");
        WriteGroup(sb, 0, "TABLE"); WriteGroup(sb, 2, "STYLE"); WriteGroup(sb, 70, "1");
        WriteGroup(sb, 0, "STYLE"); WriteGroup(sb, 2, "STANDARD"); WriteGroup(sb, 70, "0");
        WriteGroup(sb, 40, "0.0000"); WriteGroup(sb, 41, "1.0000"); WriteGroup(sb, 50, "0.0000");
        WriteGroup(sb, 71, "0"); WriteGroup(sb, 42, "1.0000"); WriteGroup(sb, 3, "txt"); WriteGroup(sb, 4, string.Empty);
        WriteGroup(sb, 0, "ENDTAB"); WriteGroup(sb, 0, "ENDSEC");
        WriteGroup(sb, 0, "SECTION"); WriteGroup(sb, 2, "BLOCKS");
        int featureNumber = 0, notchCount = 0, grainCount = 0, internalCount = 0, drillCount = 0;
        for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
        {
            PatternPiece piece = pieces[pieceIndex]; _measurementEngine.RecalculatePieceMetrics(piece);
            string block = pieceIndex.ToString(CultureInfo.InvariantCulture);
            WriteGroup(sb, 0, "BLOCK"); WriteGroup(sb, 8, "0"); WriteGroup(sb, 2, block); WriteGroup(sb, 70, "0"); WriteGroup(sb, 10, "0.0000"); WriteGroup(sb, 20, "0.0000");
            WriteGroup(sb, 0, "POLYLINE"); WriteGroup(sb, 8, "1"); WriteGroup(sb, 66, "1"); WriteGroup(sb, 70, "1");
            // Keep curves geometric: an AAMA POLYLINE has no cubic-Bezier
            // payload, so each cubic is adaptively tessellated in millimetres
            // before its vertices are written.  Exporting only P0/P3 turns a
            // curved edge into a chord and visibly changes the pattern.
            var contourPoints = new List<PhysicalPointMm>();
            const double curveToleranceMm = 0.05;
            for (int segmentIndex = 0; segmentIndex < piece.Vertices.Count; segmentIndex++)
            {
                CubicBezierSegment segment = piece.GetSegment(segmentIndex);
                if (!segment.IsCurve)
                {
                    contourPoints.Add(new PhysicalPointMm(segment.P0.XMm, FlipY(segment.P0.YMm)));
                    continue;
                }

                List<PhysicalPointMm> sampled = SampleBezierAdaptive(segment, curveToleranceMm);
                for (int sampleIndex = 0; sampleIndex < sampled.Count - 1; sampleIndex++)
                {
                    PhysicalPointMm sample = sampled[sampleIndex];
                    contourPoints.Add(new PhysicalPointMm(sample.XMm, FlipY(sample.YMm)));
                }
            }
            // AAMA readers treat the first non-VERTEX entity as the end of a
            // POLYLINE, even if its SEQEND has not yet been read.  Therefore
            // emit every contour vertex as one contiguous run, then SEQEND,
            // then the point/text records.  Interleaving POINT/TEXT here was
            // the direct cause of AccuMark's "perimeter missing in block 0".
            foreach (PhysicalPointMm point in contourPoints)
            {
                WriteGroup(sb, 0, "VERTEX");
                WriteGroup(sb, 8, "1");
                WriteGroup(sb, 10, Fmt(point.XMm));
                WriteGroup(sb, 20, Fmt(point.YMm));
            }
            WriteGroup(sb, 0, "SEQEND"); WriteGroup(sb, 8, "1");
            foreach (ContourVertex vertex in piece.Vertices)
            {
                WriteGroup(sb, 0, "POINT"); WriteGroup(sb, 8, "2");
                WriteGroup(sb, 10, Fmt(vertex.PositionMm.XMm)); WriteGroup(sb, 20, Fmt(FlipY(vertex.PositionMm.YMm)));
                WriteGroup(sb, 0, "TEXT"); WriteGroup(sb, 8, "2");
                WriteGroup(sb, 10, Fmt(vertex.PositionMm.XMm)); WriteGroup(sb, 20, Fmt(FlipY(vertex.PositionMm.YMm)));
                WriteGroup(sb, 40, "10.00"); WriteGroup(sb, 1, $"#-{++featureNumber}");
            }
            foreach (NotchItem notch in piece.Notches)
            {
                CubicBezierSegment seg = piece.GetSegment(notch.SegmentIndex);
                PhysicalPointMm p = seg.Evaluate(notch.ParameterT);
                // LookStailorX/Gemini DXF-AAMA does not write a notch as a
                // visible LINE.  It writes its feature record as a TEXT label
                // followed by a POINT on layer 4; group 30 identifies the
                // marker and group 50 carries its angle.  Several garment CAD
                // importers use precisely this record to create the notch.
                double x = p.XMm;
                double y = FlipY(p.YMm);
                WriteGroup(sb, 0, "TEXT"); WriteGroup(sb, 8, "4");
                WriteGroup(sb, 10, Fmt(x)); WriteGroup(sb, 20, Fmt(y));
                WriteGroup(sb, 40, "10.00"); WriteGroup(sb, 1, $"#-{++featureNumber}");
                WriteGroup(sb, 0, "POINT"); WriteGroup(sb, 8, "4");
                WriteGroup(sb, 10, Fmt(x)); WriteGroup(sb, 20, Fmt(y));
                WriteGroup(sb, 30, "15");
                WriteGroup(sb, 50, notch.AngleDegrees.ToString("G17", CultureInfo.InvariantCulture));
                notchCount++;
            }
            foreach (InternalLineItem line in piece.InternalLines) { string layer = line.LineType == InternalLineType.GrainAxis ? "7" : "8"; IReadOnlyList<PhysicalPointMm> points = line.GetExportPoints(); for (int p = 1; p < points.Count; p++) WriteLineEntity(sb, layer, points[p - 1].XMm, FlipY(points[p - 1].YMm), points[p].XMm, FlipY(points[p].YMm)); if (layer == "7") grainCount++; else internalCount++; }
            foreach (InternalPointItem point in piece.InternalPoints) { WriteGroup(sb, 0, "POINT"); WriteGroup(sb, 8, "13"); WriteGroup(sb, 10, Fmt(point.PositionMm.XMm)); WriteGroup(sb, 20, Fmt(FlipY(point.PositionMm.YMm))); drillCount++; }
            Rect box = piece.BoundingBoxMm; foreach (string text in new[] { $"PIECE NAME: {piece.Name}", "SIZE: MB", "ANNOTATION: ", "QUANTITY: 1", "MATERIAL: " }) { WriteGroup(sb, 0, "TEXT"); WriteGroup(sb, 8, "1"); WriteGroup(sb, 10, Fmt(box.X + box.Width / 2)); WriteGroup(sb, 20, Fmt(FlipY(box.Y + box.Height + 12))); WriteGroup(sb, 40, "10.00"); WriteGroup(sb, 1, text); }
            WriteGroup(sb, 0, "ENDBLK");
        }
        WriteGroup(sb, 0, "ENDSEC"); WriteGroup(sb, 0, "SECTION"); WriteGroup(sb, 2, "ENTITIES");
        for (int i = 0; i < pieces.Count; i++) { WriteGroup(sb, 0, "INSERT"); WriteGroup(sb, 8, "1"); WriteGroup(sb, 2, i.ToString(CultureInfo.InvariantCulture)); WriteGroup(sb, 10, "0"); WriteGroup(sb, 20, "0"); }
        string[] metadata = { $"STYLE NAME: {style}", $"CREATION DATE: {DateTime.Now:dd.MM.yyyy}", $"CREATION TIME: {DateTime.Now:h:mm:ss tt}", "AUTHOR: GEMINI CAD SYSTEMS", "PRODUCT: GEMINI PATTERN EDITOR", "SAMPLE SIZE: MB", $"GRADE RULE TABLE: {style}", "UNITS: METRIC" };
        for (int i = 0; i < metadata.Length; i++) { WriteGroup(sb, 0, "TEXT"); WriteGroup(sb, 8, "1"); WriteGroup(sb, 10, "0"); WriteGroup(sb, 20, (-20 - (12 * i)).ToString(CultureInfo.InvariantCulture)); WriteGroup(sb, 40, "10.00"); WriteGroup(sb, 1, metadata[i]); }
        WriteGroup(sb, 0, "ENDSEC"); WriteGroup(sb, 0, "EOF"); File.WriteAllText(dxfPath, sb.ToString(), Encoding.ASCII);
        return new AamaDxfExportResult { DxfPath = dxfPath, PieceCount = pieces.Count, NotchCount = notchCount, GrainLineCount = grainCount, InternalLineCount = internalCount, DrillPointCount = drillCount };
    }

    // =======================================================================
    // INDEPENDENT DXF PARSER & GEOMETRY VALIDATOR
    // =======================================================================

    /// <summary>
    /// Independently parses a DXF file from disk and validates its units ($INSUNITS=4),
    /// closed piece contours, bounding box dimensions, perimeters, areas, notches, internal lines,
    /// and compares them against the expected PatternPiece geometry.
    /// </summary>
    public DxfValidationReport ParseAndValidateDxfFile(
        string dxfFilePath,
        IReadOnlyList<PatternPiece>? referencePieces = null)
    {
        var errors = new List<string>();
        if (!File.Exists(dxfFilePath))
        {
            errors.Add($"DXF file does not exist: {dxfFilePath}");
            return new DxfValidationReport { IsValid = false, Errors = errors };
        }

        string[] lines = File.ReadAllLines(dxfFilePath);
        int insUnits = 0;
        var parsedPieces = new List<DxfParsedPieceSummary>();
        int notchCount = 0;
        int grainCount = 0;
        int internalLineCount = 0;
        int drillCount = 0;

        for (int i = 0; i + 1 < lines.Length; i += 2)
        {
            if (!int.TryParse(lines[i].Trim(), out int code))
            {
                continue;
            }
            string val = lines[i + 1].Trim();

            if (code == 9 && val == "$INSUNITS" && i + 3 < lines.Length)
            {
                int.TryParse(lines[i + 3].Trim(), out insUnits);
            }
            else if (code == 0 && val == "POLYLINE")
            {
                string layer = string.Empty;
                string pieceName = $"Piece {parsedPieces.Count + 1}";
                int flags = 0;
                var polyVertices = new List<PhysicalPointMm>();

                i += 2;
                while (i + 1 < lines.Length)
                {
                    int subCode = int.Parse(lines[i].Trim(), CultureInfo.InvariantCulture);
                    string subVal = lines[i + 1].Trim();

                    if (subCode == 0)
                    {
                        if (subVal == "VERTEX")
                        {
                            double vx = 0, vy = 0;
                            i += 2;
                            while (i + 1 < lines.Length)
                            {
                                int vCode = int.Parse(lines[i].Trim(), CultureInfo.InvariantCulture);
                                string vVal = lines[i + 1].Trim();
                                if (vCode == 0)
                                {
                                    i -= 2;
                                    break;
                                }
                                if (vCode == 10) vx = double.Parse(vVal, CultureInfo.InvariantCulture);
                                else if (vCode == 20) vy = double.Parse(vVal, CultureInfo.InvariantCulture);
                                i += 2;
                            }
                            polyVertices.Add(new PhysicalPointMm(vx, vy));
                        }
                        else if (subVal == "SEQEND")
                        {
                            break;
                        }
                    }
                    else if (subCode == 8) layer = subVal;
                    else if (subCode == 1) pieceName = subVal;
                    else if (subCode == 70) int.TryParse(subVal, out flags);

                    i += 2;
                }

                if (layer == "1")
                {
                    bool isClosed = (flags & 1) != 0;
                    if (!isClosed)
                    {
                        errors.Add($"{pieceName}: POLYLINE is not marked closed (flag 70={flags}).");
                    }
                    if (polyVertices.Count < 3)
                    {
                        errors.Add($"{pieceName}: POLYLINE has fewer than 3 vertices ({polyVertices.Count}).");
                    }

                    double perim = 0;
                    double twiceArea = 0;
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;

                    for (int k = 0; k < polyVertices.Count; k++)
                    {
                        PhysicalPointMm p0 = polyVertices[k];
                        PhysicalPointMm p1 = polyVertices[(k + 1) % polyVertices.Count];
                        perim += p0.DistanceTo(p1);
                        twiceArea += (p0.XMm * p1.YMm) - (p1.XMm * p0.YMm);
                        if (p0.XMm < minX) minX = p0.XMm;
                        if (p0.YMm < minY) minY = p0.YMm;
                        if (p0.XMm > maxX) maxX = p0.XMm;
                        if (p0.YMm > maxY) maxY = p0.YMm;
                    }

                    parsedPieces.Add(new DxfParsedPieceSummary
                    {
                        PieceName = pieceName,
                        IsClosed = isClosed,
                        VertexCount = polyVertices.Count,
                        BoundingBoxMm = new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY)),
                        PerimeterMm = perim,
                        AreaMm2 = 0.5 * Math.Abs(twiceArea),
                        VerticesMm = polyVertices
                    });
                }
            }
            else if (code == 0 && val == "LINE")
            {
                string layer = string.Empty;
                int j = i + 2;
                while (j + 1 < lines.Length)
                {
                    int lCode = int.Parse(lines[j].Trim(), CultureInfo.InvariantCulture);
                    string lVal = lines[j + 1].Trim();
                    if (lCode == 0) break;
                    if (lCode == 8) layer = lVal;
                    j += 2;
                }
                // Legacy generic DXF uses LINE/layer 4 for a notch; retain
                // that parser support.  LookStailor-compatible AAMA uses a
                // POINT/layer 4 and is counted below.
                if (layer == "4") notchCount++;
                else if (layer == "7") grainCount++;
                else if (layer == "8") internalLineCount++;
            }
            else if (code == 0 && val == "POINT")
            {
                string layer = string.Empty;
                int j = i + 2;
                while (j + 1 < lines.Length)
                {
                    int pCode = int.Parse(lines[j].Trim(), CultureInfo.InvariantCulture);
                    string pVal = lines[j + 1].Trim();
                    if (pCode == 0) break;
                    if (pCode == 8) layer = pVal;
                    j += 2;
                }
                if (layer == "4") notchCount++;
                else if (layer == "13") drillCount++;
            }
        }

        if (insUnits != 4)
        {
            errors.Add($"Expected $INSUNITS = 4 (Millimeters), but found {insUnits}.");
        }

        if (parsedPieces.Count == 0)
        {
            errors.Add("No closed piece POLYLINE contours found on Layer 1.");
        }

        double maxDimErrMm = 0.0;
        double maxPerimErrMm = 0.0;
        double maxAreaErrCm2 = 0.0;

        if (referencePieces is not null)
        {
            if (parsedPieces.Count != referencePieces.Count)
            {
                errors.Add($"Piece count mismatch: DXF has {parsedPieces.Count}, expected {referencePieces.Count}.");
            }
            else
            {
                for (int idx = 0; idx < referencePieces.Count; idx++)
                {
                    PatternPiece expected = referencePieces[idx];
                    DxfParsedPieceSummary actual = parsedPieces[idx];

                    double errW = Math.Abs(actual.WidthMm - expected.BoundingBoxMm.Width);
                    double errH = Math.Abs(actual.HeightMm - expected.BoundingBoxMm.Height);
                    double errPerim = Math.Abs(actual.PerimeterMm - expected.PerimeterMm);
                    double errAreaCm2 = Math.Abs((actual.AreaMm2 - expected.AreaMm2) / 100.0);

                    maxDimErrMm = Math.Max(maxDimErrMm, Math.Max(errW, errH));
                    maxPerimErrMm = Math.Max(maxPerimErrMm, errPerim);
                    maxAreaErrCm2 = Math.Max(maxAreaErrCm2, errAreaCm2);

                    if (errW > 0.5 || errH > 0.5)
                    {
                        errors.Add($"{expected.Name}: Bounding box dimension error ({errW:F3} mm, {errH:F3} mm) exceeds 0.5 mm tolerance.");
                    }
                    if (errPerim > 1.0)
                    {
                        errors.Add($"{expected.Name}: Perimeter error ({errPerim:F3} mm) exceeds 1.0 mm tolerance.");
                    }
                }
            }
        }

        return new DxfValidationReport
        {
            IsValid = errors.Count == 0,
            InsUnitsCode = insUnits,
            ParsedPieces = parsedPieces,
            NotchLineCount = notchCount,
            GrainLineCount = grainCount,
            InternalLineCount = internalLineCount,
            DrillPointCount = drillCount,
            MaxDimensionErrorMm = Math.Round(maxDimErrMm, 4),
            MaxPerimeterErrorMm = Math.Round(maxPerimErrMm, 4),
            MaxAreaErrorCm2 = Math.Round(maxAreaErrCm2, 4),
            Errors = errors
        };
    }

    private static void WriteLineEntity(
        StringBuilder sb,
        string layer,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        WriteGroup(sb, 0, "LINE");
        WriteGroup(sb, 8, layer);
        WriteGroup(sb, 10, Fmt(x1));
        WriteGroup(sb, 20, Fmt(y1));
        WriteGroup(sb, 30, "0.0");
        WriteGroup(sb, 11, Fmt(x2));
        WriteGroup(sb, 21, Fmt(y2));
        WriteGroup(sb, 31, "0.0");
    }


    private static List<PhysicalPointMm> SampleBezierAdaptive(CubicBezierSegment segment, double toleranceMm)
    {
        var points = new List<PhysicalPointMm> { segment.P0 };
        Subdivide(segment, Math.Max(0.0005, toleranceMm), 0, points);
        return points;
    }

    private static void Subdivide(
        CubicBezierSegment segment,
        double toleranceMm,
        int depth,
        List<PhysicalPointMm> output)
    {
        double flatness = Math.Max(
            DistanceToLine(segment.C1, segment.P0, segment.P3),
            DistanceToLine(segment.C2, segment.P0, segment.P3));
        if (flatness <= toleranceMm || depth >= 20)
        {
            output.Add(segment.P3);
            return;
        }

        (CubicBezierSegment left, CubicBezierSegment right, _) = segment.SplitAt(0.5);
        Subdivide(left, toleranceMm, depth + 1, output);
        Subdivide(right, toleranceMm, depth + 1, output);
    }

    private static double DistanceToLine(PhysicalPointMm point, PhysicalPointMm start, PhysicalPointMm end)
    {
        double dx = end.XMm - start.XMm;
        double dy = end.YMm - start.YMm;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-12)
        {
            return point.DistanceTo(start);
        }
        return Math.Abs((dy * point.XMm) - (dx * point.YMm) +
                        (end.XMm * start.YMm) - (end.YMm * start.XMm)) / length;
    }

    private static void WriteGroup(StringBuilder sb, int code, string value)
    {
        sb.AppendLine(code.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(value);
    }

    private static string Fmt(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
}
