# RetroRewind ModHub

RetroRewind ModHub is a Windows desktop application for managing mods and related game-development/modding workflows.

The project is currently maintained as a .NET 10 WPF application targeting Windows x64.

## Current Features

### Mod Management
- Browse and manage locally installed mods.
- Install and remove mod content.
- Organize mod files and related game content.
- Validate mod installations and identify common issues.

### Asset Workshop
- Work with supported Unreal Engine/game assets.
- Inspect and manage asset-related content used by supported workflows.

### Save Management
- Manage supported save-game files.
- Create and work with local save backups.

### Video Tools
- Tools for supported video/media workflows used by the project.

### Merge Mods
- Combine supported mod content through the application's merge workflow.

### Health & Validation
- Validate project/mod content.
- Surface errors and warnings to help diagnose installation problems.

### Nexus Mods Integration
The application includes an existing Nexus Mods integration layer for supported account and mod-management operations, including:
- User/account validation.
- Tracked mods.
- Mod endorsements.
- Nexus mod metadata.
- Mod file lists.
- Downloads and download links.
- MD5 lookup.
- Opening Nexus Mods pages.
- Nexus SSO groundwork.

The Nexus integration is designed around user-initiated actions and locally protected credentials. Further API/SSO capabilities are subject to Nexus Mods approval and API requirements.

### Steam Integration
- Supported Steam account/game-related functionality used by the application.
- Local credential protection is used where credentials are required.

### Localization
The project includes localization resources for supported languages.

## Requirements

- Windows x64
- .NET 10 / Windows Desktop runtime as required by the selected build configuration
- A supported installation of the target game(s) for the relevant mod-management features

## Building

The project can be built using the included build scripts or from Visual Studio / the .NET CLI.

The primary project file is:

`RetroRewindModhub.csproj`

The repository includes a release build script:

`build_release.bat`

## Project Structure

Key areas of the project include:

- `RetroRewindModhub.csproj` — main WPF project
- `MainWindow_*.cs` — application UI and feature modules
- `Nexus*.cs` — Nexus Mods integration
- `SteamSecretStore.cs` — local Steam credential protection
- `AccountProfileCache.cs` — local account/profile caching
- `Localization/` — localization resources
- `Documentation/` — project documentation and handoff information
- `build_release.bat` — release build helper

## Source Control

The stable 1.0.3 baseline is maintained on the `main` branch.

The `development` branch is used for ongoing development work.

## Security

Local credentials and secrets should never be committed to source control.

The repository's `.gitignore` excludes local credential/configuration files such as:

- `nexus_api_key.dat`
- `.env` files
- `secrets.json`
- credential/configuration files
- local download folders
- build output

Nexus credentials stored by the application use Windows-provided protection mechanisms rather than storing the user's API key directly in source code.

## Third-Party Components

RetroRewind ModHub uses third-party libraries, frameworks, fonts, and other components. Those components remain subject to their respective licenses and notices.

Notable project dependencies include libraries distributed under licenses such as MIT, Apache-2.0, and BSD-3-Clause. Third-party license files/notices included with the project must be preserved where applicable.

The MIT license in the root `LICENSE` file applies to the original RetroRewind ModHub code authored by the project copyright holder. It does **not** replace or relicense third-party components.

## License

RetroRewind ModHub's original source code is licensed under the **MIT License**.

See the [`LICENSE`](LICENSE) file for the complete license text.

Third-party libraries, fonts, assets, and other components remain under their respective licenses.

## Nexus Mods

Nexus Mods integration is included for supported functionality, but the project does not claim to be an official Nexus Mods product.

Any future public API, OAuth/SSO, download, or automatic-update functionality will be implemented only in accordance with the applicable Nexus Mods policies, API requirements, and approval where required.

## Project Status

RetroRewind ModHub 1.0.3 is an active development project.

The repository is being made available so that the project can be reviewed and development can continue in an open and documented manner.
