# NavisHelper Launch Kit

## GitHub About

Navisworks Manage plugin and local MCP server for BIM coordinators working with model search, viewpoints, properties, and Clash Detective.

## Topics

`navisworks`, `bim`, `mcp`, `mcp-server`, `autodesk`, `clash-detection`, `dotnet`, `ai-agent`, `bim-coordination`, `navisworks-plugin`

## awesome-mcp-servers submission

- [NavisHelper](https://github.com/mikhalchankasm/NavisWorksMaster) - Local MCP server and Navisworks Manage plugin for model search, selection, viewpoints, property export, and Clash Detective workflows.

## Announcement posts

### LinkedIn

I built NavisHelper for my own Navisworks coordination work and am publishing it in case it is useful to others. It combines a Navisworks Manage plugin with a local MCP server for model search and selection, viewpoints, property export, and Clash Detective workflows. The current release supports Navisworks Manage 2024–2027 and provides 100 MCP tools. The plugin UI is currently Russian-only; English localization is planned but not yet implemented. Repository: https://github.com/mikhalchankasm/NavisWorksMaster

### Reddit r/bim

I made a Navisworks Manage plugin and local MCP server for my own BIM coordination tasks and have put the source and installer on GitHub. It can search and select model items, manage viewpoints and sets, export properties, and work with existing Clash Detective tests and results. It supports Navisworks Manage 2024–2027 and exposes 100 MCP tools. The plugin UI is Russian-only for now, so English-speaking users should account for that. Sharing it here in case the workflow is useful to someone else: https://github.com/mikhalchankasm/NavisWorksMaster

### Telegram BIM

Сделал для своих задач NavisHelper — плагин для Navisworks Manage и локальный MCP-сервер. Он позволяет через агента искать и выделять элементы модели, работать с точками обзора и наборами, экспортировать свойства, разбирать и изолировать результаты Clash Detective. Поддерживаются версии Navisworks Manage 2024–2027, зарегистрировано 100 MCP-инструментов. Выложил исходники и установщик, вдруг кому-то пригодится: https://github.com/mikhalchankasm/NavisWorksMaster

## Demo video script

**End-to-end task:** review the first active result in an existing `HVAC vs Structure` Clash Detective test and save the prepared view for the coordination team.

| Time | On screen | Voice-over |
|---|---|---|
| 0:00–0:15 | Open a coordination model in Navisworks Manage. Show the existing `HVAC vs Structure` test in Clash Detective, then return to the model view. | “This model already has a Clash Detective test named `HVAC vs Structure`. I need to review its active results and prepare one saved view for coordination.” |
| 0:15–0:35 | Open the MCP client next to Navisworks. Enter: “List the active clashes in test `HVAC vs Structure`, then isolate the first result.” | “I send one task to the agent. It maps the request to `clash_list_results` and `clash_isolate_result`.” |
| 0:35–1:00 | Show the returned result list with item names and the first active result. Keep Navisworks visible beside the response. | “The first step is read-only. The server lists existing results from the named test so I can inspect the first active clash.” |
| 1:00–1:25 | Show the isolation dry-run returned by `clash_isolate_result`, including the planned section box and visibility changes. | “Isolation defaults to a dry-run. I can review the planned change before allowing it to modify the current Navisworks view.” |
| 1:25–1:50 | Confirm the operation. Show Navisworks isolate the clash pair and clip the view around the clash point. | “After confirmation, the plugin isolates the selected clash result and applies the prepared view in Navisworks.” |
| 1:50–2:10 | Enter: “Create a saved viewpoint named `HVAC vs Structure - review 001` from the current view.” Show the `create_viewpoint` preview. | “The isolated view is now the review context. I ask the agent to save the current view under a clear coordination name.” |
| 2:10–2:30 | Confirm viewpoint creation, then show `HVAC vs Structure - review 001` in Saved Viewpoints and activate it once. | “After reviewing the preview, I confirm the write. The saved viewpoint can now be reopened and shared through the normal Navisworks workflow.” |
