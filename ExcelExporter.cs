using ClosedXML.Excel;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ContentTracker;

public sealed class ExcelExporter
{
    private const string DefaultFileName = "FFXIV_Content_Tracker.xlsx";
    private readonly IPluginLog log;

    public ExcelExporter(IPluginLog log) => this.log = log;

    public string Export(TrackerData data, string exportDirectory)
    {
        if (string.IsNullOrWhiteSpace(exportDirectory))
            throw new InvalidOperationException("Kein Exportordner eingestellt.");

        Directory.CreateDirectory(exportDirectory);

        var targetPath = Path.Combine(exportDirectory, DefaultFileName);
        var tempPath = Path.Combine(
            exportDirectory,
            $"FFXIV_Content_Tracker.{Guid.NewGuid():N}.tmp.xlsx");

        try
        {
            using (var workbook = new XLWorkbook())
            {
                CreateOverviewSheet(workbook, data);
                CreateRunsSheet(workbook, data);
                CreateContentSummarySheet(workbook, data);
                CreateContentCharacterSheet(workbook, data);
                CreateCharacterSheet(workbook, data);
                CreatePlayerSummarySheet(workbook, data);
                CreateRunPlayersSheet(workbook, data);
                CreateGilSheet(workbook, data);
                CreateDailySheet(workbook, data);
                workbook.SaveAs(tempPath);
            }

            try
            {
                File.Move(tempPath, targetPath, true);
                log.Information("ContentTracker: Excel exportiert nach {Path}", targetPath);
                return targetPath;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var fallbackPath = CreateFallbackPath(exportDirectory);
                File.Move(tempPath, fallbackPath, false);
                log.Warning(
                    ex,
                    "ContentTracker: Haupt-Exceldatei konnte nicht ersetzt werden. Ausweichdatei wurde erstellt: {Path}",
                    fallbackPath);
                return fallbackPath;
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Eine übrig gebliebene Temp-Datei ist unkritisch.
            }
        }
    }

    private static string CreateFallbackPath(string exportDirectory)
    {
        var timestamp = DateTime.Now.ToString(
            "yyyy-MM-dd_HHmmss",
            CultureInfo.InvariantCulture);

        var baseName = $"FFXIV_Content_Tracker_{timestamp}";
        var path = Path.Combine(exportDirectory, $"{baseName}.xlsx");
        var counter = 2;

        while (File.Exists(path))
        {
            path = Path.Combine(exportDirectory, $"{baseName}_{counter}.xlsx");
            counter++;
        }

        return path;
    }

    private static void CreateOverviewSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Übersicht");
        ws.TabColor = XLColor.FromHtml("#8B1E24");

        ws.Range("A1:G1").Merge();
        ws.Cell("A1").Value = "FFXIV Content Tracker";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 18;
        ws.Cell("A1").Style.Font.FontColor = XLColor.White;
        ws.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#8B1E24");
        ws.Row(1).Height = 27;

        ws.Range("A2:G2").Merge();
        ws.Cell("A2").Value = $"Export erstellt am {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        ws.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#666666");

        var runs = data.Runs;
        var sessions = data.GilSessions ?? new();
        var totalSeconds = runs.Sum(x => x.DurationSeconds);
        var dutyGil = runs.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
        var sessionGil = sessions.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
        var uniquePlayers = runs
            .SelectMany(x => x.Players ?? new())
            .Select(CreatePlayerKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var stats = new (string Label, object Value)[]
        {
            ("Gesamte Runs", runs.Count),
            ("Clears", runs.Count(x => x.Completed)),
            ("Wipes", runs.Sum(x => x.WipeCount)),
            ("Gesamtzeit", TimeSpan.FromSeconds(totalSeconds)),
            ("Gil +/- (Duty)", dutyGil),
            ("Gil +/- (Sessions)", sessionGil),
            ("Gil-Sessions", sessions.Count),
            ("Charaktere", data.Characters.Count),
            ("Verschiedene Inhalte", runs.Select(x => x.ContentFinderConditionId).Where(x => x != 0).Distinct().Count()),
            ("Verschiedene Mitspieler", uniquePlayers)
        };

        for (var i = 0; i < stats.Length; i++)
        {
            var row = i + 4;
            ws.Cell(row, 1).Value = stats[i].Label;
            ws.Cell(row, 1).Style.Font.Bold = true;

            if (stats[i].Value is TimeSpan ts)
            {
                ws.Cell(row, 2).Value = ts;
                ws.Cell(row, 2).Style.DateFormat.Format = "[h]:mm:ss";
            }
            else if (stats[i].Value is int iv)
            {
                ws.Cell(row, 2).Value = iv;
            }
            else if (stats[i].Value is long lv)
            {
                ws.Cell(row, 2).Value = lv;
            }
            else
            {
                ws.Cell(row, 2).Value = stats[i].Value.ToString();
            }
        }

        ws.Cell(8, 2).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
        ws.Cell(9, 2).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

        var characterTitleRow = 16;
        ws.Range(characterTitleRow, 1, characterTitleRow, 7).Merge();
        ws.Cell(characterTitleRow, 1).Value = "Charakterübersicht";
        ApplySectionTitle(ws.Range(characterTitleRow, 1, characterTitleRow, 7));

        var headerRow = characterTitleRow + 1;
        WriteHeadersAt(
            ws,
            headerRow,
            new[] { "Charakter", "Runs", "Clears", "Wipes", "Gesamtzeit", "Duty Gil +/-", "Session Gil +/-" });

        var rowOut = headerRow + 1;

        foreach (var c in data.Characters.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var charRuns = runs.Where(x => x.CharacterContentId == c.ContentId).ToList();
            var charSessions = sessions.Where(x => x.CharacterContentId == c.ContentId).ToList();

            ws.Cell(rowOut, 1).Value = c.DisplayName;
            ws.Cell(rowOut, 2).Value = charRuns.Count;
            ws.Cell(rowOut, 3).Value = charRuns.Count(x => x.Completed);
            ws.Cell(rowOut, 4).Value = charRuns.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(rowOut, 5), charRuns.Sum(x => x.DurationSeconds));
            ws.Cell(rowOut, 6).Value = charRuns.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(rowOut, 6).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(rowOut, 7).Value = charSessions.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(rowOut, 7).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            rowOut++;
        }

        ws.SheetView.FreezeRows(2);
        ws.Columns().AdjustToContents();
        foreach (var column in ws.ColumnsUsed())
            if (column.Width > 45) column.Width = 45;
    }

    private static void CreateRunsSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Runs");
        WriteHeaders(ws, new[]
        {
            "#", "Charakter", "Home World", "Inhalt DE", "Content EN",
            "Content ID", "Territory ID", "Datum", "Start", "Ende", "Dauer",
            "Gil Start", "Gil Ende", "Gil +/-", "Duty gestartet", "Clear",
            "Wipes", "Endgrund", "Wiederhergestellt", "Mitspieler", "Spielerliste"
        });

        var row = 2;

        foreach (var run in data.Runs.OrderBy(x => x.StartedUtc))
        {
            var start = run.StartedUtc.ToLocalTime();
            var end = run.EndedUtc.ToLocalTime();
            var players = run.Players ?? new();

            ws.Cell(row, 1).Value = run.Id;
            ws.Cell(row, 2).Value = run.CharacterName;
            ws.Cell(row, 3).Value = run.CharacterHomeWorldName;
            ws.Cell(row, 4).Value = run.ContentNameGerman;
            ws.Cell(row, 5).Value = run.ContentNameEnglish;
            ws.Cell(row, 6).Value = run.ContentFinderConditionId;
            ws.Cell(row, 7).Value = run.TerritoryTypeId;
            ws.Cell(row, 8).Value = start.Date;
            ws.Cell(row, 8).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 9).Value = start;
            ws.Cell(row, 9).Style.DateFormat.Format = "HH:mm:ss";
            ws.Cell(row, 10).Value = end;
            ws.Cell(row, 10).Style.DateFormat.Format = "HH:mm:ss";
            SetDuration(ws.Cell(row, 11), run.DurationSeconds);
            SetNullableNumber(ws.Cell(row, 12), run.GilStart);
            SetNullableNumber(ws.Cell(row, 13), run.GilEnd);
            SetNullableNumber(ws.Cell(row, 14), run.GilDelta, true);
            ws.Cell(row, 15).Value = run.DutyStarted ? "Ja" : "Nein";
            ws.Cell(row, 16).Value = run.Completed ? "Ja" : "Nein";
            ws.Cell(row, 17).Value = run.WipeCount;
            ws.Cell(row, 18).Value = run.EndReason;
            ws.Cell(row, 19).Value = run.RecoveredSession ? "Ja" : "Nein";
            ws.Cell(row, 20).Value = players.Count;
            ws.Cell(row, 21).Value = string.Join(", ", players.Select(x => x.DisplayName));
            row++;
        }

        FormatSheet(ws);
    }

    private static void CreateContentSummarySheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Inhalte");
        WriteHeaders(ws, new[]
        {
            "Inhalt DE", "Content EN", "Content ID", "Runs", "Clears", "Clear %",
            "Wipes", "Gesamtzeit", "Ø Dauer", "Gil +/-", "Verschiedene Mitspieler",
            "Zuletzt gespielt"
        });

        var row = 2;

        foreach (var g in data.Runs
                     .GroupBy(x => new
                     {
                         x.ContentFinderConditionId,
                         x.ContentNameGerman,
                         x.ContentNameEnglish
                     })
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key.ContentNameGerman))
        {
            var totalSeconds = g.Sum(x => x.DurationSeconds);

            ws.Cell(row, 1).Value = g.Key.ContentNameGerman;
            ws.Cell(row, 2).Value = g.Key.ContentNameEnglish;
            ws.Cell(row, 3).Value = g.Key.ContentFinderConditionId;
            ws.Cell(row, 4).Value = g.Count();
            ws.Cell(row, 5).Value = g.Count(x => x.Completed);
            ws.Cell(row, 6).Value = g.Any()
                ? (double)g.Count(x => x.Completed) / g.Count()
                : 0;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.0%";
            ws.Cell(row, 7).Value = g.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 8), totalSeconds);
            SetDuration(ws.Cell(row, 9), g.Any() ? totalSeconds / g.Count() : 0);
            ws.Cell(row, 10).Value = g.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 10).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(row, 11).Value = g
                .SelectMany(x => x.Players ?? new())
                .Select(CreatePlayerKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            ws.Cell(row, 12).Value = g.Max(x => x.EndedUtc).ToLocalTime();
            ws.Cell(row, 12).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            row++;
        }

        FormatSheet(ws);
    }

    private static void CreateContentCharacterSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Inhalte-Charaktere");
        WriteHeaders(ws, new[]
        {
            "Charakter", "Inhalt DE", "Content EN", "Content ID", "Runs",
            "Clears", "Wipes", "Gesamtzeit", "Ø Dauer", "Gil +/-"
        });

        var row = 2;

        foreach (var g in data.Runs
                     .GroupBy(x => new
                     {
                         x.CharacterContentId,
                         x.CharacterName,
                         x.CharacterHomeWorldName,
                         x.ContentFinderConditionId,
                         x.ContentNameGerman,
                         x.ContentNameEnglish
                     })
                     .OrderBy(x => x.Key.CharacterName)
                     .ThenByDescending(x => x.Count()))
        {
            var total = g.Sum(x => x.DurationSeconds);

            ws.Cell(row, 1).Value = CharacterDisplay(
                g.Key.CharacterName,
                g.Key.CharacterHomeWorldName);

            ws.Cell(row, 2).Value = g.Key.ContentNameGerman;
            ws.Cell(row, 3).Value = g.Key.ContentNameEnglish;
            ws.Cell(row, 4).Value = g.Key.ContentFinderConditionId;
            ws.Cell(row, 5).Value = g.Count();
            ws.Cell(row, 6).Value = g.Count(x => x.Completed);
            ws.Cell(row, 7).Value = g.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 8), total);
            SetDuration(ws.Cell(row, 9), g.Any() ? total / g.Count() : 0);
            ws.Cell(row, 10).Value = g.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 10).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            row++;
        }

        FormatSheet(ws);
    }

    private static void CreateCharacterSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Charaktere");
        WriteHeaders(ws, new[]
        {
            "Charakter", "Content ID", "Home World", "Runs", "Clears", "Wipes",
            "Gesamtzeit", "Duty Gil +/-", "Session Gil +/-",
            "Erstmals gesehen", "Zuletzt gesehen"
        });

        var row = 2;

        foreach (var c in data.Characters.OrderBy(
                     x => x.DisplayName,
                     StringComparer.OrdinalIgnoreCase))
        {
            var runs = data.Runs.Where(x => x.CharacterContentId == c.ContentId).ToList();
            var sessions = data.GilSessions
                .Where(x => x.CharacterContentId == c.ContentId)
                .ToList();

            ws.Cell(row, 1).Value = c.Name;
            ws.Cell(row, 2).Value = c.ContentId.ToString(CultureInfo.InvariantCulture);
            ws.Cell(row, 3).Value = c.HomeWorldName;
            ws.Cell(row, 4).Value = runs.Count;
            ws.Cell(row, 5).Value = runs.Count(x => x.Completed);
            ws.Cell(row, 6).Value = runs.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 7), runs.Sum(x => x.DurationSeconds));

            ws.Cell(row, 8).Value = runs
                .Where(x => x.GilDelta.HasValue)
                .Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 8).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

            ws.Cell(row, 9).Value = sessions
                .Where(x => x.GilDelta.HasValue)
                .Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 9).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

            ws.Cell(row, 10).Value = c.FirstSeenUtc.ToLocalTime();
            ws.Cell(row, 10).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            ws.Cell(row, 11).Value = c.LastSeenUtc.ToLocalTime();
            ws.Cell(row, 11).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            row++;
        }

        FormatSheet(ws);
    }

    private static void CreatePlayerSummarySheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Spieler");
        WriteHeaders(ws, new[]
        {
            "Spieler", "World", "Content ID", "Gemeinsame Runs",
            "Gemeinsame Clears", "Gemeinsame Runzeit", "Erstmals gesehen",
            "Zuletzt gesehen", "Eigene Charaktere", "Inhalte"
        });

        var encounters = data.Runs
            .SelectMany(r => (r.Players ?? new())
                .Select(p => new PlayerEncounter(r, p)))
            .ToList();

        var row = 2;

        foreach (var g in encounters
                     .GroupBy(x => CreatePlayerKey(x.Player), StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(x => x.Select(y => y.Run.Id).Distinct().Count()))
        {
            var latest = g
                .OrderByDescending(x => x.Player.LastSeenUtc)
                .First()
                .Player;

            var runGroups = g
                .GroupBy(x => x.Run.Id)
                .Select(x => x.First())
                .ToList();

            ws.Cell(row, 1).Value = latest.Name;
            ws.Cell(row, 2).Value = latest.WorldName;
            ws.Cell(row, 3).Value = latest.ContentId == 0
                ? string.Empty
                : latest.ContentId.ToString(CultureInfo.InvariantCulture);
            ws.Cell(row, 4).Value = runGroups.Count;
            ws.Cell(row, 5).Value = runGroups.Count(x => x.Run.Completed);
            SetDuration(ws.Cell(row, 6), runGroups.Sum(x => x.Run.DurationSeconds));
            ws.Cell(row, 7).Value = g.Min(x => x.Player.FirstSeenUtc).ToLocalTime();
            ws.Cell(row, 7).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            ws.Cell(row, 8).Value = g.Max(x => x.Player.LastSeenUtc).ToLocalTime();
            ws.Cell(row, 8).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            ws.Cell(row, 9).Value = string.Join(
                ", ",
                g.Select(x => x.Run.CharacterDisplayName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x));
            ws.Cell(row, 10).Value = string.Join(
                ", ",
                g.Select(x => string.IsNullOrWhiteSpace(x.Run.ContentNameGerman)
                        ? x.Run.ContentNameEnglish
                        : x.Run.ContentNameGerman)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x));
            row++;
        }

        FormatSheet(ws);
    }

    private static void CreateRunPlayersSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Run-Spieler");
        WriteHeaders(ws, new[]
        {
            "Run #", "Datum", "Eigener Charakter", "Inhalt DE", "Content EN",
            "Spieler", "World", "Content ID", "Erstmals im Run gesehen",
            "Zuletzt im Run gesehen"
        });

        var row = 2;

        foreach (var run in data.Runs.OrderBy(x => x.StartedUtc))
        foreach (var player in (run.Players ?? new())
                     .OrderBy(x => x.Name)
                     .ThenBy(x => x.WorldName))
        {
            ws.Cell(row, 1).Value = run.Id;
            ws.Cell(row, 2).Value = run.StartedUtc.ToLocalTime();
            ws.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            ws.Cell(row, 3).Value = run.CharacterDisplayName;
            ws.Cell(row, 4).Value = run.ContentNameGerman;
            ws.Cell(row, 5).Value = run.ContentNameEnglish;
            ws.Cell(row, 6).Value = player.Name;
            ws.Cell(row, 7).Value = player.WorldName;
            ws.Cell(row, 8).Value = player.ContentId == 0
                ? string.Empty
                : player.ContentId.ToString(CultureInfo.InvariantCulture);
            ws.Cell(row, 9).Value = player.FirstSeenUtc.ToLocalTime();
            ws.Cell(row, 9).Style.DateFormat.Format = "HH:mm:ss";
            ws.Cell(row, 10).Value = player.LastSeenUtc.ToLocalTime();
            ws.Cell(row, 10).Style.DateFormat.Format = "HH:mm:ss";
            row++;
        }

        FormatSheet(ws);
    }

    private static void CreateGilSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Gil");
        ws.TabColor = XLColor.FromHtml("#D4A017");

        var sessions = data.GilSessions ?? new();
        var sessionTotal = sessions
            .Where(x => x.GilDelta.HasValue)
            .Sum(x => x.GilDelta!.Value);

        var dutyTotal = data.Runs
            .Where(x => x.GilDelta.HasValue)
            .Sum(x => x.GilDelta!.Value);

        // --- Session-Gil Zusammenfassung ---
        ws.Range("A1:H1").Merge();
        ws.Cell("A1").Value = "Gil – Session-Übersicht";
        ApplySectionTitle(ws.Range("A1:H1"));

        ws.Cell("A2").Value = "Session Gil +/- gesamt";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("B2").Value = sessionTotal;
        ws.Cell("B2").Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

        ws.Cell("A3").Value = "Duty Gil +/- gesamt";
        ws.Cell("A3").Style.Font.Bold = true;
        ws.Cell("B3").Value = dutyTotal;
        ws.Cell("B3").Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

        ws.Cell("A4").Value = "Gespeicherte Sessions";
        ws.Cell("A4").Style.Font.Bold = true;
        ws.Cell("B4").Value = sessions.Count;

        var summaryHeader = 6;
        WriteHeadersAt(
            ws,
            summaryHeader,
            new[] { "Charakter", "Sessions", "Gesamtzeit", "Session Gil +/-" });

        var row = summaryHeader + 1;

        foreach (var g in sessions
                     .GroupBy(x => new
                     {
                         x.CharacterContentId,
                         x.CharacterName,
                         x.CharacterHomeWorldName
                     })
                     .OrderBy(x => x.Key.CharacterName))
        {
            ws.Cell(row, 1).Value = CharacterDisplay(
                g.Key.CharacterName,
                g.Key.CharacterHomeWorldName);
            ws.Cell(row, 2).Value = g.Count();
            SetDuration(ws.Cell(row, 3), g.Sum(x => x.DurationSeconds));
            ws.Cell(row, 4).Value = g
                .Where(x => x.GilDelta.HasValue)
                .Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 4).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            row++;
        }

        // --- Session-Verlauf ---
        var sessionTitleRow = Math.Max(row + 2, 10);
        ws.Range(sessionTitleRow, 1, sessionTitleRow, 8).Merge();
        ws.Cell(sessionTitleRow, 1).Value = "Session-Verlauf";
        ApplySectionTitle(ws.Range(sessionTitleRow, 1, sessionTitleRow, 8));

        var sessionHeaderRow = sessionTitleRow + 1;
        WriteHeadersAt(
            ws,
            sessionHeaderRow,
            new[]
            {
                "#", "Charakter", "Start", "Ende", "Dauer",
                "Gil Start", "Gil Ende", "Gil +/-"
            });

        row = sessionHeaderRow + 1;

        foreach (var session in sessions.OrderBy(x => x.StartedUtc))
        {
            ws.Cell(row, 1).Value = session.Id;
            ws.Cell(row, 2).Value = session.CharacterDisplayName;
            ws.Cell(row, 3).Value = session.StartedUtc.ToLocalTime();
            ws.Cell(row, 3).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            ws.Cell(row, 4).Value = session.EndedUtc.ToLocalTime();
            ws.Cell(row, 4).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            SetDuration(ws.Cell(row, 5), session.DurationSeconds);
            SetNullableNumber(ws.Cell(row, 6), session.GilStart);
            SetNullableNumber(ws.Cell(row, 7), session.GilEnd);
            SetNullableNumber(ws.Cell(row, 8), session.GilDelta, true);
            row++;
        }

        // --- Duty-Gil ---
        var dutyTitleRow = Math.Max(row + 2, sessionHeaderRow + 5);
        ws.Range(dutyTitleRow, 1, dutyTitleRow, 8).Merge();
        ws.Cell(dutyTitleRow, 1).Value = "Duty-Gil (an Inhalte gebunden)";
        ApplySectionTitle(ws.Range(dutyTitleRow, 1, dutyTitleRow, 8));

        var dutyHeaderRow = dutyTitleRow + 1;
        WriteHeadersAt(
            ws,
            dutyHeaderRow,
            new[]
            {
                "Datum", "Charakter", "Inhalt DE", "Content EN",
                "Gil Start", "Gil Ende", "Gil +/-", "Clear"
            });

        row = dutyHeaderRow + 1;

        foreach (var run in data.Runs.OrderBy(x => x.StartedUtc))
        {
            ws.Cell(row, 1).Value = run.EndedUtc.ToLocalTime();
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            ws.Cell(row, 2).Value = run.CharacterDisplayName;
            ws.Cell(row, 3).Value = run.ContentNameGerman;
            ws.Cell(row, 4).Value = run.ContentNameEnglish;
            SetNullableNumber(ws.Cell(row, 5), run.GilStart);
            SetNullableNumber(ws.Cell(row, 6), run.GilEnd);
            SetNullableNumber(ws.Cell(row, 7), run.GilDelta, true);
            ws.Cell(row, 8).Value = run.Completed ? "Ja" : "Nein";
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 45)
                column.Width = 45;
            else if (column.Width < 10)
                column.Width = 10;
        }
    }

    private static void CreateDailySheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Tage");
        WriteHeaders(ws, new[]
        {
            "Datum", "Runs", "Clears", "Wipes", "Zeit in Inhalten",
            "Duty Gil +/-", "Session Gil +/-", "Charaktere", "Inhalte"
        });

        var allDates = data.Runs
            .Select(x => x.StartedUtc.ToLocalTime().Date)
            .Concat((data.GilSessions ?? new())
                .Select(x => x.StartedUtc.ToLocalTime().Date))
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        var row = 2;

        foreach (var date in allDates)
        {
            var runs = data.Runs
                .Where(x => x.StartedUtc.ToLocalTime().Date == date)
                .ToList();

            var sessions = (data.GilSessions ?? new())
                .Where(x => x.StartedUtc.ToLocalTime().Date == date)
                .ToList();

            ws.Cell(row, 1).Value = date;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 2).Value = runs.Count;
            ws.Cell(row, 3).Value = runs.Count(x => x.Completed);
            ws.Cell(row, 4).Value = runs.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 5), runs.Sum(x => x.DurationSeconds));

            ws.Cell(row, 6).Value = runs
                .Where(x => x.GilDelta.HasValue)
                .Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 6).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

            ws.Cell(row, 7).Value = sessions
                .Where(x => x.GilDelta.HasValue)
                .Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 7).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";

            ws.Cell(row, 8).Value = runs
                .Select(x => x.CharacterContentId)
                .Concat(sessions.Select(x => x.CharacterContentId))
                .Distinct()
                .Count();

            ws.Cell(row, 9).Value = runs
                .Select(x => x.ContentFinderConditionId)
                .Distinct()
                .Count();

            row++;
        }

        FormatSheet(ws);
    }

    private static string CharacterDisplay(string name, string world) =>
        string.IsNullOrWhiteSpace(world)
            ? name
            : $"{name} @ {world}";

    private static string CreatePlayerKey(EncounteredPlayerRecord player) =>
        player.ContentId != 0
            ? $"cid:{player.ContentId}"
            : $"name:{player.Name.Trim().ToUpperInvariant()}|" +
              $"world:{player.WorldId}|" +
              $"{player.WorldName.Trim().ToUpperInvariant()}";

    private static void SetNullableNumber(
        IXLCell cell,
        int? value,
        bool signed = false)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = signed
                ? "+#,##0;[Red]-#,##0;0"
                : "#,##0";
        }
        else
        {
            cell.Value = string.Empty;
        }
    }

    private static void SetDuration(IXLCell cell, long seconds)
    {
        cell.Value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        cell.Style.DateFormat.Format = "[h]:mm:ss";
    }

    private static void WriteHeaders(
        IXLWorksheet ws,
        IReadOnlyList<string> headers) =>
        WriteHeadersAt(ws, 1, headers);

    private static void WriteHeadersAt(
        IXLWorksheet ws,
        int row,
        IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
            ws.Cell(row, i + 1).Value = headers[i];

        var range = ws.Range(row, 1, row, headers.Count);
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#8B1E24");
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#5C1116");
        ws.Row(row).Height = 21;
    }

    private static void ApplySectionTitle(IXLRange range)
    {
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.FromHtml("#8B1E24");
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCEBEC");
        range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor = XLColor.FromHtml("#D9A1A5");
    }

    private static void FormatSheet(IXLWorksheet ws)
    {
        ws.SheetView.FreezeRows(1);

        var used = ws.RangeUsed();

        if (used != null)
        {
            used.SetAutoFilter();
            used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        ws.Columns().AdjustToContents();

        foreach (var column in ws.ColumnsUsed())
        {
            if (column.Width > 45)
                column.Width = 45;
            else if (column.Width < 10)
                column.Width = 10;
        }

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            if (row.RowNumber() % 2 == 0)
                row.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
        }
    }

    private sealed record PlayerEncounter(
        DutyRunRecord Run,
        EncounteredPlayerRecord Player);
}
