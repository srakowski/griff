# Griff

A game engine and editor for game jams, in C# (.NET 10, WPF editor). README.md is short; the docs (griff.games) say
what it does and how: pages in `site/src/content/docs/docs/`, a guide each for the editor, scenes and components,
scripting, multiplayer, exporting and so on (see Docs below).

- Build: `dotnet build src/Griff.Editor/Griff.Editor.csproj` (also publishes the game players). Tests:
  `dotnet test tests/Griff.Tests` and `dotnet test tests/Griff.Editor.Tests`.
- `src/Griff.Editor/Diagnostics/` holds Debug-only tools: the snapshot runner (`GRIFF_SNAPSHOT_DIR`) and the demo
  player (`Diagnostics/Demo`).

## Docs

- The guides are Markdown in `site/src/content/docs/docs/`; a change updates the guide for its area (new pages go in the
  sidebar in `site/astro.config.mjs`) and `docs/status.md`'s known gaps. `site/README.md` says how to build and preview.
- The reference (every component with its properties, every editor command, the scripting API) is not written by hand:
  `tools/docs.cs` writes it from the code (`ComponentRegistry`, `CommandRegistry` and the `///` docs) into
  `site/src/content/docs/reference/` (git-ignored; `npm run reference` in `site/`). A new component or command appears
  there by itself, so give it a `///` summary and describe its properties.

## Demos

Every user-visible change gets a narrated demo, and the demos stay current:

- `/demo` (`.claude/skills/demo/SKILL.md`) writes or updates a `.demo` script in `demos/` for the current changes,
  checks it, and plays it in the editor. Format: `demos/README.md`.
- When a change alters something an existing demo shows (menu text, component or field names, a flow), update
  that demo in the same change. Only check and play the demos for the current changes; never run other demos
  (`-All -Check`, the tour) unless asked, including before a commit.
- The website's tutorials (`site/tutorials/*.demo`, `site/README.md`) are demos too, and their pages and screenshots come
  from running them: a change that alters something one shows updates that script, and `npm run tutorials -- --only <name>`
  in `site/` plays it and takes its pictures again.
- The guided tour (`tools\demo.ps1 -Tour`, `Diagnostics/Demo/DemoScript.cs`) builds a whole game; keep it working
  when the features it uses change.

## Agents (MCP)

The editor serves MCP at `http://127.0.0.1:7650/mcp` while it's open (`src/Griff.Editor/Features/Agent`; the protocol,
scene tools and reflection live in `src/Griff/Agent`). It has no list of features: its tools are projections of what
the editor already registers, so new capabilities reach agents by themselves. Keep it that way by putting each
capability where a projection sees it:

- A user action is an `EditorCommand` (it becomes a tool named by its id) with a `///` summary, which describes the
  tool; `tests/Griff.Editor.Tests` fails for a command without one.
- Game data is a component's public property (any component, scripts too, is created and set by the scene tools).
- Questions to the user go through `IDialogService` (agents answer them with `answers`).
- Window and panel state and actions live in their view model; editor services are DI singletons (both reached by
  `ui_inspect`/`ui_set`/`ui_invoke`).
- Not in code-behind event handlers: a capability that only exists in a click handler is invisible to agents. Make it
  a command (as Open Scene and Set as Start Scene are) and let the UI call that.
