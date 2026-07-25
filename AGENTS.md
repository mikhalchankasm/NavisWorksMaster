# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

Read [BUILD_BUNDLE_RULES.md](BUILD_BUNDLE_RULES.md) before changing build configurations, SDK version bindings, or bundle deployment logic.

## Parallel Development Roles

This repository is developed in parallel from two workstations. At the start of
work, determine which role was assigned to the current agent and read the
corresponding permanent workflow instructions before changing files:

- **Primary Developer:** read
  [docs/agent-roles/PRIMARY_DEVELOPER.md](docs/agent-roles/PRIMARY_DEVELOPER.md).
- **Secondary Developer:** read
  [docs/agent-roles/SECONDARY_DEVELOPER.md](docs/agent-roles/SECONDARY_DEVELOPER.md).

If the role has not been assigned, ask the user whether this workstation is
Primary or Secondary before starting development. Do not infer the role from
the current branch. Both roles must use one new task branch per task, created
from the current `origin/main`; direct development and direct push to `main`
are prohibited.

## External Claude Code Review

Before release prep, before risky parser/diagnostic/UX changes, and whenever the user asks for an external review, run a mandatory read-only Claude Code review. Prefer the installed Codex slash command `/claude-review`.

Rules:

- Prefer the repository wrapper `scripts/review/claude-review.ps1` when it exists. It keeps the review tool-less and avoids accidentally using `ANTHROPIC_API_KEY` instead of the local Claude Code subscription/session.
- If invoking Claude manually without the wrapper, Codex must collect the necessary repository context first and pipe that plain text bundle to Claude. Do not ask Claude to inspect the workspace itself.
- The review must be tool-less:
  `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/review/claude-review.ps1`
- Do not use `ANTHROPIC_API_KEY`.
- Do not pass `--bare`; local Claude Code is expected to work through the user's subscription/session.
- Do not allow Claude to edit files, run tools/commands, commit, push, publish, or perform release actions.
- Do not set a low `--max-budget-usd`; Claude Code may reject even short prompts.
- If Claude requests or implies a tool call, treat the external review as failed and continue with a Codex-only review.
- Treat Claude's output as review input. Apply fixes yourself after evaluating findings.

## Project Overview

NavisHelper is a C# plugin suite for Autodesk Navisworks Manage (2024/2025/2026/2027). It automates model manipulation tasks: bulk color assignment, attribute loading from CSV, clash detection, viewpoint management, and AI-driven object coloring via external API. The UI and documentation are in Russian.

## Documentation Language Policy

- Code comments, protocol contracts, MCP tool descriptions, `docs/MCP_*`, and agent-facing docs should be written in English.
- User-facing README sections, UI strings, prompts, release notes, and end-user workflow documentation should be written in Russian unless an existing file clearly uses English.

## Structural Ratchets

- A new feature family must not be introduced as another partial of
  `NavisHelperPanel`, `DocumentCommandService`, or an MCP tool container.
  New behavior gets its own type; existing partial files may be changed only
  when maintaining their existing feature family.
- Do not raise `scripts/navishelper_partial_limits.txt` merely to make a new
  partial compile. A deliberate boundary migration must reduce or preserve the
  reviewed count and explain why. The automated partial-count ratchet currently
  covers the compatibility god-classes listed in that file; the separate-type
  rule for MCP tool containers is enforced by review plus MCP catalog/stdin
  smoke checks.
- Every compiled class derived from `AddInPlugin` must either have an active
  `[Plugin]` attribute or be referenced from another compiled source file.
  `scripts/check_navishelper_compile.py` enforces this reachability guard.

## Build Commands

Build via Visual Studio 2022 or MSBuild CLI. Platform is always **x64**.

```bash
# Build for Navisworks 2026 (default)
msbuild NavisHelper.sln /p:Configuration=Release /p:Platform=x64

# Build for Navisworks 2024
msbuild NavisHelper.sln /p:Configuration=Release2024 /p:Platform=x64

# Build for Navisworks 2025
msbuild NavisHelper.sln /p:Configuration=Release2025 /p:Platform=x64

# Build for Navisworks 2026 (explicit)
msbuild NavisHelper.sln /p:Configuration=Release2026 /p:Platform=x64

# Build for Navisworks 2027
msbuild NavisHelper.sln /p:Configuration=Release2027 /p:Platform=x64
```

**Build configurations:** Debug, Release, Debug2024, Release2024, Debug2025, Release2025, Debug2026, Release2026, Debug2027, Release2027. The "plain" Debug/Release targets Navisworks 2026 by default.

**Output:** `NavisHelper/bin/x64/<Configuration>/NavisHelper.dll`

**Bundle copy:** `Debug2024`/`Release2024` copy the DLL to `NavisHelper.bundle/Contents/2024/NavisHelper.dll`. `Debug2025`/`Release2025` copy the DLL to `NavisHelper.bundle/Contents/2025/NavisHelper.dll`. `Debug`/`Release`/`Debug2026`/`Release2026` copy the DLL to `NavisHelper.bundle/Contents/2026/NavisHelper.dll`. `Debug2027`/`Release2027` copy the DLL to `NavisHelper.bundle/Contents/2027/NavisHelper.dll`. When creating packages or deployment artifacts, run the full build matrix first so local bundle assemblies exist for `2024`, `2025`, `2026`, and `2027`.

**Live smoke deployment:** Navisworks loads the per-user bundle from `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`, which can be a separate copy from the repository `NavisHelper.bundle`. A plain MSBuild updates the repository bundle but may leave the AppData bundle stale. Before live Navisworks smoke tests, deploy or copy the freshly built DLLs/configs to the AppData bundle, restart Navisworks, and verify the loaded assembly timestamp/SHA from the AppData path.

**Bundle binaries:** compiled DLL/PDB files under `NavisHelper.bundle/Contents/<version>/` are ignored build artifacts, not tracked source. Git keeps `PackageContents.xml`, `.dll.config`, `icons/`, and `ICONS.md`; release ZIPs/installers carry the compiled binaries from local build outputs.

**Requirements:** .NET Framework 4.8.1, Navisworks Manage SDK installed at `C:\Program Files\Autodesk\Navisworks Manage 20XX\`. The csproj conditionally references different API DLL versions based on the build configuration.

Initial automated tests exist for MCP-server pure helpers:

```bash
dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj /p:Configuration=Release
```

GitHub Actions runs the non-Navisworks CI subset: `NavisHelper.Contracts`, `NavisHelper.McpServer`, `NavisHelper.McpConfigurator`, and `NavisHelper.McpServer.Tests` on .NET 9. The full Navisworks plugin build matrix is local-only because CI runners do not have the Autodesk Navisworks SDK.

Most Navisworks-plugin behavior still requires build matrix and live Navisworks smoke/regression checks because it depends on the Autodesk runtime.

## Architecture

### Plugin System

Entry point is `RibbonLoader.cs` — a `CommandHandlerPlugin` decorated with `[Plugin]`, `[RibbonLayout]`, `[RibbonTab]`, and `[Command]` attributes. It routes ribbon button clicks to the corresponding `AddInPlugin` implementations via `Application.Plugins.ExecuteAddInPlugin()`.

The ribbon UI is defined in `CustomRibbon.xaml` (embedded resource) using Autodesk's AdWindows ribbon framework.

### Key Plugins (each is an `AddInPlugin` with its own `.addin` manifest)

- **ColorsByName** (`ColorsByName.cs`) — Core plugin. Reads a text file with `name;R,G,B;transparency` lines and applies colors to matching model items. Uses a 3-tier search fallback: internal property name → display name property → recursive display name matching.
- **AIColorObjects** (`AIColorObjects.cs`) — Colors selected objects using AI API. Delegates to `LocalColorBridge` which launches `ColorService.exe` (separate .NET 9.0 project in `ColorService/`) as a subprocess, communicating via JSON temp files in `%TEMP%`.
- **AIColorSchemeSelector** (`AIColorSchemeSelector.cs`) — UI for selecting from 10 predefined color schemes defined in `ColorSchemes.cs`.
- **CsvAttributeLoader** (`CsvAttributeLoader.cs`) — Bulk loads attributes from semicolon-delimited CSV files. Builds an indexed lookup via `SearchCondition`-based queries.
- **MarkupViewpoint** (`MarkupViewpoint.cs`) — Creates a saved viewpoint with red ellipse markups around each selected element from the current orthographic or perspective camera. It reuses the MCP `MarkupSelection` workflow and `View.ProjectPoint()` projection. Prompts for viewpoint name via WinForms dialog (with clipboard auto-fill).
- **ShortestDistanceMarker** (`ShortestDistanceMarker.cs`) — Compatibility command that opens the `Высоты Z` tab. The active workflow reads every selected item's bounding-box `Max Z`, then creates persistent vector labels or dimension lines from the top-face center to a configurable global Z level.
- **TopViewSection** (`TopViewSection.cs`) — Switches to orthographic top-down view, zooms to selected elements (`ZoomBox`), and enables section plane. Uses reflection to call internal `LcRmFrameworkInterface.ExecuteCommand()` for section toggle.
- **TopViewBoundingRect** (`TopViewBoundingRect.cs`) — Draws a bounding rectangle (4 `RedlineLine` segments) around all selected elements on the current view. Uses its own `View.ProjectPoint()`-based redline projection. Prompts for viewpoint name via WinForms dialog (with clipboard auto-fill).
- **AboutNavisHelper** (`AboutDialog.cs`) — Shows version and plugin information dialog.

### AI Integration Layer

`AIColorService.cs` calls the DeepSeek API (configurable endpoint). Configuration is loaded by `AIConfig.cs` from environment variables or `%APPDATA%\NavisHelper\ai_config.json`. `LocalColorBridge.cs` provides file-based IPC between the Navisworks process and the standalone `ColorService/` executable, with fallback to direct API calls.

### Core Utilities

- `Core/Logger.cs` — Static file-based logger writing to temp directory or alongside the model file.
- `Core/ColorParser.cs` — Parses `#AARRGGBB`, `#RRGGBB`, and `R,G,B` color formats.

### Navisworks API Patterns

Plugins access models via `Application.ActiveDocument`. Key API operations:
- **Selection:** `doc.CurrentSelection`, `ModelItemCollection`
- **Search:** `Search` class with `SearchCondition` (by internal name or display name)
- **Color override:** `doc.Models.OverridePermanentColor()` / `OverridePermanentTransparency()`
- **Progress:** `Application.BeginProgress()` / `EndProgress()` for long operations
- **Bounding box:** `selection.BoundingBox()` returns combined `BoundingBox3D` for a `ModelItemCollection`. Note: `BoundingBox3D.Copy()` does NOT exist.
- **Saved viewpoints:** `doc.SavedViewpoints.InsertCopy()` + `ReplaceFromCurrentView()` to save current view with redlines.

### Redline (Markup) JSON Format

Redlines are set/get via `activeView.SetRedlines(json)` / `activeView.GetRedlines()`.

- **Collection wrapper:** `{"Type":"RedlineCollection","Version":1,"Values":[...]}`
- **Line:** `{"Type":"RedlineLine","Version":1,"Thickness":3,"Color":[1,0,0],"Start":[x1,y1],"End":[x2,y2]}`
- **Ellipse:** `{"Type":"RedlineEllipse","Version":1,"Thickness":3,"Color":[1.0,0.0,0.0],"MinPoint":[x1,y1],"MaxPoint":[x2,y2]}`

Important gotchas:
- `RedlineLine` requires `Start`/`End` fields. Using `MinPoint`/`MaxPoint` is silently discarded.
- `RedlineFreehand` type is NOT supported by `SetRedlines()` — silently discarded.
- `RedlineArrow` type is NOT supported by `SetRedlines()` — it throws `ArgumentException` and rejects the entire collection. Navisworks XML `<rlarrow>` is a storage primitive, not a writable JSON primitive; convert arrows to three `RedlineLine` values before calling the API.
- Never infer the writable `View.SetRedlines()` type set from Saved Viewpoints XML. XML is a storage format; the JSON writer accepts a narrower, independently verified set.
- Color format varies: `RedlineLine` uses integers `[1,0,0]`, `RedlineEllipse` uses floats `[1.0,0.0,0.0]`.

### WorldToRedline Projection

3D-to-2D projection for redline coordinates uses the Navisworks camera quaternion layout `Rotation3D(A=X, B=Y, C=Z, D=W)` and the transposed rotation matrix for world-to-camera conversion, then:
- **Orthographic:** `rx = camX`, `ry = camY` (camera-space offsets from position)
- **Perspective:** `rx = -projX / (2 * tan(HeightField))`, `ry = -projY / (2 * tan(HeightField))` where `projX = camX / (-camZ)`. Note: this manual formula has known accuracy issues for perspective views. The official `View.ProjectPoint()` API is the recommended approach for precise projection.

Top-down view quaternion: `Rotation3D(0, 0, 0, -1)` (X=0, Y=0, Z=0, W=-1), which is the identity orientation up to quaternion sign.

Projection regression tests must include a rotated camera, preferably with roll. A top-view-only test is invalid because `(0,0,0,-1)` can mask a broken quaternion layout. Never introduce a compensating sign or constant merely to match one camera; fix the coordinate model instead.

Large markup merges must go through `MarkupFrameGroupingHelper` safety limits and spatial sweep. Do not reintroduce an unconditional all-pairs scan for `markMergeGapMm`; selections above 1000 items have previously hard-crashed Navisworks on that path.

### Internal API Access via Reflection

Some Navisworks internal types (`LcRmFrameworkInterface`, `LcUCIPExecutionContext`) are not publicly accessible. Access them via reflection:
```csharp
Type FindType(string fullName) {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
        var type = asm.GetType(fullName);
        if (type != null) return type;
    }
    return null;
}
// Usage: FindType("Autodesk.Navisworks.Internal.ApiImplementation.LcRmFrameworkInterface")
```
Section enable command: `LcRmFrameworkInterface.ExecuteCommand("RoamerGUI_OM_SECTION_MASTER_ENABLE", LcUCIPExecutionContext.eTOOLBAR)`

### Solution Structure

- `NavisHelper/` — Main plugin DLL project (.NET Framework 4.8.1)
- `ColorService/` — Standalone .NET 9.0 console app for AI API calls
- `docs/research/navisworks-api-notes.md` — distilled Navisworks API notes from Autodesk SDK samples
- `NavisHelper.bundle/` — Bundle resources for deployment

**Important:** `NavisHelper/NavisHelper.csproj` is the main plugin project and is **non-SDK-style**. New `.cs` files must be explicitly added via `<Compile Include="NewFile.cs" />`. There is intentionally no root-level `NavisHelper.csproj`; build through `NavisHelper.sln` or the project paths documented above.

### Common Pitfalls

- **Type ambiguity:** Adding `using System.Windows.Forms;` causes conflict between `System.Windows.Forms.View` and `Autodesk.Navisworks.Api.View`. Fix: fully qualify as `Autodesk.Navisworks.Api.View activeView = doc.ActiveView;`
- **Deployment:** `NavisHelper.bundle/Contents/<version>/` is populated by the local build matrix. Do not commit DLL/PDB build artifacts; package/release only after the four supported configurations have rebuilt the local bundle.

### Navisworks ProjectPoint API

`View.ProjectPoint(Point3D, bool, bool)` returns a `ProjectionResult` with `X`, `Y`, `Depth` properties. This is the official Navisworks .NET API for 3D-to-2D projection; the local distilled note is `docs/research/navisworks-api-notes.md`. Prefer this over manual quaternion-based projection for perspective views.

### Conditional Compilation

The `.csproj` uses conditional `<ItemGroup>` blocks to select Navisworks API DLL paths based on the active configuration. Configurations containing "2024" reference Navisworks Manage 2024, "2025" reference 2025, "2026" reference 2026, and "2027" reference 2027. The default (plain Debug/Release) also references Navisworks 2026. All DLL paths follow the pattern `C:\Program Files\Autodesk\Navisworks Manage 20XX\<DllName>.dll`.
