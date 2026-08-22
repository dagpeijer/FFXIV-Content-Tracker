ContentTracker v0.4.3 – Patch readiness

Changed / added:
- BuildInfo.cs (new)
- ContentTracker.csproj
- Models.cs
- TrackerStore.cs
- MainWindow.cs
- .github/workflows/main.yml
- PATCH_UPDATE.md (new)

Purpose:
- Central compatibility metadata for Dalamud API and data schema.
- pluginmaster.json gets DalamudApiLevel automatically from ContentTracker.csproj.
- Plugin settings show plugin version, Dalamud API, database schema and target framework.
- Data schema number is no longer duplicated in C# source.

After replacing the files:
dotnet restore
dotnet build -c Debug

Do not tag/release before the local build and in-game check pass.
