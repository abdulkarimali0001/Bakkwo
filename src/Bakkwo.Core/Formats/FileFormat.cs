namespace Bakkwo.Core.Formats;

public enum FormatCategory
{
    Image,
    Document,
    Data,
    Text,
    Video,
    Audio,
    Subtitle,
    Archive,
    Folder,
}

/// <summary>A file format Bakkwo knows about. <see cref="Id"/> is the short, lowercase key ("png", "tgz").</summary>
public sealed record FileFormat(string Id, string DisplayName, FormatCategory Category, IReadOnlyList<string> Extensions)
{
    /// <summary>The extension used when writing this format, including the dot (empty for folders).</summary>
    public string PrimaryExtension => Extensions.Count > 0 ? Extensions[0] : "";

    public bool IsFolder => Category == FormatCategory.Folder;

    /// <summary>Short label for buttons and the ring menu.</summary>
    public string Label => Id switch
    {
        "folder" => "Extract",
        "tgz" => "TAR.GZ",
        _ => Id.ToUpperInvariant(),
    };

    public override string ToString() => Id;
}
