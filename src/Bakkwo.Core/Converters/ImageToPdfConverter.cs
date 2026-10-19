using Bakkwo.Core.Conversion;
using Bakkwo.Core.Pdf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Bakkwo.Core.Converters;

/// <summary>Image → PDF, one page per image.</summary>
public sealed class ImageToPdfConverter : ConverterBase
{
    public override string Name => "Image to PDF";

    public override IEnumerable<ConversionEdge> Edges => ImageConverter.Readable.Select(f => Edge(f, "pdf"));

    public override async Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct)
    {
        var page = await ToJpegPageAsync(step.InputPath, options, ct);
        progress?.Report(0.7);
        await File.WriteAllBytesAsync(step.OutputPath, PdfImageWriter.Build([page]), ct);
        progress?.Report(1);
    }

    /// <summary>Puts several images into one PDF, in the order given.</summary>
    public static async Task MergeAsync(IReadOnlyList<string> imagePaths, string outputPath, ConversionOptions options, CancellationToken ct = default)
    {
        var pages = new List<JpegPage>();
        foreach (var path in imagePaths)
        {
            pages.Add(await ToJpegPageAsync(path, options, ct));
        }
        await File.WriteAllBytesAsync(outputPath, PdfImageWriter.Build(pages), ct);
    }

    internal static async Task<JpegPage> ToJpegPageAsync(string path, ConversionOptions options, CancellationToken ct)
    {
        using var image = await Image.LoadAsync(path, ct);
        ImageOps.Prepare(image, options);
        image.Mutate(x => x.BackgroundColor(Color.White));

        // DCTDecode with /DeviceRGB needs a 3-channel JPEG, even for grayscale images.
        using var rgb = image.CloneAs<Rgb24>();
        using var buffer = new MemoryStream();
        await rgb.SaveAsJpegAsync(buffer, new JpegEncoder
        {
            Quality = Math.Clamp(options.Quality, 1, 100),
            ColorType = JpegEncodingColor.YCbCrRatio420,
        }, ct);
        return new JpegPage(buffer.ToArray(), rgb.Width, rgb.Height);
    }
}
