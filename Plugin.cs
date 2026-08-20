name: Build and Release

on:
  push:
    tags:
      - 'v*'
  workflow_dispatch:

permissions:
  contents: write

jobs:
  build:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build Release
        run: dotnet build -c Release --no-restore

      - name: Prepare package
        shell: pwsh
        run: |
          $version = '${{ github.ref_name }}'
          if ([string]::IsNullOrWhiteSpace($version) -or $version -eq 'main') {
            $version = 'manual-${{ github.run_number }}'
          }

          $packageDir = Join-Path $PWD 'release-package'
          New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

          Copy-Item 'bin/Release/*' $packageDir -Recurse -Force
          Copy-Item 'ContentTracker.json' $packageDir -Force
          Copy-Item 'LICENSE' $packageDir -Force

          Get-ChildItem $packageDir -Recurse -Directory -Filter 'ref' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
          Get-ChildItem $packageDir -Recurse -Include '*.pdb','*.xml' | Remove-Item -Force -ErrorAction SilentlyContinue

          $zipName = "ContentTracker-$version.zip"
          Compress-Archive -Path "$packageDir/*" -DestinationPath $zipName -Force
          "ZIP_NAME=$zipName" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append

      - name: Upload build artifact
        uses: actions/upload-artifact@v4
        with:
          name: ContentTracker-${{ github.ref_name }}
          path: ${{ env.ZIP_NAME }}

      - name: Create GitHub Release
        if: startsWith(github.ref, 'refs/tags/v')
        uses: softprops/action-gh-release@v2
        with:
          files: ${{ env.ZIP_NAME }}
          generate_release_notes: true
          draft: false
          prerelease: false
