---
title: "Fighting games"
description: "Hitboxes and hurtboxes on each frame of an animation, drawn in the Clip Editor, that land hits with damage, knockback and hitstun, frame by frame."
---

## A sparring pair

**Entity → Genres → Fighting → Sparring Pair** makes something to try it on straight away: a brawler to play and a sandbag beside it, on a floor of their own, with a line over them showing the sandbag's health. Play, walk up to the sandbag (the arrows or WASD) and punch it with the Attack action (X, L, the right mouse button or the gamepad's X): on the frames its fist is out it lands, once a punch, and the sandbag flashes, loses 2 health and flies back (the brawler's head takes half as much again, for a second brawler to find). Turn on **Game → Show Hitboxes** to watch the boxes as it plays: hitboxes red, hurtboxes blue. Its pictures go in `Assets/Fighting`, to repaint; select the brawler and open **Art → Animation Clips** to see its boxes on each frame and change them. Everything is in the Inspector, with no code.

## Hitboxes and hurtboxes

A **Hitboxes** gives an entity boxes on each frame of its **Animation**'s clips: **hitboxes** (red), where it hits, and **hurtboxes** (blue), where it can be hit. While the entity shows a frame, it has that frame's boxes and no others, so a punch hits only on the frames its fist is out, a dodge's frames with no hurtboxes can't be hit, and a kick's leg can be hit as it swings. Each box is a box or a circle, placed from the entity's middle in world units, facing right as its picture is drawn; when its sprite is flipped to face left (**Flip X**, as movement does), its boxes are flipped with it. An entity with no animation (a sandbag, a wall that can be hit) has one frame's boxes.

Each hitbox has its **damage**, its **knockback** (how fast it sends what it hits flying, in units a second: away from it, and up), its **stun** (how many steps, or frames at 60 a second, what it hits is in hitstun for) and a **group**. A swing hits each thing once per group, however many frames touch it: give the boxes of a punch's frames the same group and it lands once; give a flurry's three strikes groups 1, 2 and 3 and each lands. A swing starts over each time its clip does (an **Attack**'s action starts it from its first frame), and each time round a looping clip. Each hurtbox has a **multiplier** for the damage of what lands on it: 1.5 on a head takes half as much again, 0.5 on a shield arm half as much. A hitbox touching several of a target's hurtboxes at once lands on the one that takes the most.

What hits what: an entity's hitboxes hit other entities' hurtboxes, never its own (or those of what it's inside, or what's inside it), and with a **Team** only other teams' (two fighters on `Red` don't hit each other). In a network game, as with every hit, players' own things and teammates don't hurt each other unless the game has **Friendly Fire**. Its **Layers** reach further: things on those collision layers with a **Health** but no boxes of their own are hit through their colliders, so a hero whose sword is drawn on the frames of its swing hits a platformer's slimes too.

The boxes are lines in its **Boxes** (`Punch 2 hit at 0.6,0.3 size 0.5x0.25 damage 3 knockback 4,2 stun 12`, `Idle 0 hurt at 0,0.7 radius 0.3 multiplier 1.5`, `*` for the animation's own frames), but they're drawn rather than typed.

## Drawing them in the Clip Editor

Select the entity and choose **Art → Animation Clips**. Pick a clip and the frame to draw on (click it in the strip, or step with Left and Right): its boxes are drawn over the picture, with their list beside it under **Boxes**.

- **Hitbox** (H) and **Hurtbox** (U): drag over the picture to draw one, snapped to its pixels. **Draw circles** (C) draws circles instead.
- **Select** (S): click a box to pick it, drag it to move it, drag its corners to resize it. Delete removes it.
- The box picked shows its **Damage**, **Knockback** (away and up), **Stun** and **Group**, or a hurtbox its **Multiplier**, to type.
- **Copy From Previous Frame** (Ctrl+D) gives the frame the boxes of the frame before it, to move on a little as the fist goes out.
- **Onion skin** (O) shows the frame before underneath, faintly, with its boxes dashed, to line the new ones up with.

The first box drawn gives the entity a **Hitboxes**. Every change is one undo step (Ctrl+Z and Ctrl+Y in the Clip Editor, or Edit → Undo), and renaming or removing a clip takes its boxes along in the same step. Agents draw and set boxes through the same Clip Editor (`ui_invoke ClipEditor AddBox`, `ui_set ClipEditor BoxDamage`), or set **Boxes** with the scene tools.

## What a hit does

A hit lands on the step the frame showing it comes, after physics: the boxes are where everything is that step. All the hits of a step are found before any lands, so two fighters striking each other at once both land (a trade).

- The target's **Health** (its own, or the nearest above it) takes the damage, times the hurtbox's multiplier, flashing and blinking as any hit does. While it can't be hurt (its **Invincible Time** after a hit), nothing lands on it, and a swing still under way lands once it can: give fighters an Invincible Time of 0 so every hit of a combo counts.
- Its **Body** is sent flying at the knockback, turned the way the attacker faces.
- Its **Hitstun** is set to the stun, counting down a step at a time; its **Stunned** output is on meanwhile, and its hurtboxes show yellow under Show Hitboxes. Its **Stun** action puts it in hitstun for a number of steps, and **Recover** ends it.
- The attacker's **Hit Sound** plays where the boxes met, its **Effect** prefab is spawned there, and the game freezes for its **Hit Stop** seconds so the hit lands hard.
- The attacker sends its **Hit Message** and the target its **Got Hit Message**, each with the damage as its data (a **Counter** adds a combo's damage up). Its outputs **Hit** and **Got Hit** are on for the step the hit lands, for a wire into any action (a screen shake, a sound), **Hits** counts the hits it has landed, and **Hitstun** is how much it has left.

## Seeing the boxes in play

**Game → Show Hitboxes** draws every box in play over the running game: hitboxes red, hurtboxes blue (yellow in hitstun), and a hitbox that has already hit this swing faint. It stays on for the games played after, in Play with Players' windows and after a rewind. A game can show them itself, for a training mode: `Game.ShowHitboxes = true`.

## Frame by frame, rewinds and multiplayer

The boxes follow the frame the entity shows at each step of the game, worked out just as the animation picks it, so a hit lands on the same step every time, at any frame rate, and the same input always lands the same hits. An animation's **Time**, the hitstun, what a swing has already hit and the hits landed are kept with the scene, so a rewind back into the middle of a punch carries on from there and lands as it did. In a network game the host works out the hits (as it does all damage) and everyone sees them.

## Scripting

A **Hitboxes** raises `Hit` as one of its hitboxes lands and `GotHit` as one of its hurtboxes is hit, each with a `HitInfo`: the `Attacker`, the `Target`, the `Damage` dealt, the `Knockback` it was sent flying at, the `Stun`, the hitbox's `Group` and the `Point` where the boxes met. `Now()` gives the boxes of the frame the entity shows, `Showing()` which clip and frame that is, and `On("Punch", 2)` the boxes of any frame; `FrameBox.TryParse` and `ToString` read and write their lines.

```csharp
public class Fighter : Behavior
{
    protected override void Start() => GetComponent<Hitboxes>()!.GotHit += hit =>
    {
        if (hit.Damage >= 3)
            Game.Send("BigHit", hit.Damage);
    };
}
```

## Mixing it with other games

A **Hitboxes** takes no input of its own, so it goes on anything: a platformer hero's sword drawn on its Attack clip (an **Attack** with no Hitbox plays the clip, and its frames hit), an enemy's bite, a boss whose weak spot is a hurtbox with a multiplier of 3. Its actions (**Stun**, **Recover**) are for triggers and wires, and its outputs (**Hit**, **Got Hit**, **Hits**, **Hitstun**, **Stunned**) for counters, bars, texts and wires.
