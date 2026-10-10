# Griff

A game engine and editor for game jams, in C# (.NET 10, WPF editor). See README.md for what it does.

- Build: `dotnet build src/Griff.Editor/Griff.Editor.csproj` (also publishes the game players). Tests:
  `dotnet test tests/Griff.Tests`.
- `src/Griff.Editor/Diagnostics/` holds Debug-only tools: the snapshot runner (`GRIFF_SNAPSHOT_DIR`) and the demo
  player (`Diagnostics/Demo`).

## Demos

Every user-visible change gets a narrated demo, and the demos stay current:

- `/demo` (`.claude/skills/demo/SKILL.md`) writes or updates a `.demo` script in `demos/` for the current changes,
  checks it, and plays it in the editor. Format: `demos/README.md`.
- When a change alters something an existing demo shows (menu text, component or field names, a flow), update
  that demo in the same change. Only check and play the demos for the current changes; never run other demos
  (`-All -Check`, the tour) unless asked, including before a commit.
- The guided tour (`tools\demo.ps1 -Tour`, `Diagnostics/Demo/DemoScript.cs`) builds a whole game; keep it working
  when the features it uses change.
