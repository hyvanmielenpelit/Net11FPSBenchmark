using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Reflection;

namespace Net11FPSBenchmark;

/// <summary>
/// SkiaSharp-drawn image button control — mirrors GnollHack's GHCachedImage + LabeledImageButton.
/// Uses SKCanvasView (not standard MAUI Image) to draw the button bitmap with SkiaSharp,
/// exactly the same rendering pipeline as GnollHack's UI buttons.
/// </summary>
public class CachedImageButton : SKCanvasView
{
    private SKImage? _buttonImage;
    private string? _label;
    private static readonly SKPaint _imagePaint = new SKPaint();
    private static readonly SKFont _labelFont = new SKFont(
        SKTypeface.FromFamilyName("sans-serif", SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 18);
    private static readonly SKPaint _labelPaint = new SKPaint
    {
        Color = SKColors.White,
        IsAntialias = true
    };
    private static readonly SKPaint _labelStrokePaint = new SKPaint
    {
        Color = SKColors.Black,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2f
    };

    public CachedImageButton() : base()
    {
        PaintSurface += OnPaintSurface;
    }

    /// <summary>
    /// Load a button image from the app package assets.
    /// </summary>
    public async Task LoadImageAsync(string assetPath)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(assetPath);
            using var memStream = new MemoryStream();
            await stream.CopyToAsync(memStream);
            memStream.Position = 0;
            var bitmap = SKBitmap.Decode(memStream);
            bitmap.SetImmutable();
            _buttonImage = SKImage.FromBitmap(bitmap);
            MainThread.BeginInvokeOnMainThread(() => InvalidateSurface());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load button image {assetPath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Set the label text drawn below the button image.
    /// </summary>
    public void SetLabel(string label)
    {
        _label = label;
        InvalidateSurface();
    }

    /// <summary>
    /// Paint handler — draws the button image using SKCanvas.DrawImage,
    /// same rendering path as GnollHack's GHCachedImage.CustomCanvasView_PaintSurface.
    /// </summary>
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        SKImageInfo info = e.Info;
        canvas.Clear();

        float canvasWidth = info.Width;
        float canvasHeight = info.Height;
        if (canvasWidth <= 0 || canvasHeight <= 0)
            return;

        SKImage? targetBitmap = _buttonImage;
        if (targetBitmap != null)
        {
            // Draw the image with aspect-fit (same as GnollHack's GHCachedImage AspectFit)
            SKRect sourceRect = new SKRect(0, 0, targetBitmap.Width, targetBitmap.Height);

            // Reserve space for label at the bottom
            float imageAreaHeight = string.IsNullOrEmpty(_label) ? canvasHeight : canvasHeight * 0.7f;
            float sourceWHRatio = (float)targetBitmap.Width / targetBitmap.Height;
            bool widthSmaller = canvasWidth < imageAreaHeight;
            float drawWidth = widthSmaller ? canvasWidth : imageAreaHeight * sourceWHRatio;
            float drawHeight = widthSmaller ? canvasWidth / sourceWHRatio : imageAreaHeight;
            float hPadding = (drawWidth - canvasWidth) / 2f;
            float vPadding = (drawHeight - imageAreaHeight) / 2f;
            SKRect targetRect = new SKRect(-hPadding, -vPadding,
                -hPadding + drawWidth, -vPadding + drawHeight);

            var sampling = new SKSamplingOptions(SKFilterMode.Linear);
            canvas.DrawImage(targetBitmap, sourceRect, targetRect, sampling, _imagePaint);
        }

        // Draw label text below the image (stroke + fill, same as GnollHack)
        if (!string.IsNullOrEmpty(_label))
        {
            float labelY = canvasHeight * 0.88f;
            float labelX = canvasWidth / 2f;

            // Stroke outline
            canvas.DrawText(_label, labelX, labelY, SKTextAlign.Center, _labelFont, _labelStrokePaint);
            // Fill
            canvas.DrawText(_label, labelX, labelY, SKTextAlign.Center, _labelFont, _labelPaint);
        }
    }
}
