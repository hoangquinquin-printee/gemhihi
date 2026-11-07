using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PatternDigitizerPro.Core.Models;

namespace PatternDigitizerPro.Core.Engines;

/// <summary>
/// In-memory 32-bpp BGRA image buffer managed by ImageEngine.
/// </summary>
public sealed class RawImageBuffer
{
    public const double UnifiedDisplayDpi = 96.0;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    /// <summary>
    /// Original image horizontal DPI metadata from file header (EXIF/JFIF). Kept for reference only; never alters display or geometric coordinates.
    /// </summary>
    public double DpiX { get; init; } = UnifiedDisplayDpi;
    /// <summary>
    /// Original image vertical DPI metadata from file header (EXIF/JFIF). Kept for reference only; never alters display or geometric coordinates.
    /// </summary>
    public double DpiY { get; init; } = UnifiedDisplayDpi;
    public required byte[] BgraPixels { get; init; }
    public string FilePath { get; init; } = string.Empty;

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int idx = (y * Stride) + (x * 4);
        return (BgraPixels[idx + 2], BgraPixels[idx + 1], BgraPixels[idx], BgraPixels[idx + 3]);
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        int idx = (y * Stride) + (x * 4);
        BgraPixels[idx] = b;
        BgraPixels[idx + 1] = g;
        BgraPixels[idx + 2] = r;
        BgraPixels[idx + 3] = a;
    }

    public RawImageBuffer Clone()
    {
        byte[] copy = new byte[BgraPixels.Length];
        Buffer.BlockCopy(BgraPixels, 0, copy, 0, BgraPixels.Length);
        return new RawImageBuffer
        {
            Width = Width,
            Height = Height,
            Stride = Stride,
            DpiX = DpiX,
            DpiY = DpiY,
            BgraPixels = copy,
            FilePath = FilePath
        };
    }

    /// <summary>
    /// Converts the raw pixel buffer into a WPF BitmapSource normalized to 96.0 DPI (1 image pixel = 1 canvas DIP)
    /// so that background image rendering, canvas overlay geometry, mouse hit-testing, zoom, and pan share an identical 1:1 coordinate space.
    /// </summary>
    public BitmapSource ToBitmapSource()
    {
        var bmp = BitmapSource.Create(
            Width,
            Height,
            UnifiedDisplayDpi,
            UnifiedDisplayDpi,
            PixelFormats.Bgra32,
            null,
            BgraPixels,
            Stride);
        bmp.Freeze();
        return bmp;
    }
}

/// <summary>
/// Phase 2.1 Image Engine:
/// Handles loading raster images (.jpg, .jpeg, .png, .bmp), reading pixel dimensions,
/// cropping ROIs, image preprocessing, background illumination estimation, and debug PNG output.
/// </summary>
public sealed class ImageEngine
{
    public RawImageBuffer LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Image file path cannot be empty.", nameof(filePath));
        }

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Image file not found: {fullPath}", fullPath);
        }

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];

        if (frame.Format != PixelFormats.Bgra32 && frame.Format != PixelFormats.Bgr32)
        {
            frame = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        }

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        frame.CopyPixels(pixels, stride, 0);

        // Ensure alpha channel is 255 for Bgr32 images
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 0)
            {
                pixels[i] = 255;
            }
        }

        return new RawImageBuffer
        {
            Width = width,
            Height = height,
            Stride = stride,
            DpiX = frame.DpiX > 0 ? frame.DpiX : 96.0,
            DpiY = frame.DpiY > 0 ? frame.DpiY : 96.0,
            BgraPixels = pixels,
            FilePath = fullPath
        };
    }

    public RawImageBuffer CreateSolidBuffer(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        return new RawImageBuffer
        {
            Width = width,
            Height = height,
            Stride = stride,
            BgraPixels = pixels
        };
    }

    public RawImageBuffer Crop(RawImageBuffer source, CropBoxPixels cropBox)
    {
        ArgumentNullException.ThrowIfNull(source);

        int x0 = Math.Clamp(cropBox.StartX, 0, source.Width - 1);
        int y0 = Math.Clamp(cropBox.StartY, 0, source.Height - 1);
        int x1 = Math.Clamp(cropBox.StopX, x0 + 1, source.Width);
        int y1 = Math.Clamp(cropBox.StopY, y0 + 1, source.Height);

        int cropW = x1 - x0;
        int cropH = y1 - y0;
        int dstStride = cropW * 4;
        byte[] dst = new byte[dstStride * cropH];

        for (int y = 0; y < cropH; y++)
        {
            int srcOffset = ((y0 + y) * source.Stride) + (x0 * 4);
            int dstOffset = y * dstStride;
            Buffer.BlockCopy(source.BgraPixels, srcOffset, dst, dstOffset, dstStride);
        }

        return new RawImageBuffer
        {
            Width = cropW,
            Height = cropH,
            Stride = dstStride,
            DpiX = source.DpiX,
            DpiY = source.DpiY,
            BgraPixels = dst,
            FilePath = source.FilePath
        };
    }

    public byte[] ExtractGrayscale(RawImageBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int w = source.Width;
        int h = source.Height;
        byte[] gray = new byte[w * h];
        byte[] src = source.BgraPixels;
        int stride = source.Stride;

        for (int y = 0; y < h; y++)
        {
            int rowIdx = y * stride;
            int outRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int idx = rowIdx + (x * 4);
                int b = src[idx];
                int g = src[idx + 1];
                int r = src[idx + 2];
                gray[outRow + x] = (byte)(((r * 77) + (g * 150) + (b * 29)) >> 8);
            }
        }

        return gray;
    }

    public byte[] BoxBlur3x3(byte[] gray, int width, int height)
    {
        byte[] dst = new byte[width * height];
        Buffer.BlockCopy(gray, 0, dst, 0, gray.Length);

        for (int y = 1; y < height - 1; y++)
        {
            int r0 = (y - 1) * width;
            int r1 = y * width;
            int r2 = (y + 1) * width;
            for (int x = 1; x < width - 1; x++)
            {
                int sum =
                    gray[r0 + x - 1] + gray[r0 + x] + gray[r0 + x + 1] +
                    gray[r1 + x - 1] + gray[r1 + x] + gray[r1 + x + 1] +
                    gray[r2 + x - 1] + gray[r2 + x] + gray[r2 + x + 1];
                dst[r1 + x] = (byte)(sum / 9);
            }
        }

        return dst;
    }

    public RawImageBuffer GrayscaleToBuffer(byte[] gray, int width, int height)
    {
        int stride = width * 4;
        byte[] bgra = new byte[stride * height];
        for (int i = 0; i < gray.Length; i++)
        {
            byte v = gray[i];
            int idx = i * 4;
            bgra[idx] = v;
            bgra[idx + 1] = v;
            bgra[idx + 2] = v;
            bgra[idx + 3] = 255;
        }

        return new RawImageBuffer
        {
            Width = width,
            Height = height,
            Stride = stride,
            BgraPixels = bgra
        };
    }

    public void SavePng(RawImageBuffer buffer, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        string fullPath = Path.GetFullPath(outputPath);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(buffer.ToBitmapSource()));
        using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }
}
