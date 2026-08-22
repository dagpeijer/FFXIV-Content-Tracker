using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.IO;

namespace ContentTracker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/ctt";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IClientState clientState;
    private readonly ICommandManager commandManager;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly TrackerStore store;
    private readonly ExcelExporter exporter;
    private readonly DutyTracker tracker;
    private readonly SessionGilTracker sessionGilTracker;
    private readonly WindowSystem windowSystem = new("ContentTracker");
    private readonly MainWindow mainWindow;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IPlayerState playerState,
        IDutyState dutyState,
        IDataManager dataManager,
        IGameInventory gameInventory,
        IPartyList partyList,
        IFramework framework,
        ICommandManager commandManager,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.clientState = clientState;
        this.commandManager = commandManager;
        this.log = log;

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(pluginInterface);
        config.Save();

        store = new TrackerStore(pluginInterface.ConfigDirectory.FullName, log);
        exporter = new ExcelExporter(log);

        sessionGilTracker = new SessionGilTracker(
            clientState,
            playerState,
            gameInventory,
            framework,
            log);

        tracker = new DutyTracker(
            dutyState,
            clientState,
            playerState,
            dataManager,
            gameInventory,
            partyList,
            framework,
            log,
            store,
            OnRunFinished);

        mainWindow = new MainWindow(
            config,
            store,
            tracker,
            sessionGilTracker,
            ExportNow);

        windowSystem.AddWindow(mainWindow);

        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Öffnet den FFXIV Content Tracker."
        });

        pluginInterface.UiBuilder.Draw += DrawUi;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUi;
        pluginInterface.UiBuilder.OpenConfigUi += OpenMainUi;
        clientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        clientState.Logout -= OnLogout;
        pluginInterface.UiBuilder.Draw -= DrawUi;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUi;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenMainUi;

        commandManager.RemoveHandler(CommandName);

        tracker.Dispose();
        sessionGilTracker.Dispose();
        store.Save();

        if (config.ExportOnPluginDispose)
            SafeExport("Plugin-Ende");

        windowSystem.RemoveAllWindows();
    }

    private void DrawUi() => windowSystem.Draw();

    private void OpenMainUi() => mainWindow.IsOpen = true;

    private void OnCommand(string command, string arguments)
    {
        mainWindow.IsOpen = true;
    }

    private void OnLogout(int type, int code)
    {
        tracker.HandleLogout();
        store.Save();

        if (config.ExportOnLogout)
            SafeExport("Logout");
    }

    private void OnRunFinished()
    {
        if (config.ExportAfterDuty)
            SafeExport("Run-Ende");
    }

    private string ExportNow()
    {
        store.Save();
        return exporter.Export(store.Data, config.ExportDirectory);
    }

    private void SafeExport(string source)
    {
        try
        {
            var path = ExportNow();
            var fallback = !string.Equals(
                Path.GetFileName(path),
                "FFXIV_Content_Tracker.xlsx",
                StringComparison.OrdinalIgnoreCase);

            mainWindow.SetStatus(fallback
                ? $"Automatischer Export ({source}): Hauptdatei gesperrt, Ausweichdatei erstellt: {path}"
                : $"Automatischer Export ({source}) erfolgreich: {path}");
        }
        catch (Exception ex)
        {
            mainWindow.SetStatus($"Automatischer Export ({source}) fehlgeschlagen: {ex.Message}");
            log.Error(ex, "ContentTracker: Excel-Export bei {Source} fehlgeschlagen.", source);
        }
    }
}
