---
title: Agents (MCP)
description: Griff's editor serves the Model Context Protocol, so an agent like Claude Code can build games with it.
---

While the editor is open it runs a [Model Context Protocol](https://modelcontextprotocol.io) server on this computer, so an agent like Claude Code can build games with it:

```sh
claude mcp add --transport http griff http://127.0.0.1:7650/mcp
```

**Help → Copy MCP Setup Command** copies that, **Help → MCP Server** turns the server off, and `GRIFF_MCP_PORT` picks another port. It listens on the loopback address only and refuses requests from web pages.

The server lists no features of its own. Its tools are read from what the editor already has, so everything the editor can do is there, and anything added later shows up by itself:

- **Every command** in the menus and toolbar is a tool, named by its id (`Entity_CreatePlatformerPlayer`, `Game_Play`, `Scene_Save`) and described by its documentation. A question a command asks comes back to the agent, which answers it.
- **Every component**, Griff's and the project's scripts, can be added and set with `entity_create` and `entity_update`, and is described by `components_list` and `component_describe`. `scene_tree`, `entity_get` and `entity_find` look at the scene, the running game included. Each change is one undo step.
- **Every window, panel and editor service** (the Sprite Painter, the Sound Maker, the Controls window, the Scene view's tools...) can be read and worked with `ui_roots`, `ui_inspect`, `ui_set` and `ui_invoke`.
- **Files**: `asset_write` writes scripts (compiled at once, errors reported), pictures and sounds; `asset_read` reads them.
- **Checking work**: `editor_screenshot` shows the game or the editor, `game_input` plays the running game with keys and the mouse, and `editor_state` and `output_read` say what's going on.
- **Debugging**: breakpoints, stepping and pausing where a script throws work for agents too.

This site's screenshots are taken this way: `site/scripts/screenshots.mjs` makes a project from each template over the MCP, plays it and saves what `editor_screenshot` shows.

The full list is in the [README](https://github.com/srakowski/griff#agents-mcp).
