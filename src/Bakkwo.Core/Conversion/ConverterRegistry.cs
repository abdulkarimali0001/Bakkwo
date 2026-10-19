using Bakkwo.Core.Converters;

namespace Bakkwo.Core.Conversion;

public sealed class ConverterRegistry
{
    private readonly List<IConverter> _converters = [];

    public IReadOnlyList<IConverter> Converters => _converters;

    public ConverterRegistry Add(IConverter converter)
    {
        _converters.Add(converter);
        return this;
    }

    /// <summary>All built-in converters. FFmpeg and LibreOffice are included but only active when installed.</summary>
    public static ConverterRegistry CreateDefault() => new ConverterRegistry()
        .Add(new ImageConverter())
        .Add(new ImageToPdfConverter())
        .Add(new DataConverter())
        .Add(new TextConverter())
        .Add(new WordConverter())
        .Add(new LibreOfficeConverter())
        .Add(new ArchiveConverter())
        .Add(new FfmpegConverter());
}
