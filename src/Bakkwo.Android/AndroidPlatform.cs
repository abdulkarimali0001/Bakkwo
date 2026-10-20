using Android.Content;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using AndroidX.Core.Content;
using Bakkwo.Android.Converters;
using Bakkwo.Core.Conversion;
using Bakkwo.Mobile;

namespace Bakkwo.Android;

/// <summary>Sharing, saving and the phone's own decoders, for the shared phone screen.</summary>
internal sealed class AndroidPlatform : IPhonePlatform
{
    private static Activity Activity => MainActivity.Current ?? throw new InvalidOperationException("바꿔 isn't open.");

    public IEnumerable<IConverter> ExtraConverters => [new HeifImageConverter(), new AudioExtractConverter()];

    public string WorkDirectory => WorkDirectoryFor(Activity);

    /// <summary>The app's private cache; Android may clear it when space runs low, which is fine for temporary copies.</summary>
    public static string WorkDirectoryFor(Context context) =>
        Path.Combine(context.CacheDir?.AbsolutePath ?? Path.GetTempPath(), "Bakkwo");

    public Task ShareAsync(IReadOnlyList<string> paths)
    {
        var authority = (Activity.PackageName ?? "io.github.abdulkarimali0001.bakkwo") + ".files";
        var uris = paths
            .Select(p => FileProvider.GetUriForFile(Activity, authority, new Java.IO.File(p))!)
            .ToList();

        var intent = new Intent(uris.Count == 1 ? Intent.ActionSend : Intent.ActionSendMultiple);
        intent.SetType(uris.Count == 1 ? MimeOf(paths[0]) : "*/*");
        if (uris.Count == 1)
        {
            intent.PutExtra(Intent.ExtraStream, uris[0]);
        }
        else
        {
            intent.PutParcelableArrayListExtra(Intent.ExtraStream, uris.Cast<IParcelable>().ToList());
        }
        // ClipData makes the read permission reach the app the user picks.
        var clip = ClipData.NewRawUri(Path.GetFileName(paths[0]), uris[0])!;
        foreach (var uri in uris.Skip(1)) clip.AddItem(new ClipData.Item(uri));
        intent.ClipData = clip;
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);

        Activity.StartActivity(Intent.CreateChooser(intent, (string?)null));
        return Task.CompletedTask;
    }

    public Task<string?> SaveToDownloadsAsync(string path)
    {
        // Android 10+: add the file to Downloads through MediaStore, no permission needed.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29)) return Task.FromResult<string?>(null);
        return Task.Run(() => OperatingSystem.IsAndroidVersionAtLeast(29) ? SaveToDownloads(path) : null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private string? SaveToDownloads(string path)
    {
        var resolver = Activity.ContentResolver ?? throw new IOException("No content resolver.");
        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, Path.GetFileName(path));
        values.Put(MediaStore.IMediaColumns.MimeType, MimeOf(path));
        values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads + "/Bakkwo");
        values.Put(MediaStore.IMediaColumns.IsPending, 1);

        var uri = resolver.Insert(MediaStore.Downloads.ExternalContentUri, values)
                  ?? throw new IOException("Downloads isn't available.");
        try
        {
            using (var target = resolver.OpenOutputStream(uri) ?? throw new IOException("Can't write to Downloads."))
            using (var source = File.OpenRead(path))
            {
                source.CopyTo(target);
            }
            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
        return global::Android.OS.Environment.DirectoryDownloads + "/Bakkwo";
    }

    private static string MimeOf(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? extension switch
        {
            "md" => "text/markdown",
            "yaml" or "yml" => "application/yaml",
            "srt" => "application/x-subrip",
            _ => "application/octet-stream",
        };
    }
}
