---
name: enhance
description: Build the next item on Griff's enhancement backlog - implement it, test it through the Griff MCP, demo it, then commit and merge it to main and tick it off. When the backlog is empty, build a new one with the user. Use only when the user types /enhance.
disable-model-invocation: true
---

# /enhance: build the next backlog item, end to end

The backlog is `.claude/skills/enhance/backlog.md`: an ordered checklist of small prompts. Each run builds exactly
one item, the first unchecked one under **Next**. Do it well rather than doing more.

## 0. Pick the item

1. Read `backlog.md`. If an arg was given (`/enhance <text>`), build that instead and leave the list alone, unless it
   matches an item (then that item is the one to tick).
2. If **Next** has no unchecked items, go to **Building a new backlog** below and stop there.
3. Size it before starting. Each run should be one focused session: a few components or one editor feature, with
   its tests, MCP check and demo. If the item is bigger than that (several systems, a template plus the components it
   needs, a rewrite), split it in `backlog.md` into pieces that each stand on their own, in build order, commit just
   that change to main (stage `backlog.md` by path and nothing else), and build only the first piece.
4. Check the Griff MCP is on main (`src/Griff.Agent/`, the MCP server the editor serves). Every item is tested through
   it; if it isn't there yet, say so and stop before writing any code.

## 1. Work in a worktree

Other agents may be working in the main checkout at the same time, so never switch branches or stage files there.

1. Make a short kebab-case slug for the item (e.g. `grid-movement`) and create a worktree off main:
   `git worktree add ..\griff-enhance-<slug> -b enhance/<slug> main` (or use the EnterWorktree tool). Do all the
   work below in that worktree.
2. If the branch or worktree already exists from an interrupted run, pick up where it left off rather than starting
   over.

## 2. Build it

- Treat the item as the whole prompt, the way the user would have typed it. Read how the neighbouring features are
  built first (git history, the docs in `site/src/content/docs/docs/`, `src/Griff/Scenes/*Components.cs`, the templates in `src/Griff.Editing/Templates/` and the genres in `src/Modules/`)
  and follow the same shape: no-code components that work from the Inspector, scripting API where it helps, a
  playable (and audible) template when the item names one, multiplayer and rewind support like the existing
  components, and undo for everything in the editor.
- Keep to the item, and finish all of it: each run is a standalone effort that defers nothing. Don't leave part of
  the item for later, don't add follow-up items to the backlog, and don't leave known gaps in what the item covers.
  If something you find along the way is outside the item, fix it in this change when it's small; otherwise mention it
  in the report and leave the backlog alone.
- Update the docs (`site/src/content/docs/docs/`; the README only links to them): the guide for the item's area (add a
  section, or a page and its sidebar entry in `site/astro.config.mjs`), and the known gaps in `docs/status.md` (remove
  gaps this closes). The reference pages are generated from the code, so give new components and commands `///`
  summaries instead of writing them up.
- Add unit tests in `tests/Griff.Tests`, including playing any new template. Existing demos and the guided tour
  that show something this changes get updated (see `CLAUDE.md`).
- `dotnet build src/Griff.Editor/Griff.Editor.csproj` and `dotnet test tests/Griff.Tests` both pass before going on.

## 3. Test it through the Griff MCP

Never launch anything while the user is busy: `CLAUDE.local.md` (local, not in git) may name programs that mean so;
while any of them is running, wait until it's closed.

1. Start the editor from the worktree's build with the MCP server on, the way `src/Griff.Agent/` and `docs/agents.md` say
   to. Use the Griff MCP tools if this session has them connected; otherwise talk to it over HTTP with JSON-RPC
   (`initialize`, then `tools/list` and `tools/call`) at `http://127.0.0.1:<port>/mcp` (7650 by default).
2. Exercise the new feature the way an agent building a game would: create entities, add and set the new
   components, read them back, build a small scene (or open the new template) and check what the scene holds is what
   you meant. Also check the new components show up in `components_list` and `component_describe` with useful
   descriptions.
3. If the MCP can't reach something the feature needs (no tool for it, or a value it can't set or read), extend the
   MCP in the same change rather than working around it, with tests.
4. Fix what this turns up, rerun the tests, and close the editor.

## 4. Demo it

Run `/demo` (the Skill tool, skill `demo`) for this change only: it writes or updates the `.demo` script, checks it
and plays it. Only the demos for this change: never `-All -Check` or the tour unless the user asks. Fix anything
the demo turns up before going on.

## 5. Commit, merge, tick it off

1. In `backlog.md`, move the item from **Next** to **Done** as `- [x] <item> (YYYY-MM-DD)`.
2. Commit everything in the worktree (code, tests, docs, demos, `backlog.md`) as one commit on `enhance/<slug>`.
   Use the repo's message style, e.g. `Add grid movement: things that step tile by tile on a tilemap`.
3. Bring it up to date with main and merge it:
   - In the worktree: `git rebase main`, then build and test again if main moved.
   - In the main checkout: `git merge --ff-only enhance/<slug>`. This only touches files the change touched. If git
     refuses because another agent has uncommitted edits to those files, stop and tell the user; don't stash,
     reset or overwrite anyone's work.
4. Remove the worktree and delete the branch (`git worktree remove`, `git branch -d enhance/<slug>`). Don't push. The file-based tools build in the worktree's git-ignored
   `artifacts/tools` (`tools/Directory.Build.props`), so removing the worktree removes their builds too.

## 6. Report

Say what was built, what the MCP testing and the demo turned up, anything found outside the item, and the next item on the list
(the one the next `/enhance` will build).

## Building a new backlog

When **Next** is empty, help the user plan the next round rather than building anything.

1. Gather candidates: the known gaps in `site/src/content/docs/docs/status.md`, the deferred notes under **Done**, genres or features the
   engine still handles poorly (read the components and templates to judge), and rough edges you saw while building
   the last items.
2. Propose a short ordered list (foundations first, then what builds on them; the riskiest core changes last), each
   item one small prompt in the same style as the old ones. Ask the user which to keep, drop or add
   (AskUserQuestion, multi-select, works well), and what they care about most.
3. Write the agreed items into **Next**, keeping **Done** as history, and commit `backlog.md` on its own straight to
   main (`Plan the next enhancements`), staging it by path (`git add .claude/skills/enhance/backlog.md`) and nothing
   else. Then ask whether to start the first one.
