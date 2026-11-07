using System.Windows;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

/// <summary>
/// Phase 2.1 Viewport & Coordinate Engine:
/// Manages zoom, pan, fit-to-window, zoom-to-cursor, zoom-to-area, and bi-directional coordinate
/// conversion between ImagePixelPoint (px), ViewportPoint (screen px), and PhysicalPointMm (mm).
/// Strictly guarantees that Pan/Zoom never mutate PatternPiece geometry.
/// </summary>
public sealed class ViewportEngine
{
    public const double MinZoomPercent = 15.0;
    public const double MaxZoomPercent = 600.0;

    public double ZoomPercent { get; private set; } = 100.0;
    public double PanOffsetX { get; private set; }
    public double PanOffsetY { get; private set; }

    public double ZoomScale => ZoomPercent / 100.0;

    public void SetZoomPercent(double zoomPercent)
    {
        ZoomPercent = Math.Clamp(zoomPercent, MinZoomPercent, MaxZoomPercent);
    }

    public void SetPanOffset(double panOffsetX, double panOffsetY)
    {
        PanOffsetX = panOffsetX;
        PanOffsetY = panOffsetY;
    }

    public void PanBy(double deltaScreenX, double deltaScreenY)
    {
        PanOffsetX += deltaScreenX;
        PanOffsetY += deltaScreenY;
    }

    /// <summary>
    /// Zooms around a specific cursor point in viewport screen space so the underlying image pixel stays anchored under the cursor.
    /// </summary>
    public void ZoomAtCursor(double zoomFactor, ViewportPoint cursorInViewport)
    {
        ImagePixelPoint anchorPixel = ViewportToImage(cursorInViewport);
        double newZoomPercent = Math.Clamp(ZoomPercent * zoomFactor, MinZoomPercent, MaxZoomPercent);
        ZoomPercent = newZoomPercent;
        double newScale = ZoomScale;

        PanOffsetX = cursorInViewport.X - (anchorPixel.X * newScale);
        PanOffsetY = cursorInViewport.Y - (anchorPixel.Y * newScale);
    }

    public void ZoomIn(ViewportPoint? cursorAnchor = null)
    {
        if (cursorAnchor.HasValue)
        {
            ZoomAtCursor(1.25, cursorAnchor.Value);
        }
        else
        {
            SetZoomPercent(ZoomPercent * 1.25);
        }
    }

    public void ZoomOut(ViewportPoint? cursorAnchor = null)
    {
        if (cursorAnchor.HasValue)
        {
            ZoomAtCursor(1.0 / 1.25, cursorAnchor.Value);
        }
        else
        {
            SetZoomPercent(ZoomPercent / 1.25);
        }
    }

    public void ZoomToFit(double viewportWidth, double viewportHeight, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            ZoomPercent = 100.0;
            PanOffsetX = 0.0;
            PanOffsetY = 0.0;
            return;
        }

        double usableW = Math.Max(32.0, viewportWidth - 8.0);
        double usableH = Math.Max(32.0, viewportHeight - 8.0);
        double sx = usableW / imageWidth;
        double sy = usableH / imageHeight;
        double scale = Math.Min(sx, sy);

        SetZoomPercent(scale * 100.0);
        PanOffsetX = 0.0;
        PanOffsetY = 0.0;
    }

    public void ZoomToImageRect(Rect imageRect, double viewportWidth, double viewportHeight)
    {
        if (imageRect.Width <= 1.0 || imageRect.Height <= 1.0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        double marginFactor = 0.90;
        double sx = (viewportWidth * marginFactor) / imageRect.Width;
        double sy = (viewportHeight * marginFactor) / imageRect.Height;
        double scale = Math.Min(sx, sy);

        SetZoomPercent(scale * 100.0);
        double actualScale = ZoomScale;

        double centerImgX = imageRect.X + (imageRect.Width * 0.5);
        double centerImgY = imageRect.Y + (imageRect.Height * 0.5);

        PanOffsetX = (viewportWidth * 0.5) - (centerImgX * actualScale);
        PanOffsetY = (viewportHeight * 0.5) - (centerImgY * actualScale);
    }

    // =======================================================================
    // COORDINATE SYSTEM CONVERSIONS (Image <-> Viewport <-> Physical mm)
    // =======================================================================

    public ViewportPoint ImageToViewport(ImagePixelPoint imagePoint)
    {
        double s = ZoomScale;
        return new ViewportPoint(
            PanOffsetX + (imagePoint.X * s),
            PanOffsetY + (imagePoint.Y * s));
    }

    public ImagePixelPoint ViewportToImage(ViewportPoint viewportPoint)
    {
        double s = ZoomScale > 1e-9 ? ZoomScale : 1.0;
        return new ImagePixelPoint(
            (viewportPoint.X - PanOffsetX) / s,
            (viewportPoint.Y - PanOffsetY) / s);
    }

    public PhysicalPointMm ImageToPhysicalMm(ImagePixelPoint imagePoint, CalibrationProfile calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        return calibration.PixelToPhysicalMm(imagePoint);
    }

    public ImagePixelPoint PhysicalMmToImage(PhysicalPointMm physicalPoint, CalibrationProfile calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        return calibration.PhysicalMmToPixel(physicalPoint);
    }

    public PhysicalPointMm ViewportToPhysicalMm(ViewportPoint viewportPoint, CalibrationProfile calibration)
    {
        ImagePixelPoint imgPt = ViewportToImage(viewportPoint);
        return ImageToPhysicalMm(imgPt, calibration);
    }

    public ViewportPoint PhysicalMmToViewport(PhysicalPointMm physicalPoint, CalibrationProfile calibration)
    {
        ImagePixelPoint imgPt = PhysicalMmToImage(physicalPoint, calibration);
        return ImageToViewport(imgPt);
    }
}
