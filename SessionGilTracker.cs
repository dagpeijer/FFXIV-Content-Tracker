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

    private DateTime nextUpdateUtc = DateTime.MinValue;
    private ulong sessionCharacterContentId;

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
        IPluginLog log)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.gameInventory = gameInventory;
        this.framework = framework;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
        clientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        clientState.Logout -= OnLogout;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        if (now < nextUpdateUtc)
            return;

        nextUpdateUtc = now + UpdateInterval;

        if (!clientState.IsLoggedIn || !playerState.IsLoaded || playerState.ContentId == 0)
        {
            if (IsActive)
                Reset();

            return;
        }

        if (!IsActive || sessionCharacterContentId != playerState.ContentId)
            StartSession(now);

        if (TryGetGil(out var gil))
            CurrentGil = gil;
    }

    private void StartSession(DateTime now)
    {
        Reset();

        sessionCharacterContentId = playerState.ContentId;
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
        Reset();
    }

    private void Reset()
    {
        IsActive = false;
        StartedUtc = null;
        StartGil = null;
        CurrentGil = null;
        sessionCharacterContentId = 0;
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
