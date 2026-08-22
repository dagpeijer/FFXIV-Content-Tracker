using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using System;

namespace ContentTracker;

public sealed class SessionGilTracker : IDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IGameInventory gameInventory;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly TrackerStore store;

    private DateTime nextUpdateUtc = DateTime.MinValue;
    private ulong sessionCharacterContentId;
    private uint sessionHomeWorldId;

    public bool IsActive { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public int? StartGil { get; private set; }
    public int? CurrentGil { get; private set; }

    public int? GilDelta =>
        StartGil.HasValue && CurrentGil.HasValue
            ? CurrentGil.Value - StartGil.Value
            : null;

    public string CharacterName { get; private set; } = string.Empty;
    public string HomeWorldName { get; private set; } = string.Empty;

    public string CharacterDisplayName =>
        string.IsNullOrWhiteSpace(HomeWorldName)
            ? CharacterName
            : $"{CharacterName} @ {HomeWorldName}";

    public SessionGilTracker(
        IClientState clientState,
        IPlayerState playerState,
        IGameInventory gameInventory,
        IFramework framework,
        IPluginLog log,
        TrackerStore store)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.gameInventory = gameInventory;
        this.framework = framework;
        this.log = log;
        this.store = store;

        framework.Update += OnFrameworkUpdate;
        clientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.Logout -= OnLogout;

        CompleteSession("Plugin beendet");
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;

        if (now < nextUpdateUtc)
            return;

        nextUpdateUtc = now + UpdateInterval;

        if (!clientState.IsLoggedIn)
        {
            if (IsActive)
                CompleteSession("Logout");

            return;
        }

        // Während Ladebildschirmen kann PlayerState kurz nicht verfügbar sein.
        // Die Session soll dadurch nicht künstlich beendet werden.
        if (!playerState.IsLoaded || playerState.ContentId == 0)
            return;

        if (!IsActive)
        {
            StartSession(now);
        }
        else if (sessionCharacterContentId != playerState.ContentId)
        {
            CompleteSession("Charakterwechsel");
            StartSession(now);
        }

        if (TryGetGil(out var gil))
            CurrentGil = gil;
    }

    private void StartSession(DateTime now)
    {
        Reset();

        sessionCharacterContentId = playerState.ContentId;
        sessionHomeWorldId = playerState.HomeWorld.RowId;
        CharacterName = playerState.CharacterName;
        HomeWorldName = playerState.HomeWorld.IsValid
            ? playerState.HomeWorld.Value.Name.ExtractText()
            : string.Empty;

        StartedUtc = now;
        IsActive = true;

        if (TryGetGil(out var gil))
        {
            StartGil = gil;
            CurrentGil = gil;
        }

        log.Information(
            "ContentTracker: Gil-Session gestartet für {Character} mit {Gil} Gil.",
            CharacterDisplayName,
            StartGil?.ToString("#,##0") ?? "?");
    }

    private void OnLogout(int type, int code)
    {
        CompleteSession("Logout");
    }

    private void CompleteSession(string reason)
    {
        if (!IsActive || !StartedUtc.HasValue)
            return;

        if (TryGetGil(out var gil))
            CurrentGil = gil;

        var endedUtc = DateTime.UtcNow;
        var record = new GilSessionRecord
        {
            Id = store.GetNextGilSessionId(),
            CharacterContentId = sessionCharacterContentId,
            CharacterName = CharacterName,
            CharacterHomeWorldId = sessionHomeWorldId,
            CharacterHomeWorldName = HomeWorldName,
            StartedUtc = StartedUtc.Value,
            EndedUtc = endedUtc,
            DurationSeconds = Math.Max(
                0,
                (long)Math.Round((endedUtc - StartedUtc.Value).TotalSeconds)),
            GilStart = StartGil,
            GilEnd = CurrentGil,
            GilDelta = StartGil.HasValue && CurrentGil.HasValue
                ? CurrentGil.Value - StartGil.Value
                : null,
            EndReason = reason
        };

        store.AddGilSession(record);

        log.Information(
            "ContentTracker: Gil-Session gespeichert: {Character}, Gil {Delta}, Ende: {Reason}",
            record.CharacterDisplayName,
            record.GilDelta?.ToString("+#,##0;-#,##0;0") ?? "?",
            reason);

        Reset();
    }

    private void Reset()
    {
        IsActive = false;
        StartedUtc = null;
        StartGil = null;
        CurrentGil = null;
        sessionCharacterContentId = 0;
        sessionHomeWorldId = 0;
        CharacterName = string.Empty;
        HomeWorldName = string.Empty;
        nextUpdateUtc = DateTime.MinValue;
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
            log.Warning(ex, "ContentTracker: Session-Gil konnte nicht gelesen werden.");
            return false;
        }
    }
}
