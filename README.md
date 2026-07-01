# AppTaskInfoCli

A small CLI tool for managing [AppTaskInfo](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.tasks.apptaskinfo) entries — persistent tasks shown as their own separate icons on the Windows 11 taskbar, grouped independently of any running app window. This is unrelated to jump lists.

## Requirements

- Windows 11 build 26100+ (with `AppTaskContract` v2.0 registered)
- .NET 10 SDK
- Windows SDK 10.0.26100.0

## Setup

Build once, then register the sparse package identity (required for the API to work — no signing needed):

```powershell
dotnet build
.\Register-Identity.ps1
```

## Usage

```
apptaskinfocli create <title> [subtitle]
apptaskinfocli list
apptaskinfocli clear
```

## Uninstall

```powershell
.\Unregister-Identity.ps1
```