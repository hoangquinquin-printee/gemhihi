using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

public sealed class UndoSnapshotEntry
{
    public required string ActionName { get; init; }
    public required List<PatternPiece> PiecesSnapshot { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Phase 2.4 & 2.5 Geometry Editing Engine:
/// Implements Select, Move Point (translating attached Bézier handles per verified MiscaPuncteSimple),
/// Move Piece, Add Point (exact De Casteljau cubic curve subdivision per verified SplitCurve / FUN_004bdc70),
/// Delete Point, Add/Delete Corner, Line-to-Curve, Curve-to-Line, Edit Bézier Handles,
/// Notch / Internal Point / Internal Line / Grain Line editing, and Snapshot-based Undo/Redo.
/// </summary>
public sealed class EditingEngine
{
    private readonly GeometryEngine _geometryEngine;
    private readonly MeasurementEngine _measurementEngine;
    private readonly Stack<UndoSnapshotEntry> _undoStack = new();
    private readonly Stack<UndoSnapshotEntry> _redoStack = new();

    public EditingEngine(GeometryEngine? geometryEngine = null, MeasurementEngine? measurementEngine = null)
    {
        _geometryEngine = geometryEngine ?? new GeometryEngine();
        _measurementEngine = measurementEngine ?? new MeasurementEngine();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    /// <summary>
    /// Saves a deep snapshot of all pieces before a mutating action and clears the Redo stack
    /// (matching Gemini's TImagine::CreateUndoPoint 0x5d61d0 + ClearStivaRedo 0x5d656c).
    /// </summary>
    public void PushUndoSnapshot(IEnumerable<PatternPiece> currentPieces, string actionName)
    {
        ArgumentNullException.ThrowIfNull(currentPieces);
        var cloned = currentPieces.Select(p => p.Clone()).ToList();
        _undoStack.Push(new UndoSnapshotEntry
        {
            ActionName = actionName,
            PiecesSnapshot = cloned
        });
        _redoStack.Clear();
    }

    public (bool Success, List<PatternPiece> RestoredPieces, string ActionName) Undo(IEnumerable<PatternPiece> currentPieces)
    {
        if (_undoStack.Count == 0)
        {
            return (false, currentPieces.Select(p => p.Clone()).ToList(), string.Empty);
        }

        UndoSnapshotEntry top = _undoStack.Pop();
        _redoStack.Push(new UndoSnapshotEntry
        {
            ActionName = top.ActionName,
            PiecesSnapshot = currentPieces.Select(p => p.Clone()).ToList()
        });

        return (true, top.PiecesSnapshot.Select(p => p.Clone()).ToList(), top.ActionName);
    }

    public (bool Success, List<PatternPiece> RestoredPieces, string ActionName) Redo(IEnumerable<PatternPiece> currentPieces)
    {
        if (_redoStack.Count == 0)
        {
            return (false, currentPieces.Select(p => p.Clone()).ToList(), string.Empty);
        }

        UndoSnapshotEntry top = _redoStack.Pop();
        _undoStack.Push(new UndoSnapshotEntry
        {
            ActionName = top.ActionName,
            PiecesSnapshot = currentPieces.Select(p => p.Clone()).ToList()
        });

        return (true, top.PiecesSnapshot.Select(p => p.Clone()).ToList(), top.ActionName);
    }

    // =======================================================================
    // VERTEX & CONTOUR EDITING
    // =======================================================================

    /// <summary>
    /// Moves a single contour vertex by (deltaXMm, deltaYMm) and translates its attached incoming
    /// and outgoing Cubic Bézier control handles by the exact same delta (matching MiscaPuncteSimple / FUN_004b8b1c).
    /// </summary>
    public void MoveVertexByDelta(
        PatternPiece piece,
        int vertexIndex,
        double deltaXMm,
        double deltaYMm,
        CalibrationProfile? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (vertexIndex < 0 || vertexIndex >= piece.Vertices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        }

        var delta = new PhysicalPointMm(deltaXMm, deltaYMm);
        ContourVertex v = piece.Vertices[vertexIndex];
        v.PositionMm += delta;
        v.InControlHandleMm += delta;
        v.OutControlHandleMm += delta;

        if (calibration is not null)
        {
            v.SourcePixelPosition = calibration.PhysicalMmToPixel(v.PositionMm);
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
    }

    public void MoveVertexToAbsolute(
        PatternPiece piece,
        int vertexIndex,
        PhysicalPointMm newPositionMm,
        CalibrationProfile? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (vertexIndex < 0 || vertexIndex >= piece.Vertices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        }

        PhysicalPointMm current = piece.Vertices[vertexIndex].PositionMm;
        MoveVertexByDelta(piece, vertexIndex, newPositionMm.XMm - current.XMm, newPositionMm.YMm - current.YMm, calibration);
    }

    /// <summary>
    /// Moves an entire pattern piece (all vertices, Bézier handles, notches, internal points, and internal lines)
    /// by (deltaXMm, deltaYMm).
    /// </summary>
    public void MovePieceByDelta(
        PatternPiece piece,
        double deltaXMm,
        double deltaYMm,
        CalibrationProfile? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        var delta = new PhysicalPointMm(deltaXMm, deltaYMm);

        foreach (ContourVertex v in piece.Vertices)
        {
            v.PositionMm += delta;
            v.InControlHandleMm += delta;
            v.OutControlHandleMm += delta;
            if (calibration is not null)
            {
                v.SourcePixelPosition = calibration.PhysicalMmToPixel(v.PositionMm);
            }
        }

        foreach (NotchItem n in piece.Notches)
        {
            n.PositionMm += delta;
        }

        foreach (InternalPointItem ip in piece.InternalPoints)
        {
            ip.PositionMm += delta;
        }

        foreach (InternalLineItem il in piece.InternalLines)
        {
            il.StartMm += delta;
            il.EndMm += delta;
            il.ControlPointMm += delta;
            for (int i = 0; i < il.PolylinePointsMm.Count; i++)
            {
                il.PolylinePointsMm[i] += delta;
            }
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
    }

    /// <summary>
    /// Splits segment <paramref name="segmentIndex"/> at parameter <paramref name="parameterT"/> in (0, 1)
    /// using De Casteljau cubic subdivision (matching verified @TImagine@SplitCurve$qv 0x5d53a0 -> FUN_004bdc70).
    /// Preserves the exact shape of curved Cubic Bézier segments.
    /// Returns the index of the newly inserted vertex.
    /// </summary>
    public int AddPointOnSegment(
        PatternPiece piece,
        int segmentIndex,
        double parameterT = 0.5,
        bool isGradedCorner = false,
        CalibrationProfile? calibration = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            throw new InvalidOperationException("Cannot add point to a contour with fewer than 2 vertices.");
        }

        int i0 = ((segmentIndex % n) + n) % n;
        int i1 = (i0 + 1) % n;
        double t = Math.Clamp(parameterT, 0.05, 0.95);

        CubicBezierSegment seg = piece.GetSegment(i0);
        (CubicBezierSegment left, CubicBezierSegment right, PhysicalPointMm splitPt) = seg.SplitAt(t);

        ContourVertex v0 = piece.Vertices[i0];
        ContourVertex v1 = piece.Vertices[i1];

        v0.OutControlHandleMm = left.C1;
        v1.InControlHandleMm = right.C2;

        var newVertex = new ContourVertex
        {
            PositionMm = splitPt,
            SourcePixelPosition = calibration is not null
                ? calibration.PhysicalMmToPixel(splitPt)
                : v0.SourcePixelPosition,
            IsGradedCorner = isGradedCorner,
            IsPieceOrigin = false,
            OutgoingIsCurve = seg.IsCurve,
            InControlHandleMm = left.C2,
            OutControlHandleMm = right.C1
        };

        int insertIndex = i0 + 1;
        piece.Vertices.Insert(insertIndex, newVertex);

        // Adjust segment indices of notches located after i0
        foreach (NotchItem notch in piece.Notches)
        {
            if (notch.SegmentIndex > i0)
            {
                notch.SegmentIndex++;
            }
            else if (notch.SegmentIndex == i0)
            {
                if (notch.ParameterT <= t)
                {
                    notch.ParameterT = t > 1e-6 ? notch.ParameterT / t : 0.5;
                }
                else
                {
                    notch.SegmentIndex = insertIndex;
                    notch.ParameterT = (1.0 - t) > 1e-6 ? (notch.ParameterT - t) / (1.0 - t) : 0.5;
                }
            }
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
        return insertIndex;
    }

    public int AddPointAtNearestLocation(
        PatternPiece piece,
        PhysicalPointMm queryPointMm,
        bool isGradedCorner = false,
        CalibrationProfile? calibration = null)
    {
        (int segIdx, double t, _, _) = _geometryEngine.FindNearestContourLocation(piece, queryPointMm);
        if (segIdx < 0)
        {
            return -1;
        }

        return AddPointOnSegment(piece, segIdx, t, isGradedCorner, calibration);
    }

    /// <summary>
    /// Deletes a vertex from a closed pattern piece contour (matching @TImagine@StergePuncte$qv 0x5d57c8).
    /// Enforces the geometric invariant that a closed contour must retain at least 3 vertices.
    /// </summary>
    public bool DeleteVertex(PatternPiece piece, int vertexIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n <= 3 || vertexIndex < 0 || vertexIndex >= n)
        {
            return false;
        }

        bool wasOrigin = piece.Vertices[vertexIndex].IsPieceOrigin;
        int prevIdx = (vertexIndex - 1 + n) % n;
        int nextIdx = (vertexIndex + 1) % n;

        ContourVertex vPrev = piece.Vertices[prevIdx];
        ContourVertex vNext = piece.Vertices[nextIdx];

        // If neither adjacent segment was curved, keep straight line handles; otherwise smooth chord handles
        if (!vPrev.OutgoingIsCurve && !piece.Vertices[vertexIndex].OutgoingIsCurve)
        {
            vPrev.OutControlHandleMm = vPrev.PositionMm.Lerp(vNext.PositionMm, 1.0 / 3.0);
            vNext.InControlHandleMm = vPrev.PositionMm.Lerp(vNext.PositionMm, 2.0 / 3.0);
        }

        piece.Vertices.RemoveAt(vertexIndex);

        if (wasOrigin && piece.Vertices.Count > 0)
        {
            piece.Vertices[0].IsPieceOrigin = true;
        }

        // Update notch segment indices
        for (int i = piece.Notches.Count - 1; i >= 0; i--)
        {
            if (piece.Notches[i].SegmentIndex == vertexIndex)
            {
                piece.Notches[i].SegmentIndex = Math.Max(0, vertexIndex - 1);
            }
            else if (piece.Notches[i].SegmentIndex > vertexIndex)
            {
                piece.Notches[i].SegmentIndex--;
            }
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
        return true;
    }

    /// <summary>
    /// Deletes multiple vertices from a closed pattern piece contour in descending index order.
    /// Preserves contour closure, updates adjacent handles, and guarantees at least 3 vertices remain.
    /// </summary>
    public int DeleteMultipleVertices(PatternPiece piece, IEnumerable<int> vertexIndices)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(vertexIndices);

        var sorted = vertexIndices
            .Where(idx => idx >= 0 && idx < piece.Vertices.Count)
            .Distinct()
            .OrderByDescending(idx => idx)
            .ToList();

        if (sorted.Count == 0)
        {
            return 0;
        }

        int deletedCount = 0;
        foreach (int idx in sorted)
        {
            if (piece.Vertices.Count <= 3)
            {
                break;
            }

            if (DeleteVertex(piece, idx))
            {
                deletedCount++;
            }
        }

        return deletedCount;
    }

    public void SetVertexGradedCorner(PatternPiece piece, int vertexIndex, bool isGradedCorner)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (vertexIndex < 0 || vertexIndex >= piece.Vertices.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexIndex));
        }

        piece.Vertices[vertexIndex].IsGradedCorner = isGradedCorner;
        _measurementEngine.RecalculatePieceMetrics(piece);
    }

    /// <summary>
    /// Converts a straight line segment (segmentIndex -> segmentIndex+1) into a Cubic Bézier curve
    /// (matching verified Line2CurveExecute -> @TImagine@ToCurve$qv 0x5d3f08).
    /// </summary>
    public void ConvertLineToCurve(PatternPiece piece, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return;
        }

        int i0 = ((segmentIndex % n) + n) % n;
        int i1 = (i0 + 1) % n;
        int iPrev = (i0 - 1 + n) % n;
        int iNext = (i1 + 1) % n;

        ContourVertex vPrev = piece.Vertices[iPrev];
        ContourVertex v0 = piece.Vertices[i0];
        ContourVertex v1 = piece.Vertices[i1];
        ContourVertex vNext = piece.Vertices[iNext];

        double chord = v0.PositionMm.DistanceTo(v1.PositionMm);
        if (chord < 1e-6)
        {
            return;
        }

        // Catmull-Rom / smooth tangent handle initialization with gentle outward arch
        PhysicalPointMm dir0 = v1.PositionMm - vPrev.PositionMm;
        double len0 = Math.Sqrt((dir0.XMm * dir0.XMm) + (dir0.YMm * dir0.YMm));
        PhysicalPointMm dir1 = vNext.PositionMm - v0.PositionMm;
        double len1 = Math.Sqrt((dir1.XMm * dir1.XMm) + (dir1.YMm * dir1.YMm));

        if (len0 > 1e-6 && len1 > 1e-6)
        {
            double handleLen = chord / 3.0;
            v0.OutControlHandleMm = new PhysicalPointMm(
                v0.PositionMm.XMm + ((dir0.XMm / len0) * handleLen),
                v0.PositionMm.YMm + ((dir0.YMm / len0) * handleLen));
            v1.InControlHandleMm = new PhysicalPointMm(
                v1.PositionMm.XMm - ((dir1.XMm / len1) * handleLen),
                v1.PositionMm.YMm - ((dir1.YMm / len1) * handleLen));
        }
        else
        {
            v0.OutControlHandleMm = v0.PositionMm.Lerp(v1.PositionMm, 1.0 / 3.0);
            v1.InControlHandleMm = v0.PositionMm.Lerp(v1.PositionMm, 2.0 / 3.0);
        }

        v0.OutgoingIsCurve = true;
        _measurementEngine.RecalculatePieceMetrics(piece);
    }

    /// <summary>
    /// Converts a Cubic Bézier curve segment (segmentIndex -> segmentIndex+1) into a straight line segment
    /// (matching verified Curve2LineExecute -> @TImagine@ToLine$qv 0x5d461c).
    /// </summary>
    public void ConvertCurveToLine(PatternPiece piece, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return;
        }

        int i0 = ((segmentIndex % n) + n) % n;
        int i1 = (i0 + 1) % n;

        ContourVertex v0 = piece.Vertices[i0];
        ContourVertex v1 = piece.Vertices[i1];

        v0.OutgoingIsCurve = false;
        v0.OutControlHandleMm = v0.PositionMm.Lerp(v1.PositionMm, 1.0 / 3.0);
        v1.InControlHandleMm = v0.PositionMm.Lerp(v1.PositionMm, 2.0 / 3.0);

        _measurementEngine.RecalculatePieceMetrics(piece);
    }

    public void MoveBezierControlHandle(
        PatternPiece piece,
        int segmentIndex,
        bool isStartHandleC1,
        PhysicalPointMm newHandlePositionMm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return;
        }

        int i0 = ((segmentIndex % n) + n) % n;
        int i1 = (i0 + 1) % n;

        piece.Vertices[i0].OutgoingIsCurve = true;
        if (isStartHandleC1)
        {
            piece.Vertices[i0].OutControlHandleMm = newHandlePositionMm;
        }
        else
        {
            piece.Vertices[i1].InControlHandleMm = newHandlePositionMm;
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
    }

    public void SetPieceOrigin(PatternPiece piece, int vertexIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (vertexIndex < 0 || vertexIndex >= piece.Vertices.Count)
        {
            return;
        }

        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            piece.Vertices[i].IsPieceOrigin = (i == vertexIndex);
        }
    }

    // =======================================================================
    // PATTERN TOOLS: NOTCH, INTERNAL POINT, INTERNAL LINE / GRAIN LINE
    // =======================================================================

    public NotchItem AddNotch(
        PatternPiece piece,
        PhysicalPointMm targetPointMm,
        double lengthMm = 6.0,
        double angleDegrees = 90.0,
        bool isRelativeAngle = true)
    {
        ArgumentNullException.ThrowIfNull(piece);
        (int segIdx, double t, PhysicalPointMm projPt, _) =
            _geometryEngine.FindNearestContourLocation(piece, targetPointMm);

        if (segIdx < 0)
        {
            throw new InvalidOperationException("Cannot add notch to piece with no segments.");
        }

        var notch = new NotchItem
        {
            SegmentIndex = segIdx,
            ParameterT = Math.Clamp(t, 0.02, 0.98),
            PositionMm = projPt,
            LengthMm = Math.Max(1.0, lengthMm),
            AngleDegrees = angleDegrees,
            IsRelativeAngle = isRelativeAngle
        };

        piece.Notches.Add(notch);
        _measurementEngine.RecalculatePieceMetrics(piece);
        return notch;
    }

    public bool MoveNotchAlongContour(PatternPiece piece, Guid notchId, PhysicalPointMm targetPointMm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        NotchItem? notch = piece.Notches.FirstOrDefault(n => n.Id == notchId);
        if (notch is null)
        {
            return false;
        }

        (int segIdx, double t, PhysicalPointMm projPt, _) =
            _geometryEngine.FindNearestContourLocation(piece, targetPointMm);
        if (segIdx < 0)
        {
            return false;
        }

        notch.SegmentIndex = segIdx;
        notch.ParameterT = Math.Clamp(t, 0.02, 0.98);
        notch.PositionMm = projPt;
        _measurementEngine.RecalculatePieceMetrics(piece);
        return true;
    }

    public bool MoveInternalPointTo(PatternPiece piece, Guid internalPointId, PhysicalPointMm newPositionMm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        InternalPointItem? ip = piece.InternalPoints.FirstOrDefault(p => p.Id == internalPointId);
        if (ip is null)
        {
            return false;
        }

        ip.PositionMm = newPositionMm;
        _measurementEngine.RecalculatePieceMetrics(piece);
        return true;
    }

    public bool MoveInternalLineByDelta(PatternPiece piece, Guid internalLineId, double deltaXMm, double deltaYMm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        InternalLineItem? il = piece.InternalLines.FirstOrDefault(l => l.Id == internalLineId);
        if (il is null)
        {
            return false;
        }

        var delta = new PhysicalPointMm(deltaXMm, deltaYMm);
        il.StartMm += delta;
        il.EndMm += delta;
        il.ControlPointMm += delta;
        for (int i = 0; i < il.PolylinePointsMm.Count; i++)
        {
            il.PolylinePointsMm[i] += delta;
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
        return true;
    }

    public bool MoveInternalLineEndpoint(PatternPiece piece, Guid internalLineId, bool isStartEndpoint, PhysicalPointMm newPositionMm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        InternalLineItem? il = piece.InternalLines.FirstOrDefault(l => l.Id == internalLineId);
        if (il is null)
        {
            return false;
        }

        if (isStartEndpoint)
        {
            il.StartMm = newPositionMm;
        }
        else
        {
            il.EndMm = newPositionMm;
        }

        if (!il.IsCurve)
        {
            il.PolylinePointsMm = [il.StartMm, il.EndMm];
        }
        _measurementEngine.RecalculatePieceMetrics(piece);
        return true;
    }

    public bool DeleteNotch(PatternPiece piece, Guid notchId)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int removed = piece.Notches.RemoveAll(n => n.Id == notchId);
        if (removed > 0)
        {
            _measurementEngine.RecalculatePieceMetrics(piece);
            return true;
        }
        return false;
    }

    public InternalPointItem AddInternalPoint(
        PatternPiece piece,
        PhysicalPointMm positionMm,
        double lengthMm = 5.0)
    {
        ArgumentNullException.ThrowIfNull(piece);
        var pt = new InternalPointItem
        {
            PositionMm = positionMm,
            LengthMm = Math.Max(1.0, lengthMm)
        };
        piece.InternalPoints.Add(pt);
        return pt;
    }

    public bool DeleteInternalPoint(PatternPiece piece, Guid internalPointId)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return piece.InternalPoints.RemoveAll(p => p.Id == internalPointId) > 0;
    }

    public InternalLineItem AddInternalLine(
        PatternPiece piece,
        PhysicalPointMm startMm,
        PhysicalPointMm endMm,
        InternalLineType lineType = InternalLineType.SimpleLine)
    {
        ArgumentNullException.ThrowIfNull(piece);

        // A piece can only have one GrainAxis; if adding a new GrainAxis, replace existing GrainAxis
        if (lineType == InternalLineType.GrainAxis)
        {
            piece.InternalLines.RemoveAll(l => l.LineType == InternalLineType.GrainAxis);
        }

        var line = new InternalLineItem
        {
            LineType = lineType,
            StartMm = startMm,
            EndMm = endMm,
            PolylinePointsMm = [startMm, endMm]
        };
        piece.InternalLines.Add(line);
        return line;
    }

    public InternalLineItem AddInternalCurve(PatternPiece piece, PhysicalPointMm startMm, PhysicalPointMm controlMm, PhysicalPointMm endMm)
    {
        InternalLineItem line = AddInternalLine(piece, startMm, endMm, InternalLineType.SimpleLine);
        line.IsCurve = true;
        line.ControlPointMm = controlMm;
        line.PolylinePointsMm = [startMm, controlMm, endMm];
        return line;
    }

    public bool DeleteInternalLine(PatternPiece piece, Guid internalLineId)
    {
        ArgumentNullException.ThrowIfNull(piece);
        return piece.InternalLines.RemoveAll(l => l.Id == internalLineId) > 0;
    }

    public PatternPiece CreateAuxiliaryRectanglePiece(
        int pieceIndex,
        PhysicalPointMm topLeftMm,
        double widthMm,
        double heightMm,
        CalibrationProfile? calibration = null)
    {
        double w = Math.Max(20.0, widthMm);
        double h = Math.Max(20.0, heightMm);

        PhysicalPointMm[] corners =
        [
            topLeftMm,
            new(topLeftMm.XMm + w, topLeftMm.YMm),
            new(topLeftMm.XMm + w, topLeftMm.YMm + h),
            new(topLeftMm.XMm, topLeftMm.YMm + h)
        ];

        var piece = new PatternPiece
        {
            Index = pieceIndex,
            Name = $"Piece {pieceIndex}",
            IsAuxiliaryPiece = true
        };

        for (int i = 0; i < 4; i++)
        {
            PhysicalPointMm curr = corners[i];
            PhysicalPointMm next = corners[(i + 1) % 4];
            PhysicalPointMm prev = corners[(i + 3) % 4];

            piece.Vertices.Add(new ContourVertex
            {
                PositionMm = curr,
                SourcePixelPosition = calibration is not null ? calibration.PhysicalMmToPixel(curr) : new ImagePixelPoint(curr.XMm, curr.YMm),
                IsGradedCorner = true,
                IsPieceOrigin = (i == 0),
                OutgoingIsCurve = false,
                OutControlHandleMm = curr.Lerp(next, 1.0 / 3.0),
                InControlHandleMm = prev.Lerp(curr, 2.0 / 3.0)
            });
        }

        _measurementEngine.RecalculatePieceMetrics(piece);
        return piece;
    }
}
