using System.Globalization;
using System.Text;
using System.Windows;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

/// <summary>
/// Phase 2.3 / 2.4 Geometry Engine:
/// Provides geometric projection onto lines/Cubic Béziers, De Casteljau subdivision,
/// Catmull-Rom / Least-Squares Cubic Bézier curve fitting, point-in-polygon tests,
/// and SVG path generation for piece thumbnails.
/// </summary>
public sealed class GeometryEngine
{
    /// <summary>
    /// Finds the closest point on a single CubicBezierSegment to a given query point in physical mm.
    /// </summary>
    public (double ParameterT, PhysicalPointMm ClosestPointMm, double DistanceMm) ProjectPointOntoSegment(
        CubicBezierSegment segment,
        PhysicalPointMm queryPoint)
    {
        if (!segment.IsCurve)
        {
            double dx = segment.P3.XMm - segment.P0.XMm;
            double dy = segment.P3.YMm - segment.P0.YMm;
            double lenSq = (dx * dx) + (dy * dy);
            if (lenSq < 1e-12)
            {
                return (0.0, segment.P0, queryPoint.DistanceTo(segment.P0));
            }

            double t = (((queryPoint.XMm - segment.P0.XMm) * dx) + ((queryPoint.YMm - segment.P0.YMm) * dy)) / lenSq;
            t = Math.Clamp(t, 0.0, 1.0);
            PhysicalPointMm proj = segment.P0.Lerp(segment.P3, t);
            return (t, proj, queryPoint.DistanceTo(proj));
        }

        // Coarse search + ternary/local refinement on Cubic Bézier
        const int coarseSamples = 48;
        double bestT = 0.0;
        double bestDist = double.MaxValue;
        PhysicalPointMm bestPt = segment.P0;

        for (int i = 0; i <= coarseSamples; i++)
        {
            double t = (double)i / coarseSamples;
            PhysicalPointMm pt = segment.Evaluate(t);
            double dist = queryPoint.DistanceTo(pt);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestT = t;
                bestPt = pt;
            }
        }

        double step = 1.0 / coarseSamples;
        double left = Math.Max(0.0, bestT - step);
        double right = Math.Min(1.0, bestT + step);

        for (int iter = 0; iter < 16; iter++)
        {
            double m1 = left + ((right - left) / 3.0);
            double m2 = right - ((right - left) / 3.0);
            double d1 = queryPoint.DistanceTo(segment.Evaluate(m1));
            double d2 = queryPoint.DistanceTo(segment.Evaluate(m2));
            if (d1 < d2)
            {
                right = m2;
            }
            else
            {
                left = m1;
            }
        }

        bestT = 0.5 * (left + right);
        bestPt = segment.Evaluate(bestT);
        bestDist = queryPoint.DistanceTo(bestPt);
        return (bestT, bestPt, bestDist);
    }

    /// <summary>
    /// Finds the nearest segment and parameter t on a closed PatternPiece contour to a query point in physical mm.
    /// </summary>
    public (int SegmentIndex, double ParameterT, PhysicalPointMm ClosestPointMm, double DistanceMm) FindNearestContourLocation(
        PatternPiece piece,
        PhysicalPointMm queryPoint)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Vertices.Count < 2)
        {
            return (-1, 0.0, default, double.MaxValue);
        }

        int bestSeg = 0;
        double bestT = 0.0;
        PhysicalPointMm bestPt = piece.Vertices[0].PositionMm;
        double bestDist = double.MaxValue;

        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            CubicBezierSegment seg = piece.GetSegment(i);
            (double t, PhysicalPointMm pt, double dist) = ProjectPointOntoSegment(seg, queryPoint);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestSeg = i;
                bestT = t;
                bestPt = pt;
            }
        }

        return (bestSeg, bestT, bestPt, bestDist);
    }

    /// <summary>
    /// Least-squares Cubic Bézier curve fitting to an ordered chain of points using chord-length parameterization.
    /// Returns (C1, C2, MaxFitErrorMm, IsCurvedMoreThanLine).
    /// </summary>
    public (PhysicalPointMm C1, PhysicalPointMm C2, double MaxErrorMm, bool IsSignificantlyCurved) FitCubicBezierToPointChain(
        IReadOnlyList<PhysicalPointMm> chain)
    {
        if (chain is null || chain.Count < 2)
        {
            return (default, default, 0.0, false);
        }

        PhysicalPointMm p0 = chain[0];
        PhysicalPointMm p3 = chain[^1];

        // Compute maximum deviation from straight chord P0->P3
        double maxLineDev = 0.0;
        var lineSeg = new CubicBezierSegment(p0, p0, p3, p3, false);
        for (int i = 1; i < chain.Count - 1; i++)
        {
            (_, _, double d) = ProjectPointOntoSegment(lineSeg, chain[i]);
            if (d > maxLineDev)
            {
                maxLineDev = d;
            }
        }

        PhysicalPointMm defaultC1 = p0.Lerp(p3, 1.0 / 3.0);
        PhysicalPointMm defaultC2 = p0.Lerp(p3, 2.0 / 3.0);

        if (chain.Count < 4)
        {
            return (defaultC1, defaultC2, maxLineDev, false);
        }

        // Chord-length parameterization
        double[] u = new double[chain.Count];
        u[0] = 0.0;
        for (int i = 1; i < chain.Count; i++)
        {
            u[i] = u[i - 1] + chain[i - 1].DistanceTo(chain[i]);
        }

        double totalChord = u[^1];
        if (totalChord < 1e-6)
        {
            return (defaultC1, defaultC2, 0.0, false);
        }

        for (int i = 1; i < chain.Count; i++)
        {
            u[i] /= totalChord;
        }

        // Solve 2x2 linear least-squares system for C1 and C2
        double c11 = 0.0, c12 = 0.0, c22 = 0.0;
        double x1 = 0.0, y1 = 0.0, x2 = 0.0, y2 = 0.0;

        for (int i = 1; i < chain.Count - 1; i++)
        {
            double t = u[i];
            double omt = 1.0 - t;
            double b0 = omt * omt * omt;
            double b1 = 3.0 * omt * omt * t;
            double b2 = 3.0 * omt * t * t;
            double b3 = t * t * t;

            c11 += b1 * b1;
            c12 += b1 * b2;
            c22 += b2 * b2;

            double rhsX = chain[i].XMm - (b0 * p0.XMm) - (b3 * p3.XMm);
            double rhsY = chain[i].YMm - (b0 * p0.YMm) - (b3 * p3.YMm);

            x1 += b1 * rhsX;
            y1 += b1 * rhsY;
            x2 += b2 * rhsX;
            y2 += b2 * rhsY;
        }

        double det = (c11 * c22) - (c12 * c12);
        PhysicalPointMm c1 = defaultC1;
        PhysicalPointMm c2 = defaultC2;

        if (Math.Abs(det) > 1e-9)
        {
            double c1X = ((c22 * x1) - (c12 * x2)) / det;
            double c1Y = ((c22 * y1) - (c12 * y2)) / det;
            double c2X = ((c11 * x2) - (c12 * x1)) / det;
            double c2Y = ((c11 * y2) - (c12 * y1)) / det;

            double chordLen = p0.DistanceTo(p3);
            var candC1 = new PhysicalPointMm(c1X, c1Y);
            var candC2 = new PhysicalPointMm(c2X, c2Y);

            // Guard against wild handles when points are nearly collinear
            if (p0.DistanceTo(candC1) <= chordLen * 1.2 && p3.DistanceTo(candC2) <= chordLen * 1.2)
            {
                c1 = candC1;
                c2 = candC2;
            }
        }

        var fittedSeg = new CubicBezierSegment(p0, c1, c2, p3, true);
        double maxCurveErr = 0.0;
        for (int i = 1; i < chain.Count - 1; i++)
        {
            PhysicalPointMm eval = fittedSeg.Evaluate(u[i]);
            double err = eval.DistanceTo(chain[i]);
            if (err > maxCurveErr)
            {
                maxCurveErr = err;
            }
        }

        bool isSignificantlyCurved = maxLineDev > 2.2 && maxCurveErr < maxLineDev * 0.72;
        return (c1, c2, maxCurveErr, isSignificantlyCurved);
    }

    public bool IsPointInsidePiece(PatternPiece piece, PhysicalPointMm pointMm)
    {
        ArgumentNullException.ThrowIfNull(piece);
        var polygon = MeasurementEngine.SampleClosedContourPoints(piece, curveSubdivisions: 12);
        if (polygon.Count < 3)
        {
            return false;
        }

        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            double xi = polygon[i].XMm, yi = polygon[i].YMm;
            double xj = polygon[j].XMm, yj = polygon[j].YMm;

            bool intersect = ((yi > pointMm.YMm) != (yj > pointMm.YMm)) &&
                             (pointMm.XMm < ((xj - xi) * (pointMm.YMm - yi) / ((yj - yi) + 1e-12)) + xi);
            if (intersect)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Generates the normalized SVG path string ("M ... L ... C ... Z") for displaying the piece in the Stock Area card.
    /// </summary>
    public string BuildStockThumbnailPathData(PatternPiece piece, double boxWidth = 64.0, double boxHeight = 52.0)
    {
        ArgumentNullException.ThrowIfNull(piece);
        if (piece.Vertices.Count < 3)
        {
            return string.Empty;
        }

        Rect bbox = piece.BoundingBoxMm;
        if (bbox.IsEmpty || bbox.Width < 1e-6 || bbox.Height < 1e-6)
        {
            var meas = new MeasurementEngine();
            bbox = meas.ComputePieceBoundingBoxMm(piece);
        }

        if (bbox.IsEmpty || bbox.Width < 1e-6 || bbox.Height < 1e-6)
        {
            return string.Empty;
        }

        double scale = Math.Min(boxWidth / bbox.Width, boxHeight / bbox.Height);
        double offsetX = 8.0 + ((boxWidth - (bbox.Width * scale)) * 0.5);
        double offsetY = 6.0 + ((boxHeight - (bbox.Height * scale)) * 0.5);

        (double X, double Y) Map(PhysicalPointMm pt) => (
            offsetX + ((pt.XMm - bbox.X) * scale),
            offsetY + ((pt.YMm - bbox.Y) * scale));

        var sb = new StringBuilder();
        var (x0, y0) = Map(piece.Vertices[0].PositionMm);
        sb.Append( string.Create(CultureInfo.InvariantCulture, $"M {x0:F1},{y0:F1}") );

        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            CubicBezierSegment seg = piece.GetSegment(i);
            var (x3, y3) = Map(seg.P3);
            if (seg.IsCurve)
            {
                var (cx1, cy1) = Map(seg.C1);
                var (cx2, cy2) = Map(seg.C2);
                sb.Append(string.Create(CultureInfo.InvariantCulture, $" C {cx1:F1},{cy1:F1} {cx2:F1},{cy2:F1} {x3:F1},{y3:F1}"));
            }
            else
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $" L {x3:F1},{y3:F1}"));
            }
        }

        sb.Append(" Z");
        return sb.ToString();
    }
}
