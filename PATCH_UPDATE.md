# PATCH_UPDATE.md

# Content Tracker – Patch / Dalamud Update Checklist

Die Patch-Kompatibilität ist ab v0.4.3 zentralisiert.

## Normaler FFXIV-Patch

Wenn Dalamud nach dem Patch wieder freigegeben wird:

1. FFXIV starten.
2. Content Tracker laden.
3. Kurz prüfen:
   - `/ctt`
   - Duty Start / Ende
   - Wipe
   - Gil Session
   - Party-Spieler
   - Excel-Export
4. Wenn alles funktioniert, ist kein neues Release nötig.

## Dalamud API wechselt, z. B. API 15 -> API 16

In `ContentTracker.csproj` liegen die beiden Werte direkt zusammen:

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.0">

<DalamudApiLevel>15</DalamudApiLevel>
```

Bei API 16 wird daraus beispielsweise:

```xml
<Project Sdk="Dalamud.NET.Sdk/16.0.0">

<DalamudApiLevel>16</DalamudApiLevel>
```

Danach:

```powershell
dotnet restore
dotnet build -c Debug
```

Fehler beheben, Plugin im Spiel testen, committen und eine neue Version taggen.

Der GitHub-Workflow übernimmt danach automatisch:
- Release-Build
- Release-ZIP
- `pluginmaster.json`
- `DalamudApiLevel` im Third-Party-Repo

## Datenbankversion

`DataSchemaVersion` nur erhöhen, wenn sich `tracker-data.json` strukturell ändert.

```xml
<DataSchemaVersion>4</DataSchemaVersion>
```

Bestehende Nutzerdaten niemals allein wegen eines FFXIV-/Dalamud-Patches löschen.

## Im Plugin sichtbar

Unter `Einstellungen / Export -> Kompatibilität` zeigt Content Tracker:
- Pluginversion
- Dalamud API
- Datenbankversion
- Target Framework

Damit kann nach einem Patch schnell kontrolliert werden, welche Build-Version gerade geladen ist.
