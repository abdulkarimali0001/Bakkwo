using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Bakkwo.Core.Conversion;
using Path = System.IO.Path;

namespace Bakkwo.Android.Converters;

/// <summary>
/// HEIC photos (Samsung and iPhone cameras) and AVIF images, decoded by Android itself and saved as
/// JPG, PNG or WebP. Other targets (PDF, GIF, …) follow from there through the normal image converters.
/// </summary>
internal sealed class HeifImageConverter : ConverterBase
{
    public override string Name => "Android image decoder";

    // ImageDecoder (Android 9) reads HEIF and turns the photo upright from its EXIF orientation.
    public override bool IsAvailable => OperatingSystem.IsAndroidVersionAtLeast(28);

    public override string? UnavailableReason => IsAvailable ? null : "HEIC needs Android 9 or newer.";

    public override int? MaxConcurrency => 2; // full-size photos use a lot of memory

    private static IEnumerable<string> Sources =>
        OperatingSystem.IsAndroidVersionAtLeast(31) ? ["heic", "avif"] : ["heic"]; // AVIF from Android 12

    public override IEnumerable<ConversionEdge> Edges =>
        from source in Sources
        from target in new[] { "jpg", "png", "webp" }
        select Edge(source, target);

    public override Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(28)) throw new PlatformNotSupportedException(UnavailableReason);
            using var source = ImageDecoder.CreateSource(new Java.IO.File(step.InputPath));
            using var listener = new SizeListener(options.MaxDimension);
            using var decoded = ImageDecoder.DecodeBitmap(source, listener);
            ct.ThrowIfCancellationRequested();
            progress?.Report(0.6);

            using var bitmap = options.Grayscale ? Gray(decoded) : decoded;
            var (format, quality) = step.To.Id switch
            {
                "png" => (Bitmap.CompressFormat.Png!, 100),
#pragma warning disable CA1422 // plain Webp is the lossy one before Android 11
                "webp" => (OperatingSystem.IsAndroidVersionAtLeast(30) ? Bitmap.CompressFormat.WebpLossy! : Bitmap.CompressFormat.Webp!, options.Quality),
#pragma warning restore CA1422
                _ => (Bitmap.CompressFormat.Jpeg!, options.Quality),
            };
            using (var output = File.Create(step.OutputPath))
            {
                if (!bitmap.Compress(format, Math.Clamp(quality, 1, 100), output))
                {
                    throw new InvalidOperationException($"Android couldn't save {Path.GetFileName(step.OutputPath)}.");
                }
            }
            progress?.Report(1);
        }, ct);

    private static Bitmap Gray(Bitmap color)
    {
        var gray = Bitmap.CreateBitmap(color.Width, color.Height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(gray);
        using var matrix = new ColorMatrix();
        matrix.SetSaturation(0);
        using var paint = new Paint(PaintFlags.FilterBitmap);
        paint.SetColorFilter(new ColorMatrixColorFilter(matrix));
        canvas.DrawBitmap(color, 0, 0, paint);
        return gray;
    }

    /// <summary>Asks for a software bitmap (needed to save it) and shrinks while decoding when a max size is set.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private sealed class SizeListener(int? maxDimension) : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            // setAllocator(ALLOCATOR_SOFTWARE): the C# binding doesn't expose it, so call it through JNI.
            var setAllocator = JNIEnv.GetMethodID(decoder.Class.Handle, "setAllocator", "(I)V");
            JNIEnv.CallVoidMethod(decoder.Handle, setAllocator, new JValue((int)ImageDecoderAllocator.Software));
            var width = info.Size.Width;
            var height = info.Size.Height;
            if (maxDimension is { } max && Math.Max(width, height) > max)
            {
                var scale = max / (double)Math.Max(width, height);
                decoder.SetTargetSize(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
            }
        }
    }
}
