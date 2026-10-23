<div align="center">

# NavisHelper

**A panel and an AI assistant for Autodesk Navisworks Manage 2024–2027**

Tell Claude, Codex or Cursor what to do with your model, and NavisHelper does it in the Navisworks window you have open.

[![Latest release](https://img.shields.io/github/v/release/mikhalchankasm/NavisWorksMaster?label=release)](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest)
[![Navisworks](https://img.shields.io/badge/Navisworks_Manage-2024%E2%80%932027-0696D7)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[⬇ Download the installer](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest)** · [What it does](#what-it-does) · [Install](#install) · [Русский](README.ru.md)

</div>

<!-- Promo video: insert here. -->

## What it does

**In the Navisworks panel, no AI needed**
- find, select, hide and color items;
- selection and search sets, saved viewpoints, section boxes and markups;
- export properties and model-tree names;
- Clash Detective: groups, isolation, viewpoints, reports.

**With AI** (Claude, Codex, Cursor and others)
- describe the task in plain words; the AI does it through NavisHelper's 120 tools;
- the AI shows each change before making it, and makes it only after confirmation;
- in **protected mode** the AI cannot change the document until you allow it in the panel.

## What you can ask

> "Find all pumps, select them and zoom to them."

> "Export the properties of the selected items to Excel."

> "Show the active clashes in `HVAC vs Structure` and isolate the first one."

> "Make a saved viewpoint with a section box around the selection."

> "Color the model by system, but show me the result first."

Three complete workflows, with what to check after each step, are in the [user workflows (Russian)](docs/USER_WORKFLOWS.md).

## Install

Download `NavisHelperSetup-<version>.exe` from the [latest release](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest) and run it. Close Navisworks first. Administrator rights are not required.

1. **Open Navisworks.** The ribbon gets a **NavisHelper** tab; its **NavisHelper** button opens the panel.
2. **Restart the AI apps you use.** If the box on the installer's last page stayed checked, NavisHelper is already connected to them.
3. **Claude Desktop:** open Settings → Extensions, install an extension from a file and pick `NavisHelper.mcpb`. The Start-menu shortcut "NavisHelper extension for Claude Desktop" opens its folder.

Other ways to install (ZIP, installation by an AI agent, manual client setup) are in the [installation guide](docs/MCP_DISTRIBUTION_PLAN.md).

### Requirements

| | |
|---|---|
| Navisworks | Autodesk Navisworks Manage 2024, 2025, 2026 or 2027, Windows x64 |
| Microsoft component | .NET 9 Runtime; the installer offers to download it if it is missing |
| AI app | any app that can connect MCP servers: Claude Code, Claude Desktop, Codex, Cursor and others. The panel works without one |

### Verify the download

For those who want to check that the installer is intact and matches the published one.

<details>
<summary>Download, check SHA256 and run from PowerShell</summary>

Run these three blocks in the same PowerShell session:

```powershell
$release = Invoke-RestMethod `
  "https://api.github.com/repos/mikhalchankasm/NavisWorksMaster/releases/latest"
$installerAsset = $release.assets |
  Where-Object name -Like "NavisHelperSetup-*.exe" |
  Select-Object -First 1
$checksumsAsset = $release.assets |
  Where-Object name -Like "SHA256SUMS*.txt" |
  Select-Object -First 1
if (-not $installerAsset -or -not $checksumsAsset) {
  throw "Required release assets were not found."
}
```

```powershell
$installer = Join-Path $env:TEMP $installerAsset.name
$checksums = Join-Path $env:TEMP $checksumsAsset.name
Invoke-WebRequest $installerAsset.browser_download_url -OutFile $installer
Invoke-WebRequest $checksumsAsset.browser_download_url -OutFile $checksums
$line = Get-Content $checksums |
  Where-Object { $_ -match "^\s*[0-9A-Fa-f]{64}\s+\*?$([regex]::Escape($installerAsset.name))\s*$" } |
  Select-Object -First 1
$expected = ($line -split "\s+")[0]
$actual = (Get-FileHash $installer -Algorithm SHA256).Hash
if (-not $expected -or $actual -ne $expected) {
  throw "Installer checksum verification failed."
}
```

```powershell
if (Get-Process Roamer -ErrorAction SilentlyContinue) {
  throw "Close Navisworks before installing."
}
Start-Process $installer -Wait
```

</details>

## Safety

- **Everything stays local.** NavisHelper opens no network ports; the AI app talks to Navisworks only on your computer.
- **Preview first.** Most commands that change the model show what they will do and run only with an explicit confirmation.
- **Protected mode.** Turn it on in the panel settings, and every change an agent makes to the document waits for your "Allow".

More in [security and compatibility](docs/SECURITY_AND_COMPATIBILITY.md).

## Documentation

| If you want to | Read |
|---|---|
| Start working with AI | [Quickstart](docs/NAVISWORKS_MCP_QUICKSTART.md) |
| Follow ready-made workflows | [User workflows (Russian)](docs/USER_WORKFLOWS.md) |
| Set up AI apps | [Client guide](docs/MCP_CLIENT_GUIDE.md) |
| See what is new | [Releases](https://github.com/mikhalchankasm/NavisWorksMaster/releases) |
| Read product details, the comparison and verification results | [Project details](docs/reference/PROJECT_DETAILS.md) |

## For developers

The source registers **120 distinct MCP tools**. `scripts/check_mcp_command_catalog.py` derives their names from the 120 `[McpServerTool]` methods and verifies the generated 120-row index. Inputs, outputs and limits of every tool are in the [tool contracts](docs/MCP_TOOL_CONTRACTS.md); the design is in the [architecture](docs/MCP_ARCHITECTURE.md). Building and contributing: [Contributing](CONTRIBUTING.md) and the [build rules](BUILD_BUNDLE_RULES.md).

## License and status

The code is available under the [MIT License](LICENSE); third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). NavisHelper is maintained at low activity: pull requests are welcome, responses to issues are not guaranteed.

<sub>Autodesk and Navisworks are registered trademarks or trademarks of Autodesk, Inc. and/or its subsidiaries and/or affiliates. NavisHelper is an independent project and is not affiliated with, authorized by, endorsed by, sponsored by, or otherwise approved by Autodesk, Inc. See Autodesk's [trademark list](https://www.autodesk.com/company/legal-notices-trademarks/intellectual-property/trademarks) and [guidelines for compatible products](https://www.autodesk.com/company/legal-notices-trademarks/trademarks/guidelines-for-use).</sub>
