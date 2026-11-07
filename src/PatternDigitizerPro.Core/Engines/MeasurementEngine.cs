using System.Windows;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

/// <summary>
/// Phase 2.5 Measurement Engine:
/// Computes straight Euclidean distance, contour perimeter distance between points/vertices,
/// Cubic Bézier arc length via Gauss-Legendre quadrature, closed piece perimeter, closed piece area,
/// and vertex/tangent angles in physical units (mm / cm / degrees).
/// </summary>
public sealed class MeasurementEngine
{
    // 5-point Gauss-Legendre quadrature nodes and weights on [-1, 1]
    private static readonly double[] GaussNodes =
    [
        -0.9061798459386640,
        -0.5384693101056831,
         0.0,
         0.5384693101056831,
         0.9061798459386640
    ];

    private static readonly double[] GaussWeights =
    [
        0.2369268850561891,
        0.4786286704993665,
        0.5688888888888889,
        0.4786286704993665,
        0.2369268850561891
    ];

    public double ComputeStraightDistanceMm(PhysicalPointMm a, PhysicalPointMm b) =>
        a.DistanceTo(b);

    public double ComputeStraightDistanceCm(PhysicalPointMm a, PhysicalPointMm b) =>
        a.DistanceTo(b) / 10.0;

    /// <summary>
    /// Computes the arc length in mm of a segment (or sub-segment [t0, t1]) using 5-point Gauss-Legendre quadrature.
    /// </summary>
    public double ComputeSegmentArcLengthMm(CubicBezierSegment segment, double t0 = 0.0, double t1 = 1.0)
    {
        t0 = Math.Clamp(t0, 0.0, 1.0);
        t1 = Math.Clamp(t1, 0.0, 1.0);
        if (t1 <= t0)
        {
            return 0.0;
        }

        if (!segment.IsCurve)
        {
            return segment.P0.DistanceTo(segment.P3) * (t1 - t0);
        }

        const int intervals = 8;
        double dt = (t1 - t0) / intervals;
        double totalLength = 0.0;

        for (int i = 0; i < intervals; i++)
        {
            double a = t0 + (i * dt);
            double b = a + dt;
            double halfSpan = 0.5 * (b - a);
            double mid = 0.5 * (a + b);

            double subSum = 0.0;
            for (int q = 0; q < GaussNodes.Length; q++)
            {
                double t = mid + (halfSpan * GaussNodes[q]);
                PhysicalPointMm deriv = segment.Derivative(t);
                double speed = Math.Sqrt((deriv.XMm * deriv.XMm) + (deriv.YMm * deriv.YMm));
                subSum += GaussWeights[q] * speed;
            }

            totalLength += halfSpan * subSum;
        }

        return totalLength;
    }

    public double ComputePiecePerimeterMm(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Vertices.Count < 2)
        {
            return 0.0;
        }

        double sum = 0.0;
        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            sum += ComputeSegmentArcLengthMm(piece.GetSegment(i));
        }

        return sum;
    }

    public double ComputePiecePerimeterCm(PatternPiece piece) =>
        ComputePiecePerimeterMm(piece) / 10.0;

    /// <summary>
    /// Computes closed contour area in mm^2 using exact 5-point Gauss-Legendre quadrature of Green's theorem
    /// line integral 0.5 * \oint (x dy - y dx) over all straight and Cubic Bézier segments.
    /// Because x(t)y'(t) - y(t)x'(t) is degree <= 5 for cubic Béziers and 5-point Gauss-Legendre is exact
    /// up to degree 9, this area calculation is analytically exact and invariant under De Casteljau subdivision!
    /// </summary>
    public double ComputePieceAreaMm2(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Vertices.Count < 3)
        {
            return 0.0;
        }

        double signedTwiceArea = 0.0;
        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            CubicBezierSegment seg = piece.GetSegment(i);
            if (!seg.IsCurve)
            {
                signedTwiceArea += (seg.P0.XMm * seg.P3.YMm) - (seg.P3.XMm * seg.P0.YMm);
            }
            else
            {
                double segIntegral = 0.0;
                for (int q = 0; q < GaussNodes.Length; q++)
                {
                    double t = 0.5 + (0.5 * GaussNodes[q]);
                    PhysicalPointMm pos = seg.Evaluate(t);
                    PhysicalPointMm deriv = seg.Derivative(t);
                    double cross = (pos.XMm * deriv.YMm) - (pos.YMm * deriv.XMm);
                    segIntegral += GaussWeights[q] * cross;
                }

                signedTwiceArea += 0.5 * segIntegral;
            }
        }

        return 0.5 * Math.Abs(signedTwiceArea);
    }

    public double ComputePieceAreaCm2(PatternPiece piece) =>
        ComputePieceAreaMm2(piece) / 100.0;

    /// <summary>
    /// Computes the distance along the contour between two vertex indices (matching Gemini's ComputeContourDistance 0x64c5d0).
    /// Returns the shorter path along the closed loop when shortestPathAlongLoop is true, and also returns both forward/backward distances.
    /// </summary>
    public (double ShortestDistanceMm, double ForwardDistanceMm, double BackwardDistanceMm) ComputeContourDistanceBetweenVerticesMm(
        PatternPiece piece,
        int startVertexIndex,
        int endVertexIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return (0.0, 0.0, 0.0);
        }

        int i0 = ((startVertexIndex % n) + n) % n;
        int i1 = ((endVertexIndex % n) + n) % n;
        if (i0 == i1)
        {
            return (0.0, 0.0, ComputePiecePerimeterMm(piece));
        }

        double forward = 0.0;
        int curr = i0;
        while (curr != i1)
        {
            forward += ComputeSegmentArcLengthMm(piece.GetSegment(curr));
            curr = (curr + 1) % n;
        }

        double total = ComputePiecePerimeterMm(piece);
        double backward = Math.Max(0.0, total - forward);
        double shortest = Math.Min(forward, backward);
        return (shortest, forward, backward);
    }

    /// <summary>
    /// Computes contour distance between any two arbitrary points projected onto the piece perimeter.
    /// </summary>
    public (double ShortestDistanceMm, double ForwardDistanceMm, double BackwardDistanceMm) ComputeContourDistanceBetweenLocationsMm(
        PatternPiece piece,
        int segA,
        double tA,
        int segB,
        double tB)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return (0.0, 0.0, 0.0);
        }

        segA = ((segA % n) + n) % n;
        segB = ((segB % n) + n) % n;
        tA = Math.Clamp(tA, 0.0, 1.0);
        tB = Math.Clamp(tB, 0.0, 1.0);

        double total = ComputePiecePerimeterMm(piece);
        double posA = 0.0;
        for (int i = 0; i < segA; i++)
        {
            posA += ComputeSegmentArcLengthMm(piece.GetSegment(i));
        }
        posA += ComputeSegmentArcLengthMm(piece.GetSegment(segA), 0.0, tA);

        double posB = 0.0;
        for (int i = 0; i < segB; i++)
        {
            posB += ComputeSegmentArcLengthMm(piece.GetSegment(i));
        }
        posB += ComputeSegmentArcLengthMm(piece.GetSegment(segB), 0.0, tB);

        double forward = posB >= posA ? (posB - posA) : (total - (posA - posB));
        double backward = Math.Max(0.0, total - forward);
        return (Math.Min(forward, backward), forward, backward);
    }

    /// <summary>
    /// Computes the interior/vertex angle in degrees at a given vertex (matching Gemini's CalculeazaUnghi 0x64d04c).
    /// </summary>
    public double ComputeVertexAngleDegrees(PatternPiece piece, int vertexIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 3)
        {
            return 0.0;
        }

        int idx = ((vertexIndex % n) + n) % n;
        int prevIdx = (idx - 1 + n) % n;

        CubicBezierSegment prevSeg = piece.GetSegment(prevIdx);
        CubicBezierSegment nextSeg = piece.GetSegment(idx);

        // Vector pointing from vertex back toward previous segment
        PhysicalPointMm dIn = prevSeg.Derivative(1.0);
        double vPrevX = -dIn.XMm;
        double vPrevY = -dIn.YMm;

        // Vector pointing from vertex forward along next segment
        PhysicalPointMm dOut = nextSeg.Derivative(0.0);
        double vNextX = dOut.XMm;
        double vNextY = dOut.YMm;

        double len1 = Math.Sqrt((vPrevX * vPrevX) + (vPrevY * vPrevY));
        double len2 = Math.Sqrt((vNextX * vNextX) + (vNextY * vNextY));
        if (len1 < 1e-9 || len2 < 1e-9)
        {
            return 180.0;
        }

        double cosTheta = ((vPrevX * vNextX) + (vPrevY * vNextY)) / (len1 * len2);
        cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
        return Math.Acos(cosTheta) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Computes the Ending tangent angle (deg), Starting tangent angle (deg), and outgoing Bezier handle length (cm)
    /// for the Selected Points Properties panel on the right dock.
    /// </summary>
    public (double EndingAngleDeg, double StartingAngleDeg, double HandleLengthCm) ComputeVertexTangentProperties(
        PatternPiece piece,
        int vertexIndex)
    {
        ArgumentNullException.ThrowIfNull(piece);
        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return (0.0, 0.0, 0.0);
        }

        int idx = ((vertexIndex % n) + n) % n;
        int prevIdx = (idx - 1 + n) % n;

        CubicBezierSegment prevSeg = piece.GetSegment(prevIdx);
        CubicBezierSegment nextSeg = piece.GetSegment(idx);

        PhysicalPointMm dEnd = prevSeg.Derivative(1.0);
        PhysicalPointMm dStart = nextSeg.Derivative(0.0);

        double endDeg = NormalizeDegrees(Math.Atan2(-dEnd.YMm, -dEnd.XMm) * 180.0 / Math.PI);
        double startDeg = NormalizeDegrees(Math.Atan2(dStart.YMm, dStart.XMm) * 180.0 / Math.PI);

        ContourVertex v = piece.Vertices[idx];
        double handleLenMm = v.OutgoingIsCurve ? v.PositionMm.DistanceTo(v.OutControlHandleMm) : 0.0;
        return (Math.Round(endDeg, 1), Math.Round(startDeg, 1), Math.Round(handleLenMm / 10.0, 2));
    }

    public Rect ComputePieceBoundingBoxMm(PatternPiece piece)
    {
        if (piece.Vertices.Count == 0)
        {
            return Rect.Empty;
        }

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        double maxX = double.MinValue;
        double maxY = double.MinValue;

        void Include(PhysicalPointMm pt)
        {
            if (pt.XMm < minX) minX = pt.XMm;
            if (pt.YMm < minY) minY = pt.YMm;
            if (pt.XMm > maxX) maxX = pt.XMm;
            if (pt.YMm > maxY) maxY = pt.YMm;
        }

        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            CubicBezierSegment segment = piece.GetSegment(i);
            Include(segment.P0);
            Include(segment.P3);
            if (!segment.IsCurve)
            {
                continue;
            }

            foreach (double t in CubicExtremaParameters(
                         segment.P0.XMm, segment.C1.XMm, segment.C2.XMm, segment.P3.XMm))
            {
                Include(segment.Evaluate(t));
            }
            foreach (double t in CubicExtremaParameters(
                         segment.P0.YMm, segment.C1.YMm, segment.C2.YMm, segment.P3.YMm))
            {
                Include(segment.Evaluate(t));
            }
        }

        return new Rect(minX, minY, Math.Max(0.0, maxX - minX), Math.Max(0.0, maxY - minY));
    }

    private static IEnumerable<double> CubicExtremaParameters(double p0, double p1, double p2, double p3)
    {
        double a = -p0 + (3.0 * p1) - (3.0 * p2) + p3;
        double b = (3.0 * p0) - (6.0 * p1) + (3.0 * p2);
        double c = (-3.0 * p0) + (3.0 * p1);
        double qa = 3.0 * a;
        double qb = 2.0 * b;
        const double epsilon = 1e-12;

        if (Math.Abs(qa) < epsilon)
        {
            if (Math.Abs(qb) >= epsilon)
            {
                double root = -c / qb;
                if (root > 0.0 && root < 1.0)
                {
                    yield return root;
                }
            }
            yield break;
        }

        double discriminant = (qb * qb) - (4.0 * qa * c);
        if (discriminant < 0.0)
        {
            yield break;
        }

        double sqrt = Math.Sqrt(Math.Max(0.0, discriminant));
        double root1 = (-qb + sqrt) / (2.0 * qa);
        double root2 = (-qb - sqrt) / (2.0 * qa);
        if (root1 > 0.0 && root1 < 1.0)
        {
            yield return root1;
        }
        if (root2 > 0.0 && root2 < 1.0 && Math.Abs(root2 - root1) > epsilon)
        {
            yield return root2;
        }
    }

    public void RecalculatePieceMetrics(PatternPiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        piece.PerimeterMm = ComputePiecePerimeterMm(piece);
        piece.AreaMm2 = ComputePieceAreaMm2(piece);
        piece.BoundingBoxMm = ComputePieceBoundingBoxMm(piece);

        int n = piece.Vertices.Count;
        if (n < 2)
        {
            return;
        }

        foreach (NotchItem notch in piece.Notches)
        {
            int segIdx = ((notch.SegmentIndex % n) + n) % n;
            notch.SegmentIndex = segIdx;
            CubicBezierSegment seg = piece.GetSegment(segIdx);
            notch.PositionMm = seg.Evaluate(notch.ParameterT);

            // Distance from previous graded point along contour
            double distMm = ComputeSegmentArcLengthMm(seg, 0.0, notch.ParameterT);
            int walkIdx = segIdx;
            int safety = 0;
            while (!piece.Vertices[walkIdx].IsGradedCorner && safety < n)
            {
                walkIdx = (walkIdx - 1 + n) % n;
                distMm += ComputeSegmentArcLengthMm(piece.GetSegment(walkIdx));
                safety++;
            }

            notch.DistanceFromPrevGradedPointMm = distMm;
        }
    }

    /// <summary>
    /// Fits the complete outer contour to an operator-entered frame size.  The
    /// scale is deliberately applied to curve handles and every attached
    /// feature as well as vertices, so exported geometry cannot retain a
    /// smaller pre-fit bounding box.
    /// </summary>
    public (double ScaleX, double ScaleY) FitPieceBoundingBoxToDimensionsCm(PatternPiece piece, double widthCm, double heightCm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (!double.IsFinite(widthCm) || !double.IsFinite(heightCm) || widthCm <= 0 || heightCm <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthCm), "Frame dimensions must be positive centimetres.");

        Rect bounds = ComputePieceBoundingBoxMm(piece);
        if (bounds.Width <= 1e-9 || bounds.Height <= 1e-9)
            throw new InvalidOperationException("Cannot fit a degenerate pattern contour to a frame.");

        double sx = (widthCm * 10.0) / bounds.Width;
        double sy = (heightCm * 10.0) / bounds.Height;
        PhysicalPointMm Scale(PhysicalPointMm p) => new(p.XMm * sx, p.YMm * sy);

        foreach (ContourVertex vertex in piece.Vertices)
        {
            vertex.PositionMm = Scale(vertex.PositionMm);
            vertex.InControlHandleMm = Scale(vertex.InControlHandleMm);
            vertex.OutControlHandleMm = Scale(vertex.OutControlHandleMm);
        }
        foreach (InternalPointItem point in piece.InternalPoints)
            point.PositionMm = Scale(point.PositionMm);
        foreach (InternalLineItem line in piece.InternalLines)
        {
            line.StartMm = Scale(line.StartMm);
            line.EndMm = Scale(line.EndMm);
            for (int i = 0; i < line.PolylinePointsMm.Count; i++)
                line.PolylinePointsMm[i] = Scale(line.PolylinePointsMm[i]);
        }

        RecalculatePieceMetrics(piece);
        return (sx, sy);
    }

    public static List<PhysicalPointMm> SampleClosedContourPoints(PatternPiece piece, int curveSubdivisions = 20)
    {
        var pts = new List<PhysicalPointMm>();
        int n = piece.Vertices.Count;
        if (n == 0)
        {
            return pts;
        }

        for (int i = 0; i < n; i++)
        {
            CubicBezierSegment seg = piece.GetSegment(i);
            int steps = seg.IsCurve ? Math.Max(4, curveSubdivisions) : 1;
            for (int s = 0; s < steps; s++)
            {
                double t = (double)s / steps;
                pts.Add(seg.Evaluate(t));
            }
        }

        return pts;
    }

    private static double NormalizeDegrees(double deg)
    {
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }
}
