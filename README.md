# Bobrlog – Eseménynapló Linuxra

Windows Event Viewer-szerű grafikus naplónézegető (C#, .NET 10, Avalonia 12). Lekérdezések írása nélkül,
kategóriánként és súlyosság-ikonokkal mutatja a rendszereseményeket, és megmagyarázza, miért állt le vagy indult újra a gép.

## Funkciók
- **Áttekintés**: kritikus, hiba és figyelmeztetés számok (24 óra / 7 nap), napi bontás 14 napra (kattintható),
  legutóbbi rendszerindítások, leggyakoribb hibaforrások.
- **Rendszerindítások**: minden bootnál megállapítja, hogyan ért véget (szabályos leállítás vagy újraindítás, váratlan leállás,
  alvásból vissza nem tért gép), és felsorolja a valószínű okokat (GPU-reset, lockup, OOM, MCE, I/O hiba, pstore,
  program-összeomlás stb.).
- **Kategóriák**: fagyások és összeomlások, kernel és hardver, szolgáltatások, energiagazdálkodás, biztonság, hálózat,
  alkalmazások, egyéb, valamint az összes esemény. A besorolást a `src/Bobrlog.Core/Rules/default-rules.json` szabályai végzik, a választott nyelvű magyarázattal.
- **Szűrők**: időszak, adott nap vagy boot, minimális súlyosság, keresés (regex), forrás vagy egység; élő követés;
  „journalctl parancs” másolása.
- **Hibajelentések**: apport (`/var/crash`), coredumpctl, pstore.
- **Nyelvek**: angol (alapértelmezett), magyar, finn és német. A Beállítások → Nyelv menüben választható; újraindítás után lép életbe.
  A felület szövegei a `Resources/*.resx` fájlokban, a szabályok magyarázatai nyelvenként a `default-rules.json`-ban vannak.

## Futtatás
```bash
dotnet run --project src/Bobrlog.App      # fejlesztés
packaging/install-user.sh                        # telepítés a saját felhasználónak (menübejegyzéssel)
packaging/install-user.sh --uninstall
```
Sudo nem kell: az `adm` vagy `systemd-journal` csoport tagjai a teljes rendszernaplót látják.

## Opcionális háttérszolgáltatás
Beállítások → Háttérszolgáltatás → Telepítés. A `pkexec` kéri a jelszót. A szolgáltatás:
- `/opt/bobrlog/service`, `bobrlog.service` (root, `Nice=-5`, sandbox: `ProtectSystem=strict`, csak olvasási jogok),
- a `/run/bobrlog/bobrlog.sock` socketen szolgál ki. Csak a root és az `/etc/bobrlog/allowed-uids`
  fájlban felsorolt UID-ok érhetik el (SO_PEERCRED ellenőrzéssel),
- látja a root-only forrásokat is (pstore, minden hibajelentés). Ha fut, a GUI automatikusan ezt használja.

Az eltávolítás ugyanott történik.

## Felépítés
| Projekt | Tartalom |
|---|---|
| `Bobrlog.Core` | journalctl JSON parser, szabálymotor, `BootAnalyzer`, crash-olvasók, socket protokoll, telepítő |
| `Bobrlog.App` | Avalonia 12 GUI (MVVM, CommunityToolkit.Mvvm) |
| `Bobrlog.Service` | Háttérszolgáltatás (Generic Host + systemd notify, Unix socket) |
| `tests/Bobrlog.Core.Tests` | xUnit tesztek (`dotnet test`) |

Az élő követés lekérdezéssel (polling) működik, nem a `journalctl -f`-fel. Így akkor is működik, ha a felhasználó
inotify-kerete elfogyott.
