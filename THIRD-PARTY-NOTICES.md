# Third-party software and licensing report

Quest Map Studio 1.0.0. Generated from the restored dependency graph and the official package notices on 2026-11-18. This is an independent desktop companion; it is not an official BMBF, QuestPatcher, BeatSaver, Meta or Beat Games release. No BMBF source, game binaries, map audio or map art are included.

## Components

| Package | Version | Package license declaration | Authors |
|---|---|---|---|
| Avalonia | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Angle.Windows.Natives | 2.1.25547.20250602 | LICENSE | Avalonia Team |
| Avalonia.BuildServices | 11.3.2 | MIT | Avalonia Team |
| Avalonia.Desktop | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Fonts.Inter | 11.3.22 | MIT | Avalonia Team |
| Avalonia.FreeDesktop | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Native | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Remote.Protocol | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Skia | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Themes.Fluent | 11.3.22 | MIT | Avalonia Team |
| Avalonia.Win32 | 11.3.22 | MIT | Avalonia Team |
| Avalonia.X11 | 11.3.22 | MIT | Avalonia Team |
| HarfBuzzSharp | 8.3.1.1 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.1 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.1 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.1 | MIT | Microsoft |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.1 | MIT | Microsoft |
| MicroCom.Runtime | 0.11.0 | MIT | MicroCom.Runtime |
| SkiaSharp | 2.88.9 | MIT | Microsoft |
| SkiaSharp.NativeAssets.Linux | 2.88.9 | MIT | Microsoft |
| SkiaSharp.NativeAssets.macOS | 2.88.9 | MIT | Microsoft |
| SkiaSharp.NativeAssets.WebAssembly | 2.88.9 | MIT | Microsoft |
| SkiaSharp.NativeAssets.Win32 | 2.88.9 | MIT | Microsoft |
| Tmds.DBus.Protocol | 0.21.3 | MIT | Tom Deseyn |

The dependency graph also includes Linux/macOS/WebAssembly assets and build-only packages because Avalonia.Desktop is cross-platform. Their presence in this inventory does not imply a separately supported Linux/macOS product. The published executable targets Windows x64.

## Additional redistributed components

- .NET Windows x64 runtime 10.0.14: MIT and component-specific third-party notices, preserved in `Licenses/dotnet-runtime-*`. The application is self-contained; no separate .NET runtime installation is required.
- Android ADB and its Windows helper DLLs: unmodified files from Google's official Windows platform-tools archive. The complete upstream `NOTICE.txt` and `source.properties` are shipped beside the files. They contain the applicable Apache-2.0 and third-party notices; this report does not replace them.
- Inter font: packaged with Avalonia.Fonts.Inter. Preserve its SIL Open Font License notice in `Licenses/Inter-OFL.txt`.
- QuestPatcher attribution: map-import/browser components were adapted from the earlier QuestPatcher Maps preview developed for this task. This is an altered, independent source distribution. The original QuestPatcher zlib license is retained verbatim in `Licenses/QuestPatcher-zlib.txt`; no original authorship is claimed.

## Distribution conditions

The MIT, BSD, zlib and Apache components permit use and redistribution subject to their respective attribution, notice and other license conditions. Keep this report, the complete Licenses directory, and the ADB notices with redistributed binary archives. Components under Microsoft terms remain subject to those terms rather than MIT. Own application code is MIT; that license does not relicense third-party code or artwork.

The BeatSaver link opens the site in the system browser. Each map, song recording, cover and playlist remains subject to its author's rights and the service's rules; the application's license grants no rights to those contents. SongCore and PlaylistCore are separately installed Quest mods, not redistributed in this program. Game compatibility is distinct from copyright permission.

## Upstream sources

- https://github.com/Lauriethefish/QuestPatcher
- https://github.com/AvaloniaUI/Avalonia
- https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid
- https://github.com/mono/SkiaSharp
- https://github.com/rsms/inter
- https://developer.android.com/tools/releases/platform-tools
- https://github.com/dotnet/runtime

The package `.nuspec` files and supplied license/notice files are included under Licenses/ for exact provenance. Where a package declares a license by SPDX expression, that declaration is recorded above and the relevant license text/attribution is retained.
