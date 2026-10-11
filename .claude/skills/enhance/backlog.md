# Enhance backlog

`/enhance` builds the first unchecked item, then ticks it in the same commit. Items are in order: later ones build on
earlier ones, so reorder with care. Keep each item one small thing, a single focused session; split anything that
grows. The headings only group items; build order is top to bottom.

## Next

### Editor: panels, animation and tilemaps

- [ ] Add tilemap layers: several layers on one tilemap, painted separately, each with its own collision settings.

### Gameplay foundations

- [ ] Add grid movement for players: a no-code component that steps tile by tile on a tilemap, blocked by walls.
- [ ] Add grid movement for enemies and other movers: stepping toward a target or along a path, tile by tile.
- [ ] Add turn-based play: a turn order where each side acts on its own turn, alongside the existing real-time play.
- [ ] Make grid movers take turns in a turn order, roguelike style: everything moves when the player moves.
- [ ] Add choices inside dialogue that branch to different lines.
- [ ] Add dialogue flags: lines that set flags and lines shown only when a flag is set, kept in save data.
- [ ] Add portraits and speaker names to dialogue.
- [ ] Add drag-and-drop with the mouse: things to pick up and targets to drop them on, with snapping and messages.
- [ ] Add drag-and-drop with a controller: pick up, carry and drop with buttons.
- [ ] Add a beat clock: the music's playback position and beat and bar events that scripts and components can follow, corrected for output latency.

### Genres: puzzles

- [ ] Add grid pushing: crates pushed tile by tile, goals that count when covered, and a message when every goal is covered.
- [ ] Add move undo and level restart for grid puzzles.
- [ ] Add a Puzzle template: Sokoban-style levels built from grid pushing.
- [ ] Add a match-3 board: swap tiles, matches clear, and tiles cascade and refill.
- [ ] Add a Match-3 template: scoring, moves left and level goals.
- [ ] Add a falling-block board: pieces fall, rotate and lock, full lines clear, and the speed goes up.
- [ ] Add a Falling Blocks template.

### Genres: roguelikes and tactics

- [ ] Generate dungeons from a seed: rooms and corridors on a tilemap.
- [ ] Add turn-based enemies that see and attack on the grid, and items to pick up and use.
- [ ] Add a Roguelike template.
- [ ] Add tactics units on a grid: select a unit, see how far it can move, then move and attack.
- [ ] Add a computer opponent for tactics.
- [ ] Add a Tactics template.

### Genres: cards, adventures and stories

- [ ] Add decks, hands and play zones: draw, shuffle, and play cards by dragging them.
- [ ] Add card effects without code (damage, draw, cost) and a turn-based card opponent.
- [ ] Add a Cards template.
- [ ] Add hotspots and click-to-walk: click somewhere to walk there around obstacles; click a hotspot to look at it or use it.
- [ ] Add using inventory items on hotspots and on each other.
- [ ] Add an Adventure template: a point-and-click adventure.
- [ ] Add visual novel scenes: backgrounds and characters on screen with positions and expressions, driven by dialogue.
- [ ] Add visual novel save slots, a log of lines already read, skip and auto-advance.
- [ ] Add a Visual Novel template.

### Genres: rhythm and fighting

- [ ] Add rhythm notes: a chart of notes timed to the beat clock, scrolling lanes, and hit judgments.
- [ ] Add rhythm chart recording: tap along to the music in the editor to place notes.
- [ ] Add a Rhythm template.
- [ ] Add hitboxes and hurtboxes for each animation frame, edited in the clip editor.
- [ ] Add fighting moves: blocking, hitstun, an input buffer and special-move inputs like quarter circles.
- [ ] Add a Fighter template with local versus play.

### Views

- [ ] Add isometric tilemaps: diamond tiles drawn, painted and depth sorted.
- [ ] Add movement and collision on isometric maps.
- [ ] Add an Isometric template.
- [ ] Add hex tilemaps, drawn and painted.
- [ ] Make grid movement and turn-based play work on hex maps.

### Profiler

- [ ] Add a profiler panel: a frame-time graph and entity counts while playing.
- [ ] Show each script's and component's cost per frame in the profiler, and the garbage allocated per frame.

### Physics

- [ ] Make physics bodies rotate (spin, torque, rotated box colliders) as an option per body, so existing components and templates keep working unchanged.
- [ ] Make stacks of rotating bodies settle and rest without jittering.
- [ ] Make circles roll.
- [ ] Add polygon colliders that rotate, with their points edited in the Scene view.
- [ ] Add hinge joints, and let welded contraptions turn.
- [ ] Make ropes swing and sag.
- [ ] Add physics puzzler pieces: a slingshot launcher and blocks that break on hard impacts.
- [ ] Add a Physics template, in the Angry Birds style.
- [ ] Add pinball pieces: flippers, bumpers, a plunger and tilt.
- [ ] Add a Pinball template.

### Platforms

- [ ] Make the game player open and draw in its own window without WPF.
- [ ] Rasterize text without WPF.
- [ ] Move the player's input and sound off anything still tied to WPF, with controllers through GameInput or Windows.Gaming.Input, so the same player can be hosted as a UWP app.
- [ ] Compile a project's scripts into the exported game at export time, instead of loading them at runtime.
- [ ] Make scene loading and components safe for Native AOT (no runtime code generation, nothing trimmed away), and publish exported games with Native AOT.
- [ ] Put rendering behind a renderer interface, with OpenGL as its first implementation.
- [ ] Add a Direct3D 11 renderer that draws the same as the OpenGL one (Xbox has no OpenGL).
- [ ] Prove a web export: the smallest build that draws a sprite and reads input in a browser.
- [ ] Add a web export of whole games, with sound and saves, from the Build menu.
- [ ] Prove an Xbox export: the smallest x64 UWP package that draws a sprite and reads a controller on an Xbox Series X in Developer Mode.
  - Xbox games must use Direct3D (feature level 11.0 in Game Mode on Series X|S), and .NET reaches UWP only through Native AOT, which Microsoft has in preview for .NET 9 and later. This item proves .NET, Native AOT and Direct3D work on the console before the rest is built. Testing needs the user's console in Developer Mode (a Partner Center developer account): build the package, then ask the user to deploy it through the Xbox Device Portal and report back. Retail release goes through ID@Xbox and is out of scope.
- [ ] Add an Xbox export of whole games from the Build menu, as an x64 UWP package.
- [ ] Make exported games fit the Xbox: controller-only menus, TV-safe margins and 4K output.

### Debugger

- [ ] Add breakpoints to the code editor: the game pauses at the line, showing the values in scope.
- [ ] Add stepping (over, into, out) and continue.
- [ ] Pause on a script exception at the line that threw, with the values in scope, and let rewind go back to just before it.

### Benchmarks

- [ ] Add a BunnyMark benchmark: a scene that keeps adding bouncing sprites, shows the fps and the sprite count, and records how many it holds at 60 fps.
- [ ] Reach the BunnyMark target, recording the result in the README.
  - Target: at least 50,000 bunnies (each an ordinary entity with a Sprite and a Body or script) at 60 fps in an exported game, with 1% lows also at 60 fps and no garbage collected per frame, so a jam game never has to think about performance. That's above Godot 3's C# result (about 27,500), and it leaves room for a real game's logic on top. Profile and fix the bottlenecks (sprite batching, per-entity overhead, allocations, physics broadphase) one at a time; each fix that outgrows one session becomes its own item right after this one.
- [ ] Add a kata runner that builds a 20 Games Challenge clone (start with Pong) and records what it needed: no-code components, lines of script, and what was missing.
- [ ] Add Breakout, Flappy Bird and Space Invaders to the kata runner.

### 3D and VR

- [ ] Add a 3D renderer: meshes and a perspective camera with depth.
- [ ] Add 3D lighting and materials.
- [ ] Show and move things in 3D in the Scene view.
- [ ] Draw first-person levels as real 3D geometry: walls of different heights, and textured floors and ceilings.
- [ ] Let first-person players aim up and down.
- [ ] Add VR through OpenXR: stereo rendering on a PC headset and head tracking, with a VR camera component.
- [ ] Add VR hands: tracked controllers that grab, point and press buttons.
- [ ] Add VR movement: teleport and smooth movement, with comfort options (snap turning, a vignette while moving).
- [ ] Add a VR template.
  - Testing VR needs a headset: check what tests and the MCP can, then ask the user to try it on their headset and report back.

## Done

<!-- /enhance moves finished items here: "- [x] <item> (YYYY-MM-DD)", plus a short note on anything left for later. -->

- [x] Add multi-select in the Hierarchy: Shift/Ctrl-click, with delete, duplicate, cut, copy and paste working on all of them as one undo step. (2026-10-10) Dragging rows in the Hierarchy still moves one at a time; the Inspector shows the last one picked (multi-editing is its own item).
- [x] Add multi-select in the Scene view: Shift/Ctrl-click and box selection, with dragging moving them all. (2026-10-10) Agents box-select and move the selection through the SceneViewModel service, and View > Frame Selection (F) is now a command. A full-screen backdrop sprite still catches drags that start on it (no locking or picking-through yet).
- [x] Edit several selected entities in the Inspector at once: the components and fields they share, with mixed values shown as mixed. (2026-10-10) Names stay one per entity (the header shows how many), and the component catalog still checks requirements against the primary entity. Clicking a mixed check box clears it on all of them (WPF goes from in-between to unchecked).
- [x] Lock entities in the Scene view so clicks and box selections pass through them: a backdrop or a big tilemap stops catching every drag. (2026-10-10) A lock by each Hierarchy row and Entity > Lock; locking is saved in the scene and covers children. Agents set "locked" with entity_create/entity_update, and click the Scene view with SceneViewModel.SelectAt.
- [x] Add a rotate handle to the Scene view. (2026-10-10) A knob on a stem above the primary selection; several turn around the one picked last, as one undo step, landing on whole degrees (Ctrl snaps to 15). Agents turn the selection with SceneViewModel.RotateSelection. It turns Rotation Z only (the engine is 2D), and colliders still don't rotate in physics. Demos gain rotate:, the handle:rotate target and expectvalue on one axis of a vector.
- [x] Add scale handles to the Scene view. (2026-10-10) Squares on the primary selection's corners (both axes) and edges (one), scaling around its position along its own axes; several scale around the one picked last, as one undo step. Shift keeps proportions, Ctrl snaps to tenths, otherwise hundredths; scales never reach zero, and dragging past the pivot flips. Handles hide on things drawn smaller than 16 pixels (zoom in). Agents scale with SceneViewModel.ScaleSelection. Others in a multi-selection that are turned differently from the primary scale along their own axes, which is only an approximation. Demos gain scale: and handle:<corner or edge>.
- [x] Add Scene-view handles to resize sprites and box and circle colliders. (2026-10-10) View > Scale, Resize and Collider Handles (S, R, C in the Scene view) pick what the square handles do. A sprite resizes around its position, so its colliders and children stay put (keeping the opposite edge would move the entity and everything on it); a box collider keeps its opposite side (moving its offset), a circle its middle. Shift keeps proportions, Ctrl snaps to half units, one undo step per drag (SetPropertyGroupEdit). Only the entity picked last resizes. Agents set SceneViewModel.Handles and call ResizeShape. Demos gain resize:.
- [x] Add Scene-view handles for radii and distances: light radii, ranges and sight. (2026-10-10) A [Distance] attribute marks a number as a distance from the entity (Center names an offset, When a property that has to be set), so scripts get handles too; the selection's distances all draw as dashed circles, and View > Distance Handles (D) puts a named knob on each, spaced round the circle. Ctrl snaps to half units, one undo step per drag. Distances of 0 (often "anywhere") have no circle or knob. Agents list them with SceneViewModel.Distances and drag with DragDistance; component_describe says which properties are distances. Demos gain distance: and handle:<Component.Property>. Fighter.Range (from its edge), Chase.GiveUp (on top of Range), Multiplayer.ViewDistance and FirstPersonView.FogDistance aren't marked.
- [x] Make fields that name an entity (a Switch's Targets, a Dialogue's Box, a Wire's From and the like) pickers: pick from a list or drag an entity onto them from the Hierarchy. (2026-10-10) An [EntityName] attribute (Many for comma lists, Inside for an attack's Hitbox or a panel's First) marks the fields, scripts' too; component_describe says which name entities. The list is indented like the Hierarchy; on a list field picking adds or takes out, per entity with several selected. The Hierarchy now selects rows on mouse-up, so dragging one doesn't change the selection. Agents use PickEntity, DropEntity and EntityChoices on the Inspector's fields. Demos gain pickentity:, dropentity: and the pick: target, and DemoInput.DragAndDrop for drags Windows turns into drag-and-drop. Fields still hold names, so renames and unmatched names are the next item. Multiplayer's Player lists the scene's entities but not prefabs (it can name either; typing a prefab still works).
- [x] Update entity references when an entity is renamed, and flag references that don't match anything. (2026-10-10) Renaming (Hierarchy, Inspector, entity_update) renames the entity in every field that names it, as one undo step, when that field means this entity as the game finds it: one inside the field's own entity first (each lever's own Hint), otherwise the only one in the scene with the name (several with it are left alone). Names matching no entity (or prefab) show amber with a warning sign in the Inspector; scene_tree lists them and entity_get shows them as unmatched. Only the open scene: other scenes, prefabs and scripts keep the old name. Demos gain expectflagged:.
- [x] Draw the selection's links to other entities (Switch targets, Wire sources, Follow targets) in the Scene view. (2026-10-10) Every field marked [EntityName] on a selected entity draws a dashed arrow to each entity it names, and fields elsewhere naming a selected entity draw one into it, labelled with the fields (several fields between the same two share an arrow); arrows bow so links both ways stay apart, and a label too long for its arrow goes past the tip. A name several entities share links to each (Wire uses the nearest in play). This replaces the Switch and Door lines written for each component. EntityName gains Unless (a Door's Target isn't linked or flagged when it goes to another Scene). Agents read SceneViewModel.Links. Demos gain expectlink: and the link: target. Labels aren't clickable yet, and nothing toggles the arrows off.
- [x] Draw Mover paths and Patrol routes in the Scene view, with points you drag to edit. (2026-10-10) Patrol had no route, so it gained Left and Right (how far either side of where it's placed it walks before turning; 0 walks on to a wall or an edge) and Walked (how far along it is, counted at the end of each frame and saved, so rewinds keep the route in place). A selected mover's points and a selected patrol's turns get knobs whatever handles are out: drag a point (Ctrl snaps to half units), drag a plus between points or past the last to add one, double-click to take one off (a mover keeps one; a patrol's turn clears); a patrol's open side is a hollow knob on a dotted arrow. Agents use SceneViewModel.PathPoints, DragPathPoint, InsertPathPoint and RemovePathPoint. Demos gain path: and removepoint:, and handle: names path knobs. A mover's start stays where the entity is (move the entity to move it), only the first Mover or Patrol on an entity gets knobs, and patrol routes are level only.
- [x] Link placed copies to their prefab: copies remember their prefab, and saving the prefab updates every copy in every scene. (2026-10-10) An entity's Prefab (its prefab file's path in Assets) is saved in scenes as "prefab"; dropping, Add to Scene, duplicating and entity_create with a prefab all make linked copies. Save as Prefab on a copy saves over its own prefab; saving makes the entity a copy, and every other copy takes the prefab's components, scale and children, keeping its name, place, id and enabled and locked state: in the scene being edited as one undo step (while playing too), in other scenes straight to their files. A .prefab an agent writes with asset_write updates the copies the same way. Entity > Unlink from Prefab; the Hierarchy marks copies with a package and the Inspector names the prefab; scene_tree and entity_get show the link. Demos gain answer: (a message box's button), expectprefab: and the prefab:/prefabnote targets. Editor tests now keep settings in memory (they had been writing recent projects to the user's settings). Per-copy changes are lost on the next save until overrides land; renaming or moving a prefab file unlinks its copies, and copies inside prefab files aren't updated (new item).
- [x] Keep per-copy overrides on linked prefabs: changed fields stay changed, marked in the Inspector, with Revert and Apply to Prefab. (2026-10-10) Overrides aren't stored: they're whatever a copy (or something inside it) differs from its prefab in, matched part by part (children by name, components by type, in order); a copy's own name, place, enabled and locked state never count. Saving a prefab (Save as Prefab, Apply to Prefab, an agent's asset_write, which now passes the file's old text) merges each copy three ways against the old version: changed fields, added components and children, and ones taken away are kept. The Inspector marks changed fields (a mark by the field and a bold name, the prefab's value in the tooltip; click the mark or right-click the name for Revert and Apply to Prefab), says how many changes a copy has, and marks added components Added; Entity > Revert to Prefab puts whole copies back. Agents see "overrides" in entity_get and use RevertToPrefab/ApplyToPrefab on the Inspector's fields. Demos gain revertfield:, applyfield:, expectoverrides: and the override: target. A copy nested inside another copy follows its own prefab (the outer copy's changes to it aren't kept), renaming a child of a copy counts as removing it and adding another, and added or removed components can only be applied or reverted with the whole copy (Save as Prefab, Revert to Prefab).
- [x] Open a prefab to edit it on its own, in its own tab. (2026-10-10) Double-click it in Assets (or Edit Prefab on its menu), Entity > Edit Prefab on a copy, or File > Open Prefab: it opens in a document tab beside Scene and Game, and the Hierarchy, Inspector and Scene view (which moves into the tab) edit it, with its own undo; each tab keeps its selection and view. What's made, pasted or dropped at the top goes inside the prefab (a prefab is one entity; saving with several at the top is refused), and only names of things inside a field's own entity are flagged there. Save (Ctrl+S, Scene_Save) writes the prefab and updates every copy as Save as Prefab does; a prefab saved from elsewhere updates its open tab (keeping unsaved changes). File > Close Prefab, the tab's x or a middle-click close it, asking to save. Playing plays the scene (PlayMode had copied whatever was showing); Build saves open prefabs too (Workspace.SaveAllAsync). PrefabFiles moved to Core/Workspace so the workspace can update copies. Agents use Prefab_Open, Entity_EditPrefab, Prefab_Close and Workspace.ShowScene; entity_create without a parent goes inside the prefab, and scene_tree and editor_state say a prefab is showing. Prefab tabs keep no crash-recovery copy, and renaming a child in a prefab's tab loses what copies changed on it.
- [x] Rename and move prefab files in Assets (and with an agent's asset_move), keeping every copy linked: in scenes, inside other prefabs and in open tabs. (2026-10-10) Rename... on a prefab's Assets menu, dragging it onto a folder (or empty space, for Assets) and New Folder... are AssetsPanel methods (RenamePrefab, MovePrefab, NewFolder), so agents reach them with ui_invoke too, and asset_move moves prefabs for agents. PrefabFiles.Relink rewrites the links in every scene and prefab file on disk (PrefabCopies.RelinkFile, RelinkPrefabFile) and in the open scene and prefab tabs as an undo step each (staying saved when they were); the prefab's own tab follows its file (SceneDocument.MoveTo). Demos gain prompt:, assetmenu:, assetdrop:, expectlinked: and the asset: target. Only prefabs move (not other files or folders), a rename outside Griff still unlinks copies, and renaming doesn't change the name a Spawner's Prefab or Game.Spawn uses.
- [x] Update copies nested inside other prefabs when the inner prefab is saved. (2026-10-10) Saving a prefab (Save, Save as Prefab, Apply to Prefab, asset_write) updates its copies inside every other prefab file (PrefabCopies.UpdatePrefabFile), keeping what that prefab changed on them, and inside open prefab tabs as an undo step (staying saved, as their files are updated too); something in a tab saved as a prefab becomes a copy there. Two prefabs holding copies of each other are left alone, with a note. A placed chest's coins were already updated in scenes, so a chest copy now matches its prefab after a coin is saved. A prefab's tab now opens with the prefab selected.
- [x] Make panels dockable: drag a panel to dock it to any side, tab it with another, or float it. (2026-10-11) PanelLayout (a DI service) holds the layout as splits, tabbed groups and floating windows around the document area, and PanelHost builds it on screen, moving each panel's view and tab rather than making new ones. Dragging a tab shows a box where it will go: right by the window's edge docks on that side, near a panel's (or the documents') edge docks beside it, on its tabs or middle tabs with it, and over the middle of the documents or outside the editor floats it (Ctrl floats anywhere, Escape calls it off). Floating windows keep the editor's shortcuts; closing one puts its panels back with the ones they start beside. Agents use PanelLayout's Dock, Tab, Float, CloseFloating, Return and Show, and read its Summary. Demos gain dock:, float:, closefloat:, expectlayout: and the float: target, and panel: finds panels in floating windows. Layouts aren't saved yet (next item), floating windows hold tabs only (no splits), and documents stay in the main window.
- [x] Save and restore panel layouts, with a reset to the default layout. (2026-10-11) The editor reopens with the panels where they were (floating windows too) and at its last size and place on screen, kept in the user's settings (EditorSettings.PanelLayout); a floating window or editor window that would open off every screen is put back on one. View > Save Layout... saves by name, View > Layouts and View > Delete Layout list the saved ones in submenus, and View > Reset Layout puts everything back. Layouts are per user, not per project. A kept layout that names panels that are gone drops them, and panels added since go where they start out. Commands can now offer Choices (shown as a submenu; agents pass "choice" to the command's tool, or answer its prompt), and agents also use PanelLayout's Save, Load, Delete, Reset and SavedLayouts. Demos and snapshot runs start from the default layout and keep layouts in memory, so they never touch the user's. Demos' menu: walks into submenus (menu: View > Layouts > Art). Floating windows still hold tabs only, and documents stay in the main window.
- [x] Add an animation clip editor that lists a sprite's clips and previews them playing. (2026-10-11) A Clip Editor window (Art > Animation Clips, like the Sprite Painter, so the default panel layout is unchanged) follows the selection: it lists the Animation's clips with their frames, speed and looping (and its own frames, when it has no clips or starts without one), and plays the one picked in a preview cut from the sheet, with a strip of its frames; pause, step (Space, Left, Right) or click a frame. Add (the first clip takes the animation's own frames and starts the game), Rename (the starting clip follows), Remove and Start With This are one undo step each; a sprite with no Animation offers Add Animation. Its view model is the ClipEditor service, so agents reach it with ui_inspect/ui_invoke (SelectClip, AddClip, RenameClip...), and the window is Art_Clips. Animation gains ClipSettings, FrameAt and FormatClips. Demos gain clips and the clips: targets. Frames, speed and looping are still typed in Clips (next item), and renaming a clip doesn't rename it in scripts or the names movement components look for.
- [x] Pick a clip's frames from the sprite sheet visually in the clip editor, and set its speed and looping there. (2026-10-11) The Clip Editor shows the whole sheet under the preview with the picked clip's frames marked: drag across frames to make it play those (a click picks one), Shift-click to stretch it to reach a frame; Speed and Loops set how it plays (giving a clip its own, with a note while it uses the animation's), and on the own frames all three set the animation's First Frame, Frame Count, Frames Per Second and Loop. One undo step each. Agents use SetFrames, ExtendFrames, SetSpeed and SetLoop (or set Speed and Loops) on ClipEditor, and its Sheet lists each frame as in the clip or showing; ui_set now says why a setter refused a value. Demos gain clipframes:, clipspeed: and the clips:sheet, clips:speed, clips:loop, clips:settings and clips:note targets. A clip is still one run of frames in order (no skips, repeats or playing backwards), and a clip's own speed or looping can only go back to the animation's by editing Clips.
- [x] Add rectangle and fill tools to tilemap painting. (2026-10-11) Tool buttons under the Scene view's tile palette, or B, R and G as in the Sprite Painter: the Brush as before, the Rectangle fills the box dragged out (shown while dragging; Esc lets go), and Fill fills the cell and every cell joined to it side by side holding the same tile (or all empty), within the grid, showing what it would cover on hover (up to 4096 cells). Shift erases with any of them; each is one undo step, and a rectangle past the left or top edge grows the map as painting does. Painting moved out of the Scene view's code-behind into SceneViewModel (TileTool, BrushTile, PaintTarget, CellAt, PaintTile, PaintRectangle, FillTiles, PickTile), so agents paint with ui_invoke; scripts get Tilemap.FillRect and FloodFill (Tilemap.PaintedRect, Filled and FillRegion underneath). The MCP now returns tuples as arrays and describes them as [..] or null. Demos gain scenehover:, expecttile:, the tiles: target (a tool's button or a palette tile), and expectvalue shows tile rows. Rectangles are filled only (no outline-only box), and fill doesn't reach diagonally or past the grid's edges.
- [x] Let a tilemap use more than one tileset, removing the 62-tile limit. (2026-10-11) A map's Image is its first tileset (tiles 0 on); More Tilesets adds others, one per line ("Tiles/props.png 4x4"), each numbered from the next hundred after the tiles before it, so a tileset that grows doesn't renumber the ones after it (Tilemap.Tilesets(), TilesetOf, ImageOf; TileRect cuts from the tile's own tileset, in the game, first-person walls, block icons and the Scene view). Cells stay one character: tiles 62 on are accented letters, then CJK characters (up to 21451), and scenes now save letters past ASCII as themselves rather than as escapes (quotes and HTML characters are escaped as before). Tile-number fields (Background, One Way, Ladder, Slope, Auto and Animated Tiles, Blocks' sets) take the new numbers, and keep sets only as long as the highest tile listed. The Scene view's palette has a tab per tileset and a + tab (Art > Add Tileset to Tilemap, whose submenu lists the project's images: a picture painted in Griff keeps its grid, any other is cut into tiles the size of the first tileset's; a map with no image takes it as its first), scrolls with the wheel when a tileset has more tiles than fit, and [ ] step through the tileset shown. Agents use SceneViewModel.AddTileset, ShowTileset and PaletteTileset, or the Art_AddTileset command. Demos gain the tiles:<tileset name> and tiles:+ targets. Taking a tileset out or reordering them renumbers the ones after it, and the Paint button on a tilemap paints its first tileset only.
