using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ContentTracker;

public sealed class TrackerStore
{
    private readonly IPluginLog log;
    private readonly string dataFile;
    private readonly object sync = new();
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public TrackerData Data { get; private set; }
    public string DataFilePath => dataFile;

    public TrackerStore(string configDirectory, IPluginLog log)
    {
        this.log = log;
        Directory.CreateDirectory(configDirectory);
        dataFile = Path.Combine(configDirectory, "tracker-data.json");
        Data = Load();
    }

    public void UpsertCharacter(CharacterRecord character)
    {
        lock (sync)
        {
            var existing = Data.Characters.FirstOrDefault(
                x => x.ContentId != 0 && x.ContentId == character.ContentId);

            if (existing == null)
            {
                Data.Characters.Add(character);
            }
            else
            {
                existing.Name = character.Name;
                existing.HomeWorldId = character.HomeWorldId;
                existing.HomeWorldName = character.HomeWorldName;
                existing.FirstSeenUtc = existing.FirstSeenUtc == default
                    ? character.FirstSeenUtc
                    : existing.FirstSeenUtc;
                existing.LastSeenUtc = character.LastSeenUtc;
            }

            SaveLocked();
        }
    }

    public long GetNextRunId()
    {
        lock (sync)
            return Data.Runs.Count == 0 ? 1 : Data.Runs.Max(x => x.Id) + 1;
    }

    public long GetNextGilSessionId()
    {
        lock (sync)
            return Data.GilSessions.Count == 0 ? 1 : Data.GilSessions.Max(x => x.Id) + 1;
    }

    public void AddRun(DutyRunRecord run)
    {
        lock (sync)
        {
            Data.Runs.Add(run);
            Data.PendingRun = null;
            SaveLocked();
        }
    }

    public void AddGilSession(GilSessionRecord session)
    {
        lock (sync)
        {
            Data.GilSessions.Add(session);
            SaveLocked();
        }
    }

    public void SetPendingRun(ActiveRunSnapshot snapshot)
    {
        lock (sync)
        {
            Data.PendingRun = snapshot;
            SaveLocked();
        }
    }

    public void ClearPendingRun()
    {
        lock (sync)
        {
            if (Data.PendingRun == null)
                return;

            Data.PendingRun = null;
            SaveLocked();
        }
    }

    public void Save()
    {
        lock (sync)
            SaveLocked();
    }

    private TrackerData Load()
    {
        try
        {
            if (!File.Exists(dataFile))
                return new TrackerData();

            var json = File.ReadAllText(dataFile);
            var data = JsonSerializer.Deserialize<TrackerData>(json, jsonOptions)
                       ?? new TrackerData();

            data.SchemaVersion = BuildInfo.DataSchemaVersion;
            data.Characters ??= new();
            data.Runs ??= new();
            data.GilSessions ??= new();

            foreach (var run in data.Runs)
            {
                run.Players ??= new();
                run.EndReason ??= string.Empty;
            }

            foreach (var session in data.GilSessions)
                session.EndReason ??= string.Empty;

            if (data.PendingRun != null)
                data.PendingRun.Players ??= new();

            return data;
        }
        catch (Exception ex)
        {
            log.Error(ex, "ContentTracker: tracker-data.json konnte nicht geladen werden.");

            try
            {
                if (File.Exists(dataFile))
                {
                    var backup = dataFile + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}.json";
                    File.Copy(dataFile, backup, true);
                }
            }
            catch
            {
                // Best effort backup only.
            }

            return new TrackerData();
        }
    }

    private void SaveLocked()
    {
        try
        {
            var temp = dataFile + ".tmp";
            var json = JsonSerializer.Serialize(Data, jsonOptions);
            File.WriteAllText(temp, json);
            File.Move(temp, dataFile, true);
        }
        catch (Exception ex)
        {
            log.Error(ex, "ContentTracker: Daten konnten nicht gespeichert werden.");
        }
    }
}
