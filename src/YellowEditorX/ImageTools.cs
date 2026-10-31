using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YellowEditorX;

static class ImageTools
{
    // Same sizes the old editor produced (app.config "image": 44,64,177,254).
    public const int Width = 177, Height = 254, ThumbWidth = 44, ThumbHeight = 64;

    /// <summary>Loads an image without keeping the file open, so it can be replaced while shown.</summary>
    public static BitmapSource? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Stretches the image to the card picture size and writes it as a JPEG.</summary>
    public static void SaveJpeg(BitmapSource src, string path, int w, int h)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            dc.DrawImage(src, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new JpegBitmapEncoder { QualityLevel = 100 };
        enc.Frames.Add(BitmapFrame.Create(rtb));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public static void WriteCardImage(BitmapSource src, string picture, string? thumbnail)
    {
        SaveJpeg(src, picture, Width, Height);
        if (thumbnail != null) SaveJpeg(src, thumbnail, ThumbWidth, ThumbHeight);
    }
}
