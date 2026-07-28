# AI Color Objects

`AI Color Objects` assigns colors to selected Navisworks objects from their
display names. When the external AI path is available, color grouping is
requested through OpenRouter. Otherwise the command uses the selected local
color scheme.

## Execution path

1. The plugin filters the current selection to colorable objects.
2. `AIColorUtils.GetObjectNamesFromSelection` extracts object names.
3. `LocalColorBridge` writes the names and selected scheme to temporary JSON.
4. If `ColorService.exe` is present next to `NavisHelper.dll`, it is started
   with the temporary request and response paths.
5. `ColorService.exe` calls OpenRouter only when `OPEN_ROUTER_NW_KEY` is set.
6. If the service executable or key is unavailable, local colors are generated
   from `ColorSchemes`.

The distribution and installer built by `tools/build_installer.ps1` do not
include `ColorService.exe`. An installation from those artifacts therefore
uses the local fallback unless the executable is supplied separately beside
the plugin.

## OpenRouter configuration

The integration is bring-your-own-key. Create a personal OpenRouter key and
set it for the current Windows user:

```cmd
setx OPEN_ROUTER_NW_KEY "your_openrouter_key"
```

Restart Navisworks after changing the environment variable.

The key is read from `OPEN_ROUTER_NW_KEY`. It is not serialized to
`%APPDATA%\NavisHelper\ai_config.json`.

The configuration file contains non-secret settings:

```json
{
  "ApiUrl": "https://openrouter.ai/api/v1/chat/completions",
  "RequestTimeout": 60000,
  "MaxRetries": 2,
  "EnableLogging": true,
  "ModelName": "claude-sonnet-4.6",
  "Temperature": 0.3,
  "MaxTokens": 2000,
  "ColorScheme": 8,
  "EnableThinking": true
}
```

The settings tab writes the selected model and thinking mode to this file.
The endpoint and other non-secret values can also be edited there directly.

## Available models

The selectable models are defined by `AIModels.Available`:

| Display name | OpenRouter model ID | Thinking mode |
|---|---|---|
| `claude-sonnet-4.6` | `anthropic/claude-sonnet-4.6` | Supported |
| `claude-opus-4.6` | `anthropic/claude-opus-4.6` | Supported |
| `glm-5-turbo` | `z-ai/glm-5-turbo` | Not enabled |
| `gpt-5.4` | `openai/gpt-5.4` | Not enabled |
| `gemini-3-flash` | `google/gemini-3-flash-preview` | Supported |

Unknown model names fall back to the first entry,
`claude-sonnet-4.6`.

## Use

1. Open a model in Navisworks.
2. Select the objects to color.
3. Select the model, thinking mode, and color scheme in the NavisHelper panel.
4. Run `AI Color Objects`.

The command applies the returned or locally generated RGB values as permanent
color overrides.

## Data egress

When `ColorService.exe` has been supplied separately and
`OPEN_ROUTER_NW_KEY` is available, selected object names and the selected
color-scheme name are sent to
`https://openrouter.ai/api/v1/chat/completions`. OpenRouter then routes the
request to the selected model provider under the user's key. Account for this
when working with NDA-controlled models.

The MCP server does not use this OpenRouter path. The local color fallback does
not send model data to an external API.

## Defaults and failure handling

- HTTP timeout: 60 seconds.
- Maximum attempts: 2.
- Response token limit: 2000, adjusted when supported thinking mode is used.
- Default model: `claude-sonnet-4.6`.
- Default color scheme: 8, Architectural.
- Default thinking mode: enabled.
- Invalid or unavailable AI responses fall back to local color generation or
  return no color changes, depending on where the failure occurs.

Operations are written through the NavisHelper logger. The usual fallback log
path is `%TEMP%\navishelper_log.txt`.
