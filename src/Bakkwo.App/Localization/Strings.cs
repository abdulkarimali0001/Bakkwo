using System.Globalization;

namespace Bakkwo.App.Localization;

public enum AppLanguage
{
    English,
    Korean,
}

/// <summary>
/// Every piece of UI text in English and Korean. The app starts in the system language
/// (Korean if macOS/Windows is set to 한국어) and can be switched from the header.
/// </summary>
public static class Strings
{
    public static AppLanguage Language { get; private set; } = Detect();

    public static event Action? LanguageChanged;

    public static bool IsKorean => Language == AppLanguage.Korean;

    public static void Set(AppLanguage language)
    {
        if (Language == language) return;
        Language = language;
        LanguageChanged?.Invoke();
    }

    public static AppLanguage Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? AppLanguage.Korean : AppLanguage.English;

    private static string T(string en, string ko) => IsKorean ? ko : en;

    // Name and identity
    public const string NameKorean = "바꿔";
    public const string NameLatin = "Bakkwo";
    public static string AppName => $"{NameKorean} {NameLatin}";
    public static string Tagline => T("Drop it. Pick a format. Changed.", "놓고, 고르면, 바뀝니다.");
    public static string Meaning => T("바꿔 (bakkwo) means \"change it!\" in Korean.", "바꿔는 \"change it!\"이라는 뜻이에요.");
    public static string Subtitle => T(
        "Drop files and pick a format from the ring. Everything stays on your computer.",
        "파일을 놓고 링에서 형식을 고르세요. 모든 변환은 내 컴퓨터 안에서만 이루어집니다.");
    public static string LanguageButton => T("한국어", "English");

    // Options
    public static string Quality => T("Quality", "품질");
    public static string MaxSize => T("Max size", "최대 크기");
    public static string Original => T("Original", "원본");
    public static string Grayscale => T("Grayscale", "흑백");

    // Drop zone
    public static string DropTitle => T("Drop files or folders here", "파일이나 폴더를 여기에 놓으세요");
    public static string DropHint => T(
        "Keep the mouse held and release over a format in the ring, or drop and click one.",
        "마우스를 누른 채로 링의 형식 위에서 놓거나, 먼저 놓은 뒤 형식을 클릭하세요.");
    public static string ChooseFiles => T("Choose files…", "파일 선택…");
    public static string PickerTitle => T("Choose files to convert", "변환할 파일 선택");

    // Recent list
    public static string Recent => T("Recent", "최근 변환");
    public static string Show => T("Show", "보기");
    public static string Converting => T("Converting…", "변환 중…");
    public static string Zipping => T("Zipping…", "압축 중…");
    public static string Saved(string name, string via, double seconds) => IsKorean
        ? $"{name} 저장됨{(via.Length > 0 ? $" ({via} 경유)" : "")} · {seconds:0.0}초"
        : $"Saved {name}{(via.Length > 0 ? $" (via {via})" : "")} · {seconds:0.0}s";
    public static string SavedPlain(string name) => T($"Saved {name}", $"{name} 저장됨");
    public static string Failed(string error) => T("Failed: ", "실패: ") + error;
    public static string ItemsToZip(int count) => T($"{count} items → ZIP", $"{count}개 항목 → ZIP");

    // Ring
    public static string Cancel => T("Cancel", "취소");
    public static string FilesCount(int count) => T($"{count} files", $"파일 {count}개");
    public static string ZipAll => T("ZIP all", "모두 ZIP");
    public static string Extract => T("Extract", "풀기");
    public static string MoreLabel => T("More", "더보기");
    public static string BackLabel => T("Back", "처음으로");
    public static string MoreHint(int count) => T($"{count} more formats", $"다른 형식 {count}개 더 보기");
    public static string BackHint => T("Back to the suggestions", "추천 형식으로 돌아가기");
    public static string Smaller => T("smaller", "작게");
    public static string SmallerHint(string label) => T($"Make a smaller {label}", $"{label} 작게 만들기");
    public static string ExtractHint => T("Unpack into a folder", "폴더로 압축 풀기");
    public static string ZipHint => T("Put it in a ZIP", "ZIP으로 압축하기");
    public static string ZipAllHint => T("Put them all in one ZIP", "모두 ZIP 하나로 압축하기");
    public static string CaptionFolder => T("folder", "폴더");
    public static string CaptionArchive => T("archive", "압축");

    public static string Caption(Bakkwo.Core.Formats.FormatCategory category) => category switch
    {
        Bakkwo.Core.Formats.FormatCategory.Image => T("image", "이미지"),
        Bakkwo.Core.Formats.FormatCategory.Document => T("document", "문서"),
        Bakkwo.Core.Formats.FormatCategory.Data => T("data", "데이터"),
        Bakkwo.Core.Formats.FormatCategory.Text => T("text", "텍스트"),
        Bakkwo.Core.Formats.FormatCategory.Video => T("video", "영상"),
        Bakkwo.Core.Formats.FormatCategory.Audio => T("audio", "오디오"),
        Bakkwo.Core.Formats.FormatCategory.Subtitle => T("subtitles", "자막"),
        Bakkwo.Core.Formats.FormatCategory.Archive => T("archive", "압축"),
        _ => "",
    };

    public static string ConvertHint(Bakkwo.Core.Formats.FileFormat target) => target.Category switch
    {
        // "형식으로" is correct after any format name, unlike choosing 로/으로 for "MP3", "WEBP"…
        Bakkwo.Core.Formats.FormatCategory.Subtitle => T($"Save subtitles as {target.Label}", $"자막을 {target.Label} 형식으로 저장"),
        _ => T($"Convert to {target.DisplayName}", $"{target.Label} 형식으로 변환"),
    };

    // Status line
    public static string Ready => T("Ready.", "준비됐어요.");
    public static string ReleaseOverFormat => T("Release over a format.", "원하는 형식 위에서 놓으세요.");
    public static string ClickFormat => T("Click a format, or press Esc to cancel.", "형식을 클릭하거나 Esc로 취소하세요.");
    public static string UnknownTypes => T(
        "Some of these files aren't a type Bakkwo converts yet — they can still be zipped.",
        "아직 변환할 수 없는 파일이 있어요. ZIP으로 묶을 수는 있어요.");
    public static string ConvertingTo(int count, string format) =>
        T($"Converting {count} file(s) to {format}…", $"파일 {count}개를 {format}(으)로 변환 중…");
    public static string DoneConverted(int count, string format) =>
        T($"Done — {count} file(s) converted to {format}.", $"완료 — 파일 {count}개를 {format}(으)로 변환했어요.");
    public static string PartlyDone(int ok, int total) =>
        T($"{ok} of {total} converted. See the list for what failed.", $"{total}개 중 {ok}개 변환됨. 실패한 항목은 목록을 확인하세요.");
    public static string DoneZipped => T("Done — zipped.", "완료 — 압축했어요.");
    public static string ZipFailed => T("Zipping failed.", "압축에 실패했어요.");
    public static string SomethingWrong(string message) => T("Something went wrong: ", "문제가 생겼어요: ") + message;
    public static string OpenedFromFinder(int count) => T(
        $"Opened {count} file(s). Click a format, or press Esc to cancel.",
        $"파일 {count}개를 열었어요. 형식을 클릭하거나 Esc로 취소하세요.");

    // Engines
    public static string AllReady => T(
        "All converters ready, including audio, video and Word → PDF.",
        "오디오·비디오와 Word → PDF를 포함한 모든 변환기가 준비됐어요.");
    public static string LibreOfficeMissing => T(
        "LibreOffice not found — install it (brew install --cask libreoffice) for DOC/RTF/ODT and Word → PDF.",
        "LibreOffice가 없어요 — DOC/RTF/ODT와 Word → PDF 변환을 하려면 설치하세요 (brew install --cask libreoffice).");

    /// <summary>One line about optional engines: all ready, or what to install.</summary>
    public static string EngineStatus(IEnumerable<string> missingEngines)
    {
        var missing = missingEngines.ToList();
        if (missing.Count == 0) return AllReady;
        return string.Join("  ", missing.Select(name => name switch
        {
            "FFmpeg" => FfmpegMissing,
            "LibreOffice" => LibreOfficeMissing,
            _ => name,
        }));
    }
    public static string FfmpegMissing => T(
        "FFmpeg not found — install it (brew install ffmpeg) to convert audio and video.",
        "FFmpeg가 없어요 — 오디오·비디오를 변환하려면 설치하세요 (brew install ffmpeg).");

    // About window
    public static string About => T("About Bakkwo", "바꿔 정보");
    public static string Version(string v) => T($"Version {v}", $"버전 {v}");
    public static string FormatsSupported(int count) => T($"{count} formats, converted offline", $"{count}가지 형식을 오프라인으로 변환");
    public static string MadeBy => T("Made by Khamis Abdulkarim Ali", "만든 사람: Khamis Abdulkarim Ali");
    public static string Inspired => T("Ring idea inspired by Filee (MIT).", "링 아이디어는 Filee(MIT)에서 영감을 받았어요.");
    public static string Close => T("Close", "닫기");
    public static string Quit => T("Quit Bakkwo", "바꿔 종료");
}
