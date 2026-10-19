# Security

Bakkwo runs entirely on your computer. It opens only files you choose, writes results next to them,
and makes no network requests.

## Reporting a problem

Please open a GitHub issue, or for anything sensitive, contact the maintainer privately through
GitHub before posting details publicly.

## Known dependency advisories

Bakkwo uses SixLabors.ImageSharp 3.1.12, the newest release that builds without a licence key.
These advisories are fixed only in ImageSharp 4.1.2+, which requires a Six Labors licence key for
Release builds. Each one is suppressed in `src/Bakkwo.Core/Bakkwo.Core.csproj` for the reason below.

| Advisory | Impact | Why it's acceptable here |
| --- | --- | --- |
| [GHSA-j3p4-wp97-rph4](https://github.com/advisories/GHSA-j3p4-wp97-rph4) | crash in HistogramEqualization | Bakkwo never calls it |
| [GHSA-j9gm-c75j-xc9q](https://github.com/advisories/GHSA-j9gm-c75j-xc9q) | crash in TIFF CCITT T4 encoder | Bakkwo never writes CCITT TIFF |
| [GHSA-jjfr-hcj7-qf5w](https://github.com/advisories/GHSA-jjfr-hcj7-qf5w) | crash in TIFF CCITT T6 encoder | Bakkwo never writes CCITT TIFF |
| [GHSA-gwg2-r3hj-4w44](https://github.com/advisories/GHSA-gwg2-r3hj-4w44) | high memory use on a malformed ICC profile | local denial of service only |
| [GHSA-wmxv-xphr-5c9g](https://github.com/advisories/GHSA-wmxv-xphr-5c9g) | brief hang on a malformed BigTIFF | local denial of service only |

None allow code execution or data access. The two that apply to decoding can at worst slow down or
crash Bakkwo itself while it converts a deliberately malformed file. Revisit this if Bakkwo ever
processes files from other people automatically (for example as a server).
