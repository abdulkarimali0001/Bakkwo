using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Bakkwo.Core.Converters;

/// <summary>
/// Writes a multi-size .ico. Each size is stored as an embedded PNG, which Windows has
/// supported since Vista and macOS/Linux viewers read too.
/// </summary>
internal static class IcoWriter
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    public static async Task WriteAsync(Image source, string path, CancellationToken ct)
    {
        var largestSide = Math.Max(source.Width, source.Height);
        var sizes = Sizes.Where(s => s <= Math.Max(largestSide, 16)).ToList();

        using var rgba = source.CloneAs<Rgba32>();
        var entries = new List<(int Size, byte[] Png)>();
        foreach (var size in sizes)
        {
            using var frame = rgba.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Pad, // keep aspect ratio, fill the rest with transparency
                PadColor = Color.Transparent,
            }));
            using var buffer = new MemoryStream();
            await frame.SaveAsPngAsync(buffer, ct);
            entries.Add((size, buffer.ToArray()));
        }

        await using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        // ICONDIR
        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)entries.Count);

        // ICONDIRENTRY × n
        var offset = 6 + 16 * entries.Count;
        foreach (var (size, png) in entries)
        {
            var dimension = (byte)(size >= 256 ? 0 : size); // 0 means 256
            writer.Write(dimension);         // width
            writer.Write(dimension);         // height
            writer.Write((byte)0);           // palette colours
            writer.Write((byte)0);           // reserved
            writer.Write((ushort)1);         // colour planes
            writer.Write((ushort)32);        // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in entries) writer.Write(png);
    }
}
