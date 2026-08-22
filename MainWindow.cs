using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;

namespace ContentTracker;

public sealed class MainWindow : Window
{
    private readonly Configuration config;
    private readonly TrackerStore store;
    private readonly DutyTracker tracker;
    private readonly SessionGilTracker sessionGilTracker;
    private readonly Func<string> exportNow;
    private string status = "Bereit";
    private ulong selectedCharacterId;
    private string contentFilter = string.Empty;
    private bool onlyClears;
    private string exportDirectoryEdit;

    public MainWindow(
        Configuration config,
        TrackerStore store,
        DutyTracker tracker,
        SessionGilTracker sessionGilTracker,
        Func<string> exportNow)
        : base("FFXIV Content Tracker###ContentTrackerMain")
    {
        this.config = config;
        this.store = store;
        this.tracker = tracker;
        this.sessionGilTracker = sessionGilTracker;
        this.exportNow = exportNow;
        exportDirectoryEdit = config.ExportDirectory;
        Size = new Vector2(900, 650);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawActiveRun();
        DrawFilters();

        var filtered = GetFilteredRuns().ToList();

        if (ImGui.BeginTabBar("ContentTrackerTabs"))
        {
            if (ImGui.BeginTabItem("Übersicht"))
            {
                DrawOverview(filtered);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Runs"))
            {
                DrawRuns(filtered);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Spieler"))
            {
                DrawPlayers(filtered);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Einstellungen / Export"))
            {
                DrawSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    public void SetStatus(string text) => status = text;

    private void DrawActiveRun()
    {
        if (!tracker.HasActiveRun || !tracker.ActiveStartedUtc.HasValue)
            return;

        var elapsed = DateTime.UtcNow - tracker.ActiveStartedUtc.Value;

        ImGui.TextUnformatted($"AKTIVER INHALT: {tracker.ActiveContentName}");
        ImGui.TextUnformatted(
            $"Zeit: {FormatDuration((long)elapsed.TotalSeconds)}   " +
            $"Mitspieler: {tracker.ActivePlayerCount}   " +
            $"Wipes: {tracker.ActiveWipeCount}");

        ImGui.TextUnformatted(
            tracker.ActiveDutyStarted
                ? "Status: Duty gestartet"
                : "Status: Instanz betreten / wartet auf Duty Start");

        ImGui.Separator();
    }

    private void DrawFilters()
    {
        var selectedLabel = "Alle Charaktere";

        if (selectedCharacterId != 0)
        {
            selectedLabel = store.Data.Characters
                .FirstOrDefault(x => x.ContentId == selectedCharacterId)?
                .DisplayName ?? "Alle Charaktere";
        }

        ImGui.SetNextItemWidth(360);

        if (ImGui.BeginCombo("##CharacterFilter", selectedLabel))
        {
            if (ImGui.Selectable("Alle Charaktere", selectedCharacterId == 0))
                selectedCharacterId = 0;

            foreach (var character in store.Data.Characters.OrderBy(
                         x => x.DisplayName,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (ImGui.Selectable(
                        character.DisplayName,
                        selectedCharacterId == character.ContentId))
                {
                    selectedCharacterId = character.ContentId;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("Charakter");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint(
            "##ContentFilter",
            "Inhalt suchen (DE/EN)",
            ref contentFilter,
            128);

        ImGui.SameLine();
        ImGui.Checkbox("Nur Clears", ref onlyClears);

        ImGui.SameLine();

        if (ImGui.Button("Filter zurücksetzen"))
        {
            selectedCharacterId = 0;
            contentFilter = string.Empty;
            onlyClears = false;
        }

        var filteredCount = GetFilteredRuns().Count();
        var totalCount = store.Data.Runs.Count;

        ImGui.TextUnformatted($"Angezeigt: {filteredCount} von {totalCount} Runs");
        ImGui.Separator();
    }

    private void DrawOverview(IReadOnlyList<DutyRunRecord> runs)
    {
        var totalSeconds = runs.Sum(x => x.DurationSeconds);
        var totalGil = runs
            .Where(x => x.GilDelta.HasValue)
            .Sum(x => x.GilDelta!.Value);

        var uniqueContents = runs
            .Select(x => x.ContentFinderConditionId)
            .Where(x => x != 0)
            .Distinct()
            .Count();

        var uniquePlayers = runs
            .SelectMany(x => x.Players ?? new())
            .Select(CreatePlayerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        ImGui.TextUnformatted(
            $"Runs: {runs.Count}   " +
            $"Clears: {runs.Count(x => x.Completed)}   " +
            $"Wipes: {runs.Sum(x => x.WipeCount)}");

        ImGui.TextUnformatted(
            $"Gesamtzeit: {FormatDuration(totalSeconds)}   " +
            $"Gil +/-: {totalGil:+#,##0;-#,##0;0}");

        DrawSessionGil();

        ImGui.TextUnformatted(
            $"Inhalte: {uniqueContents}   " +
            $"verschiedene Mitspieler: {uniquePlayers}");

        if (runs.Count > 0)
        {
            var lastRun = runs.MaxBy(x => x.EndedUtc);

            if (lastRun != null)
            {
                var contentName = string.IsNullOrWhiteSpace(lastRun.ContentNameGerman)
                    ? lastRun.ContentNameEnglish
                    : lastRun.ContentNameGerman;

                ImGui.TextUnformatted(
                    $"Letzter Run: {contentName} – " +
                    $"{lastRun.EndedUtc.ToLocalTime():dd.MM.yyyy HH:mm}");
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Häufigste Inhalte");

        if (ImGui.BeginTable(
                "contentSummary",
                5,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable,
                new Vector2(0, 250)))
        {
            SetupColumns("Inhalt", "Runs", "Clears", "Zeit", "Gil +/-");

            foreach (var g in runs
                         .GroupBy(x => new
                         {
                             x.ContentFinderConditionId,
                             x.ContentNameGerman,
                             x.ContentNameEnglish
                         })
                         .OrderByDescending(x => x.Count())
                         .ThenBy(x => x.Key.ContentNameGerman)
                         .Take(50))
            {
                ImGui.TableNextRow();

                Cell(
                    string.IsNullOrWhiteSpace(g.Key.ContentNameGerman)
                        ? g.Key.ContentNameEnglish
                        : g.Key.ContentNameGerman);

                Cell(g.Count().ToString());
                Cell(g.Count(x => x.Completed).ToString());
                Cell(FormatDuration(g.Sum(x => x.DurationSeconds)));

                Cell(
                    g.Where(x => x.GilDelta.HasValue)
                        .Sum(x => x.GilDelta!.Value)
                        .ToString("+#,##0;-#,##0;0"));
            }

            ImGui.EndTable();
        }
    }

    private void DrawSessionGil()
    {
        if (!sessionGilTracker.IsActive)
        {
            ImGui.TextUnformatted("Session Gil +/-: -");
            return;
        }

        var delta = sessionGilTracker.GilDelta?.ToString("+#,##0;-#,##0;0") ?? "?";
        var start = sessionGilTracker.StartGil?.ToString("#,##0") ?? "?";
        var current = sessionGilTracker.CurrentGil?.ToString("#,##0") ?? "?";

        var duration = sessionGilTracker.StartedUtc.HasValue
            ? DateTime.UtcNow - sessionGilTracker.StartedUtc.Value
            : TimeSpan.Zero;

        ImGui.TextUnformatted(
            $"Session Gil +/-: {delta}   " +
            $"Start: {start}   Aktuell: {current}   " +
            $"Session: {FormatDuration((long)duration.TotalSeconds)}");
    }

    private void DrawRuns(IReadOnlyList<DutyRunRecord> runs)
    {
        if (ImGui.BeginTable(
                "runs",
                8,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable,
                new Vector2(0, 470)))
        {
            SetupColumns(
                "Datum",
                "Charakter",
                "Inhalt",
                "Dauer",
                "Gil",
                "Clear",
                "Wipes",
                "Spieler");

            foreach (var run in runs.OrderByDescending(x => x.StartedUtc))
            {
                ImGui.TableNextRow();
                Cell(run.StartedUtc.ToLocalTime().ToString("dd.MM.yy HH:mm"));
                Cell(run.CharacterDisplayName);

                Cell(
                    string.IsNullOrWhiteSpace(run.ContentNameGerman)
                        ? run.ContentNameEnglish
                        : run.ContentNameGerman);

                Cell(FormatDuration(run.DurationSeconds));
                Cell(run.GilDelta?.ToString("+#,##0;-#,##0;0") ?? "?");
                Cell(run.Completed ? "Ja" : "Nein");
                Cell(run.WipeCount.ToString());
                Cell((run.Players?.Count ?? 0).ToString());
            }

            ImGui.EndTable();
        }
    }

    private void DrawPlayers(IReadOnlyList<DutyRunRecord> runs)
    {
        var players = runs
            .SelectMany(r => (r.Players ?? new()).Select(p => new
            {
                Run = r,
                Player = p
            }))
            .GroupBy(
                x => CreatePlayerKey(x.Player),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Player = g
                    .OrderByDescending(x => x.Player.LastSeenUtc)
                    .First()
                    .Player,

                Runs = g
                    .Select(x => x.Run.Id)
                    .Distinct()
                    .Count(),

                Clears = g
                    .Where(x => x.Run.Completed)
                    .Select(x => x.Run.Id)
                    .Distinct()
                    .Count(),

                Seconds = g
                    .GroupBy(x => x.Run.Id)
                    .Sum(x => x.First().Run.DurationSeconds),

                LastSeen = g.Max(x => x.Player.LastSeenUtc)
            })
            .OrderByDescending(x => x.Runs)
            .ThenBy(
                x => x.Player.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        ImGui.TextUnformatted(
            $"Verschiedene Mitspieler im aktuellen Filter: {players.Count}");

        if (ImGui.BeginTable(
                "players",
                5,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.Borders |
                ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.Resizable,
                new Vector2(0, 450)))
        {
            SetupColumns(
                "Spieler",
                "Runs",
                "Clears",
                "Gemeinsame Runzeit",
                "Zuletzt gesehen");

            foreach (var p in players)
            {
                ImGui.TableNextRow();
                Cell(p.Player.DisplayName);
                Cell(p.Runs.ToString());
                Cell(p.Clears.ToString());
                Cell(FormatDuration(p.Seconds));
                Cell(p.LastSeen.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));
            }

            ImGui.EndTable();
        }
    }

    private void DrawSettings()
    {
        var logout = config.ExportOnLogout;

        if (ImGui.Checkbox(
                "Beim Logout automatisch exportieren",
                ref logout))
        {
            config.ExportOnLogout = logout;
            config.Save();
        }

        var dispose = config.ExportOnPluginDispose;

        if (ImGui.Checkbox(
                "Beim Entladen/Beenden des Plugins exportieren",
                ref dispose))
        {
            config.ExportOnPluginDispose = dispose;
            config.Save();
        }

        var afterDuty = config.ExportAfterDuty;

        if (ImGui.Checkbox(
                "Nach jedem beendeten Inhalt exportieren",
                ref afterDuty))
        {
            config.ExportAfterDuty = afterDuty;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Exportordner");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(
            "##ExportDirectory",
            ref exportDirectoryEdit,
            512);

        if (ImGui.Button("Pfad speichern"))
        {
            config.ExportDirectory = exportDirectoryEdit.Trim();
            config.Save();
            status = "Exportpfad gespeichert.";
        }

        ImGui.SameLine();

        if (ImGui.Button("Jetzt exportieren"))
        {
            try
            {
                var path = exportNow();

                var fallback = !string.Equals(
                    Path.GetFileName(path),
                    "FFXIV_Content_Tracker.xlsx",
                    StringComparison.OrdinalIgnoreCase);

                status = fallback
                    ? $"Hauptdatei war gesperrt. Ausweichdatei erstellt: {path}"
                    : $"Exportiert: {path}";
            }
            catch (Exception ex)
            {
                status = $"Exportfehler: {ex.Message}";
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Ordner öffnen"))
        {
            try
            {
                Directory.CreateDirectory(config.ExportDirectory);

                Process.Start(new ProcessStartInfo(config.ExportDirectory)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                status = $"Ordner konnte nicht geöffnet werden: {ex.Message}";
            }
        }

        ImGui.Spacing();
        ImGui.TextWrapped(status);
        ImGui.Separator();

        ImGui.TextUnformatted(
            $"Gespeicherte Runs: {store.Data.Runs.Count}   " +
            $"Charaktere: {store.Data.Characters.Count}");

        ImGui.TextWrapped($"Interne Datendatei: {store.DataFilePath}");

        ImGui.TextWrapped(
            "Hinweis: Die JSON-Datei ist die dauerhafte Datenquelle. " +
            "Die Excel-Datei wird daraus jederzeit neu erzeugt.");

        ImGui.TextWrapped(
            "Ist die normale Excel-Datei beim Export geöffnet/gesperrt, " +
            "wird automatisch eine Zeitstempel-Ausweichdatei erzeugt.");

        ImGui.TextWrapped(
            "Pluginfenster öffnen: /ctt");

        ImGui.Separator();
        ImGui.TextUnformatted("Kompatibilität");
        ImGui.TextWrapped(
            $"Content Tracker v{BuildInfo.PluginVersion}   |   " +
            $"Dalamud API {BuildInfo.DalamudApiLevel}   |   " +
            $"Datenbank {BuildInfo.DataSchemaVersion}   |   " +
            $"{BuildInfo.TargetFramework}");
    }

    private IEnumerable<DutyRunRecord> GetFilteredRuns()
    {
        IEnumerable<DutyRunRecord> runs = store.Data.Runs;

        if (selectedCharacterId != 0)
            runs = runs.Where(x => x.CharacterContentId == selectedCharacterId);

        if (onlyClears)
            runs = runs.Where(x => x.Completed);

        if (!string.IsNullOrWhiteSpace(contentFilter))
        {
            var needle = contentFilter.Trim();

            runs = runs.Where(x =>
                x.ContentNameGerman.Contains(
                    needle,
                    StringComparison.OrdinalIgnoreCase) ||
                x.ContentNameEnglish.Contains(
                    needle,
                    StringComparison.OrdinalIgnoreCase));
        }

        return runs;
    }

    private static void SetupColumns(params string[] names)
    {
        foreach (var name in names)
            ImGui.TableSetupColumn(name);

        ImGui.TableHeadersRow();
    }

    private static void Cell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(text);
    }

    private static string CreatePlayerKey(EncounteredPlayerRecord player) =>
        player.ContentId != 0
            ? $"cid:{player.ContentId}"
            : $"name:{player.Name.Trim().ToUpperInvariant()}|" +
              $"world:{player.WorldId}|" +
              $"{player.WorldName.Trim().ToUpperInvariant()}";

    private static string FormatDuration(long seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));

        return ts.TotalHours >= 24
            ? $"{(long)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : ts.ToString(@"hh\:mm\:ss");
    }
}
