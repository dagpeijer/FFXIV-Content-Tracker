# Releasing ContentTracker

Future releases are automated through GitHub Actions.

## Normal release flow

1. Commit and push the finished source code to `main`.
2. Create and push a version tag, for example:
   `v0.4.1`
3. GitHub Actions automatically:
   - downloads the Dalamud development files,
   - builds ContentTracker in Release mode,
   - sets the assembly version from the tag,
   - creates `ContentTracker-v0.4.1.zip`,
   - creates/updates the GitHub Release,
   - attaches the plugin ZIP,
   - updates `pluginmaster.json` on `main`.

Dalamud users who added this custom repository URL will then receive the new version:

https://raw.githubusercontent.com/dagpeijer/FFXIV-Content-Tracker/main/pluginmaster.json

## Manual test build

The workflow can also be started manually from the Actions tab.
A manual run only creates the `ContentTracker-build` artifact. It does not publish a release or change `pluginmaster.json`.
