using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.DutyState;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ContentTracker;

public sealed class DutyTracker : IDisposable
{
    private static readonly TimeSpan PartyCaptureInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(15);

    private readonly IDutyState dutyState;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IDataManager dataManager;
    private readonly IGameInventory gameInventory;
    private readonly IPartyList partyList;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly TrackerStore store;
    private readonly System.Action? runFinished;

    private ActiveRun? activeRun;
    private DateTime nextPartyCaptureUtc = DateTime.MinValue;
    private DateTime nextSnapshotUtc = DateTime.MinValue;
    private bool initialRecoveryChecked;

    public DutyTracker(
        IDutyState dutyState,
        IClientState clientState,
        IPlayerState playerState,
        IDataManager dataManager,
        IGameInventory gameInventory,
        IPartyList partyList,
        IFramework framework,
        IPluginLog log,
        TrackerStore store,
        System.Action? runFinished = null)
    {
        this.dutyState = dutyState;
        this.clientState = clientState;
        this.playerState = playerState;
        this.dataManager = dataManager;
        this.gameInventory = gameInventory;
        this.partyList = partyList;
        this.framework = framework;
        this.log = log;
        this.store = store;
        this.runFinished = runFinished;

        dutyState.DutyStarted += OnDutyStarted;
        dutyState.DutyCompleted += OnDutyCompleted;
        dutyState.DutyWiped += OnDutyWiped;
        clientState.ZoneInit += OnZoneInit;
        clientState.TerritoryChanged += OnTerritoryChanged;
        framework.Update += OnFrameworkUpdate;
    }

    public bool HasActiveRun => activeRun != null;
    public DateTime? ActiveStartedUtc => activeRun?.StartedUtc;
    public int ActivePlayerCount => activeRun?.Players.Count ?? 0;
    public int ActiveWipeCount => activeRun?.WipeCount ?? 0;
    public string ActiveContentName => activeRun == null
        ? string.Empty
        : (string.IsNullOrWhiteSpace(activeRun.ContentNameGerman) ? activeRun.ContentNameEnglish : activeRun.ContentNameGerman);
    public bool ActiveDutyStarted => activeRun?.DutyStarted ?? false;

    public void Dispose()
    {
        dutyState.DutyStarted -= OnDutyStarted;
        dutyState.DutyCompleted -= OnDutyCompleted;
        dutyState.DutyWiped -= OnDutyWiped;
        clientState.ZoneInit -= OnZoneInit;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        framework.Update -= OnFrameworkUpdate;

        // Nicht künstlich als abgebrochenen Run speichern. Der Pending-Snapshot erlaubt,
        // denselben Run nach Plugin-Reload/Update fortzusetzen.
        PersistSnapshot(force: true);
    }

    public void HandleLogout()
    {
        if (activeRun != null)
            FinishActiveRun(false, "Logout");
        else
            CloseStalePendingRun("Logout");
    }

    private void OnZoneInit(ZoneInitEventArgs args)
    {
        var cfcId = args.ContentFinderCondition.RowId;
        var territoryId = args.TerritoryType.RowId;

        if (cfcId == 0)
        {
            if (activeRun != null)
                FinishActiveRun(false, "Inhalt verlassen");
            return;
        }

        if (activeRun != null && activeRun.ContentFinderConditionId == cfcId)
        {
            activeRun.TerritoryTypeId = territoryId;
            PersistSnapshot(force: true);
            return;
        }

        if (activeRun != null)
            FinishActiveRun(false, "In anderen Inhalt gewechselt");

        StartRun(cfcId, territoryId, DateTime.UtcNow, recovered: false, dutyStarted: dutyState.IsDutyStarted);
    }

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        if (activeRun == null)
        {
            var cfcId = dutyState.ContentFinderCondition.RowId;
            if (cfcId != 0)
                StartRun(cfcId, clientState.TerritoryType, DateTime.UtcNow, recovered: true, dutyStarted: true);
        }
        else
        {
            activeRun.DutyStarted = true;
            PersistSnapshot(force: true);
        }
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        if (activeRun == null)
        {
            var cfcId = dutyState.ContentFinderCondition.RowId;
            if (cfcId != 0)
                StartRun(cfcId, clientState.TerritoryType, DateTime.UtcNow, recovered: true, dutyStarted: true);
        }

        FinishActiveRun(true, "Abgeschlossen");
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        if (activeRun == null)
            return;

        activeRun.WipeCount++;
        PersistSnapshot(force: true);
    }

    private void OnTerritoryChanged(uint territoryId)
    {
        // ZoneInit übernimmt den normalen Übergang. Dieser Fallback fängt Fälle ab,
        // in denen kein verwertbarer ZoneInit-Content geliefert wird.
        if (activeRun != null && territoryId != activeRun.TerritoryTypeId)
            nextSnapshotUtc = DateTime.MinValue;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;

        if (!initialRecoveryChecked && clientState.IsLoggedIn && playerState.IsLoaded)
        {
            initialRecoveryChecked = true;
            RecoverOrStartCurrentDuty(now);
        }

        if (activeRun == null)
            return;

        if (TryGetGil(out var currentGil))
            activeRun.LastKnownGil = currentGil;

        if (now >= nextPartyCaptureUtc)
        {
            nextPartyCaptureUtc = now + PartyCaptureInterval;
            CapturePartyMembers(now);
        }

        if (now >= nextSnapshotUtc)
        {
            nextSnapshotUtc = now + SnapshotInterval;
            PersistSnapshot(force: true);
        }
    }

    private void RecoverOrStartCurrentDuty(DateTime now)
    {
        var currentCfc = dutyState.ContentFinderCondition.RowId;
        var pending = store.Data.PendingRun;

        if (pending != null)
        {
            var sameCharacter = pending.CharacterContentId != 0 && pending.CharacterContentId == playerState.ContentId;
            var sameDuty = pending.ContentFinderConditionId != 0 && pending.ContentFinderConditionId == currentCfc;

            if (sameCharacter && sameDuty)
            {
                activeRun = ActiveRun.FromSnapshot(pending);
                activeRun.RecoveredSession = true;
                nextPartyCaptureUtc = DateTime.MinValue;
                nextSnapshotUtc = DateTime.MinValue;
                CapturePartyMembers(now);
                log.Information("ContentTracker: Laufende Session wiederhergestellt: {Content}", activeRun.ContentNameEnglish);
                return;
            }

            CloseStalePendingRun("Unterbrochene Session wiederhergestellt");
        }

        if (currentCfc != 0)
            StartRun(currentCfc, clientState.TerritoryType, now, recovered: true, dutyStarted: dutyState.IsDutyStarted);
    }

    private void StartRun(uint cfcId, uint territoryId, DateTime startedUtc, bool recovered, bool dutyStarted)
    {
        if (!playerState.IsLoaded || cfcId == 0)
            return;

        var character = GetCurrentCharacter(startedUtc);
        store.UpsertCharacter(character);

        int? gil = TryGetGil(out var currentGil) ? currentGil : null;

        activeRun = new ActiveRun
        {
            Character = character,
            ContentFinderConditionId = cfcId,
            TerritoryTypeId = territoryId,
            ContentNameEnglish = GetContentName(cfcId, ClientLanguage.English),
            ContentNameGerman = GetContentName(cfcId, ClientLanguage.German),
            StartedUtc = startedUtc,
            GilStart = gil,
            LastKnownGil = gil,
            RecoveredSession = recovered,
            DutyStarted = dutyStarted
        };

        nextPartyCaptureUtc = DateTime.MinValue;
        nextSnapshotUtc = DateTime.MinValue;
        CapturePartyMembers(startedUtc);
        PersistSnapshot(force: true);

        log.Information("ContentTracker: Inhalt betreten: {Content} für {Character}", activeRun.ContentNameEnglish, character.DisplayName);
    }

    private void CapturePartyMembers(DateTime now)
    {
        if (activeRun == null)
            return;

        try
        {
            foreach (var member in partyList)
            {
                if (member == null)
                    continue;

                if (member.ContentId != 0 && member.ContentId == activeRun.Character.ContentId)
                    continue;

                var name = member.Name.TextValue?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var worldId = member.World.RowId;
                var worldName = member.World.IsValid ? member.World.Value.Name.ExtractText() : string.Empty;
                var key = CreatePlayerKey(member.ContentId, name, worldId, worldName);

                if (activeRun.Players.TryGetValue(key, out var existing))
                {
                    existing.Name = name;
                    existing.WorldId = worldId;
                    existing.WorldName = worldName;
                    existing.LastSeenUtc = now;
                    continue;
                }

                activeRun.Players[key] = new EncounteredPlayerRecord
                {
                    ContentId = member.ContentId,
                    Name = name,
                    WorldId = worldId,
                    WorldName = worldName,
                    FirstSeenUtc = now,
                    LastSeenUtc = now
                };
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "ContentTracker: Party-/Alliance-Spieler konnten nicht erfasst werden.");
        }
    }

    private void FinishActiveRun(bool completed, string reason)
    {
        if (activeRun == null)
            return;

        var end = DateTime.UtcNow;
        CapturePartyMembers(end);

        int? gilEnd = TryGetGil(out var currentGil) ? currentGil : activeRun.LastKnownGil;
        int? gilDelta = activeRun.GilStart.HasValue && gilEnd.HasValue
            ? gilEnd.Value - activeRun.GilStart.Value
            : null;

        var record = new DutyRunRecord
        {
            Id = store.GetNextRunId(),
            CharacterContentId = activeRun.Character.ContentId,
            CharacterName = activeRun.Character.Name,
            CharacterHomeWorldId = activeRun.Character.HomeWorldId,
            CharacterHomeWorldName = activeRun.Character.HomeWorldName,
            ContentFinderConditionId = activeRun.ContentFinderConditionId,
            TerritoryTypeId = activeRun.TerritoryTypeId,
            ContentNameEnglish = activeRun.ContentNameEnglish,
            ContentNameGerman = activeRun.ContentNameGerman,
            StartedUtc = activeRun.StartedUtc,
            EndedUtc = end,
            DurationSeconds = Math.Max(0, (long)Math.Round((end - activeRun.StartedUtc).TotalSeconds)),
            GilStart = activeRun.GilStart,
            GilEnd = gilEnd,
            GilDelta = gilDelta,
            DutyStarted = activeRun.DutyStarted,
            Completed = completed,
            WipeCount = activeRun.WipeCount,
            EndReason = reason,
            RecoveredSession = activeRun.RecoveredSession,
            Players = activeRun.Players.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.WorldName, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        activeRun = null;
        nextPartyCaptureUtc = DateTime.MinValue;
        nextSnapshotUtc = DateTime.MinValue;
        store.AddRun(record);

        log.Information("ContentTracker: Run gespeichert: {Content}, {Seconds}s, Gil {GilDelta}, {Players} Mitspieler, Ende: {Reason}",
            record.ContentNameEnglish, record.DurationSeconds, record.GilDelta?.ToString("+#,##0;-#,##0;0") ?? "?", record.Players.Count, reason);

        try
        {
            runFinished?.Invoke();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "ContentTracker: Aktion nach Run-Ende fehlgeschlagen.");
        }
    }

    private void CloseStalePendingRun(string reason)
    {
        var pending = store.Data.PendingRun;
        if (pending == null)
            return;

        var end = pending.LastSnapshotUtc == default ? DateTime.UtcNow : pending.LastSnapshotUtc;
        var record = new DutyRunRecord
        {
            Id = store.GetNextRunId(),
            CharacterContentId = pending.CharacterContentId,
            CharacterName = pending.CharacterName,
            CharacterHomeWorldId = pending.CharacterHomeWorldId,
            CharacterHomeWorldName = pending.CharacterHomeWorldName,
            ContentFinderConditionId = pending.ContentFinderConditionId,
            TerritoryTypeId = pending.TerritoryTypeId,
            ContentNameEnglish = pending.ContentNameEnglish,
            ContentNameGerman = pending.ContentNameGerman,
            StartedUtc = pending.StartedUtc,
            EndedUtc = end,
            DurationSeconds = Math.Max(0, (long)Math.Round((end - pending.StartedUtc).TotalSeconds)),
            GilStart = pending.GilStart,
            GilEnd = pending.LastKnownGil,
            GilDelta = pending.GilStart.HasValue && pending.LastKnownGil.HasValue
                ? pending.LastKnownGil.Value - pending.GilStart.Value
                : null,
            DutyStarted = pending.DutyStarted,
            Completed = false,
            WipeCount = pending.WipeCount,
            EndReason = reason,
            RecoveredSession = true,
            Players = pending.Players ?? new()
        };

        store.AddRun(record);
    }

    private void PersistSnapshot(bool force)
    {
        if (activeRun == null)
            return;

        var now = DateTime.UtcNow;
        if (!force && now < nextSnapshotUtc)
            return;

        store.SetPendingRun(activeRun.ToSnapshot(now));
    }

    private CharacterRecord GetCurrentCharacter(DateTime now)
    {
        var worldName = playerState.HomeWorld.IsValid ? playerState.HomeWorld.Value.Name.ExtractText() : string.Empty;
        return new CharacterRecord
        {
            ContentId = playerState.ContentId,
            Name = playerState.CharacterName,
            HomeWorldId = playerState.HomeWorld.RowId,
            HomeWorldName = worldName,
            FirstSeenUtc = now,
            LastSeenUtc = now
        };
    }

    private string GetContentName(uint cfcId, ClientLanguage language)
    {
        try
        {
            var sheet = dataManager.GetExcelSheet<ContentFinderCondition>(language);
            if (sheet.TryGetRow(cfcId, out var row))
            {
                var name = row.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "ContentTracker: Name für CFC {Id} ({Language}) konnte nicht gelesen werden.", cfcId, language);
        }

        return $"Content {cfcId}";
    }

    private bool TryGetGil(out int gil)
    {
        gil = 0;
        try
        {
            var currency = gameInventory.GetInventoryItems(GameInventoryType.Currency);
            foreach (ref readonly var item in currency)
            {
                if (!item.IsEmpty && item.ItemId == 1)
                {
                    gil = item.Quantity;
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "ContentTracker: Gil konnte nicht gelesen werden.");
            return false;
        }
    }

    private static string CreatePlayerKey(ulong contentId, string name, uint worldId, string worldName)
    {
        if (contentId != 0)
            return $"cid:{contentId}";
        return $"name:{name.Trim().ToUpperInvariant()}|world:{worldId}|{worldName.Trim().ToUpperInvariant()}";
    }

    private sealed class ActiveRun
    {
        public required CharacterRecord Character { get; init; }
        public uint ContentFinderConditionId { get; init; }
        public uint TerritoryTypeId { get; set; }
        public string ContentNameEnglish { get; init; } = string.Empty;
        public string ContentNameGerman { get; init; } = string.Empty;
        public DateTime StartedUtc { get; init; }
        public int? GilStart { get; init; }
        public int? LastKnownGil { get; set; }
        public bool DutyStarted { get; set; }
        public int WipeCount { get; set; }
        public bool RecoveredSession { get; set; }
        public Dictionary<string, EncounteredPlayerRecord> Players { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ActiveRunSnapshot ToSnapshot(DateTime now) => new()
        {
            CharacterContentId = Character.ContentId,
            CharacterName = Character.Name,
            CharacterHomeWorldId = Character.HomeWorldId,
            CharacterHomeWorldName = Character.HomeWorldName,
            ContentFinderConditionId = ContentFinderConditionId,
            TerritoryTypeId = TerritoryTypeId,
            ContentNameEnglish = ContentNameEnglish,
            ContentNameGerman = ContentNameGerman,
            StartedUtc = StartedUtc,
            LastSnapshotUtc = now,
            GilStart = GilStart,
            LastKnownGil = LastKnownGil,
            DutyStarted = DutyStarted,
            WipeCount = WipeCount,
            RecoveredSession = RecoveredSession,
            Players = Players.Values.ToList()
        };

        public static ActiveRun FromSnapshot(ActiveRunSnapshot snapshot)
        {
            var run = new ActiveRun
            {
                Character = new CharacterRecord
                {
                    ContentId = snapshot.CharacterContentId,
                    Name = snapshot.CharacterName,
                    HomeWorldId = snapshot.CharacterHomeWorldId,
                    HomeWorldName = snapshot.CharacterHomeWorldName,
                    FirstSeenUtc = snapshot.StartedUtc,
                    LastSeenUtc = snapshot.LastSnapshotUtc
                },
                ContentFinderConditionId = snapshot.ContentFinderConditionId,
                TerritoryTypeId = snapshot.TerritoryTypeId,
                ContentNameEnglish = snapshot.ContentNameEnglish,
                ContentNameGerman = snapshot.ContentNameGerman,
                StartedUtc = snapshot.StartedUtc,
                GilStart = snapshot.GilStart,
                LastKnownGil = snapshot.LastKnownGil,
                DutyStarted = snapshot.DutyStarted,
                WipeCount = snapshot.WipeCount,
                RecoveredSession = true
            };

            foreach (var player in snapshot.Players ?? new())
                run.Players[CreatePlayerKey(player.ContentId, player.Name, player.WorldId, player.WorldName)] = player;

            return run;
        }
    }
}
