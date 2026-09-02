# RetroRewind ModHub

RetroRewind ModHub is a Windows desktop application for managing and working with RetroRewind mods, assets, saves, and related tools from a single interface.

## Current Version

**1.0.3**

This repository reflects the current 1.0.3 source baseline.

## Features

### Mod Management

* Enable and disable mods
* Install and remove mods
* Import and export mod content
* Required-file checking
* Conflict checking
* Mod information and validation
* Mod organization and management

### Asset Workshop

* Work with RetroRewind assets
* Import and manage images
* Transfer assets
* Save and restore asset information
* Open relevant save folders

### Save Management

* Manage RetroRewind save data
* Open save folders
* Transfer save-related content
* Save and restore information

### Video Tools

* Video management and playback tools
* MP4 selection and processing
* Video editor controls
* Video download functionality
* Poster and video-related asset handling

### Merge Mods

Tools for working with and merging mod content.

### Health & Validation

Built-in tools for checking project/mod state and identifying invalid or conflicting content.

### Nexus Mods Integration

The current version contains Nexus Mods integration for supported functionality, including:

* Nexus user validation
* Tracked mods
* Mod information
* Mod file information
* Downloads
* Download links
* MD5-based file lookup
* Endorsements
* Nexus page access
* Nexus SSO groundwork

Nexus authentication and API functionality are implemented in the current source and are subject to the requirements and policies of Nexus Mods.

### Steam Integration

The application includes Steam-related verification and local credential handling functionality.

### Localization

RetroRewind ModHub includes localization resources for multiple languages, including:

* Arabic
* Bulgarian
* Chinese Simplified
* Chinese Traditional
* Croatian
* Czech
* Danish
* Dutch
* English
* Finnish
* French
* German
* Greek
* Hebrew
* Hungarian
* Italian
* Japanese
* Korean
* Norwegian
* Polish
* Portuguese
* Portuguese (Brazil)
* Romanian
* Russian
* Slovak
* Slovenian
* Spanish
* Swedish
* Turkish
* Ukrainian

## Requirements

The project targets:

* Windows
* x64
* .NET 10
* WPF

The release configuration is designed for Windows self-contained deployment.

## Building

The repository contains build scripts for Windows releases.

Relevant scripts include:

```text
build_windows.bat
build_release.bat
BuildRelease.cmd
```

The project file is:

```text
RetroRewindModhub.csproj
```

A compatible .NET 10 SDK is required to build the project.

## Project Structure

```text
RetroRewindModhub/
├── Assets/                 Application images, icons and fonts
├── Documentation/          Project documentation
├── Engine/                 Supporting engine functionality
├── Localization/           Localization resources
├── Tools/                  Supporting tools
├── App.xaml                WPF application definition
├── MainWindow.xaml         Main application window
├── MainWindow_*.cs         Main application functionality
├── Nexus*.cs               Nexus Mods integration
├── Steam*.cs               Steam integration
├── RetroRewindModhub.csproj
└── build_*.bat             Build scripts
```

## Source Control

The repository uses Git.

### Branches

`main` represents the stable project baseline.

`development` is used for ongoing development.

Feature work should be developed separately and merged into `development` when appropriate.

## Security

Local credentials and generated files are intentionally excluded from source control.

The repository's `.gitignore` excludes local credential files, build output, downloads, and other machine-specific data.

**Never commit API keys, passwords, authentication tokens, or other private credentials to this repository.**

## Third-Party Components

RetroRewind ModHub includes or uses third-party components and assets. Their respective license files and notices are retained in the repository where applicable.

Font license files are located under:

```text
Assets/Fonts/
```

## Status

**Version 1.0.3 — Current baseline**

This README describes the application as it exists in the 1.0.3 source baseline. Features not present in the current release are intentionally not described as implemented.

## Credits

RetroRewind ModHub is developed as part of the RetroRewind project.

Third-party software, assets, fonts, and services remain the property of their respective authors and are subject to their applicable licenses and terms.
