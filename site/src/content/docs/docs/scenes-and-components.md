---
title: "Scenes and components"
description: "How a game is put together: entities, components, scenes, prefabs and spawners."
---

A game is made of **scenes**. A scene holds **entities** (things with a name and a place), and an entity does what its **components** say: a Sprite draws it, a Body moves it, a Health lets it be hurt. Components are plain data, edited in the Inspector and saved in the scene's JSON file, so most games need little or no code. The guides in this section cover the ready-made components by what they're for; the [component reference](/reference/components/) lists every one with each property, and an agent can ask for the same text with `component_describe`.

Scripts are components too: see [Scripting](/docs/scripting/).

## Spawners and lifetimes

A **Spawner** spawns its **Prefab** every **Interval** seconds where it stands, after a **Delay**, within a **Radius**, up to **Count** in all and **Max Alive** at once; with **Beats** set, it spawns on the music's beat instead (see [Keeping the beat](/docs/audio/#keeping-the-beat)). A **Lifetime** destroys its entity after **Seconds**, fading it out over the last **Fade Out** seconds: bullets, effects, score popups.

## Prefabs

Select an entity and choose **Entity → Save as Prefab** to save it (with its children) to `Assets/Prefabs`. Drag a prefab into the Scene view to place it, or spawn it from a script. Save as Prefab works while playing too, to keep a live entity you've tweaked.

Placed copies stay linked to their prefab: the Hierarchy marks them with a package, and the Inspector says which prefab each is a copy of. Change one copy and choose **Save as Prefab** again: it saves over its own prefab (whatever the copy is called), and every other copy, in this scene and in every other scene, is brought up to date. Each copy takes the prefab's components, scale and children, and keeps its own name and place, so the fields that name it still find it. In the open scene that's one undo step; other scenes are saved straight away. Copies placed inside other prefabs (coins in a chest) are updated too, keeping what that prefab changed on them, so every chest's coins follow the coin (two prefabs that hold copies of each other are left alone). Saving an ordinary entity over an existing prefab links it and updates that prefab's copies the same way. **Entity → Unlink from Prefab** turns copies back into ordinary entities that saving the prefab leaves alone.

What a copy changes is its own. Make one gem red and the Inspector marks its **Tint** (an orange mark by the field, its name in bold, and the prefab's value in its tooltip) and says "Copy of the Gem prefab, with 1 change"; saving the prefab from another copy gives the red gem everything new except its colour. Click the mark (or right-click the field's name) for **Revert to Prefab**, which puts the prefab's value back, and **Apply to Prefab**, which saves this one field into the prefab, so every copy that hasn't changed it takes it too. Components a copy adds show **Added** on their header and come along when the prefab is saved, and components or children it takes away stay away. Things inside a copy count against the matching part of the prefab (children are matched by name), so a brighter lantern on one torch is a change too. **Entity → Revert to Prefab** puts whole copies back as their prefab has them, keeping their name and place, as one undo step. A change is simply whatever differs from the prefab, so setting a field back by hand reverts it as well.

To change the prefab itself, open it on its own: double-click it in Assets (or right-click it and choose **Edit Prefab**), select a copy and choose **Entity → Edit Prefab**, or use **File → Open Prefab**. It opens in a tab of its own beside the scene's, with the prefab selected, and the Hierarchy, Inspector and Scene view edit it there, with its own undo history and a reminder at the top of the Scene view. Whatever you add goes inside it (a prefab is one entity), and fields naming things in the scene, like a chaser's **Target**, aren't flagged, since they're found wherever it's placed. **Save** (Ctrl+S) saves it and brings every copy, in every scene, up to date, each keeping what it changed. Pick the Scene tab to go back (each tab keeps its own selection and view), and close the prefab's tab with its ×, a middle-click or **File → Close Prefab**, which asks to save any changes. Playing plays the scene. If the prefab is saved from a copy while its tab is open, the tab shows the new version too.

Games can switch scenes with `Game.LoadScene("Level2")`. **Game → Clear Save Data** forgets what the game saved while playing in the editor.
