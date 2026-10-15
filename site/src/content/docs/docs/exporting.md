---
title: "Exporting"
description: "Build Game for the desktop, Build Small Game, the web player, and the licenses every export carries."
---

Griff exports a game two ways: as a Windows program, from the **Game** menu, and for a web page, which is a proof so far.

## On the desktop

**Game → Build Game** (Ctrl+B) makes a single `.exe` that carries everything, .NET included, so players install nothing. Zip it and upload it to your jam's page. **Game → Build Small Game** (Ctrl+Shift+B) makes a 4 MB `.exe` for players who have the .NET Desktop Runtime. Built games go fullscreen with F11.

Exported games' scripts are compiled optimized, like a Release build; the editor compiles them for debugging, with a breakpoint probe on each line (see [The script editor](/docs/scripting/#the-script-editor)), which makes saving a script take a little longer there (about 50 ms for the Arcade template's five scripts, against 15). Exported games are self-contained: they include the .NET runtime and run on any 64-bit Windows PC without installing anything (expect a zipped download of about 60 MB).

A multiplayer game's export hosts and joins from its own menu: see [Multiplayer](/docs/multiplayer/).

## In a browser

Games are on their way to the browser, so a jam game can be played from its page. So far it's a proof: the web player
(`src/Griff.Web`) runs a game's start scene and draws its sprites and text in Edge or Chrome, taking the keyboard and mouse, and
the Build menu doesn't offer it yet. To try it, `powershell -NoProfile -ExecutionPolicy Bypass -File tools\web.ps1`
publishes the web player, exports the proof game (the griffin, which the arrow keys or WASD move with a script, under a label) and
serves it at `http://127.0.0.1:8091/` until Ctrl+C; `-Project <folder>` serves a project of yours instead, `-Port`
another port, and `-Out <folder>` keeps the exported site. `-Check` plays it in a headless Edge or Chrome (no window, and
software WebGL, so it needs no GPU), takes a screenshot, holds the right arrow, takes another, and passes if the label's text
is drawn as wide as Griff lays it out and the griffin is drawn and has moved right.

How it works:
- **Griff runs on .NET for WebAssembly unchanged.** `Griff.Web` is a `Microsoft.NET.Sdk.WebAssembly` project that
  references `src/Griff` as it is: all of it compiles for `browser-wasm`, and no workload is needed (no `wasm-tools`),
  since the runtime is the Mono interpreter that ships in the SDK's runtime pack. Native AOT for the browser, which would
  need the workload, isn't used.
- **A game is exported for the web** by `WebExporter` (`Griff.Scripting`, beside the desktop `GameExporter`): the
  published player (`index.html`, `main.js`, `griff.js` and .NET in `_framework`), and in `game/` the same game folder
  the desktop player carries (the manifest, Assets without the script sources, and the scripts compiled as `Scripts.dll`),
  with a `files.txt` listing it. A browser can't list a folder, so the player fetches what `files.txt` names into its
  in-memory file system and loads the game from there with `GameLoader`, as the desktop player does.
- **Scripts compiled at export load in the browser.** The interpreter loads `Scripts.dll` with `Assembly.Load` like the
  desktop does: the proof game's mover is a script, and the Arcade template (five scripts, three scenes) and the
  Platformer load and play. Roslyn doesn't run in the browser; compiling at export means it needn't.
- **The main loop is the browser's.** `griff.js` calls `WebPlayer.Frame` (a `[JSExport]`) on each
  `requestAnimationFrame`, which takes the game's fixed steps that frame's time holds (`Game.Advance`, as on the desktop),
  builds its `RenderFrame`, and hands it back. The browser build is single-threaded, so the player sets `Workers.Count`
  to 1 and a big scene's work is done on the page's thread.
- **Drawing is WebGL 2.** `WebFrame` lays a frame out as the desktop renderer's instanced quads (corners in clip space,
  the part of the image, tint and flash) with batches split by image, one `[JSImport]` call a frame passes them (as
  bytes: a memory view can only be of bytes, ints or doubles), and `griff.js` draws them with the desktop shaders in
  GLSL ES 3.00. Images are read from the game's files by .NET and decoded by the browser (`createImageBitmap`); a missing
  one draws as the magenta checker.
- **Text draws as on the desktop**, from the same glyph atlas (`Griff.Fonts`, which runs in the browser as it is): a quad
  a glyph, with the rows of an atlas page that glyphs were added to that frame copied to its texture before drawing.
  Font files in the game's Assets work; an installed font can't (the browser has only the game's files), so its text
  draws in the built-in font and the export warns of it. `-Check` also checks the proof game's label ("Griff on the Web
  ♥") is drawn, as wide in the browser as Griff lays it out.
- **Input comes from DOM events**: keys by `KeyboardEvent.code` (where the key is, as Griff's keys are on Windows; the
  page keeps the ones the game takes, so the arrows don't scroll it, and leaves the browser its F keys and Ctrl
  shortcuts), typed text, and the mouse over the canvas (its position in the world through the last frame's camera, its
  buttons and wheel).

What it found for the rest of the web export:
- **Size:** .NET and Griff are 27 MB untrimmed (7.9 MB compressed with Brotli, which the publish writes beside each
  file for a server to send); the proof game is 7 KB. Trimming is off, since scenes load by reflection and scripts are compiled against
  all of .NET after the player is published. The largest pieces are CoreLib (4.9 MB), System.Private.Xml (3.1 MB, pulled
  in though games don't use XML), the runtime (3.0 MB), Griff (2.1 MB) and the ICU culture data (2 MB, which invariant
  globalization would drop). The proof game ran 2 to 4 seconds after the page opened, served locally.
- **Lights, shadows and screen effects** aren't drawn yet (a dark scene shows lit), nor are **sound** (Windows'
  NAudio; the browser has Web Audio), **saves** (they'd go to the in-memory file system and be lost with the page: they
  need the browser's storage), **controllers** (the Gamepad API) and **fullscreen**. Multiplayer can't use UDP in a
  browser; it would need WebSockets or WebRTC.
- File-based C# apps (`dotnet run tool.cs`) default to Native AOT, which turns off the reflection Griff's JSON uses;
  `tools\web.cs` turns it off as `toolsunnymark.cs` does. Native AOT exports will meet the same wall.

## Licenses

Every game Griff exports, for the desktop or the web, carries software and fonts made by others: .NET (MIT), and in the
player NAudio (MIT), NVorbis (MIT), Silk.NET (MIT), StbImageSharp and StbTrueTypeSharp (public domain; stb is MIT or the
Unlicense), and the built-in fonts inside `Griff.Fonts`. Each export writes `THIRD-PARTY-NOTICES.txt` with their
licenses' texts (beside the game's `.exe` and inside it, or at the top of the web export), worked out from the files the
player actually carries (`ThirdPartyNotices`, which reads a single-file player's bundle), and the export tests fail if
it carries an assembly or font with no notice.
- **Built-in fonts** may only be ones whose licenses let them be shipped and embedded in games, and ship with their
  license texts. Selawik Semibold is Microsoft's file as released (1.01), unchanged: the SIL Open Font License 1.1
  reserves the name Selawik, so it's never cut down or altered. DejaVu Sans Bold 2.37 is cut down to the characters the
  built-in font lacks with fontTools (`src/Griff.Fonts/Fonts`) and renamed "DejaVu Sans Griff Subset" in its name table,
  as Bitstream Vera's license asks of a changed font (no "Bitstream" or "Vera" in its names, and no "Arev" or "Tavmjong
  Bah" for Arev's glyphs), keeping its copyright notices. JetBrains Mono, the script editor's font (SIL Open Font License;
  see `Themes/Fonts`), is the editor's own and never goes into a game.
- **Installed fonts** (Segoe UI, Consolas, Arial...) are licensed to the computer they're on: Griff draws them there at
  run time and never copies, bakes or embeds one into an export. In a browser their text draws in the built-in font, and
  the web export warns of it rather than failing.
- **A game maker's own Assets** (pictures, sounds, music, fonts, scripts) are theirs to license; Griff just ships them.
