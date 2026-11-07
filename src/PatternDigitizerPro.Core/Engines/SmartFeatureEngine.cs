using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

/// <summary>
/// Clean-room image-area feature extraction for the Gemini-style Smart tools.
/// The native Gemini implementation is not available; these routines therefore
/// expose measured confidence and never fabricate an edit when evidence is weak.
/// </summary>
public sealed class SmartFeatureEngine
{
    private readonly GeometryEngine _geometry;

    public SmartFeatureEngine(GeometryEngine geometry)
    {
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
    }

    public bool TryDetectCornerOnContour(
        RawImageBuffer image,
        CalibrationProfile calibration,
        PatternPiece piece,
        ImagePixelPoint areaCenterPx,
        double radiusPx,
        out PhysicalPointMm contourPointMm,
        out double confidence)
    {
        contourPointMm = default;
        confidence = 0;
        if (radiusPx < 8 || piece.Vertices.Count < 3)
        {
            return false;
        }

        int x0 = Math.Max(2, (int)Math.Floor(areaCenterPx.X - radiusPx));
        int y0 = Math.Max(2, (int)Math.Floor(areaCenterPx.Y - radiusPx));
        int x1 = Math.Min(image.Width - 3, (int)Math.Ceiling(areaCenterPx.X + radiusPx));
        int y1 = Math.Min(image.Height - 3, (int)Math.Ceiling(areaCenterPx.Y + radiusPx));
        double bestScore = 0;
        ImagePixelPoint bestPixel = default;

        for (int y = y0; y <= y1; y += 2)
        {
            for (int x = x0; x <= x1; x += 2)
            {
                double dx = x - areaCenterPx.X;
                double dy = y - areaCenterPx.Y;
                if ((dx * dx) + (dy * dy) > radiusPx * radiusPx)
                {
                    continue;
                }

                double gx = SobelX(image, x, y);
                double gy = SobelY(image, x, y);
                double ax = Math.Abs(gx);
                double ay = Math.Abs(gy);
                double cornerScore = Math.Min(ax, ay) * Math.Sqrt((ax * ax) + (ay * ay));
                if (cornerScore <= bestScore)
                {
                    continue;
                }

                PhysicalPointMm candidateMm = calibration.PixelToPhysicalMm(new ImagePixelPoint(x, y));
                (_, _, PhysicalPointMm projected, double distanceMm) = _geometry.FindNearestContourLocation(piece, candidateMm);
                if (distanceMm > Math.Max(8.0, radiusPx * Math.Max(calibration.ScaleXMmPerPx, calibration.ScaleYMmPerPx) * 0.30))
                {
                    continue;
                }

                bestScore = cornerScore;
                bestPixel = calibration.PhysicalMmToPixel(projected);
                contourPointMm = projected;
            }
        }

        double normalization = 255.0 * 255.0;
        confidence = Math.Clamp(bestScore / normalization, 0.0, 1.0);
        return bestScore > 1200.0 && bestPixel.X >= 0;
    }

    public bool TryDetectInternalPoint(
        RawImageBuffer image,
        CalibrationProfile calibration,
        PatternPiece piece,
        ImagePixelPoint areaCenterPx,
        double radiusPx,
        out PhysicalPointMm pointMm,
        out double confidence)
    {
        pointMm = default;
        confidence = 0;
        List<(int X, int Y, double Lum)> samples = CollectInteriorSamples(image, calibration, piece, areaCenterPx, radiusPx);
        if (samples.Count < 30)
        {
            return false;
        }

        double mean = samples.Average(s => s.Lum);
        double variance = samples.Average(s => (s.Lum - mean) * (s.Lum - mean));
        double sigma = Math.Sqrt(variance);
        double threshold = Math.Max(20.0, sigma * 1.6);
        var candidates = samples.Where(s => Math.Abs(s.Lum - mean) >= threshold).ToList();
        if (candidates.Count < 8 || candidates.Count > samples.Count * 0.35)
        {
            return false;
        }

        double weightedX = 0;
        double weightedY = 0;
        double totalWeight = 0;
        foreach (var sample in candidates)
        {
            double weight = Math.Abs(sample.Lum - mean);
            weightedX += sample.X * weight;
            weightedY += sample.Y * weight;
            totalWeight += weight;
        }

        var center = new ImagePixelPoint(weightedX / totalWeight, weightedY / totalWeight);
        pointMm = calibration.PixelToPhysicalMm(center);
        confidence = Math.Clamp((candidates.Average(s => Math.Abs(s.Lum - mean)) - threshold) / 80.0 + 0.35, 0.0, 1.0);
        return _geometry.IsPointInsidePiece(piece, pointMm) && confidence >= 0.35;
    }

    public bool TryDetectNotchOnContour(
        PatternPiece piece,
        PhysicalPointMm areaCenterMm,
        double radiusMm,
        out PhysicalPointMm notchMm,
        out double confidence)
    {
        notchMm = default;
        confidence = 0;
        if (piece.Vertices.Count < 5 || radiusMm <= 0)
        {
            return false;
        }

        double signedArea2 = 0;
        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            PhysicalPointMm a = piece.Vertices[i].PositionMm;
            PhysicalPointMm b = piece.Vertices[(i + 1) % piece.Vertices.Count].PositionMm;
            signedArea2 += (a.XMm * b.YMm) - (b.XMm * a.YMm);
        }
        double orientation = Math.Sign(signedArea2);
        if (orientation == 0)
        {
            return false;
        }

        double bestScore = 0;
        for (int i = 0; i < piece.Vertices.Count; i++)
        {
            PhysicalPointMm prev = piece.Vertices[(i - 1 + piece.Vertices.Count) % piece.Vertices.Count].PositionMm;
            PhysicalPointMm curr = piece.Vertices[i].PositionMm;
            PhysicalPointMm next = piece.Vertices[(i + 1) % piece.Vertices.Count].PositionMm;
            double distance = curr.DistanceTo(areaCenterMm);
            if (distance > radiusMm)
            {
                continue;
            }

            double ax = curr.XMm - prev.XMm;
            double ay = curr.YMm - prev.YMm;
            double bx = next.XMm - curr.XMm;
            double by = next.YMm - curr.YMm;
            double lenA = Math.Sqrt((ax * ax) + (ay * ay));
            double lenB = Math.Sqrt((bx * bx) + (by * by));
            if (lenA < 1e-6 || lenB < 1e-6)
            {
                continue;
            }

            double cross = (ax * by) - (ay * bx);
            bool isConcave = Math.Sign(cross) != orientation;
            if (!isConcave)
            {
                continue;
            }

            double dot = (ax * bx) + (ay * by);
            double turn = Math.Acos(Math.Clamp(dot / (lenA * lenB), -1.0, 1.0));
            double locality = 1.0 - (distance / radiusMm);
            double score = (turn / Math.PI) * (0.6 + (0.4 * locality));
            if (score > bestScore)
            {
                bestScore = score;
                notchMm = curr;
            }
        }

        confidence = Math.Clamp(bestScore, 0.0, 1.0);
        return confidence >= 0.18;
    }

    public bool TryDetectInternalLine(
        RawImageBuffer image,
        CalibrationProfile calibration,
        PatternPiece piece,
        ImagePixelPoint areaCenterPx,
        double radiusPx,
        out PhysicalPointMm startMm,
        out PhysicalPointMm endMm,
        out double confidence)
    {
        startMm = default;
        endMm = default;
        confidence = 0;
        List<(int X, int Y, double Lum)> samples = CollectInteriorSamples(image, calibration, piece, areaCenterPx, radiusPx);
        if (samples.Count < 50)
        {
            return false;
        }

        double mean = samples.Average(s => s.Lum);
        double sigma = Math.Sqrt(samples.Average(s => (s.Lum - mean) * (s.Lum - mean)));
        double threshold = Math.Max(16.0, sigma * 1.25);
        var dark = samples.Where(s => mean - s.Lum >= threshold).ToList();
        if (dark.Count < 16 || dark.Count > samples.Count * 0.30)
        {
            return false;
        }

        double cx = dark.Average(s => s.X);
        double cy = dark.Average(s => s.Y);
        double xx = 0, xy = 0, yy = 0;
        foreach (var s in dark)
        {
            double dx = s.X - cx;
            double dy = s.Y - cy;
            xx += dx * dx;
            xy += dx * dy;
            yy += dy * dy;
        }

        double angle = 0.5 * Math.Atan2(2.0 * xy, xx - yy);
        double vx = Math.Cos(angle);
        double vy = Math.Sin(angle);
        var projections = dark.Select(s => ((s.X - cx) * vx) + ((s.Y - cy) * vy)).OrderBy(v => v).ToList();
        double low = projections[(int)(projections.Count * 0.08)];
        double high = projections[(int)(projections.Count * 0.92)];
        double trace = xx + yy;
        double root = Math.Sqrt(Math.Max(0.0, ((xx - yy) * (xx - yy)) + (4.0 * xy * xy)));
        double major = (trace + root) / (2.0 * dark.Count);
        double minor = Math.Max(1e-6, (trace - root) / (2.0 * dark.Count));
        double anisotropy = major / minor;
        if (high - low < 12 || anisotropy < 2.2)
        {
            return false;
        }

        startMm = calibration.PixelToPhysicalMm(new ImagePixelPoint(cx + (low * vx), cy + (low * vy)));
        endMm = calibration.PixelToPhysicalMm(new ImagePixelPoint(cx + (high * vx), cy + (high * vy)));
        confidence = Math.Clamp((anisotropy - 2.0) / 8.0, 0.0, 1.0);
        return _geometry.IsPointInsidePiece(piece, startMm) && _geometry.IsPointInsidePiece(piece, endMm);
    }

    private List<(int X, int Y, double Lum)> CollectInteriorSamples(
        RawImageBuffer image,
        CalibrationProfile calibration,
        PatternPiece piece,
        ImagePixelPoint center,
        double radiusPx)
    {
        int x0 = Math.Max(1, (int)Math.Floor(center.X - radiusPx));
        int y0 = Math.Max(1, (int)Math.Floor(center.Y - radiusPx));
        int x1 = Math.Min(image.Width - 2, (int)Math.Ceiling(center.X + radiusPx));
        int y1 = Math.Min(image.Height - 2, (int)Math.Ceiling(center.Y + radiusPx));
        var result = new List<(int X, int Y, double Lum)>();
        for (int y = y0; y <= y1; y += 2)
        {
            for (int x = x0; x <= x1; x += 2)
            {
                double dx = x - center.X;
                double dy = y - center.Y;
                if ((dx * dx) + (dy * dy) > radiusPx * radiusPx)
                {
                    continue;
                }

                PhysicalPointMm mm = calibration.PixelToPhysicalMm(new ImagePixelPoint(x, y));
                if (_geometry.IsPointInsidePiece(piece, mm))
                {
                    result.Add((x, y, Luminance(image, x, y)));
                }
            }
        }
        return result;
    }

    private static double SobelX(RawImageBuffer image, int x, int y) =>
        -Luminance(image, x - 1, y - 1) + Luminance(image, x + 1, y - 1)
        - (2 * Luminance(image, x - 1, y)) + (2 * Luminance(image, x + 1, y))
        - Luminance(image, x - 1, y + 1) + Luminance(image, x + 1, y + 1);

    private static double SobelY(RawImageBuffer image, int x, int y) =>
        -Luminance(image, x - 1, y - 1) - (2 * Luminance(image, x, y - 1)) - Luminance(image, x + 1, y - 1)
        + Luminance(image, x - 1, y + 1) + (2 * Luminance(image, x, y + 1)) + Luminance(image, x + 1, y + 1);

    private static double Luminance(RawImageBuffer image, int x, int y)
    {
        int index = (y * image.Stride) + (x * 4);
        byte b = image.BgraPixels[index];
        byte g = image.BgraPixels[index + 1];
        byte r = image.BgraPixels[index + 2];
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }
}
