using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Avalonia;
using Avalonia.Android;
using Bakkwo.Mobile;

namespace Bakkwo.Android;

/// <summary>
/// The app's one screen. Besides opening from the home screen, it appears in Android's share sheet
/// ("Share → 바꿔") and in "Open with" for any file, so photos and documents can be sent straight to it.
/// </summary>
[Activity(
    Label = "바꿔",
    Theme = "@style/BakkwoTheme",
    Icon = "@mipmap/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
[IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeType = "*/*")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataScheme = "content", DataMimeType = "*/*")]
public class MainActivity : AvaloniaMainActivity<App>
{
    /// <summary>The screen that's showing now; Android may replace it (e.g. after a theme change).</summary>
    internal static MainActivity? Current { get; private set; }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        Mobile.App.Platform = new AndroidPlatform();
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
        // Opened from another app's "Share" or "Open with" (not when Android restores the screen).
        if (savedInstanceState is null) Receive(Intent);
    }

    protected override void OnResume()
    {
        Current = this;
        base.OnResume();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Receive(intent);
    }

    /// <summary>Copies shared files into the app's folder (they arrive as content:// links) and opens the ring.</summary>
    private void Receive(Intent? intent)
    {
        var uris = SharedUris(intent);
        if (uris.Count == 0) return;
        _ = Task.Run(() =>
        {
            var paths = new List<string>();
            foreach (var uri in uris)
            {
                try
                {
                    paths.Add(CopyIn(uri));
                }
                catch (Exception ex) when (ex is IOException or Java.Lang.Exception or UnauthorizedAccessException)
                {
                    // Skip files the other app won't let us read.
                }
            }
            Inbox.Deliver(paths);
        });
    }

    private static List<global::Android.Net.Uri> SharedUris(Intent? intent)
    {
        var list = new List<global::Android.Net.Uri>();
        if (intent is null) return list;
        switch (intent.Action)
        {
            case Intent.ActionSend:
                if (GetStream(intent) is { } one) list.Add(one);
                break;
            case Intent.ActionSendMultiple:
                if (GetStreams(intent) is { } many) list.AddRange(many);
                break;
            case Intent.ActionView:
                if (intent.Data is { } data) list.Add(data);
                break;
        }
        // Some apps only fill in ClipData.
        if (list.Count == 0 && intent.ClipData is { } clip)
        {
            for (var i = 0; i < clip.ItemCount; i++)
            {
                if (clip.GetItemAt(i)?.Uri is { } uri) list.Add(uri);
            }
        }
        return list;
    }

    private static global::Android.Net.Uri? GetStream(Intent intent) =>
        OperatingSystem.IsAndroidVersionAtLeast(33)
            ? intent.GetParcelableExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(global::Android.Net.Uri))) as global::Android.Net.Uri
#pragma warning disable CA1422 // the older call is the only one before Android 13
            : intent.GetParcelableExtra(Intent.ExtraStream) as global::Android.Net.Uri;
#pragma warning restore CA1422

    private static IEnumerable<global::Android.Net.Uri>? GetStreams(Intent intent) =>
        (OperatingSystem.IsAndroidVersionAtLeast(33)
            ? intent.GetParcelableArrayListExtra(Intent.ExtraStream, Java.Lang.Class.FromType(typeof(global::Android.Net.Uri)))
#pragma warning disable CA1422
            : intent.GetParcelableArrayListExtra(Intent.ExtraStream))
#pragma warning restore CA1422
        ?.Cast<global::Android.Net.Uri>();

    private string CopyIn(global::Android.Net.Uri uri)
    {
        var resolver = ContentResolver ?? throw new IOException("No content resolver.");
        var folder = Path.Combine(AndroidPlatform.WorkDirectoryFor(this), "in", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, DisplayName(uri));

        using var source = resolver.OpenInputStream(uri) ?? throw new IOException("Can't open the shared file.");
        using var copy = File.Create(target);
        source.CopyTo(copy);
        return target;
    }

    /// <summary>The file's real name ("IMG_2041.HEIC"), which the format detection relies on.</summary>
    private string DisplayName(global::Android.Net.Uri uri)
    {
        string? name = null;
        try
        {
            using var cursor = ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst()) name = cursor.GetString(0);
        }
        catch (Java.Lang.Exception)
        {
            // Fall back to the last part of the link below.
        }
        name ??= uri.LastPathSegment ?? "file";
        name = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        if (Path.GetExtension(name).Length == 0 && ContentResolver?.GetType(uri) is { } mime
            && global::Android.Webkit.MimeTypeMap.Singleton?.GetExtensionFromMimeType(mime) is { } ext)
        {
            name += "." + ext;
        }
        return name.Length == 0 ? "file" : name;
    }
}
