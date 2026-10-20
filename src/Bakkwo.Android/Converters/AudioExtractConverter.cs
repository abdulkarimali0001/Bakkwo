using Android.Media;
using Bakkwo.Core.Conversion;
using Java.Nio;

namespace Bakkwo.Android.Converters;

/// <summary>
/// Video → M4A by copying the video's AAC sound track into its own file (Android's MediaExtractor and
/// MediaMuxer). Nothing is re-encoded, so it's fast and the sound is exactly the original.
/// </summary>
internal sealed class AudioExtractConverter : ConverterBase
{
    private const string AacMime = "audio/mp4a-latm";

    public override string Name => "Android audio extractor";

    public override int? MaxConcurrency => 2;

    public override IEnumerable<ConversionEdge> Edges =>
        new[] { "mp4", "mov", "3gp", "mkv", "webm" }.Select(video => Edge(video, "m4a"));

    public override Task ConvertAsync(ConversionStep step, ConversionOptions options, IProgress<double>? progress, CancellationToken ct) =>
        Task.Run(() =>
        {
            using var extractor = new MediaExtractor();
            extractor.SetDataSource(step.InputPath);

            var (track, format) = FindAudio(extractor);
            if (format.GetString(MediaFormat.KeyMime) != AacMime)
            {
                throw new InvalidOperationException(
                    "This video's sound isn't AAC, so it can't be copied to M4A on the phone. The computer version can convert it.");
            }
            extractor.SelectTrack(track);
            var duration = format.ContainsKey(MediaFormat.KeyDuration) ? format.GetLong(MediaFormat.KeyDuration) : 0;
            var bufferSize = format.ContainsKey(MediaFormat.KeyMaxInputSize) ? format.GetInteger(MediaFormat.KeyMaxInputSize) : 0;
            var buffer = ByteBuffer.AllocateDirect(Math.Max(bufferSize, 256 * 1024))!;

            var muxer = new MediaMuxer(step.OutputPath, MuxerOutputType.Mpeg4);
            try
            {
                var output = muxer.AddTrack(format);
                muxer.Start();
                var info = new MediaCodec.BufferInfo();
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    var size = extractor.ReadSampleData(buffer, 0);
                    if (size < 0) break;
                    var flags = extractor.SampleFlags.HasFlag(MediaExtractorSampleFlags.Sync) ? MediaCodecBufferFlags.KeyFrame : MediaCodecBufferFlags.None;
                    info.Set(0, size, extractor.SampleTime, flags);
                    muxer.WriteSampleData(output, buffer, info);
                    if (duration > 0) progress?.Report(Math.Clamp(extractor.SampleTime / (double)duration, 0, 0.99));
                    extractor.Advance();
                }
                muxer.Stop();
            }
            finally
            {
                muxer.Release();
            }
            progress?.Report(1);
        }, ct);

    private static (int Track, MediaFormat Format) FindAudio(MediaExtractor extractor)
    {
        for (var i = 0; i < extractor.TrackCount; i++)
        {
            var format = extractor.GetTrackFormat(i);
            if (format.GetString(MediaFormat.KeyMime)?.StartsWith("audio/", StringComparison.Ordinal) == true) return (i, format);
        }
        throw new InvalidOperationException("This video has no sound track.");
    }
}
