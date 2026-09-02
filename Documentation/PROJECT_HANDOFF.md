# Retro Rewind ModHub — Current Project Handoff

## Purpose

Retro Rewind ModHub is a Windows WPF mod-management application for Retro Rewind. This file is the **current project documentation and handoff**. Development notes, intermediate fix artifacts, and historical scripts are not part of the release source package.

If source code and this document disagree, **the source code is authoritative**.

---

## Current build

- Target framework: **.NET 10 / `net10.0-windows`**
- Runtime: **Windows x64**
- UI: **WPF**
- Assembly/application version in the current project: **1.0.0**
- Root namespace: `RogueUnicorn.Modhub`
- Self-contained Windows publish is configured.
- Single-file publish is configured.
- Trimming is disabled.
- SDK selection is controlled by `global.json` (`10.0.100`, rolling forward to the latest feature band).

### Main project

`RetroRewindModhub.csproj`

Important current packages include:

- WebView2
- SharpVectors.Wpf
- LibVLCSharp.WPF
- VideoLAN.LibVLC.Windows
- Unpaker
- UAssetAPI
- CUE4Parse
- CUE4Parse-Conversion
- SkiaSharp native Win32 assets
- SharpCompress
- System.Diagnostics.PerformanceCounter

---

## Source layout

The main application is split across:

- `MainWindow.xaml` / `MainWindow.xaml.cs` — main shell and shared state.
- `MainWindow_CoreFeatures.cs` — core application behavior.
- `MainWindow_ModManager.cs` — Mod Manager UI and mod-list behavior.
- `MainWindow_ModManagerOperations.cs` — Mod Manager operations.
- `MainWindow_AssetWorkshop.cs` — Asset Workshop.
- `MainWindow_DownloadsAndInstall.cs` — downloads/install pipeline.
- `MainWindow_MergeMods.cs` — Merge Mods page and merge workflow.
- `MainWindow_Performance.cs` — performance-related features.
- `MainWindow_SettingsAndShell.cs` — settings, shell, startup and related behavior.
- `ExternalTextureInjectorBridge.cs` — small process bridge used by the app to launch the **separate** `RRModHubTextureInjector.exe`.
- `Engine/` — bundled GVAS engine resources.
- `Localization/` — localization JSON.
- `Assets/` — application icons, images and fonts.
- `Themes/` — WPF themes/styles.
- `Tools/repak/` — helper installer for the external/upstream repak tool.

### Standalone tools

`RRModHubTextureInjector` and `RetroRewindPakTool` are **not part of the main application build**.

They are maintained/built separately.

The main app may launch the standalone texture injector through `ExternalTextureInjectorBridge`, but its implementation is not compiled into the ModHub application.

---

# Building the main app

## Recommended build

From the project root:

```bat
BuildRelease.cmd
```

This performs a self-contained `win-x64` publish and creates the release output under:

```text
RetroRewindModhub\
```

The release contains:

```text
RetroRewindModhub\
    RetroRewindModhub.exe
    RetroRewindModHub_Data\
        Engine\
        Localization\
        RRModHubTextureInjector.exe
```

The exact release-copy behavior is defined by `BuildRelease.cmd`.

## Alternate build scripts

`build_windows.bat` performs a direct .NET publish.

`build_release.bat` is the older/full release script that also builds the Python GVAS engine with PyInstaller. Prefer `BuildRelease.cmd` unless the bundled-engine build specifically requires the older workflow.

### Requirements

- .NET 10 SDK.
- Python 3.10+ only when using the script that rebuilds the GVAS engine.
- PyInstaller when using `build_release.bat` to rebuild the engine.

---

# External RRModHubTextureInjector

The texture injector is a **separate executable/project** and is not compiled into the main ModHub application.

The release layout is:

```text
RetroRewindModhub.exe
RetroRewindModHub_Data\
    Engine\
    Localization\
    RRModHubTextureInjector.exe
```

`ExternalTextureInjectorBridge.cs` now resolves `RRModHubTextureInjector.exe` directly from `RetroRewindModHub_Data` beside the application executable. It no longer searches the Documents tools folder or the system `PATH`.

If `RRModHubTextureInjector.exe` is present beside the project files when `BuildRelease.cmd` or `build_release.bat` runs, the release script copies it into `RetroRewindModHub_Data`. The uploaded main-app source package does not itself contain the injector binary.

Do not copy the injector source back into the main application project.

# Runtime/tool locations

Keep the bundled application data beside the ModHub executable:

```text
RetroRewindModhub.exe
RetroRewindModHub_Data\
    Engine\
    Localization\
    RRModHubTextureInjector.exe
```

The `RetroRewindModHub_Data` folder is the application-bundled data directory.

---

# Development rules / important project boundaries

1. **Do not reintroduce the standalone injector implementation into the main app.**
2. Keep `RRModHubTextureInjector` as its own executable/project.
3. Keep `RetroRewindPakTool` as its own executable/project.
4. Do not blindly copy third-party tool source into ModHub.
5. When integrating a third-party executable, use a process bridge and document its expected location.
6. Prefer the current source behavior over old release/fix notes.
7. When changing PAK parsing/writing, preserve the existing V8/V11 regression tests in the standalone PakTool.
8. Do not assume an experimental parser feature is production-ready just because an individual fixture works.
9. Keep external tool paths consistent with the current app layout.
10. After major UI changes, verify the actual WPF layout/XAML remains valid before continuing feature work.

---

# Current project state

The main application currently contains the Mod Manager, Downloads, Asset Workshop, Videos, Settings and Merge Mods work described above.

The separate tool work has been deliberately split out:

- **RetroRewindTextureInjector** — standalone texture export/replace executable.
- **RetroRewindPakTool** — standalone PAK reader/writer project.

This main project should remain focused on the ModHub application and its UI/integration code.

---

# Historical documentation policy

Development notes, intermediate fix artifacts, and historical scripts are intentionally excluded from the release source package. This document is the maintained project handoff.

Do not recreate a large collection of versioned fix-note Markdown files unless a future workflow genuinely requires separate release notes.

For future development, update this document when a change materially affects:

- architecture,
- build requirements,
- external tools,
- current feature behavior,
- project boundaries,
- runtime layout,
- or an important limitation.
