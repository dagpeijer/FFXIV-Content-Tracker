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
        var tempPath = Path.Combine(exportDirectory, $"FFXIV_Content_Tracker.{Guid.NewGuid():N}.tmp.xlsx");

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
                // Typischer Fall: Die Hauptdatei ist gerade in Excel/OneDrive geöffnet und deshalb gesperrt.
                // Die Trackingdaten sollen dadurch niemals verloren gehen oder beim Logout einen Pluginfehler erzeugen.
                var fallbackPath = CreateFallbackPath(exportDirectory);
                File.Move(tempPath, fallbackPath, false);
                log.Warning(ex,
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
                // Eine übrig gebliebene Temp-Datei ist unkritisch und darf den Export nicht erneut scheitern lassen.
            }
        }
    }

    private static string CreateFallbackPath(string exportDirectory)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
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
        ws.Cell("A1").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 27;

        ws.Range("A2:G2").Merge();
        ws.Cell("A2").Value = $"Export erstellt am {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        ws.Cell("A2").Style.Font.FontColor = XLColor.FromHtml("#666666");

        var runs = data.Runs;
        var totalSeconds = runs.Sum(x => x.DurationSeconds);
        var totalGil = runs.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
        var uniquePlayers = runs.SelectMany(x => x.Players ?? new()).Select(CreatePlayerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        var stats = new (string Label, object Value)[]
        {
            ("Gesamte Runs", runs.Count),
            ("Clears", runs.Count(x => x.Completed)),
            ("Wipes", runs.Sum(x => x.WipeCount)),
            ("Gesamtzeit", TimeSpan.FromSeconds(totalSeconds)),
            ("Gil +/-", totalGil),
            ("Charaktere", data.Characters.Count),
            ("Verschiedene Inhalte", runs.Select(x => x.ContentFinderConditionId).Where(x => x != 0).Distinct().Count()),
            ("Verschiedene Mitspieler", uniquePlayers)
        };

        for (var i = 0; i < stats.Length; i++)
        {
            var row = i + 4;
            ws.Cell(row, 1).Value = stats[i].Label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml(i % 2 == 0 ? "#F3F4F6" : "#FFFFFF");
            ws.Cell(row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml(i % 2 == 0 ? "#F3F4F6" : "#FFFFFF");

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

        ws.Cell("B8").Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
        ws.Range("A4:B11").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A4:B11").Style.Border.OutsideBorderColor = XLColor.FromHtml("#D1D5DB");

        var characterTitleRow = 14;
        ws.Range(characterTitleRow, 1, characterTitleRow, 7).Merge();
        ws.Cell(characterTitleRow, 1).Value = "Charakterübersicht";
        ApplySectionTitle(ws.Range(characterTitleRow, 1, characterTitleRow, 7));

        var characterHeaderRow = characterTitleRow + 1;
        WriteHeadersAt(ws, characterHeaderRow, new[] { "Charakter", "Runs", "Clears", "Wipes", "Gesamtzeit", "Gil +/-", "Mitspieler" });
        var rowOut = characterHeaderRow + 1;
        foreach (var g in runs.GroupBy(x => new { x.CharacterContentId, x.CharacterName, x.CharacterHomeWorldName }).OrderBy(x => x.Key.CharacterName))
        {
            ws.Cell(rowOut, 1).Value = CharacterDisplay(g.Key.CharacterName, g.Key.CharacterHomeWorldName);
            ws.Cell(rowOut, 2).Value = g.Count();
            ws.Cell(rowOut, 3).Value = g.Count(x => x.Completed);
            ws.Cell(rowOut, 4).Value = g.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(rowOut, 5), g.Sum(x => x.DurationSeconds));
            ws.Cell(rowOut, 6).Value = g.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(rowOut, 6).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(rowOut, 7).Value = g.SelectMany(x => x.Players ?? new()).Select(CreatePlayerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            rowOut++;
        }

        var contentTitleRow = Math.Max(rowOut + 2, 20);
        ws.Range(contentTitleRow, 1, contentTitleRow, 7).Merge();
        ws.Cell(contentTitleRow, 1).Value = "Häufigste Inhalte";
        ApplySectionTitle(ws.Range(contentTitleRow, 1, contentTitleRow, 7));

        var contentHeaderRow = contentTitleRow + 1;
        WriteHeadersAt(ws, contentHeaderRow, new[] { "Inhalt", "Runs", "Clears", "Wipes", "Gesamtzeit", "Gil +/-", "Mitspieler" });
        rowOut = contentHeaderRow + 1;
        foreach (var g in runs
                     .GroupBy(x => new { x.ContentFinderConditionId, x.ContentNameGerman, x.ContentNameEnglish })
                     .OrderByDescending(x => x.Count())
                     .ThenBy(x => x.Key.ContentNameGerman)
                     .Take(20))
        {
            ws.Cell(rowOut, 1).Value = string.IsNullOrWhiteSpace(g.Key.ContentNameGerman) ? g.Key.ContentNameEnglish : g.Key.ContentNameGerman;
            ws.Cell(rowOut, 2).Value = g.Count();
            ws.Cell(rowOut, 3).Value = g.Count(x => x.Completed);
            ws.Cell(rowOut, 4).Value = g.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(rowOut, 5), g.Sum(x => x.DurationSeconds));
            ws.Cell(rowOut, 6).Value = g.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(rowOut, 6).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(rowOut, 7).Value = g.SelectMany(x => x.Players ?? new()).Select(CreatePlayerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            rowOut++;
        }

        ws.SheetView.FreezeRows(2);
        ws.Column(1).Width = 34;
        ws.Column(2).Width = 16;
        ws.Columns(3, 7).AdjustToContents();
        foreach (var column in ws.ColumnsUsed())
            if (column.Width > 42) column.Width = 42;
    }

    private static void CreateRunsSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Runs");
        WriteHeaders(ws, new[] { "#", "Charakter", "Home World", "Inhalt DE", "Content EN", "Content ID", "Territory ID", "Datum", "Start", "Ende", "Dauer", "Gil Start", "Gil Ende", "Gil +/-", "Duty gestartet", "Clear", "Wipes", "Endgrund", "Wiederhergestellt", "Mitspieler", "Spielerliste" });

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
        WriteHeaders(ws, new[] { "Inhalt DE", "Content EN", "Content ID", "Runs", "Clears", "Clear %", "Wipes", "Gesamtzeit", "Ø Dauer", "Gil +/-", "Verschiedene Mitspieler", "Zuletzt gespielt" });
        var row = 2;
        foreach (var g in data.Runs.GroupBy(x => new { x.ContentFinderConditionId, x.ContentNameGerman, x.ContentNameEnglish }).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.ContentNameGerman))
        {
            var totalSeconds = g.Sum(x => x.DurationSeconds);
            ws.Cell(row, 1).Value = g.Key.ContentNameGerman;
            ws.Cell(row, 2).Value = g.Key.ContentNameEnglish;
            ws.Cell(row, 3).Value = g.Key.ContentFinderConditionId;
            ws.Cell(row, 4).Value = g.Count();
            ws.Cell(row, 5).Value = g.Count(x => x.Completed);
            ws.Cell(row, 6).Value = g.Any() ? (double)g.Count(x => x.Completed) / g.Count() : 0;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.0%";
            ws.Cell(row, 7).Value = g.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 8), totalSeconds);
            SetDuration(ws.Cell(row, 9), g.Any() ? totalSeconds / g.Count() : 0);
            ws.Cell(row, 10).Value = g.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 10).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(row, 11).Value = g.SelectMany(x => x.Players ?? new()).Select(CreatePlayerKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            ws.Cell(row, 12).Value = g.Max(x => x.EndedUtc).ToLocalTime();
            ws.Cell(row, 12).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            row++;
        }
        FormatSheet(ws);
    }

    private static void CreateContentCharacterSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Inhalte-Charaktere");
        WriteHeaders(ws, new[] { "Charakter", "Inhalt DE", "Content EN", "Content ID", "Runs", "Clears", "Wipes", "Gesamtzeit", "Ø Dauer", "Gil +/-" });
        var row = 2;
        foreach (var g in data.Runs.GroupBy(x => new { x.CharacterContentId, x.CharacterName, x.CharacterHomeWorldName, x.ContentFinderConditionId, x.ContentNameGerman, x.ContentNameEnglish }).OrderBy(x => x.Key.CharacterName).ThenByDescending(x => x.Count()))
        {
            var total = g.Sum(x => x.DurationSeconds);
            ws.Cell(row, 1).Value = CharacterDisplay(g.Key.CharacterName, g.Key.CharacterHomeWorldName);
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
        WriteHeaders(ws, new[] { "Charakter", "Content ID", "Home World", "Runs", "Clears", "Wipes", "Gesamtzeit", "Gil +/-", "Erstmals gesehen", "Zuletzt gesehen" });
        var row = 2;
        foreach (var c in data.Characters.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var runs = data.Runs.Where(x => x.CharacterContentId == c.ContentId).ToList();
            ws.Cell(row, 1).Value = c.Name;
            ws.Cell(row, 2).Value = c.ContentId.ToString(CultureInfo.InvariantCulture);
            ws.Cell(row, 3).Value = c.HomeWorldName;
            ws.Cell(row, 4).Value = runs.Count;
            ws.Cell(row, 5).Value = runs.Count(x => x.Completed);
            ws.Cell(row, 6).Value = runs.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 7), runs.Sum(x => x.DurationSeconds));
            ws.Cell(row, 8).Value = runs.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 8).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(row, 9).Value = c.FirstSeenUtc.ToLocalTime();
            ws.Cell(row, 9).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            ws.Cell(row, 10).Value = c.LastSeenUtc.ToLocalTime();
            ws.Cell(row, 10).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            row++;
        }
        FormatSheet(ws);
    }

    private static void CreatePlayerSummarySheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Spieler");
        WriteHeaders(ws, new[] { "Spieler", "World", "Content ID", "Gemeinsame Runs", "Gemeinsame Clears", "Gemeinsame Runzeit", "Erstmals gesehen", "Zuletzt gesehen", "Eigene Charaktere", "Inhalte" });

        var encounters = data.Runs.SelectMany(r => (r.Players ?? new()).Select(p => new PlayerEncounter(r, p))).ToList();
        var row = 2;
        foreach (var g in encounters.GroupBy(x => CreatePlayerKey(x.Player), StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Select(y => y.Run.Id).Distinct().Count()))
        {
            var latest = g.OrderByDescending(x => x.Player.LastSeenUtc).First().Player;
            var runGroups = g.GroupBy(x => x.Run.Id).Select(x => x.First()).ToList();
            ws.Cell(row, 1).Value = latest.Name;
            ws.Cell(row, 2).Value = latest.WorldName;
            ws.Cell(row, 3).Value = latest.ContentId == 0 ? string.Empty : latest.ContentId.ToString(CultureInfo.InvariantCulture);
            ws.Cell(row, 4).Value = runGroups.Count;
            ws.Cell(row, 5).Value = runGroups.Count(x => x.Run.Completed);
            SetDuration(ws.Cell(row, 6), runGroups.Sum(x => x.Run.DurationSeconds));
            ws.Cell(row, 7).Value = g.Min(x => x.Player.FirstSeenUtc).ToLocalTime();
            ws.Cell(row, 7).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            ws.Cell(row, 8).Value = g.Max(x => x.Player.LastSeenUtc).ToLocalTime();
            ws.Cell(row, 8).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            ws.Cell(row, 9).Value = string.Join(", ", g.Select(x => x.Run.CharacterDisplayName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            ws.Cell(row, 10).Value = string.Join(", ", g.Select(x => string.IsNullOrWhiteSpace(x.Run.ContentNameGerman) ? x.Run.ContentNameEnglish : x.Run.ContentNameGerman).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
            row++;
        }
        FormatSheet(ws);
    }

    private static void CreateRunPlayersSheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Run-Spieler");
        WriteHeaders(ws, new[] { "Run #", "Datum", "Eigener Charakter", "Inhalt DE", "Content EN", "Spieler", "World", "Content ID", "Erstmals im Run gesehen", "Zuletzt im Run gesehen" });
        var row = 2;
        foreach (var run in data.Runs.OrderBy(x => x.StartedUtc))
        foreach (var player in (run.Players ?? new()).OrderBy(x => x.Name).ThenBy(x => x.WorldName))
        {
            ws.Cell(row, 1).Value = run.Id;
            ws.Cell(row, 2).Value = run.StartedUtc.ToLocalTime();
            ws.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            ws.Cell(row, 3).Value = run.CharacterDisplayName;
            ws.Cell(row, 4).Value = run.ContentNameGerman;
            ws.Cell(row, 5).Value = run.ContentNameEnglish;
            ws.Cell(row, 6).Value = player.Name;
            ws.Cell(row, 7).Value = player.WorldName;
            ws.Cell(row, 8).Value = player.ContentId == 0 ? string.Empty : player.ContentId.ToString(CultureInfo.InvariantCulture);
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
        WriteHeaders(ws, new[] { "Datum", "Charakter", "Inhalt DE", "Content EN", "Gil +/-" });
        var row = 2;
        foreach (var run in data.Runs.OrderBy(x => x.StartedUtc))
        {
            ws.Cell(row, 1).Value = run.EndedUtc.ToLocalTime();
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy HH:mm:ss";
            ws.Cell(row, 2).Value = run.CharacterDisplayName;
            ws.Cell(row, 3).Value = run.ContentNameGerman;
            ws.Cell(row, 4).Value = run.ContentNameEnglish;
            SetNullableNumber(ws.Cell(row, 5), run.GilDelta, true);
            row++;
        }
        FormatSheet(ws);
    }

    private static void CreateDailySheet(XLWorkbook wb, TrackerData data)
    {
        var ws = wb.Worksheets.Add("Tage");
        WriteHeaders(ws, new[] { "Datum", "Runs", "Clears", "Wipes", "Zeit in Inhalten", "Gil +/-", "Charaktere", "Inhalte" });
        var row = 2;
        foreach (var g in data.Runs.GroupBy(x => x.StartedUtc.ToLocalTime().Date).OrderBy(x => x.Key))
        {
            ws.Cell(row, 1).Value = g.Key;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 2).Value = g.Count();
            ws.Cell(row, 3).Value = g.Count(x => x.Completed);
            ws.Cell(row, 4).Value = g.Sum(x => x.WipeCount);
            SetDuration(ws.Cell(row, 5), g.Sum(x => x.DurationSeconds));
            ws.Cell(row, 6).Value = g.Where(x => x.GilDelta.HasValue).Sum(x => x.GilDelta!.Value);
            ws.Cell(row, 6).Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            ws.Cell(row, 7).Value = g.Select(x => x.CharacterContentId).Distinct().Count();
            ws.Cell(row, 8).Value = g.Select(x => x.ContentFinderConditionId).Distinct().Count();
            row++;
        }
        FormatSheet(ws);
    }

    private static string CharacterDisplay(string name, string world) => string.IsNullOrWhiteSpace(world) ? name : $"{name} @ {world}";

    private static string CreatePlayerKey(EncounteredPlayerRecord player) => player.ContentId != 0
        ? $"cid:{player.ContentId}"
        : $"name:{player.Name.Trim().ToUpperInvariant()}|world:{player.WorldId}|{player.WorldName.Trim().ToUpperInvariant()}";

    private static void SetNullableNumber(IXLCell cell, int? value, bool signed = false)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            if (signed)
                cell.Style.NumberFormat.Format = "+#,##0;[Red]-#,##0;0";
            else
                cell.Style.NumberFormat.Format = "#,##0";
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

    private static void WriteHeaders(IXLWorksheet ws, IReadOnlyList<string> headers) => WriteHeadersAt(ws, 1, headers);

    private static void WriteHeadersAt(IXLWorksheet ws, int row, IReadOnlyList<string> headers)
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

    private sealed record PlayerEncounter(DutyRunRecord Run, EncounteredPlayerRecord Player);
}
