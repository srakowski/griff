---
name: demo
description: Demo the current state of changes in Griff - write (or update) a narrated .demo script for what changed, check it, then play it so the editor drives itself on screen with voice, spotlights and a control bar. Use when the user types /demo, asks to demo or show a change, or after finishing a user-visible change.
---

# /demo: show the current changes, narrated, in the real editor

Griff can play a `.demo` script: it drives its own editor with real mouse and keyboard input, speaks the narration
(Windows speech), spotlights what it talks about, and shows a control bar (pause, 1x/2x/4x, chapter skip/rewind,
Exit). The format and the command reference are in `demos/README.md`; read it before writing a demo. The machinery
is in `src/Griff.Editor/Diagnostics/Demo/` (Debug builds only); `DemoFile.cs` plays scripts.

## 1. Work out what to show

- "The current state of changes" means what's uncommitted (`git status`, `git diff`, plus new files). If nothing is
  uncommitted, use the commits not yet demoed, starting from the latest (`git log`, `git show`).
- Read the diff for the user-visible effect: what someone using the editor or playing an exported game would now
  see or do differently. Internal refactors with no visible effect don't need a demo; say so instead.
- If an arg was given (`/demo <topic>`), demo that topic instead.

## 2. Keep the existing demos current

- Search `demos/*.demo` for anything the change touches: menu item text, component and field names, commands,
  shortcuts, defaults, the flow being shown. Update those demos in place so they show what the editor does now.
- A demo that no longer makes sense (the feature it showed is gone) gets deleted, or rewritten for its replacement.
- If the change alters what the guided tour builds (`DemoScript.cs`, the Ember Hollow tour), update the tour too.

## 3. Write the demo for this change

- Add `demos/<kebab-case-topic>.demo` (or extend the existing demo for that feature rather than adding a near
  duplicate). Start with `title:`, `intro:` and a `project:` (usually a template) so it sets itself up.
- One to three chapters, a few minutes at most: show the change working the way someone would use it, and say why
  it matters. Narration is spoken: plain sentences, no symbols or code in `say:` lines.
- Prefer real UI actions (`menu:`, `set:`, `add:`, `key:`, `play`) over anything else, and spotlight (`explain:`)
  the thing that changed.
- If the demo needs a command the format doesn't have, add it to `DemoFile.cs` and document it in
  `demos/README.md`, rather than working around it.

## 4. Check, then play

Never launch anything while the user is busy: `CLAUDE.local.md` (local, not in git) may name programs that mean so;
while any of them is running, wait until it's
closed. Building is fine.

1. Check it silently: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\demo.ps1 demos\<topic>.demo -Check`
   (builds the Debug editor first; add `-NoBuild` if it's current). PASS means it reached the end. On FAIL the
   output shows where; the full log is `%TEMP%\griff-demos\demo.log`. Fix the demo, or the bug the demo found, and
   check again. Lines with `FORCED` in the log mean a click missed and a fallback did it instead: fine for a check,
   but look for the cause if they pile up.
2. Only check and play the demo(s) for what's new or changed. Don't run `tools\demo.ps1 -All -Check` as part of
   /demo or before a commit: the user only wants to see the new features. Run it only if the user asks for it.
3. Play it for the user: `tools\demo.ps1 demos\<topic>.demo -NoBuild`. It returns at once; watch progress with a
   Monitor on `%TEMP%\griff-demos\demo.log` (`tail -F`, grep for `chapter|failed|finished|exited|FORCED`).
4. Tell the user it's playing and how to drive it: move the mouse to pause and take over; the bar at the top, or
   Ctrl+Alt+Space (pause), Ctrl+Alt+Left/Right (chapters), Ctrl+Alt+Up/Down (speed), Ctrl+Alt+X (exit).

The demo owns the screen while it runs. Don't start a second one, and don't take screenshots or send input yourself
unless you're diagnosing a failure.

## 5. Finish

- Report what the demo showed and anything it turned up (a bug found while writing or checking it is worth more
  than the demo).
- Demos are part of the change: commit the new and updated `.demo` files with the code they show, when the user
  asks for a commit.
