using System.Buffers.Binary;

namespace Bakkwo.Core.Tests;

/// <summary>
/// Writes a minimal Blu-ray PGS subtitle stream (.sup) with one white box. FFmpeg can't draw picture
/// subtitles from text, so tests build one by hand and let FFmpeg turn it into DVB subtitles.
/// </summary>
internal static class Pgs
{
    public static byte[] WhiteBox(int screenWidth, int screenHeight, int x, int y, int width, int height, double fromSeconds, double toSeconds)
    {
        var output = new List<byte>();
        DisplaySet(output, (uint)(fromSeconds * 90000), show: true);
        DisplaySet(output, (uint)(toSeconds * 90000), show: false);
        return [.. output];

        void DisplaySet(List<byte> o, uint pts, bool show)
        {
            // Presentation composition: screen size, frame rate, composition state, object placement.
            var pcs = new List<byte>();
            pcs.AddRange(U16(screenWidth)); pcs.AddRange(U16(screenHeight));
            pcs.Add(0x10); pcs.AddRange(U16(show ? 1 : 2)); pcs.Add((byte)(show ? 0x80 : 0x00));
            pcs.Add(0); pcs.Add(0); pcs.Add((byte)(show ? 1 : 0));
            if (show) { pcs.AddRange(U16(0)); pcs.Add(0); pcs.Add(0); pcs.AddRange(U16(x)); pcs.AddRange(U16(y)); }
            Segment(o, 0x16, pts, pcs);

            var wds = new List<byte> { 1, 0 };
            wds.AddRange(U16(x)); wds.AddRange(U16(y)); wds.AddRange(U16(width)); wds.AddRange(U16(height));
            Segment(o, 0x17, pts, wds);

            if (show)
            {
                // Palette: 0 = transparent, 1 = opaque white (Y, Cr, Cb, alpha).
                Segment(o, 0x14, pts, [0, 0, 0, 16, 128, 128, 0, 1, 235, 128, 128, 255]);

                // Run-length encoded rows: "width pixels of colour 1", then end of line.
                var rle = new List<byte>();
                for (var row = 0; row < height; row++) rle.AddRange([0x00, (byte)(0xC0 | (width >> 8)), (byte)(width & 0xFF), 0x01, 0x00, 0x00]);
                var ods = new List<byte>();
                ods.AddRange(U16(0)); ods.Add(0); ods.Add(0xC0);
                var length = rle.Count + 4;
                ods.AddRange([(byte)(length >> 16), (byte)(length >> 8), (byte)length]);
                ods.AddRange(U16(width)); ods.AddRange(U16(height)); ods.AddRange(rle);
                Segment(o, 0x15, pts, ods);
            }
            Segment(o, 0x80, pts, []);
        }
    }

    private static void Segment(List<byte> output, byte type, uint pts, IReadOnlyCollection<byte> data)
    {
        output.AddRange("PG"u8.ToArray());
        var header = new byte[11];
        BinaryPrimitives.WriteUInt32BigEndian(header, pts);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), pts);
        header[8] = type;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(9), (ushort)data.Count);
        output.AddRange(header);
        output.AddRange(data);
    }

    private static byte[] U16(int value) => [(byte)(value >> 8), (byte)value];
}
