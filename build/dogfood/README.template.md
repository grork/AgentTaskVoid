# {IdentityName} dogfood kit

An unreleased build of `{Command}` and its host plugins, packaged for handing
to another machine. Nothing here needs a clone of the source repository.

## What's in this folder

```
{IdentityName}_<version>_x64_arm64.msixbundle   — the signed app, both architectures
{Command}-plugin-claude-code.zip                — Claude Code plugin
{Command}-plugin-copilot-cli.zip                — Copilot CLI plugin
install.ps1
uninstall.ps1
README.md                                       — this file
```

There is no certificate file. `install.ps1` reads the signing certificate
from the bundle's own signature and asks before trusting it.

## Install

```powershell
.\install.ps1
```

Trusts the bundle's signing certificate (one admin prompt, explained before
it happens), installs the bundle with `Add-AppxPackage`, then prompts to wire
each detected host's plugin. Pass `-SkipPluginWiring` to install the app
only.

After install, a fresh terminal's `{Command} doctor` reports the installed
identity and whether the `AppTaskInfo` API is supported on this machine.

## Manual plugin install

Use these if `install.ps1` didn't detect your host, or you declined its
prompt.

### Claude Code

Expand `{Command}-plugin-claude-code.zip`, then from that folder:

```powershell
claude plugin marketplace add <path to the expanded folder>
claude plugin install atv-integration@agent-task-void
```

Confirm with `claude plugin list`. `{Command} doctor` must report identity
present and the API supported before any card can render.

### Copilot CLI

Auto-wiring for Copilot CLI isn't part of this kit yet. Expand
`{Command}-plugin-copilot-cli.zip` and load it directly:

```powershell
copilot --plugin-dir <path to the expanded folder>\plugins\atv-integration
```

## Uninstall

```powershell
.\uninstall.ps1
```

Unwires any plugin `install.ps1` wired, removes the `{IdentityName}` package
(taskbar cards and app data go with it), and offers to remove the trusted
development certificate (left trusted by default).

Run this before installing a future release signed with a real certificate —
a different signing certificate means a different package identity, so an
old dogfood install and a new real-cert install can't upgrade in place.
