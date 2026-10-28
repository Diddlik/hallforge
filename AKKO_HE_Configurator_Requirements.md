# HE Configurator – Requirements & Implementation Plan

**Version:** 1.0  
**Stand:** 28. Oktober 2026  
**Status:** Ready for implementation (protocol compatibility to be verified)  
**Arbeitstitel:** HE Configurator  
**Primäres Testgerät:** AKKO MOD007B ISO HE (aus AKKO-Driver-Screenshots identifiziert)  
**Zielplattformen:** Windows 11, Linux Desktop, macOS  
**Technologie:** C# / .NET 10 LTS / Avalonia UI / MVVM / USB HID

> **Kurzfassung:** Eine plattformübergreifende Desktop-Anwendung zur komfortablen Konfiguration von Hall-Effect-Keyboards. Kernnutzen: mehrere Tasten gleichzeitig bearbeiten, komplette Spielprofile speichern, per Klick anwenden und bei Bedarf wiederherstellen – ohne mühsame Einzelkonfiguration im Hersteller-Treiber.
>
> **Technische Unsicherheit:** Das Open-Source-Projekt `echtzeit-solutions/monsgeek-akko-linux` nennt die AKKO MOD007B-HE als kompatibel und dokumentiert das RongYuan-HID-Protokoll. Das garantiert **nicht**, dass die konkret vorhandene **MOD007B ISO HE samt Firmware** denselben vollständigen Befehlssatz nutzt. **Keine Schreiboperation vor erfolgreicher Device-Probe und sicherem Backup.**

---

## 1. Ziele und Nichtziele

### 1.1 Ziele (P0)

- **Niedrige Bedienhürde:** Tastatureinstellungen für WASD, Shift, Strg, Space und freie Tastengruppen in wenigen Aktionen ändern.
- **Direkte Hardwarekommunikation:** USB-HID ohne Abhängigkeit vom AKKO Cloud Driver oder positionsabhängiger Mausautomatisierung.
- **Plattformübergreifend:** Gemeinsame UI/Businesslogik für Windows, Linux und macOS; OS-spezifische Implementierungen nur, wo für HID-Zugriff nötig.
- **Nachvollziehbarkeit:** Read -> Edit/Diff -> Preview -> Apply -> Verify; bestehende Einstellungen sichern und wiederherstellen.
- **Profile:** BF6/REDSEC, Office und leeres Profil; JSON-Import/-Export, Kopieren und Vorlagen.
- **Resilienz:** Unterstützte Geräte erkennen, nicht unterstützte Geräte niemals blind beschreiben.
- **CLI:** Automatisierbares Einlesen, Exportieren, Prüfen und Anwenden von Profilen ohne GUI (gleiche Service-Schicht).

### 1.2 Nichtziele für MVP

- Kein Firmware-Flashen, keine Bootloader-Kommandos, kein Werksreset.
- Kein RGB-Editor, keine Makroverwaltung, keine DKS-, Mod-Tap- oder Toggle-Konfiguration im MVP.
- Kein Eingriff in laufende Spiele, keine Spiel-Hooks oder Anti-Cheat-Umgehung.
- Kein globales, automatisches Umschalten beim Spielstart im MVP.
- Keine Bluetooth-/2,4-GHz-Schreibunterstützung im MVP – zuerst **kabelgebundene USB-Verbindung**.
- Kein Versprechen, dass alle AKKO-Modelle oder insbesondere der **Roar Mode** unterstützt werden.

---

## 2. Zielgruppe und User Stories

**Primäre User Story:** Als BF6-Spieler will ich alle wichtigen Tasten als Gruppe einstellen und ein Profil anwenden, ohne 10–20 Tasten einzeln konfigurieren zu müssen.

| ID | Story | Priorität | Akzeptanzbedingung |
|---|---|---|---|
| US-01 | Gerät anschließen und erkennen | P0 | Hersteller/Produkt-ID, Gerätepfad und erkannte Fähigkeiten sichtbar |
| US-02 | Aktuelle Konfiguration auslesen | P0 | Werte erscheinen für den tatsächlichen Geräteslot/die tatsächliche Layer-Zuordnung; unbekannte Werte bleiben unbekannt |
| US-03 | Alle aktuellen Einstellungen als Backup speichern | P0 | Vollständiges, lesbares Backup aller **unterstützt auslesbaren** Einstellungen inkl. Geräte-Fingerprint |
| US-04 | Mehrere Tasten gemeinsam auswählen | P0 | Mehrfachauswahl, Tastengruppen und „Alle“; Änderungen werden als Diff angezeigt |
| US-05 | Actuation/Release pro Taste oder Gruppe setzen | P0 | Gültige Werte werden angewendet und zurückgelesen |
| US-06 | Rapid Trigger pro Taste/Gruppe setzen | P0 | On/Off, Press- und Release-Sensitivität korrekt gelesen/geschrieben |
| US-07 | Profile speichern, laden, exportieren, importieren | P0 | JSON-Schema versioniert; Profile bleiben nach Neustart erhalten |
| US-08 | BF6-Balanced-Profil anwenden | P0 | Profilvorlage setzt nur explizit definierte Tasten; Rest bleibt unverändert |
| US-09 | Fehlgeschlagene Anwendung erkennen | P0 | Fehler mit betroffenen Tasten; keine falsche Erfolgsmeldung; Wiederherstellung angeboten |
| US-10 | CLI-Profil anwenden | P0 | Dry-run + explizites Apply + Readback unter Windows/Linux/macOS (sofern HID-Zugriff) |
| US-11 | Geräteunterschiede verwalten | P1 | Capability-basierte UI und Modelldefinitionen |
| US-12 | Layer/Onboard-Slots bewusst auswählen | P1 | Präzise Zuordnung, keine Annahmen aus „Layer 2“ im Herstellertreiber |
| US-13 | Roar Mode konfigurieren | P2 | Nur nach Reverse Engineering und Test mit dieser Firmware |
| US-14 | Grafische Echtzeit-Tastenhubanzeige | P2 | Nur bei nachweislich unterstützten Sensor-Events |

---

## 3. Funktionsumfang

### 3.1 Verbindung / Device Discovery

1. HID-Geräte listen und Kandidaten nach Vendor ID, Product ID, Usage Page, Usage und Interface filtern.
2. Kandidaten **ausschließlich lesend** prüfen (Firmware-/Protokollidentifikation; unterstützte GET-Kommandos).
3. Vendor-spezifische **Konfigurationsschnittstelle** öffnen, nicht das normale Tastatur-Input-Interface übernehmen.
4. Verbindungstyp, HID-Reportgrößen, Firmware/Hardwarekennung und Fähigkeiten anzeigen.
5. Gerätemodell und Tastaturlayout **nicht** allein anhand von Produktnamen oder VID/PID annehmen.
6. Abweichende oder unbekannte Geräte bleiben im **Read-only/Unsupported-Modus**.
7. Geräteabziehen, Reconnect, Zeitüberschreitungen und konkurrierenden Hersteller-Treiber sauber behandeln.
8. Nur eine Schreibsitzung je Gerät zulassen; keine parallelen Requests.

**Wichtig:** Die dokumentierten Beispiele `VID 0x3151`, `Usage Page 0xFFFF`, `Usage 0x02` und „Interface 2“ stammen von einer bestimmten RongYuan-Gerätefamilie. Bei der MOD007B-ISO-HE müssen diese Werte **ermittelt und bestätigt** werden; keine fest kodierte, einzige Gerätekennung.

### 3.2 Tastaturvisualisierung

- ISO-DE-Layout als erste Layoutdefinition; logische Key-IDs wie `KeyW`, `LeftShift` usw. strikt von HID-Matrixpositionen trennen.
- Tasten einzeln, durch Ziehen/Mehrfachauswahl oder als Gruppen selektieren.
- Presets für Tastenwahl: `WASD`, `Movement (WASD+Shift+Ctrl+Space)`, `Actions (Q,E,F,R)`, `Number Row 1–5`, `All supported keys`.
- Optische Unterscheidung: aktuelle Werte, ausstehende Änderungen, nicht unterstützte Tasten, nicht verifizierte Werte.
- Eine **separate Matrix-/Layout-Datei** definiert Positionen, Scancodes, Key-IDs und Zuordnung zum USB-Geräteprotokoll; keine hartkodierten Indexannahmen in ViewModels.
- Layer/Slot nur zur Auswahl anbieten, wenn dessen Semantik durch ausgelesene Informationen feststeht; nicht AKKO-UI-Bezeichnungen wie „Layer 2“ unbesehen übernehmen.

### 3.3 Parametereditor

| Feld | Darstellung | Verhalten |
|---|---|---|
| Actuation (Press) | mm, numerisches Feld + Slider | Per-Key-/Gruppenwert, gerätespezifische Grenzen |
| Release (Up) | mm | Getrennt von Rapid-Trigger-Release |
| Rapid Trigger | Toggle | Je Taste oder Gruppe, optional gemischter Zustand |
| RT Press Distance | mm | Nur verfügbar, wenn RT vom Gerät unterstützt |
| RT Release Distance | mm | Nur verfügbar, wenn RT vom Gerät unterstützt |
| Top/Bottom Deadzone | mm | P1, sofern zuverlässig auslesbar/schreibbar |
| Non-stop Rapid Trigger | Toggle | **Nicht** mit RT gleichsetzen; nur bei bestätigter Geräteimplementierung (P1) |
| Roar Mode | Toggle/4-Key-Auswahl | P2, bis Semantik und Protokoll bestätigt sind |

- Gültigkeitsbereich/Schrittweite vom Gerät lesen oder aus validierter Modelldefinition entnehmen; UI-Vorgabewerte sind **keine Hardwarewahrheit**.
- Millimeterwerte als `decimal` im Modell speichern; quantisierte Rohwerte über den Geräteadapter umrechnen (Precision Factor geräteabhängig).
- Widersprüchliche oder nicht darstellbare Zustände blockieren `Apply`.
- Bei Gruppenbearbeitung nicht definierte Parameter **unverändert lassen** (`Patch`-Semantik, niemals stillschweigend überschreiben).

### 3.4 Profile

- Lokal in menschenlesbarem JSON; schemaVersion und Anwendungsversion angeben.
- Keine geheimen Daten in Profilen; Geräte-Fingerprint nur im Backup, nicht notwendig in teilbaren Vorlagen.
- Gruppen und Einzelüberschreibungen; Einzelüberschreibungen haben höhere Priorität.
- Profile werden bei `Apply` gegen tatsächlich gemeldete Fähigkeiten, Layout und Wertebereiche geprüft.
- `Preview changes` zeigt pro Taste: vorher -> nachher + nicht unterstützte Optionen + Anzahl der Änderungen.
- `Apply` verlangt bestätigtes Zielgerät, Zustand und bei erster Anwendung explizite Zustimmung.
- Lokaler Profilwechsel ändert zunächst **nur die App-Vorschau**; erst `Apply` schreibt zur Tastatur.
- Export, Import, Duplicate, Rename, Delete, `Reset draft` und `Restore from backup`.

### 3.5 Schreibablauf (sicherheitskritisch)

**P0-Flow:**

1. Benutzer wählt verbundenes, eindeutig erkanntes Gerät und ein Profil.
2. `GET` der relevanten Werte und Zustand validieren.
3. Vollständiges Backup der unterstützten Konfiguration anlegen, bevor erstmals geschrieben wird.
4. Patch/Diff konstruieren, streng begrenzen und vorab prüfen.
5. Schreibzugriffe **serialisiert** durchführen; Rate/ACK/Timeouts nach verifiziertem Protokoll.
6. Nach jedem sinnvollen Batch Werte per GET zurücklesen und vergleichen.
7. Abweichungen dokumentieren; keine stillen Wiederholungen unbekannter, potenziell nicht idempotenter Befehle.
8. Nach fehlgeschlagenem Apply: Fehler markieren, Verbindung sicher schließen; soweit sicher möglich gezieltes Rollback aus Backup anbieten.
9. Bei Abziehen/Absturz niemals „erfolgreich“ melden; Zustand bei Reconnect erneut lesen.

**Hard Safety Rules:**

- Firmware-/Bootloader-Kommandos sind in der gesamten Anwendung gesperrt (keine UI-/CLI-Wege).
- Alle HID-Schreibkommandos durch Allowlist; keine freie „Raw Write“-Funktion im normalen oder Debug-Betrieb.
- Strikte Bounds-Checks für Indizes, Slots, Längen, Chunk-Nummern und Gesamtpayload; keine unkontrollierten `SET_KEYMATRIX`/`SET_MACRO`/`SET_FN`-Writes.
- Für die dokumentierte RongYuan-Firmware existieren Schwachstellen bei **Chunked SET Commands** und unzureichenden Grenzen. Kritische Kommandos sind im MVP **explizit deaktiviert**; bei späterer Unterstützung ein eigenes Sicherheitsreview durchführen.
- Die Eingabefunktion der Tastatur muss jederzeit erhalten bleiben. Nur Konfigurationsinterface öffnen; keine Treiberinstallation/Zwangsübernahme des Keyboard-Interfaces.
- Keine automatische Werkskalibrierung oder Profilübertragung bei App-Start.
- Anweisungen zur Recovery mit Herstellerdriver in README dokumentieren, aber Recovery nicht ohne Nachweis automatisieren.

---

## 4. Vorgegebene Profile – nur als **Startwerte**, nicht als Herstellervorgaben

**BF6/REDSEC Balanced (Erstkonfiguration):**

| Gruppe/Tasten | Press | Up | RT Enabled | RT Press | RT Release |
|---|---:|---:|---|---:|---:|
| WASD | 0,5 mm | 0,5 mm | Ja | 0,2 mm | 0,3 mm |
| Left Shift | 0,8 mm | 0,8 mm | Optional, standardmäßig aus | — | — |
| Left Ctrl | 0,6 mm | 0,6 mm | Optional, standardmäßig aus | — | — |
| Space | 0,8 mm | 0,8 mm | Optional, standardmäßig aus | — | — |
| Q, E, F, R | 1,0 mm | 1,0 mm | Nein | — | — |
| 1–5 | 1,5 mm | 1,5 mm | Nein | — | — |
| Restliche Tasten | **Unverändert** | **Unverändert** | Unverändert | — | — |

Weitere Vorlagen: `FPS Conservative` (WASD 0,8 mm / RT 0,3-0,4 mm), `FPS Fast` (WASD 0,2 mm / RT 0,1-0,2 mm, **Warnhinweis**), `Office` (nur nach expliziter Benutzerentscheidung; kein blindes Zurücksetzen). Falls bestimmte Parameter/Granularität von der konkreten Firmware nicht unterstützt werden, diese Änderungen nicht anwenden und eine verständliche Meldung ausgeben.

**Wichtig:** RT Press/Release sind **Empfindlichkeits-/Distanzwerte**, nicht identisch mit den absoluten Press-/Up-Positionen. `Non-stop Rapid Trigger` ist ebenfalls getrennt zu modellieren.

### Beispiel: tragbares Profilformat (Illustration, keine fertige Device-Payload)

```json
{
  "schemaVersion": 1,
  "name": "BF6 REDSEC Balanced",
  "target": {
    "deviceFamily": "akko-mod007b-he",
    "layout": "ISO-DE"
  },
  "groups": [
    {
      "keyIds": ["KeyW", "KeyA", "KeyS", "KeyD"],
      "settings": {
        "actuationMm": 0.5,
        "releaseMm": 0.5,
        "rapidTriggerEnabled": true,
        "rapidTriggerPressMm": 0.2,
        "rapidTriggerReleaseMm": 0.3
      }
    },
    {
      "keyIds": ["LeftShift"],
      "settings": { "actuationMm": 0.8, "releaseMm": 0.8 }
    },
    {
      "keyIds": ["LeftCtrl"],
      "settings": { "actuationMm": 0.6, "releaseMm": 0.6 }
    },
    {
      "keyIds": ["Space"],
      "settings": { "actuationMm": 0.8, "releaseMm": 0.8 }
    }
  ]
}
```

**Patch-Semantik:** Ausgelassene Felder / Tasten werden beim Anwenden **nicht** überschrieben. `null`, fehlende Felder und `false` müssen strikt unterschieden werden.

---

## 5. Technologiestack und Architektur

### 5.1 Stack

- **Runtime:** .NET 10 LTS, C# (nullable reference types + analyzers aktiviert).
- **Desktop UI:** Avalonia UI, aktuelle stabile Version zum Start der Implementierung (Versionspinning im Repository).
- **MVVM:** CommunityToolkit.Mvvm.
- **USB HID:** zuerst HidSharp evaluieren (Windows/macOS/Linux); nötigenfalls austauschbares HID-Backend auf Basis von hidapi oder OS-APIs.
- **JSON:** System.Text.Json + versioniertes JSON-Schema.
- **Dependency Injection:** Microsoft.Extensions.Hosting / DependencyInjection.
- **Logging:** Microsoft.Extensions.Logging oder Serilog (Datei und UI-Diagnostik, **keine** privaten Rohreports in Standardlogs).
- **Tests:** xUnit + FluentAssertions oder Built-in Assert, Fake HID Transport, Golden Packet Tests; UI-Tests separat.
- **CI:** GitHub Actions Windows, Ubuntu, macOS; Paket/Release je OS.

### 5.2 Modulstruktur (Vorschlag)

```text
HEConfigurator.sln
src/
  HEConfigurator.App/                 # Avalonia UI, Views, ViewModels
  HEConfigurator.Core/                # Domain Models, capabilities, profiles, validation
  HEConfigurator.Application/         # Services, use cases, apply orchestration
  HEConfigurator.Protocol.RongYuan/   # Reiner Protokollencoder/-decoder
  HEConfigurator.Transport.Hid/       # HID-Discovery / I/O / timeouts / OS differences
  HEConfigurator.Storage/             # Profiles, backup, settings, migrations
  HEConfigurator.Cli/                 # System.CommandLine or equivalent
  HEConfigurator.DeviceModels/        # Model IDs, keymap, ISO layout files
  HEConfigurator.Simulator/           # In-memory keyboard model, optional MVP utility
tests/
  HEConfigurator.Core.Tests/
  HEConfigurator.Protocol.Tests/
  HEConfigurator.Application.Tests/
  HEConfigurator.Integration.Tests/
  HEConfigurator.App.Tests/
docs/
  protocol-research.md
  hardware-compatibility.md
  recovery.md
  screenshots/
profiles/
  bf6-balanced.json
  fps-conservative.json
  fps-fast.json
.github/workflows/
```

### 5.3 Abstraktionen (Beispielinterfaces, keine Implementierung)

```csharp
public interface IKeyboardDiscoveryService
{
    Task<IReadOnlyList<KeyboardCandidate>> DiscoverAsync(CancellationToken ct);
}

public interface IKeyboardDevice : IAsyncDisposable
{
    KeyboardIdentity Identity { get; }
    KeyboardCapabilities Capabilities { get; }
    Task<KeyboardSnapshot> ReadAsync(CancellationToken ct);
    Task<ApplyResult> ApplyPatchAsync(KeyboardPatch patch, CancellationToken ct);
}

public interface IProfileRepository
{
    Task<IReadOnlyList<KeyboardProfile>> ListAsync(CancellationToken ct);
    Task<KeyboardProfile> LoadAsync(string name, CancellationToken ct);
    Task SaveAsync(KeyboardProfile profile, CancellationToken ct);
}
```

**Trennung:** UI und CLI sprechen ausschließlich mit `Application`-Services; `Protocol` kennt weder Avalonia noch Dateipfade; `Transport` kennt keine BF6-Profile. Das erleichtert neue Geräte und Tests.

### 5.4 Features / Capabilities

`KeyboardCapabilities` meldet **pro Gerät und Firmware** (nicht pauschal):

- Read actuation, write actuation
- Read release, write release
- Read RT sensitivity, write RT sensitivity
- Read/write RT mode flag
- Number of device slots/layers + bekannte Semantik
- Top/bottom deadzone
- Non-stop RT
- Roar Mode
- Analog key depth notifications
- Calibration support (P2, explizit)

UI-Controls sind deaktiviert bzw. ausgeblendet, wenn `capabilities` eine Funktion nicht verifiziert. Unbekannt bedeutet **nicht unterstützt für Schreiboperationen**.

---

## 6. HID-Protokoll – Forschungs- und Sicherheitsplan

### 6.1 Referenz

Das Projekt `echtzeit-solutions/monsgeek-akko-linux` beschreibt Gerätefamilien mit RongYuan RY5088/YC3121 und ein herstellerdefiniertes HID-Konfigurationsinterface. Die öffentliche `docs/PROTOCOL.md` dokumentiert u. a. `SET/GET_MULTI_MAGNETISM` für Actuation/Release/RT und eine Geräte-Datenbank.

**Links:**

- https://github.com/echtzeit-solutions/monsgeek-akko-linux
- https://github.com/echtzeit-solutions/monsgeek-akko-linux/blob/master/docs/PROTOCOL.md
- https://github.com/echtzeit-solutions/monsgeek-akko-linux/blob/master/SETUP.md
- https://docs.avaloniaui.net/docs/supported-platforms
- https://www.nuget.org/packages/HidSharp
- https://dotnet.microsoft.com/en-us/platform/support/policy

**Lizenzhinweis:** Das Referenzprojekt ist **GPL-3.0**. Beim Übernehmen/Portieren von Quellcode müssen dessen Lizenzpflichten geprüft und eingehalten werden. Für eine anders lizenzierte eigene App sind unabhängige Implementierung auf Basis dokumentierter Fakten und eine rechtliche Prüfung sinnvoll; keine ungeprüfte Übernahme von Quellcode.

### 6.2 P0 Forschungsaufgaben – vor dem ersten Geräte-Write

1. **Hardware-Inventory:** verbundene HID-Devices, VID/PID, Interface, Usage, Report Sizes, Serien-/Firmwarekennung (sofern verfügbar), Windows-Geräteinstanz lesen.
2. **Transport-Schnittstelle validieren:** Konfigurationsinterface mit ausschließlich lesenden Operationen identifizieren; normale Tastatureingaben dürfen nicht blockiert werden.
3. **Protocol Fingerprint:** bekannte ungefährliche GET-Kommandos testen, Response-Format/Checksum validieren; unbekannt = Stopp.
4. **Precision/Travel-Range:** aus dem Device-Response/firmwarespezifischen Modell sauber ableiten und gegen AKKO-UI-Readings prüfen.
5. **Keymap:** ISO-DE-Hardwarematrix mit nichtdestruktiven Informationen und Testfällen verifizieren; besondere Tasten, Encoder und Fn nicht vermischen.
6. **Profile/Slot-Semantik:** klären, welche Onboard-Slots, Layer, Fn-Layer und Config-Kanäle die Hardware hat. **„Layer 2“ im Screenshot ist keine verbindliche Protokollkennung.**
7. **Sicherheitsreview:** jede SET-Operation nur mit dokumentierter Payload, Limits, Checksum und erlaubtem Bereich freigeben.
8. **Proof of Concept:** zunächst EINEN ungefährlichen konfigurierbaren Wert EINER Nicht-Primärtaste ändern, sofort zurücklesen, Backup wiederherstellen; erst dann WASD erlauben.
9. **Cross-platform Smoke-Test:** Enumeration, GET, Backup und Einzelwert-Write für Windows/Linux/macOS separat bestätigen; noch nicht getestete OS als „experimental“ markieren.

### 6.3 Mögliche Protokolloperationen (nur Referenz, nicht blind übernehmen)

- Per-Key-Magnetism: In der Referenzdokumentation `GET_MULTI_MAGNETISM`/`SET_MULTI_MAGNETISM` (0xE5/0x65) mit Subcommands wie Press Travel, Lift Travel, RT Press, RT Lift, KEY_MODE, Top/Bottom Deadzone.
- Antwort-/Checksum-Handling: Implementierung anhand der tatsächlich identifizierten Geräte-/Firmwareversion und golden protocol tests.
- HID-Feature-Reports: In einer dokumentierten Gerätefamilie 64-Byte-Report (in manchen HID-Bibliotheken inkl. Report-ID-Byte 65 Byte API-Puffer); Layout muss anhand der realen Descriptoren bestätigt werden.
- **Nicht für MVP:** SET_KEYMATRIX, SET_MACRO, SET_FN oder jegliche Firmware-Kommandos – selbst wenn Referenzprotokoll sie dokumentiert.

### 6.4 Rechte und Parallelbetrieb

- **Windows:** HID-Konfigurationsinterface ohne Treiberersatz öffnen; eventuell sperrt der AKKO Cloud Driver die Schnittstelle – verständlich melden, nicht zwangsläufig Prozesse beenden.
- **Linux:** schlanke udev-Regeln mit geeigneter Gruppe bzw. `TAG+="uaccess"`; **keine pauschalen MODE=0666-Regeln** für alle USB-/HID-Geräte. Installationsskript + Test.
- **macOS:** HIDManager/Backend-Zugriff testen; OS-spezifische Berechtigungs- und signierte Paketanforderungen dokumentieren.
- Kein Device-Open auf Verdacht, kein `sudo`-Zwang als Standardworkflow.

---

## 7. UX-Entwurf (Avalonia)

```text
┌────────────────────────────────────────────────────────────────────────┐
│ HE Configurator    [MOD007B HE · USB · Connected] [Refresh] [Settings]│
├───────────────┬────────────────────────────────────────────────────────┤
│ Profiles      │ [Keyboard ISO-DE - clickable, multi-select]           │
│ • BF6 Balanced│                                                        │
│ • FPS Safe    │ W A S D  selected (4)                                  │
│ • FPS Fast    │                                                        │
│ • Office      ├────────────────────────────────────────────────────────┤
│ + New Profile │ Editing group: WASD                                   │
│               │ Press  [0.5 mm]   Release [0.5 mm]                     │
│ Backups       │ Rapid Trigger [ON]    RT Press [0.2]  RT Release [0.3] │
│ • Snapshot…   │                                                        │
├───────────────┴────────────────────────────────────────────────────────┤
│ Pending changes: 4 keys / 5 parameters each  [Preview]  [Apply]      │
│ Status: Read successful · Backup: saved · Writes: not yet applied     │
└────────────────────────────────────────────────────────────────────────┘
```

**Designregeln:**

- Klarer Unterschied zwischen `Current Device Settings`, `Draft Profile` und `Pending Changes`.
- Vor einem Write immer Diff + Zielgerät anzeigen.
- Bei mehreren ausgewählten Tasten mit unterschiedlichen Ist-Werten „Mixed“ statt eines falschen Einzelwerts anzeigen.
- Statusanzeige `Connected / Read-only / Applying / Verified / Partial failure / Unsupported`.
- Tastaturansicht kann skaliert werden; Touch/HiDPI/Theme-Unterstützung.
- Jeder Fehler mit technischen Details und anwenderfreundlicher Handlungsempfehlung; kopierbarer Diagnostikbericht.

---

## 8. CLI-Spezifikation (MVP)

CLI-Name vorläufig: `heconfig`.

```bash
heconfig devices list
heconfig devices probe --device <device-id>
heconfig read --device <device-id> --output current.json
heconfig backup create --device <device-id> --output backup.json
heconfig profiles validate ./profiles/bf6-balanced.json --device <device-id>
heconfig profiles diff ./profiles/bf6-balanced.json --device <device-id>
heconfig profiles apply ./profiles/bf6-balanced.json --device <device-id> --dry-run
heconfig profiles apply ./profiles/bf6-balanced.json --device <device-id> --yes
heconfig backup restore backup.json --device <device-id> --dry-run
heconfig backup restore backup.json --device <device-id> --yes
```

- `--device` ist bei Schreiboperationen Pflicht; Auswahl nicht automatisch auf das „erste“ Gerät legen.
- Ohne `--yes` ist die CLI standardmäßig interaktiv und fragt nach Bestätigung; `--dry-run` führt **keine** SET-Operationen aus.
- Bei fehlender Berechtigung, inkompatibler Firmware oder fehlendem Backup mit eindeutigen Exit Codes abbrechen.
- `read` und `backup` dürfen im Normalfall ohne besondere Bestätigung funktionieren.
- CLI-Profile werden exakt vom gleichen Validation-/Apply-Code verarbeitet wie GUI-Profile.

---

## 9. Datenhaltung, Fehler und Telemetrie

- Plattformgerechte Konfigurationsverzeichnisse (`LocalApplicationData` bzw. XDG/macOS-Konventionen).
- Geräte-Backups mit `createdAtUtc`, Model/Firmware/VID/PID, eindeutiger nicht-sensitiver Fingerprint, Formatversion und hash; reimportierbar.
- Ein Backup enthält nur Daten, die **tatsächlich** gelesen wurden. Unvollständig = deutlich gekennzeichnet, kein blindes Restoring von unbekannten Feldern.
- JSON-Validierung und Migration bei Schemawechseln; fehlerhafte Profile bleiben unverändert.
- Crash-safe File Writes: temp file + atomic replace soweit OS unterstützt.
- Lokale Logs standardmäßig ohne Roh-Report-Dumps oder Tasteneingabehistorie; Opt-in Diagnosemodus mit Redaction.
- Keine Cloud-Abhängigkeit, keine Konten, keine Telemetrie für MVP.
- Bei Write-Fehlern nachvollziehbarer Audit-Eintrag, betroffene Tasten + Status des Readbacks.

---

## 10. Qualität, Tests und Definition of Done

### 10.1 Automatisierte Tests

- Unit: Range-Checks, Partial Patch Semantik, Gruppenauflösung, ISO Key-ID-Mapping, quantisierte mm-Konvertierung.
- Unit: Packet-Encoder/Decoder, Checksum, Antwortzuordnung, Zeitüberschreitungen, unerwartete Report-ID.
- Unit: Bounds/Fuzz-Tests, unzulässige Indizes, oversized payloads, chunked commands (gesperrt).
- Integration: Simulator/Fake HID (`GET` -> Backup -> Patch -> Verify, Abziehen, Timeout, stale response).
- Regression: Golden Packet Fixtures nur für zuvor verifizierte Firmware; beim Firmwarewechsel inkompatibel behandeln.
- UI: Ausgewählte Tasten, mixed-state, Apply-Preview, Fehler, kein automatischer Apply bei Profilwechsel.
- Builds/Tests in CI auf Windows, Linux und macOS.

### 10.2 Manuelle Hardwaretests

- Test 1: Verbinden, USB-ID prüfen, nur lesen, keine Änderung im Herstellertreiber.
- Test 2: Originaleinstellungen vollständig lesen, Backup exportieren und gegen Hersteller-UI vergleichen.
- Test 3: Ein einzelner ungefährlicher Parameter an einer Nicht-Primärtaste, Write + Readback, dann Restore.
- Test 4: WASD-Gruppenwrite aus BF6-Profil, Readback, Gameplay-Validierung.
- Test 5: USB während Read/Write entfernen und danach erneute Zustandsabfrage/Recovery.
- Test 6: Herstellerdriver parallel offen: Exklusivitätsfehler erkannt und sicher behandelt.
- Test 7: App-Neustart und Profile/Backups vorhanden.
- Test 8: Cross-OS: USB-Erkennung, Lesezugriff, Write/Readback je OS dokumentiert.

### 10.3 MVP-Abnahmekriterien

- [ ] Projekt startet als Avalonia-Desktop-App auf Windows, Linux, macOS (Build/Launch bestätigt).
- [ ] MOD007B ISO HE wird auf mindestens Windows anhand echter USB-Descriptoren eindeutig erkannt.
- [ ] Aktuelle Werte für unterstützte Schlüsselparameter können gelesen werden.
- [ ] Backup vor dem ersten Write wurde angelegt und ist exportierbar.
- [ ] Multi-Key Editor funktioniert ohne einzeln jede Taste auszuwählen.
- [ ] BF6-Profil kann per Dry-run geprüft und nach Bestätigung angewendet werden.
- [ ] Write wird durch Readback verifiziert; Teilfehler werden sichtbar.
- [ ] JSON-Profilimport/-export funktioniert; nicht definierte Tasten bleiben unverändert.
- [ ] CLI nutzt denselben Use-Case-Layer wie die UI.
- [ ] Unsupported Device / Firmware = kein Write.
- [ ] Kritische Firmware-/Bootloader-/Keymatrix-/Macro-Befehle sind nicht zugänglich.
- [ ] README enthält Setup, Linux-Rechte, Sicherheit, bekannte Modell-/OS-Limits und Recovery.

**Hinweis:** „Multi-Plattform“ bedeutet zunächst plattformübergreifende **Anwendung/Builds**; Schreibfähigkeit auf einem OS wird erst nach Hardwaretests als offiziell unterstützt markiert. Windows ist das erste reale Testsystem; Linux/macOS folgen mit denselben Funktionen, sofern Geräteprotokoll und OS-Berechtigungen dies erlauben.

---

## 11. Implementierungsreihenfolge / Agent-Arbeitsaufträge

### Phase 0 – Repository + Hardware Research (unbedingt zuerst)

**Deliverable:** Kompilierende Solution, `docs/protocol-research.md`, `docs/hardware-compatibility.md`, read-only CLI `devices list/probe`, Gerätedaten aus echtem Device.

- Avalonia/.NET 10 Solution mit DI, MVVM, Logging, Tests, GitHub Actions aufsetzen.
- HIDSharp-Technikspike; Geräte enumerieren und Schnittstellen unterscheiden.
- Referenzdoku nachlesen und **vor Schreibcode** die Modell-/Firmwarekompatibilität bestimmen.
- Mock-Device mit reproduzierbaren Fixtures bauen.
- Kein Protokollwissen erfinden, unbekannte Werte offen dokumentieren.

### Phase 1 – Read-only Konfiguration / Backup

**Deliverable:** CLI `read`, `backup create`; Avalonia-Geräteübersicht; read-only Keyboard View.

- Protocol-Parser mit verifizierten GET-Kommandos.
- ISO-Keymap inkl. Hardwarematrix und Slot-Semantik.
- Originalzustand sichern und gegen Herstellerdriver prüfen.
- Golden-Tests für die tatsächlich gelesenen Reports.

### Phase 2 – Safe Writes / MVP Value

**Deliverable:** Einzeletaste und Gruppe schreiben, Diff, Apply, Verify und Restore, inklusive BF6-Preset.

- Allowlisted SET-Magnetism-Operationen implementieren.
- Erst Einzelwert außerhalb von WASD, dann Gruppeneinstellung.
- Fehler-/Recovery-Pfade testen; CLI- und GUI-Profil-Apply implementieren.
- Status-/Diagnostik-UI, Dry-run und Confirmation.

### Phase 3 – Multiplatform Verification

**Deliverable:** Releases Windows x64, Linux x64, macOS arm64/x64 (Release-Matrix nach Testlage kennzeichnen).

- Linux udev installer + Rechteprüfung.
- macOS HID Permissions und Packaging validieren.
- OS-spezifische CI-/Hardware-Testberichte und bekannte Einschränkungen.

### Meilensteine nach dem MVP (vom Owner festgelegt, 2026-10-28)

- **LED setup:** Beleuchtung (Modi, Farben, Helligkeit) für unterstützte Geräte konfigurieren. Hebt den MVP-Ausschluss „kein RGB-Editor“ für diesen Meilenstein auf.
- **Keyboard layout in the app:** Grafische, klickbare Tastaturansicht in der GUI mit Mehrfachauswahl und Trigger-Bearbeitung pro Taste.

### Phase 4 – Erweiterungen (nach MVP)

- Mehr Modelle und Tastaturlayouts (zusätzliche Product IDs, ISO/ANSI, Layout-Mapping).
- Weitere Parameter (Deadzone, Non-stop RT), falls bestätigt.
- Roar Mode erst nach dokumentiertem Protokoll-/Verhaltenstest.
- Per-App-Profilzuweisung **optional** und ausschließlich nach explizitem Opt-in; niemals unterbrechendes automatisches Schreiben im Spiel.
- Sensor-Live-View, RGB und weitere Features nur nach eigenem Scope-Review.

---

## 12. Erster Prompt für den Coding-Agent

> Implementiere Phase 0 aus `AKKO_HE_Configurator_Requirements.md`. Verwende .NET 10 LTS, Avalonia UI und CommunityToolkit.Mvvm. Lege die Solution-/Projektstruktur an, implementiere ein testbares `IKeyboardDiscoveryService`, zunächst ein read-only `HidSharp`-Backend und eine CLI mit `devices list` und `devices probe`. Zeige HID-VID/PID/Usage/Interface/Reportgrößen an und speichere einen anonymisierten Diagnosebericht. Nutze das Referenzprojekt `echtzeit-solutions/monsgeek-akko-linux` und dessen `docs/PROTOCOL.md` als Recherchequelle, aber übernehme nicht ungeprüft dessen Quellcode; prüfe GPL-3.0-Pflichten. **Keinerlei SET-/Firmware-Kommandos in Phase 0.** Erstelle Tests mit Fake-HID, GitHub-Actions-Builds für Windows/Linux/macOS und dokumentiere offene Fragen für die echte AKKO MOD007B ISO HE. Liefere kompilierbaren Code mit README und klaren manuellen Tests; erfinde keine USB- oder Firmwarekennung.

---

## 13. Offene technische Fragen (nicht blockierend für Phase 0)

1. Welche tatsächlichen VID/PID, USB-Interfaces, Report-Descriptoren und Firmwareversion hat die konkrete MOD007B ISO HE?
2. Nutzt diese Firmware exakt das dokumentierte RongYuan-Protokoll – und wie ist die mm-Präzision kodiert?
3. Sind „Layer 2“ und die Profilslots im AKKO Cloud Driver im Protokoll eindeutig zuordenbar?
4. Wie unterscheiden sich `Rapid Trigger` und `Non-stop Rapid Trigger` **in diesem Modell**?
5. Ist „Roar Mode“ ein persistenter Hardwarezustand oder eine Kombination anderer Einstellungen?
6. Ist paralleler HID-Zugriff bei geöffnetem AKKO-Treiber möglich?
7. Wie zuverlässig sind Restore und Schreibbestätigung bei USB-Trennung?
8. Welche HID-Berechtigungen und Gerätehandles funktionieren auf den konkret getesteten macOS-/Linux-Versionen?

**Entscheidungsregel:** Wenn eine Frage nicht sicher beantwortet ist, wird das entsprechende Feature als nicht unterstützt gekennzeichnet; niemals raten und an das Gerät senden.
