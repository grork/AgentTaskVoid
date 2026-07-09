# Agentaskvoid (atv)

A small CLI tool for managing [AppTaskInfo](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.tasks.apptaskinfo) entries — persistent tasks shown as their own separate icons on the Windows 11 taskbar, grouped independently of any running app window. This is unrelated to jump lists.

## Requirements

- Windows 11 build 26100+ (with `AppTaskContract` v2.0 registered)
- .NET 10 SDK
- Windows SDK 10.0.26100.0
- Developer Mode on (Settings > Privacy & security > For developers) — needed for the dev loose-layout package registration `dotnet run` does automatically

## Setup

Package identity is provisioned automatically, full-package MSIX model, no manual registration script:

```powershell
dotnet build
dotnet run
```

See `CLAUDE.md` for how the dev loop and release NativeAOT build work.

## Usage

```
atv create <title> [subtitle]
atv list
atv clear
```