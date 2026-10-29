<div align="center">

# NavisHelper

**An AI assistant and a panel for Autodesk Navisworks Manage 2024–2027**

Tell Claude, Codex or Cursor what to do with your model, and NavisHelper does it in the Navisworks window you have open.

[![Latest release](https://img.shields.io/github/v/release/mikhalchankasm/NavisWorksMaster?label=release)](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest)
[![Navisworks](https://img.shields.io/badge/Navisworks_Manage-2024%E2%80%932027-0696D7)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[⬇ Download the installer](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest)** · [Ready-made requests](docs/prompts/animation/README.md) · [Русский](README.ru.md)

</div>

<!-- Promo video: a short fly-through clip goes here. -->

## What it does

- **Find and show.** Select items by name or property, hide, isolate and color them.
- **Clashes.** Review Clash Detective results, group them, isolate a clash, save viewpoints and reports.
- **Viewpoints and sections.** One viewpoint per item, section planes and boxes, markups.
- **Fly-throughs.** Animations from saved viewpoints, exported to PNG, JPG or AVI.
- **Reports.** Properties and model-tree names to Excel.

It works from the NavisHelper panel, or from any AI app that connects MCP servers. The AI shows each change first and makes it only after you confirm.

## Try asking

> "Find all pumps, select them and zoom to them."

> "Show the active clashes in `HVAC vs Structure` and isolate the first one."

> "Make a fly-through along the instrumentation route and export it to video."

> "Export the properties of the selected items to Excel."

More: [ready-made fly-through requests](docs/prompts/animation/README.md) · [step-by-step workflows (Russian)](docs/USER_WORKFLOWS.md)

## Install

Download `NavisHelperSetup-<version>.exe` from the [latest release](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest) and run it. Close Navisworks first; administrator rights are not required.

1. **Open Navisworks.** The ribbon gets a **NavisHelper** tab; its **NavisHelper** button opens the panel.
2. **Restart the AI apps you use.** If the box on the installer's last page stayed checked, NavisHelper is already connected to them.
3. **Claude Desktop:** Settings → Extensions → install from a file → `NavisHelper.mcpb`. The Start-menu shortcut "NavisHelper extension for Claude Desktop" opens its folder.

### Requirements

- Autodesk Navisworks Manage 2024, 2025, 2026 or 2027, Windows x64.
- .NET 9 Runtime; the installer offers to download it if it is missing.
- For AI: any app that connects MCP servers (Claude Code, Claude Desktop, Codex, Cursor and others). The panel works without one.

> [!IMPORTANT]
> **Viewpoint animations are created only in Navisworks 2026 and 2027.** The 2024 and 2025 API cannot create them, so there
> `viewpoint_animation` shows the plan but does not save it; listing and deleting animations work in every version.
> Exporting an animation to PNG, JPG or AVI was checked live on 2027 only.

<details>
<summary>Other ways to install</summary>

ZIP, installation by an AI agent and manual client setup are in the [installation guide](docs/MCP_DISTRIBUTION_PLAN.md).

</details>

### Verify the download

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
- **Preview first.** Commands that change the model show what they will do and run only after you confirm.
- **Protected mode.** Turn it on in the panel settings, and every change an agent makes waits for your "Allow".

More in [security and compatibility](docs/SECURITY_AND_COMPATIBILITY.md).

## Documentation

<details>
<summary>Guides, contracts and release notes</summary>

| If you want to | Read |
|---|---|
| Start working with AI | [Quickstart](docs/NAVISWORKS_MCP_QUICKSTART.md) |
| Follow ready-made workflows | [User workflows (Russian)](docs/USER_WORKFLOWS.md) |
| Set up AI apps | [Client guide](docs/MCP_CLIENT_GUIDE.md) |
| See what is new | [Releases](https://github.com/mikhalchankasm/NavisWorksMaster/releases) |
| Read product details, the comparison and verification results | [Project details](docs/reference/PROJECT_DETAILS.md) |
| Create a saved viewpoint animation from a route | [viewpoint_animation contract](docs/MCP_TOOL_CONTRACTS.md#viewpoint_animation) |
| Check an animation export and list the exporter's options | [viewpoint_animation_export contract](docs/MCP_TOOL_CONTRACTS.md#viewpoint_animation_export) |
| Copy a ready-made fly-through, section tour or clash walkthrough request, with quality presets and MP4 compression | [animation prompts (Russian and English)](docs/prompts/animation/README.md) |

</details>

## For developers

<details>
<summary>Tool count, contracts, architecture, building</summary>

The source registers **127 distinct MCP tools**. `scripts/check_mcp_command_catalog.py` derives their names from the 127 `[McpServerTool]` methods and verifies the generated 127-row index. Inputs, outputs and limits of every tool are in the [tool contracts](docs/MCP_TOOL_CONTRACTS.md); the design is in the [architecture](docs/MCP_ARCHITECTURE.md). Building and contributing: [Contributing](CONTRIBUTING.md) and the [build rules](BUILD_BUNDLE_RULES.md).

</details>

## License

[MIT License](LICENSE); third-party notices in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Maintained at low activity: pull requests are welcome, responses to issues are not guaranteed.

<sub>Autodesk and Navisworks are registered trademarks or trademarks of Autodesk, Inc. and/or its subsidiaries and/or affiliates. NavisHelper is an independent project and is not affiliated with, authorized by, endorsed by, sponsored by, or otherwise approved by Autodesk, Inc. See Autodesk's [trademark list](https://www.autodesk.com/company/legal-notices-trademarks/intellectual-property/trademarks) and [guidelines for compatible products](https://www.autodesk.com/company/legal-notices-trademarks/trademarks/guidelines-for-use).</sub>
