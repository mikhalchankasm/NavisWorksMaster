# AI Color Objects: implementation summary

## Components

- `AIColorObjects.cs` — Navisworks plugin entry point.
- `AIColorUtils.cs` — selection filtering, object-name extraction, and color
  application.
- `AIColorObjects.cs` / `LocalColorBridge` — temporary-file IPC and local color
  fallback.
- `ColorService/Program.cs` — external OpenRouter request process.
- `AIColorService.cs` — legacy direct OpenRouter client; the current
  `AIColorObjects` command path does not call it.
- `AIConfig.cs` and `AIModels` — non-secret configuration, model mapping, and
  defaults.
- `ColorSchemes.cs` — deterministic local palettes used when the external path
  is unavailable.

## Current behavior

`AIColorObjects` reads display names from the selected colorable objects. It
attempts to run `ColorService.exe` beside the plugin and exchanges request and
response JSON through unique files under `%TEMP%`.

The distribution and installer built by `tools/build_installer.ps1` do not
include `ColorService.exe`. An installation from those artifacts therefore
uses the local fallback unless the executable is supplied separately beside
the plugin.

`ColorService.exe` uses the OpenRouter chat-completions endpoint:

```text
https://openrouter.ai/api/v1/chat/completions
```

The API key is bring-your-own-key and is read only from:

```text
OPEN_ROUTER_NW_KEY
```

The key is not written to `%APPDATA%\NavisHelper\ai_config.json`. That file
stores the endpoint, timeout, retry count, selected model, temperature, token
limit, color scheme, and thinking-mode setting.

## Models and defaults

`AIModels.Available` currently contains:

- `claude-sonnet-4.6` → `anthropic/claude-sonnet-4.6`
- `claude-opus-4.6` → `anthropic/claude-opus-4.6`
- `glm-5-turbo` → `z-ai/glm-5-turbo`
- `gpt-5.4` → `openai/gpt-5.4`
- `gemini-3-flash` → `google/gemini-3-flash-preview`

Defaults from `AIConfig.cs`:

- model: `claude-sonnet-4.6`;
- request timeout: 60000 ms;
- maximum attempts: 2;
- maximum response tokens: 2000;
- temperature: 0.3;
- color scheme: 8, Architectural;
- thinking mode: enabled.

## Fallback

If `ColorService.exe` is absent, cannot start, or has no
`OPEN_ROUTER_NW_KEY`, the implementation generates colors locally from the
selected `ColorSchemes` palette. No external API call is made on this path.

## Data egress

When `ColorService.exe` has been supplied separately and
`OPEN_ROUTER_NW_KEY` is available, the OpenRouter request contains the
selected object names and the selected color-scheme name. It does not contain
model geometry. OpenRouter routes the request to the selected model provider
under the user's key.

The MCP server does not use this AI request path.
