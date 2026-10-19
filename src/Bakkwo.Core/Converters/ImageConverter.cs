using Bakkwo.Core.Conversion;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Bakkwo.Core.Converters;

/// <summary>Raster image ↔ raster image, in pure .NET (no native libraries).</summary>
public sealed class ImageConverter : ConverterBase
{
    public static readonly string[] Readable = ["jpg", "png", "webp", "gif", "bmp", "tiff", "tga"];
    public static readonly string[] Writable = ["jpg", "png", "webp", "gif", "bmp", "tiff", "tga", "ico"];

    public override string Name => "Images";

    public override IEnumerable<ConversionEdge> Edges =>
        from f in Readable
        from t in Writable
        where f != t
        select Edge(f, t);

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(step.InputPath, ct);
        ImageOps.Prepare(image, options);
        progress?.Report(0.5);

        if (step.To.Id == "ico")
        {
            await IcoWriter.WriteAsync(image, step.OutputPath, ct);
        }
        else
        {
            if (step.To.Id == "jpg")
            {
                // JPEG has no transparency; put transparent areas on white instead of black.
                image.Mutate(x => x.BackgroundColor(Color.White));
            }
            await image.SaveAsync(step.OutputPath, EncoderFor(step.To.Id, options), ct);
        }
        progress?.Report(1);
    }

    private static IImageEncoder EncoderFor(string id, ConversionOptions o) => id switch
    {
        "jpg" => new JpegEncoder { Quality = Math.Clamp(o.Quality, 1, 100) },
        "png" => new PngEncoder(),
        "webp" => new WebpEncoder
        {
            Quality = Math.Clamp(o.Quality, 1, 100),
            FileFormat = o.Quality >= 100 ? WebpFileFormatType.Lossless : WebpFileFormatType.Lossy,
        },
        "gif" => new GifEncoder(),
        "bmp" => new BmpEncoder(),
        "tiff" => new TiffEncoder(),
        "tga" => new TgaEncoder(),
        _ => throw new NotSupportedException($"No image encoder for {id}."),
    };
}

internal static class ImageOps
{
    /// <summary>Applies EXIF rotation, then the user's size and grayscale options.</summary>
    public static void Prepare(Image image, ConversionOptions options)
    {
        image.Mutate(x => x.AutoOrient());

        if (options.MaxDimension is int max && max > 0 && (image.Width > max || image.Height > max))
        {
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(max, max), Mode = ResizeMode.Max }));
        }
        if (options.Grayscale)
        {
            image.Mutate(x => x.Grayscale());
        }
    }
}
