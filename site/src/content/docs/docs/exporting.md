---
title: "Exporting"
description: "The Build menu: Build Game and Build Small Game for Windows, Build for Web for browsers (itch.io, GitHub Pages), and the licenses every export carries."
---

Griff exports a game from the **Build** menu two ways: as a Windows program, and for a web page.

## On the desktop

**Build → Build Game** (Ctrl+B) makes a single `.exe` that carries everything, .NET included, so players install nothing. Zip it and upload it to your jam's page. **Build → Build Small Game** (Ctrl+Shift+B) makes a 4 MB `.exe` for players who have the .NET Desktop Runtime. Built games open in a 1280x720 window, or at the size the player picked in an options menu (a Choice set to Window Size; see [Options menus](/docs/ui/#options-menus)), and go fullscreen with F11: borderless at the screen's own resolution, never changing the display's mode.

Exported games' scripts are compiled optimized, like a Release build; the editor compiles them for debugging, with a breakpoint probe on each line (see [The script editor](/docs/scripting/#the-script-editor)), which makes saving a script take a little longer there (about 50 ms for the Arcade template's five scripts, against 15). Exported games are self-contained: they include the .NET runtime and run on any 64-bit Windows PC without installing anything (expect a zipped download of about 60 MB).

A game carries Griff's runtime and only the genres it uses: the players hold `Griff` and no genre, and an export puts the running parts of each genre its project uses (those its scenes and prefabs have a component of, and those its scripts name) in its game folder's `Modules`, which the player loads before the game. Nothing of the editor's (editing, templates, the agent surface, a genre's editor parts) is in a game. A Match-3 game carries the Match-3 genre (about 65 KB) and no other; a game with no genre in it carries none.

A multiplayer game's export hosts and joins from its own menu: see [Multiplayer](/docs/multiplayer/).

## In a browser

**Build → Build for Web...** makes the game playable in a browser. It asks for a folder (the project's Build folder to
start with) and writes the game there, in a folder of its own named after it ("Space Hop (Web)") unless the folder picked
is empty or an earlier web build: `index.html` and everything it needs, ready to put on GitHub Pages, a web server or any
static host as it is, and beside it a zip of the same files with `index.html` at the top, which is what itch.io wants
uploaded for an HTML game (tick "This file will be played in the browser"). Then it offers to play it in your browser:
Griff serves it on this computer only (`http://127.0.0.1:7680/`, the same port each time so the game's saves are still
there next time), since a browser won't run it opened straight from its files. Agents run it as `Game_BuildWeb`,
answering its folder and "Play in browser" questions.

What players get:
- **The same game.** It draws what the desktop draws, the same way (the next section), and plays every scene, with its
  scripts, at the same step rate.
- **Sound.** Music, sounds and Griff's synths play through Web Audio, mixed by the same mixer as on the desktop, so
  volumes, panning, pitch, crossfades and the player's Sound and volume settings work as they do there. Browsers let a
  page make sound only after the player clicks or presses a key: until then the game plays on silently, its music
  keeping time, and a line at the bottom says "Click or press a key for sound" while there's something to hear. The
  first click or key starts it, in step, and the beat clock and anything timed to the music stay in time.
- **Saves.** `SaveData` (high scores, unlocked levels, the player's settings and controls) is kept in the browser's
  storage for the page, under a key of the game's own (its name and when its project was made), so it survives
  reloading the page and playing again another day, and two games on the same site (itch.io serves every game's page
  from the same address) never share it. `SaveData.Clear()` (a "reset progress" button) clears it; so does clearing the
  site's data in the browser.
- **Controllers** through the Gamepad API: the first one plugged in, in the standard (Xbox) layout, with its sticks,
  triggers and rumble where the browser can shake it.
- **The mouse and touch.** The mouse works as on the desktop, and a first-person game takes it when clicked (pointer
  lock; Esc gives it back). A finger is the mouse: it points, and a tap is a left click, so menus, buttons and anything
  clicked play on a phone or tablet.
- **Any page size.** The game fills the page (or itch.io's frame) at the screen's own pixels, sharp on a phone or a 4K
  screen (up to 4096 pixels along its longer side, then scaled up), its view as wide as the page as on the desktop.
- **Fullscreen.** The game's Fullscreen (a toggle set to it, or `Game.Fullscreen`) fills the screen, from the click or
  key that asked for it (browsers allow it only then), and leaving it with Esc turns the setting off. F11 is the
  browser's own. The page is the game's window, so a Window Size an options menu picks is kept for the desktop and
  changes nothing here, and frames always come with the browser's (VSync off doesn't apply); a Frame Cap still counts.
- **A loading screen** with the game's name and a bar while .NET and the game come; if something goes wrong it says
  what. Quitting (a Quit button) stops the game and says so, since a page can't close itself.

**Network play isn't available in a browser.** Browsers can't use the UDP connections Griff's multiplayer plays over.
The export warns of each scene, prefab or script that hosts or joins (in the Output panel, and when it asks to play it),
and in the browser the game says so as a scene set up for network play starts, and again if someone hosts or joins:
`Network.Status` (which a lobby shows) is "Network play isn't available in the browser version of this game...", hosting
returns false and joining ends at once (`Network.Ended`). The rest of the game plays alone: a menu's Practice still works.
`Network.Unavailable` says why where it's set (in a browser, always).

### From the command line

`powershell -NoProfile -ExecutionPolicy Bypass -File tools\web.ps1` publishes the web player and builds a game for the
web: `-Project <folder>` a project, `-Template <name>` a template made into a project, or neither, the proof game (the
griffin, which the arrow keys, WASD or a stick move, under a label). `-Export -Out <folder>` writes it there and stops
(`-Zip` adds the zip for itch.io), as Build for Web does; without `-Export` it serves it at `http://127.0.0.1:8091/`
(`-Port` another) until Ctrl+C.

`-Site <folder>` makes the website's playable games (the [Play](/games/) pages): every template exported into the folder
with .NET and the player once, in `_player/`, shared by every game (a game's page loads the player from there, and a
browser keeps it between games), then a folder for each game with its page, its files and a poster (a screenshot of it
running, taken in the headless browser), and `games.json`, which the site's pages and gallery are written from. It is
`npm run games` in `site/`, and the Site workflow runs it on every push to main, so the games on griff.games are always
main's templates. A page needs a template's tagline, paragraph and controls in `site/src/data/games.mjs`, and the
build fails without them.

`-Check` plays games in a headless Chrome or Edge (no window, software WebGL, muted) and passes if all of them work: the
proof game, an adventure, rhythm lanes, and the Platformer, Shooter, Match3, Isometric, Physics, Pinball, Rhythm, BunnyMark and
Multiplayer templates (`-Template` picks some, `Proof`, `Adventure` and `Lanes` among them; `-Project` checks yours). Each must start and draw; its sound must wait for a key, saying so, with the music keeping
time, then play through Web Audio (an AnalyserNode in the page hears it) still in time; input must reach it (the arrow
keys and WASD move the player, a tap opens Match-3's first level, a tap walks the adventure's player round a table, its
pointer is a hand over a hotspot, and a right click there looks at it and a tap uses it, a key tapped is taken into the
inventory bar and, picked from there (the pointer hidden for it), clicked on a door unlocks it, and the keys put two
items together, a controller's stick moves the platformer and the
griffin, in Physics a finger, a controller and the keys each pull the slingshot back and let a bird go, in Pinball the keys, a
controller's shoulders and a finger each side (and two fingers at once) flip and the keys, a controller's right stick and a
finger on the plunger each shoot a ball, Space restarts
BunnyMark, and the rhythm lanes' notes pressed with the keys on the beat as Web Audio plays it are hits, as are a chord
tapped with two fingers at once and a hold held by one to its end, and the lanes finish with the song, and in Rhythm Enter
opens the song select, which plays the song picked out, Down picks out the next (heard in its place), a controller's A
plays it and its lanes are played the same way to the results, its best score kept); the steps of
Physics' first and third levels' collapses and of a pinball ball's play through the lanes and bumpers must take less
than a frame on average (the slowest frame's is printed too); and its saves must survive reloading the page (the proof game's script counts its
plays). The proof game's label must be drawn as wide as Griff lays it out, the game must fit a phone's page and a 4K
screen and go fullscreen and back; Match-3's Quit must end it; and Multiplayer must be warned of at export and say in the
page and on Host Game that network play isn't there. The page counts as focused throughout, as a headless page on a busy
desktop otherwise goes hidden and stops drawing. Chrome is used when it's there: a headless Edge left running on a
computer nobody is using slows its page to a frame a second or so after a few seconds, which stalls a check. Each game
says how long its warm-up took (below).

`-Compare` checks the browser draws the same pixels as the desktop: every scene of every template (`?scene=` starts the
web player in one), and three scenes of the dark (walls and crates casting shadows from a lamp, a light that doesn't,
sparks, a label), the second through screen effects and the third with a minimap (a viewport, dark and lit by itself),
are exported, held still in the headless browser after 90 steps (`-Steps`; the page's `?still=<steps>` takes them on its
first frame, with `Rand` started from 0 and its music silent, counted by those steps as the desktop's is, so what moves to
the beat is where the desktop's is, and stays there) and drawn by the desktop's OpenGL renderer off the screen after
the same steps, and the two pictures are compared: it passes if, in each, at most 0.5% of the pixels differ by more than
48 in a channel and they differ by at most 1 on average. They differ by a few hundredths on average, at most a fifth of
a percent off: single pixels along the edges of tiles and quads, where the GPU and SwiftShader round a pixel's middle
sitting on an edge each their own way. `-Template` picks some by name (`-Template "Platformer,Lights"`), and
`-Out <folder>` keeps each one's `desktop.png`, `web.png` and `diff.png`. The OpenGL renderer's own tests
(`OpenGLRendererTests`) draw sprites, the dark and screen effects off the screen and check the pixels.

### How it works

- **Griff runs on .NET for WebAssembly unchanged.** `Griff.Web` is a `Microsoft.NET.Sdk.WebAssembly` project that
  references `src/Griff` as it is (and no genre: a game brings its own, below): all of it compiles for `browser-wasm`, and no workload is needed (no `wasm-tools`),
  since the runtime is the Mono interpreter that ships in the SDK's runtime pack. The editor publishes it with itself
  (`Player\Web`, in Release, without the `.br` and `.gz` copies static hosts don't use) as it does the desktop players.
- **A game is exported for the web** by `WebExporter` (`Griff.Exporting`, beside the desktop `GameExporter`): the
  published player (`index.html`, titled with the game's name, `main.js`, `griff.js` and .NET in `_framework`), and in
  `game/` the same game folder the desktop player carries (the manifest, Assets without the script sources, the
  scripts compiled as `Scripts.dll` and the genres it uses in `Modules`), with a `files.txt` listing it. A browser can't list a folder, so the player
  fetches what `files.txt` names into its in-memory file system and loads the game from there with `GameLoader`, as the
  desktop player does. `WebExporter.Zip` packs it, and `WebServer` serves a folder to this computer.
- **Scripts compiled at export load in the browser.** The interpreter loads `Scripts.dll` with `Assembly.Load` like the
  desktop does, after the game's genres (`ModuleAssemblies.Load`, from its `Modules` folder). Roslyn doesn't run in the browser; compiling at export means it needn't.
- **The browser times the frames; the loop is the desktop's.** `griff.js` calls `WebPlayer.Frame` (a `[JSExport]`) on each
  `requestAnimationFrame`, which hands the frame to the core's `GameLoop`, the one every host runs: it takes the game's
  fixed steps that frame's time holds (`Game.Advance`), builds its `RenderFrame` and draws it on the canvas, pumps the
  sound, and takes a saved game's picture, as on the desktop. The page only hands it the canvas, its input and Web Audio. The browser build is single-threaded, so a game's `WorkerCount` starts
  at 1 there (`Workers.Default`) and a big scene's work is done on the page's thread.
- **Drawing is WebGL 2.** `WebGLRenderer`, like the desktop's `OpenGLRenderer`, turns each frame into a `DrawList`
  (`Griff.Graphics`: the quads in clip space, and the passes that draw them), and one `[JSImport]` call a frame hands the
  page its quads, passes and values, which `griff.js` runs with WebGL 2 with the desktop's shaders (`Shaders`, in GLSL ES
  3.00). Images are read from the game's files by .NET and decoded by the browser (`createImageBitmap`, with no color
  conversion); a missing one draws as the magenta checker. Text draws from the same glyph atlas as on the desktop
  (`Griff.Fonts`); font files in Assets work, an installed font can't (the export warns, and it draws in the built-in
  font).
- **Sound is mixed in .NET and played by Web Audio.** The game's `MixerAudio` runs on Griff's `Mixer`, as on the
  desktop; `BufferedOutput` keeps a tenth of a second mixed ahead each frame and hands it to the page, which queues it
  as `AudioBuffer`s back to back at 44.1 kHz through an `AnalyserNode` to the speakers. What's heard is worked out from
  what's queued and the browser's own latency, so the music's time (`Audio.MusicTime`) is what's heard; until the
  browser lets the page play, the mixer is pulled along in real time with nothing heard, as a muted desktop game's is;
  and a stall (a page hidden in a background tab stops drawing) is left out of its clock, so the music doesn't jump.
  Sounds are read by the desktop's own library (`AssetSoundLibrary`): WAV and OGG decoded by .NET (NVorbis), MP3 by the
  browser as the game starts (Windows decodes it with its own codec). Music in WAV or OGG is read as it plays: decoded whole as it starts, a few minutes of Griff's
  synth music held up that frame by some 60 ms and left megabytes for the garbage collector. Images, fonts and sounds are
  found among the files fetched as the page opened (`PreloadedAssets`), never looked for again; a desktop export's are
  read from its folder when first wanted (`AssetFolder`) and never watched, as only the editor's files change while a game
  plays.
- **Input comes from DOM events**: keys by `KeyboardEvent.code` (where the key is, as Griff's keys are on Windows; the
  page keeps the ones the game takes, so the arrows don't scroll it, and leaves the browser its F keys and Ctrl
  shortcuts), typed text, pointer events for the mouse and touch (its position in the world through the last frame's
  camera, its buttons and wheel), and `navigator.getGamepads()` each frame.
- **Saves** are a `SaveData` kept somewhere other than a file: it starts from what `localStorage` holds under the game's
  key and hands it every change.
- **.NET runs Griff in an interpreter, readied before the game starts.** Without the `wasm-tools` workload, .NET for
  WebAssembly interprets Griff: it prepares each method the first time it's called, prepares it again (optimized) once
  it has been called a thousand times, and compiles the busiest stretches to WebAssembly as they run (its
  jiterpreter). So the steps cost more than on the desktop (the Physics template's biggest collapse about 3 to 5 ms a
  step against about 1 ms), and, left to the game, preparing happens in its frames: a thousand steps in (about 17
  seconds) every step's work was prepared again at once, a frame of 50 to 110 ms in every game, and the first pile to
  collapse prepared the physics of bodies that turn as it went (frames of 20 to 45 ms). `WebWarmup` does that work
  while the loading screen still shows: it plays a scene of its own out of sight for a thousand steps, each built
  into a picture but not drawn, and, if any of the game's scenes or prefabs has a Body that turns (Rotates), starts
  it with eight crates, balls and polygons tumbling into a bin and a tower of posts and planks that break, knocked
  down by a ball. It adds 0.3 to 0.5 s to loading, 1 to 1.5 s with the pile (`-Check` prints it), and the Physics
  template's collapses then have their slowest frames at 8 to 15 ms a step, where they had frames of 40 to 110 ms. A
  still moment (`?still=`) skips it.
- **Not compiled ahead of time (AOT).** With the `wasm-tools` workload (Emscripten, about 2 GB), `RunAOTCompilation`
  compiles .NET and Griff to WebAssembly when the player is published. It needs trimming on, which drops what scripts
  compiled later might call unless every assembly is kept whole (`TrimmerRootAssembly` for each, and JSON's reflection
  turned back on), and measured on the Physics template it made the steps about desktop speed (1.2 ms a step in a
  collapse), but publishing the player took 12 to 15 minutes instead of about one, every build of the editor, and
  the download grew from about 30 MB to 100 MB (9 MB to 22 MB compressed with Brotli), which costs players more time
  loading than the warm-up does; and methods first called in play were still slow once (a 160 ms step as a bird first
  hit something). So the player is interpreted and warmed up.
- **Size:** .NET and Griff are about 30 MB untrimmed (about 10 MB as hosts send them compressed); a game adds its own
  files. Trimming is off, since scenes load by reflection and scripts are compiled against all of .NET after the player
  is published. A template starts 3 to 6 seconds after the page opens, served locally, its warm-up included.
- File-based C# apps (`dotnet run tool.cs`) default to Native AOT, which turns off the reflection Griff's JSON uses;
  `tools\web.cs` turns it off as `tools\bunnymark.cs` does.

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
- **Free fonts for games** ship beside the editor (`src/Griff.Fonts/Free`, whose README and `FreeFonts.cs` record each
  one's source, version, license and SHA-256), never inside the player: Press Start 2P 3.000, Atkinson Hyperlegible
  Bold 1.006 and Crimson Text SemiBold 1.100 from google/fonts, and JetBrains Mono Bold 2.304 from JetBrains' release,
  each under the SIL Open Font License 1.1, unmodified (so Press Start 2P's Reserved Font Name is respected), with its
  license beside it. Picking one copies it and its license into the project's Assets; an export lists every unchanged
  copy of one it carries (found by its SHA-256, whatever it's called) in `THIRD-PARTY-NOTICES.txt` with its license.
- **Installed fonts** (Segoe UI, Consolas, Arial...) are licensed to the computer they're on: Griff draws them there at
  run time (text set in one, and characters no font of the game's has) and never copies, bakes or embeds one into a
  project or an export. The font picker sets them apart with a warning; in a browser their text draws in the built-in
  font, and both exports warn of each one a scene names rather than failing.
- **A game maker's own Assets** (pictures, sounds, music, fonts, scripts) are theirs to license; Griff just ships them.
