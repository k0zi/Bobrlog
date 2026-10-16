# Third-party notices

Bobrlog's own source code is licensed under the MIT license in LICENSE.
The .deb and .rpm packages contain a self-contained .NET build, so they also redistribute the
components below. These components keep their own licenses, and the full license texts are in
`licenses/`. Listing them here does not relicense them or imply endorsement.

## Copied into the source tree

| Component | Source | License / local copy |
| --- | --- | --- |
| Material Design icon paths in `src/Bobrlog.App/Controls/Icons.cs` | Pictogrammers Material Design Icons, https://pictogrammers.com/library/mdi/ (Google Material Icons and community icons) | Apache-2.0: licenses/Apache-2.0.txt; collection terms: licenses/Pictogrammers-LICENSE.txt |

## Redistributed libraries (self-contained build)

| Component | Source | License / local copy |
| --- | --- | --- |
| .NET runtime 10 and Microsoft.Extensions.* (Hosting, Hosting.Systemd, DependencyInjection, Logging, …) | https://github.com/dotnet/runtime | MIT: licenses/DotNet-MIT.txt; upstream component notices: licenses/DotNet-THIRD-PARTY-NOTICES.txt |
| Avalonia 12 (Avalonia, Desktop, Themes.Fluent, Controls.DataGrid, Skia, HarfBuzz, X11, FreeDesktop, Remote.Protocol, Fonts.Inter wrapper) | https://github.com/AvaloniaUI/Avalonia | MIT: licenses/Avalonia-MIT.txt; upstream component notices: licenses/Avalonia-NOTICE.txt |
| Inter font, bundled by Avalonia.Fonts.Inter | https://github.com/rsms/inter | SIL Open Font License 1.1: licenses/Inter-OFL-1.1.txt (applies to the font, separately from Avalonia's MIT wrapper) |
| SkiaSharp and its Linux native library (libSkiaSharp) | https://github.com/mono/SkiaSharp | MIT: licenses/SkiaSharp-LICENSE.txt; native component notices (Skia and others): licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt |
| HarfBuzzSharp and its Linux native library (libHarfBuzzSharp) | https://github.com/mono/SkiaSharp | MIT: licenses/HarfBuzzSharp-LICENSE.txt; native component notices (HarfBuzz and others): the shared notices above |
| Tmds.DBus.Protocol | https://github.com/tmds/Tmds.DBus | MIT: licenses/Tmds.DBus-MIT.txt |
| MicroCom.Runtime | https://github.com/kekekeks/MicroCom | MIT: licenses/MicroCom-MIT.txt |
| CommunityToolkit.Mvvm | https://github.com/CommunityToolkit/dotnet | MIT: licenses/CommunityToolkit-MIT.txt; upstream notices: licenses/CommunityToolkit-THIRD-PARTY-NOTICES.txt |
| Microsoft.IO.RecyclableMemoryStream | https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream | MIT: licenses/RecyclableMemoryStream-MIT.txt |
| KD.Avalonia.Rice | https://github.com/k0zi/KD.Avalonia.Rice | MIT: licenses/KD.Avalonia.Rice-MIT.txt |
| Material.Icons and Material.Icons.Avalonia (through KD.Avalonia.Rice) | https://github.com/SKProCH/Material.Icons | Wrapper code: licenses/Material.Icons-MIT.txt; icon collection: licenses/Pictogrammers-LICENSE.txt and licenses/Apache-2.0.txt |

### Theme palettes (through KD.Avalonia.Rice)

Some themes adapt these color schemes:

| Palette | Source | License copy |
| --- | --- | --- |
| Nord, by Sven Greb | https://github.com/nordtheme/nord | licenses/Nord-MIT.txt |
| Solarized, by Ethan Schoonover | https://github.com/altercation/solarized | licenses/Solarized-MIT.txt |
| Dracula, by Dracula Theme | https://github.com/dracula/dracula-theme | licenses/Dracula-MIT.txt |
| Gruvbox, by morhetz | https://github.com/morhetz/gruvbox | licenses/Gruvbox-MIT.txt |
| Catppuccin | https://github.com/catppuccin/palette | licenses/Catppuccin-MIT.txt |

The distribution-inspired theme names (Ubuntu, Fedora, Manjaro, openSUSE, …) only identify where the
palette idea came from. The named projects are not affiliated with Bobrlog and do not endorse it.
Their names and marks belong to their respective owners.

## Not redistributed

The following are used only to build or test Bobrlog and are not included in the packages:
Nerdbank.GitVersioning (MIT), nfpm (MIT), xUnit and its Visual Studio runner (Apache-2.0),
Microsoft.NET.Test.Sdk (MIT), coverlet.collector (MIT), and AvaloniaUI.DiagnosticsSupport (Debug builds only).

## Maintenance

When you add copied code or assets, or change the dependencies, record the source, version,
author and license here, and update the license copies in `licenses/`. Before a release, check
the publish output (`dotnet publish -r linux-x64 --self-contained`) for new native libraries.
