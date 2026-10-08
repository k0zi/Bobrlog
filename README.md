<p align="center">
  <img src="packaging/icons/bobrlog-128.png" alt="Bobrlog icon" width="128" height="128">
</p>

# Bobrlog – Event Viewer for Linux

A graphical log viewer in the style of the Windows Event Viewer (C#, .NET 10, Avalonia 12). It shows system events
by category with severity icons, no queries needed, and explains why the machine shut down or restarted.

## Features
- **Overview**: critical, error and warning counts (24 hours / 7 days), a clickable per-day breakdown for the last 14 days,
  recent boots, and the most frequent error sources.
- **Boots**: for every boot, determines how it ended (clean shutdown or reboot, unexpected shutdown,
  machine that never resumed from sleep) and lists the likely causes (GPU reset, lockup, OOM, MCE, I/O error, pstore,
  application crash, etc.).
- **Categories**: freezes and crashes, kernel and hardware, services, power management, security, network,
  applications, other, plus all events. Classification is done by the rules in `src/Bobrlog.Core/Rules/default-rules.json`,
  with explanations in the selected language.
- **Filters**: time range, a specific day or boot, minimum severity, search (regex), source or unit; live follow;
  copy the equivalent `journalctl` command.
- **Crash reports**: apport (`/var/crash`), coredumpctl, pstore.
- **Themes**: frameless window with its own title bar ([KD.Avalonia.Rice](https://github.com/k0zi/KD.Avalonia.Rice)) and
  Linux distro inspired themes (Ubuntu, Fedora, Manjaro, openSUSE, Nord, …). Pick one under Settings → Theme (saved);
  the sun/moon button in the title bar switches light/dark for the current session.
- **Languages**: English (default), Hungarian, Finnish and German. Selectable under Settings → Language; takes effect after a restart.
  UI strings live in `Resources/*.resx`, and rule explanations are stored per language in `default-rules.json`.

## Running
```bash
dotnet run --project src/Bobrlog.App      # development
packaging/install.sh                      # build the .deb/.rpm for this distro and install it (apt, dnf or zypper)
packaging/install.sh --uninstall
```
No sudo required: members of the `adm` or `systemd-journal` group can see the full system journal.

## Optional background service
Settings → Background service → Install. `pkexec` will prompt for your password. The service:
- is installed to `/opt/bobrlog/service` as `bobrlog.service` (runs as root, `Nice=-5`, sandboxed with
  `ProtectSystem=strict`, read-only access),
- serves requests on the `/run/bobrlog/bobrlog.sock` socket. Only root and the UIDs listed in
  `/etc/bobrlog/allowed-uids` can connect (verified via SO_PEERCRED),
- can also read root-only sources (pstore, all crash reports). When it is running, the GUI uses it automatically.

It can be uninstalled from the same place.

## Building packages
```bash
packaging/build-packages.sh               # artifacts/packages/bobrlog_<version>-1_amd64.deb and bobrlog-<version>-1.x86_64.rpm
packaging/build-packages.sh deb           # only one format; --skip-tests, --no-bump are also accepted
```
The packages are built with [nfpm](https://nfpm.goreleaser.com/) (downloaded to `artifacts/tools` on first use, no
`dpkg-deb`/`rpmbuild` needed). They install the self-contained app to `/usr/lib/bobrlog`, `/usr/bin/bobrlog`, the menu
entry and icons. The `.deb` targets Debian/Ubuntu, the `.rpm` Fedora/RHEL and openSUSE/SUSE. Upgrading the package also
updates the background service if it is installed; removing the package removes it.

**Versioning** uses [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning): `version.json` holds
`<year>.<month>` (e.g. `26.10`) and the git height becomes the patch number (`26.10.0`, `26.10.1`, …). When a new month
starts, `build-packages.sh` updates `version.json` automatically (or edit it by hand); commit that
change. Builds from branches other than `main` (and `v*` tags) get a `~g<commit>` suffix.

## Project structure
| Project | Contents |
|---|---|
| `Bobrlog.Core` | journalctl JSON parser, rule engine, `BootAnalyzer`, crash readers, socket protocol, installer |
| `Bobrlog.App` | Avalonia 12 GUI (MVVM, CommunityToolkit.Mvvm) |
| `Bobrlog.Service` | Background service (Generic Host + systemd notify, Unix socket) |
| `tests/Bobrlog.Core.Tests` | xUnit tests (`dotnet test`) |

Live follow works by polling rather than `journalctl -f`, so it keeps working even when the user's
inotify limit has been exhausted.
