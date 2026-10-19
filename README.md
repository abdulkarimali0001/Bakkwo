<p align="center"><img src="docs/media/icon.png" width="128" alt="바꿔 Bakkwo icon"></p>

<h1 align="center">바꿔 Bakkwo</h1>

<p align="center"><b>Drop it. Pick a format. Changed. · 놓고, 고르면, 바뀝니다.</b></p>

A free, offline, cross-platform file converter written in C# with .NET 8 and Avalonia.

*Bakkwo* (바꿔) is Korean for "change it!". Drag files onto the window and a ring of formats opens under your
cursor, showing only the formats those files can actually become. Release over one and the converted
files appear next to the originals. Nothing is uploaded anywhere.

<p align="center">
  <img src="docs/media/main-ko.png" width="49%" alt="바꿔 in Korean with the format ring open over a PNG, light theme">
  <img src="docs/media/main-en.png" width="49%" alt="Bakkwo in English, dark theme">
</p>

## Features

- **Ring menu at the cursor** — a custom-drawn Avalonia control. Keep the mouse held and release over
  a slice, or drop first and click one.
- **Route planning** — formats form a graph and converters are its edges. Dijkstra's algorithm finds
  the cheapest chain, so YAML → CSV works via JSON and Markdown → text works via HTML, without a
  converter written for each pair.
- **Parallel batches** with per-file progress; output names never overwrite anything (`photo (1).webp`).
- **Shrink without changing format**: pick the file's own format (marked ↻) with a Max size to get
  `clip (720p).mp4` or a smaller photo. The ring shows popular formats first and pages the rest (More ▸).
- **Image options**: quality, maximum size and grayscale. EXIF rotation is applied automatically.
- **Word documents**: a built-in DOCX reader and writer (Open XML SDK) keeps headings, lists, tables,
  links, code and pictures, and understands documents from Korean Word. LibreOffice, when installed,
  adds DOC/RTF/ODT and an exact-layout PDF.
- **Hand-written writers** for multi-size `.ico` files and image-to-PDF (a minimal PDF 1.4 writer that
  embeds JPEG data directly).
- **Command-line tool** that shares the same engine.
- **Korean and English** (한국어 / English). It follows the system language and can be switched
  with one click; the choice is remembered.
- **A real Mac app**: its own icon in the Dock and Finder, shown as 바꿔 on a Korean Mac, an About
  window and app menu, listed under Finder's **Open With** for the formats it converts, and files
  dropped on the Dock icon open straight into the ring.
- **Light and dark themes**, following the system.

## Formats

| From | To |
| --- | --- |
| **Images**: JPG, PNG, WebP, GIF, BMP, TIFF, TGA | each other, ICO, PDF |
| **Word**: DOCX, DOCM, DOTX | Markdown, TXT, HTML; PDF, DOC, RTF, ODT² |
| **Word (older/other)**²: DOC, DOT, RTF, ODT | DOCX, PDF, DOC, RTF, ODT, TXT, HTML, Markdown |
| **Markdown, TXT** | DOCX (headings, lists, tables, links, pictures); PDF² |
| **Data**: JSON, YAML, CSV, TSV, XML | each other |
| **Text**: Markdown, HTML, TXT | HTML, TXT |
| **Archives**: ZIP, TAR, TAR.GZ | each other, extract to a folder; any files or folders → one ZIP |
| **Video**¹: MP4, MOV (incl. iPhone HEVC), MKV, WebM, AVI, WMV, FLV, MPEG, TS, 3GP, OGV, VOB | each other, animated GIF, any audio format; same format smaller (4K / 1080p / 720p) |
| **Audio**¹: MP3, M4A, Apple Lossless, AAC, WAV, FLAC, OGG, Opus, AIFF, WMA, AMR | each other |
| **Subtitles**¹: SRT, WebVTT, ASS/SSA, or the subtitle track inside MKV/MP4/MOV/WebM/TS | SRT, WebVTT, ASS |

**Subtitles stay with the video.** Converting a video keeps its subtitles where the new format can hold them: text
subtitles become a switchable track (MP4/MOV, WebM, MKV); picture subtitles from TV, DVD or Blu-ray (DVB, VobSub,
PGS) stay a track in MKV/TS and are burned into the picture for MP4/MOV/WebM, which can't store them.
`.ts` recordings and downloads (including HLS segments that start with an ID3 tag) are recognised by their packets.

¹ Needs [FFmpeg](https://ffmpeg.org) (`brew install ffmpeg`). Bakkwo asks FFmpeg which encoders it has and
falls back where it can (OGG → Opus when there's no Vorbis encoder, as in Homebrew's build) or hides what that
build can't write (AMR, OGV).
² Needs [LibreOffice](https://www.libreoffice.org) (`brew install --cask libreoffice`) for an exact-layout PDF and
the older formats. DOCX ↔ Markdown/text/HTML is built in and needs nothing else.

Without an optional engine, its formats are simply left out of the ring and the app says how to enable them.

## Run it

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.400 or newer).
On macOS: `brew install --cask dotnet-sdk`, or download the Arm64 installer for Apple Silicon.

```bash
git clone https://github.com/<your-username>/Bakkwo.git
cd Bakkwo
dotnet run --project src/Bakkwo.App      # the desktop app
dotnet test                              # the test suite (76 tests)
```

On macOS you can also double-click `build.command` to run the tests and save the output to
`build-log.txt`, or `setup-mac.command` to install the .NET 8 SDK and FFmpeg first.

### Build the Mac app

```bash
./build/package-mac.sh        # → publish/Bakkwo.app (Apple Silicon; RID=osx-x64 for Intel)
```

Drag `publish/Bakkwo.app` into Applications. The icon is generated by `build/icon/make_icon.py`.

## Command line

```bash
dotnet run --project src/Bakkwo.Cli -- convert photo.png --to webp -q 80
dotnet run --project src/Bakkwo.Cli -- convert *.csv --to json -o converted
dotnet run --project src/Bakkwo.Cli -- route yaml csv      # yaml → json → csv
dotnet run --project src/Bakkwo.Cli -- pdf scan1.jpg scan2.jpg -o scans.pdf
dotnet run --project src/Bakkwo.Cli -- zip report.docx images/ -o bundle.zip
dotnet run --project src/Bakkwo.Cli -- formats
```

## How it's built

```
src/
  Bakkwo.Core/     conversion engine, no UI
    Formats/         format catalog and detection (handles double extensions like .tar.gz)
    Conversion/      IConverter contract, RoutePlanner (Dijkstra), ConversionEngine, OutputPaths
    Converters/      Image, ImageToPdf, Data, Text, Archive, FFmpeg
    Data/            JSON-hub transforms: YAML, CSV/TSV (RFC 4180), XML
    Pdf/             minimal PDF writer
  Bakkwo.App/      Avalonia desktop app and the DonutMenu control
  Bakkwo.Cli/      command-line front end
tests/
  Bakkwo.Core.Tests/   xUnit tests for planner, transforms and end-to-end conversions
```

Adding a format means writing one class that lists its edges and implements `ConvertAsync`.
The planner, the ring, batching and the CLI pick it up automatically. See
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Security

Bakkwo never uploads anything. Known dependency advisories and why they're acceptable for a
local tool are listed in [SECURITY.md](SECURITY.md).

## Credits

The ring-at-the-cursor interaction is inspired by [Filee](https://github.com/KnifeLemon/Filee) by
KnifeLemon (MIT). Bakkwo is an independent implementation written from scratch.

Libraries: [Avalonia](https://avaloniaui.net) (MIT), [ImageSharp](https://github.com/SixLabors/ImageSharp)
(Six Labors Split License, free for open-source use; pinned to 3.1.x, which needs no licence key), [YamlDotNet](https://github.com/aaubry/YamlDotNet)
(MIT), [Markdig](https://github.com/xoofx/markdig) (BSD-2),
[Open XML SDK](https://github.com/dotnet/Open-XML-SDK) (MIT); optional [LibreOffice](https://www.libreoffice.org) (MPL-2.0) and FFmpeg run as separate programs.

## License

MIT © 2026 Khamis Abdulkarim Ali
