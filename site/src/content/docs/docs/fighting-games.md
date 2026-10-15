---
title: "Fighting games"
description: "The Fighter template, a whole versus fighting game for two at one machine or against the computer; fighters with moves in numpad notation (quarter circles, dragon punches, charges), blocking high and low, hitstun and combos, an input buffer and cancels; a character select, rounds against the clock, a computer to fight; hitboxes and hurtboxes on each frame of an animation, drawn in the Clip Editor."
---

## The Fighter template

**File → New Project → Fighter** makes *Dojo Clash*, a one-on-one fighting game with no code, for two people at one computer (sharing the keyboard, on two controllers, or one on each) or one against the computer, with a training mode.

- **The title** (`Main`) has **Versus**, **Vs Computer**, **Training** and **Quit**, with Kenta and Mira squaring up either side.
- **The character selects** (`Select` for two players, `Solo` against the computer, `Practice` for training): each player moves their own cursor over the fighters' cards with their own keys or controller and picks with a punch (or a click), the dash button changing their mind; each side shows the fighter picked big, whether they're ready and what they play with ("Left keys (WASD)", "Gamepad 1"). **Swap Controls** (Tab) swaps the two players' keyboard and controller. Against the computer, it takes the fighter after yours.
- **The fight** (`Stage`), on a dojo yard at dusk: best of three rounds of 60 seconds. Each round opens with "Round 1" (or "Final Round") and "Fight!", and ends with "K.O." (a fighter's health gone) or "Time" (the one with more health left wins; even is a draw, which counts for nobody). Health bars, names and the rounds won across the top, the clock between them, combos either side; the winner is announced with **Rematch**, **Character Select** and **Title**, and Esc (or Start) pauses.
- **Training** (`Training`): no clock and no rounds, against a dummy of the fighter after yours that gets up again when it's knocked out. **1** changes its guard (never, standing, crouching, always), **2** what it does (stand, crouch, jump, or fight back), **3** shows the hitboxes and **4** puts you both back where you started; each is a button too, and the combo and its damage are shown.

Kenta is an all-rounder in a white gi: a jab, a kick, a low kick, a kick in the air, a palm strike (`236P`), an uppercut (`623P`) and a rush (`214K`). Mira is quicker and kicks: a high axe kick that must be blocked standing (`6K`), a spin kick on a charge (`[4]6K`) and a flash kick (`[2]8K`). Each is a prefab (`Prefabs/Kenta.prefab`, `Prefabs/Mira.prefab`) with a sheet of its own painted in code (`Assets/Fighting`), its own moves (**Entity → Edit Moves...**) and boxes (the Clip Editor); a third fighter is a third prefab, added to the select's **Characters** with a card for it. Every hit, block, grunt, K.O. and bell, and the music, are made in code.

The controls: two at one keyboard, the left player walks with WASD, punches with F and kicks with G, and the right player walks with the arrows, punches with K (or the number pad's 1) and kicks with L (or 2). With one controller, one player has the keyboard (J or U to punch, K or I to kick) and the other the controller; with two, each has their own (X punches, A kicks). Up jumps, down crouches, holding back blocks. See [Several players at one machine](/docs/players/#several-players-at-one-machine).

## A sparring pair

**Entity → Genres → Fighting → Sparring Pair** makes something to try it on straight away: a brawler to play and a sandbag beside it, on a floor of their own, with a line over them showing the sandbag's health. Play, walk up to the sandbag (the arrows or WASD) and punch it with the Attack action (X, L, the right mouse button or the gamepad's X): on the frames its fist is out it lands, once a punch, and the sandbag flashes, loses 2 health and flies back (the brawler's head takes half as much again, for a second brawler to find). Turn on **Game → Show Hitboxes** to watch the boxes as it plays: hitboxes red, hurtboxes blue. Its pictures go in `Assets/Fighting`, to repaint; select the brawler and open **Art → Animation Clips** to see its boxes on each frame and change them. Everything is in the Inspector, with no code.

## A training pair

**Entity → Genres → Fighting → Training Pair** makes two fighters to try moves on: one to play and a training dummy facing it, on a floor of their own, with the dummy's health and your combo over them. Walk with the arrows or A and D (holding back, away from the dummy, blocks; down crouches; up jumps, straight or forward or back), punch with **P** (U, J or the gamepad's X) and kick with **K** (I, K or the gamepad's A). It has a jab (`5P`), a kick (`5K`), a low kick (`2K`, blocked only crouching), a kick in the air (`j.K`), a palm strike on a quarter circle forward (`236P`: down, down-forward, forward and P), an uppercut on a dragon punch (`623P`: forward, down, down-forward and P) and a rush on a charge (`[4]6K`: hold back a moment, then forward and K). The jab, kick and low kick cancel into the specials once they connect, so a jab and then a quick quarter circle lands a two-hit combo. The dummy just stands there: set its **Auto Block** to **Always** to practise against a guard, or tick its **Controllable** to play it. Its pictures go in `Assets/Fighting`; **Entity → Edit Moves...** edits the moves and the Clip Editor their boxes.

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

## Fighters

A **Fighting Character** makes an entity a fighter with no code (it needs a **Body**, a collider and a **Hitboxes**). It reads the controls of whoever plays it: the directions (the arrows, WASD, the D-pad or the stick) and its **Buttons**, each a letter its moves are written with and the input action it is (`P Punch, K Kick` to start with; `L Light, M Medium, H Heavy` with those actions added in **Game → Controls**). Forward walks at **Walk Speed**, back at **Back Walk Speed**, up jumps (**Jump Height**, sideways at **Jump Speed** for a jump forward or back), down crouches, and on the ground it slows to a stop by its **Friction** after a knockback or a lunge. It faces its **Opponent** by itself (an entity by name, or empty for the nearest other fighter not on its **Team**) whenever it's on the ground and free, so forward is always towards them, and it plays its **Animation**'s `Idle`, `Walk` (or `Run`), `WalkBack`, `Crouch`, `Jump`, `Block`, `CrouchBlock` and `Hurt` clips when it has them.

Untick **Controllable** for a fighter nobody plays (a dummy, a cutscene); its **Focus** action gives it the player's input as other genres' pieces do. Its **Perform** action does a move by name as if its input were pressed, for a trigger, a wire or a computer opponent's script.

## Moves and their inputs

Its **Moves** are a line each: a name and a colon, the input, then the move's settings.

```
Jab: 5P frames 3/2/7 cancels special
Low Kick: 2K frames 5/3/14 low cancels special
Fireball: 236P frames 12/4/30 chip 0.25
Uppercut: 623P clip Rising frames 3/10/26 push 1,8
Rush: [4]6K clip Palm frames 8/8/18 push 7,0
Air Kick: j.K clip Kick frames 3/10/2
```

Inputs are written in numpad notation, the directions as the keys of a number pad for a fighter facing right: 6 forward, 4 back, 2 down, 8 up, 3 down-forward, 1 down-back, 5 neither. Then the button.

| Input | Means |
| --- | --- |
| `5P` | P standing (also while walking, unless there's a `6P` or `4P`) |
| `2K` | K crouching (down-back and down-forward too, unless there's a `1K` or `3K`) |
| `6P` | P while holding forward |
| `P` | P whatever the direction |
| `236P` | quarter circle forward: down, down-forward, forward, P |
| `214K` | quarter circle back |
| `623P` | dragon punch: forward, down, down-forward |
| `41236P` | half circle forward |
| `22K` | down, down (let go between) |
| `[4]6P` | charge: back held for **Charge Frames** steps, then forward and P |
| `[2]8K` | charge down, then up and K |
| `j.K` | K in the air (any input can start with `j.`) |

Directions are read the way the fighter faces, so a quarter circle forward works from either side. A motion's directions must come in order within **Motion Frames** steps of the button (other directions between them don't matter, and the button can come a few steps after the last one); a charge's let-go direction can lean either way (3 or 9 for 6). When several moves fit what was pressed, the longest motion wins (a special over a normal, a half circle over a quarter circle), then of two as long the one whose last direction came nearest the button (so walking forward into a quarter circle, `6 2 3 6 P`, is the quarter circle, though `6 2 3` is in it too), then the exact direction over the near one (`6P` over `5P` while holding forward), then the move listed first. A move on the ground comes out only on the ground, one with `j.` only in the air (and ends as the fighter lands).

A move's settings, all optional:

- `clip Name`: the animation clip it plays; left out, the one named like the move without spaces (`Low Kick` plays `LowKick`).
- `frames 3/2/7`: its startup, active and recovery, in steps (frames, at 60 a second), or `startup 3`, `active 2` and `recovery 7` one at a time. See below.
- `low` or `high`: where it must be blocked (see Blocking). Mid, blockable either way, if left out.
- `blockstun 10`, `chip 0.25`: what a blocked hit does (see Blocking).
- `push 2,0`: how fast it sends the fighter forward and up as it starts to hit: a lunge, a rising uppercut.
- `message Name`, `sound Path`: a message sent and a sound played as it starts.
- `cancels special`, last: what it can be cancelled into (see Cancels and combos): move names with commas between, `special`, `normal` or `any`.

## Startup, active and recovery

A move plays its clip over its frame data: the clip's frames before its first hitbox are spread over its **startup**, the frames with hitboxes over its **active** steps, and the rest over its **recovery**. Its hitboxes hit only while it's active, whatever frame shows, so the frame data says exactly when it hits: `frames 3/2/7` hits on its fourth and fifth steps and leaves the fighter free on its thirteenth. Left out, each part takes as long as its frames take at the clip's own speed. Everything is counted in steps, at the game's step rate, so it's the same on every machine and every time; a hit stop holds the move where it is.

## The input buffer

A button pressed up to **Buffer Frames** steps (8 to start with) before the fighter can act comes out the moment it can: as a move or blockstun ends, or as it lands. A motion done during a move counts too, so a quarter circle finished a moment before the jab ends brings out the palm on its first free step, and so do presses during a hit stop, so a cancel pressed as the hit lands comes out as the freeze ends. Each press makes one move.

## Cancels and combos

Once a move has hit or been blocked, it can be cancelled, from its first active step on, into what its `cancels` names: a press of one of those (buffered or not) starts it at once, cutting the rest short. `cancels special` lets a jab go straight into any special move, `cancels Kick` chains into the kick.

A hit holds what it hits in **hitstun** for the hitbox's stun steps (its **Hitboxes**' Hitstun): a fighter in hitstun does nothing (its move is cut short, it plays its `Hurt` clip and slides to a stop) until it recovers. Each hit a fighter lands while the one it's hitting is still in hitstun from its last counts towards its **Combo**; the combo is over once they recover. The Move List shows what a move leaves on hit and on block: `+6 on hit` means the fighter is free 6 steps before its opponent, time to land another move. **Combo**, **Combo Damage** and **Best Combo** are outputs, and **Combo Hits** is the combo from its second hit on (one hit is no combo yet), for a text: `{0:0' hit combo';;#}` shows "3 hit combo", and nothing until then.

## Blocking

Holding back, away from the attacker, blocks: standing (back) blocks mid and high hits, crouching (down-back) mid and low ones. A fighter blocks only on the ground and free (not during a move or hitstun); a cross-up from behind is blocked by holding the other way. A blocked hit does only **chip damage** (its damage times the move's `chip`, or the blocker's **Chip Damage**, a tenth to start with), pushes the blocker back by **Block Pushback** of its knockback, and holds it in **blockstun** for the move's `blockstun` steps (two thirds of the hitbox's stun if left out), playing its `Block` or `CrouchBlock` clip; a hit that comes during blockstun is blocked too. It plays the blocker's **Block Sound**, spawns its **Block Effect**, sends its **Block Message** with the chip as its data, freezes the game for half the attacker's hit stop, and counts as the attack connecting, so it can cancel. **Auto Block** blocks with no one holding back: **Standing**, **Crouching** or **Always** (the right way for each hit), for a training dummy or a computer opponent. Its **Blocking**, **Blocked** and **Blockstun** outputs say what it's doing.

## The Move List

Select a fighter and choose **Entity → Edit Moves...**: its moves are listed with their inputs drawn as arrows (`↓↘→ P`), what the motion is called and their frame data. For the move picked, pick its motion from a list of every direction and the common motions (quarter circles, dragon punches, half circles, charges) and its button, or type the input; pick its clip from the animation's, and type its startup, active and recovery (0 for the clip's own), where it's blocked, its blockstun, chip, push and cancels. Its frames are drawn as a bar (startup green, active red, recovery blue) with what it leaves on hit and on block and its damage, and what's wrong is said: a clip it hasn't got or with no hitbox, a button it hasn't got, a cancel into a move it doesn't have. **Up** and **Down** order the moves (the one listed first wins a tie). Each change is one undo step, the same as typing the line. Agents work it through `ui_invoke MoveListEditor AddMove`, `ui_set MoveListEditor MoveInput 236P` and the rest, or set **Moves** with the scene tools.

## Moves in a table

A fighter's **Moves Table** names a table of more moves, a row each: the row's name, and columns **Input**, **Clip**, **Startup**, **Active** and **Recovery** (or **Frames**, `3/2/7`), **Hits** (`Low` or `High`), **Blockstun**, **Chip**, **Push**, **Cancels**, **Message** and **Sound**, any left out. They come after its own moves, and a table changed while the game runs takes effect at once. **Make Moves Table** in the Move List moves a fighter's own moves into a new table (`Fighter Moves`) and points its Moves Table at it, so a cast's frame data can be balanced side by side like a spreadsheet in **Data → Tables**.

## Versus matches

A **Versus Match** turns two fighters (its **First** and **Second**, by name) into a match of rounds, with no code. Each round starts with them back where they began, at full health, facing each other and held still (their **Held**) while "Round 1" is announced for **Intro Time** seconds; then "Fight!" and the clock runs down from **Round Time** (0 for no clock). A fighter's **Health** running out is a K.O.: the game freezes for **KO Freeze** seconds, the other wins the round (both at once is a double K.O., a draw), and **Round Over Time** seconds later the next starts. Time up, the one with more health left (as a share of its most) wins it. Once one has won **Rounds To Win** rounds (2 is best of three; the last is announced "Final Round"), "Kenta Wins" is announced and its **Win Message** sent (`Match Over`, with the winner's name), which a panel opens on. With **Rounds To Win** at 0 it's a training session: no rounds or clock, and a fighter knocked out gets up again a second later.

Its outputs are for a HUD with no code: **Round**, **Time Left** (a whole number counting down), **First Wins** and **Second Wins** (out of Rounds To Win, for **Stars**), **First Name** and **Second Name**, **Announcement** (what's said in the middle of the screen), **Winner**, **Over** and **Fighting**; a **Bar** tracking a fighter shows its health. It sends a **Round Message** as each round starts and a **Round Over Message** as one ends, plays its **Round**, **Fight**, **KO** and **Win** sounds, and its actions are **Rematch**, **Reset Positions** and **Toggle Hitboxes** (training's buttons). A fighter knocked out lies down on its `KO` clip (or `Hurt`) and does nothing more.

## Character select

A **Character Select** lets each player at the computer pick a fighter: its **Characters** are the fighters' prefabs (`Kenta, Mira`), and its children are their cards, in the same order. Each player moves their own cursor (the entities its **First Cursor** and **Second Cursor** name, moved over the card) with their own keys or controller, picks with one of its **Pick Actions** (punch, kick or Interact) and takes it back with its **Back Action** (dash); a click or tap on a card picks it for the first player still choosing. Its **First Preview** and **Second Preview** show the fighter each is on, big, with its clips. Once everyone has, it waits **Delay** seconds and loads its **Scene**, keeping the picks in the save data under **Save As** (`Pick.1`, `Pick.2`, `Pick.Computer`, and `Pick.Scene` for a "Character Select" button to come back to). With **Players** at 1 and **Computer** ticked, the computer takes the fighter after the player's.

A Versus Match with **Picks** (`Pick`) puts the fighters picked in place of the two in the scene as it starts, keeping their names, places and players, and gives the second to the computer when the picks say so (or its **Computer** is ticked); the same fighter twice, the second wears its **Second Tint**. Its outputs (**First**, **Second**, **First Status**, **Second Status**, **First Controls**, **Second Controls**) are for the screen's texts, and its **Swap Controls** action swaps the first two players' keyboard and controller.

## The computer

A **Computer Fighter** has the computer play a fighter: for one player against the computer, and for a training dummy. Its **Behavior** is **Fight**, or **Stand**, **Crouch** or **Jump** for a dummy (its **Next Behavior** action goes round them). Fighting, it walks in (now and then jumping in with an air move), attacks with the fighter's own moves (or those its **Use Moves** names) once it's within **Range**, guards as an attack comes (low against a low), backs off or waits, and follows a hit with a special the move cancels into; **Difficulty** (Easy, Normal or Hard) is how quickly it reacts and how often it guards and attacks. It holds directions and does moves by name as a player would, so frame data, blocking and combos are the same for it, and what it chooses comes from its **Seed**, kept with the scene, so a fight goes the same every time and after a rewind. A fighter's **Auto Block** blocks for it as well, whatever it's doing (training's **Next Auto Block** action).

## Frame by frame, rewinds and multiplayer

The boxes follow the frame the entity shows at each step of the game, worked out just as the animation picks it, so a hit lands on the same step every time, at any frame rate, and the same input always lands the same hits. An animation's **Time**, the hitstun, what a swing has already hit and the hits landed are kept with the scene, so a rewind back into the middle of a punch carries on from there and lands as it did. A fighter's move and the step it's on, its blockstun, combo, what it has pressed lately (the input history its motions and buffer read) and whether it's on the ground are kept too, and so is a hit stop under way, so a rewind into the middle of a quarter circle (even in the freeze of the hit it cancels) still brings out the palm, on the same step as before. A versus match's phase, clock and rounds won, a character select's cursors and picks, and a computer fighter's seed and what it's doing are kept too, so a rewind mid-round plays on to the same K.O. In a network game the host runs every fighter on its own player's keys (each player's fighter answers to their controls only) and works out the hits (as it does all damage), and everyone sees them; at one computer, each fighter's **Played By** gives it to one of the players there.

## Scripting

A **Hitboxes** raises `Hit` as one of its hitboxes lands and `GotHit` as one of its hurtboxes is hit, each with a `HitInfo`: the `Attacker`, the `Target`, the `Damage` dealt, the `Knockback` it was sent flying at, the `Stun`, the hitbox's `Group` and the `Point` where the boxes met. `Now()` gives the boxes of the frame the entity shows, `Showing()` which clip and frame that is, and `On("Punch", 2)` the boxes of any frame; `FrameBox.TryParse` and `ToString` read and write their lines. A **Fighting Character** raises `MoveStarted` with the `FighterMove` as each move starts and `Blocked` with a `HitInfo` as it blocks; `Perform("Palm")` does a move, `MoveList` and `Current` are its moves and the one it's doing, and `Phase` which part of it (`MovePhase.Startup`, `Active` or `Recovery`). `MoveInput.Parse("236P")` reads an input (`Describe()` says "quarter circle forward + P", `Arrows()` "↓↘→ P"), `FighterMove.Parse` a move's line, and `FightingCharacter.TimingOf` works out a move's startup, active and recovery and the clip frame at each step.

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

A **Hitboxes** takes no input of its own, so it goes on anything: a platformer hero's sword drawn on its Attack clip (an **Attack** with no Hitbox plays the clip, and its frames hit), an enemy's bite, a boss whose weak spot is a hurtbox with a multiplier of 3. Its actions (**Stun**, **Recover**) are for triggers and wires, and its outputs (**Hit**, **Got Hit**, **Hits**, **Hitstun**, **Stunned**) for counters, bars, texts and wires. A **Fighting Character** takes input only while it has focus (its **Controllable**), has the actions **Perform** (a move by name, so a rhythm note or a card can throw a palm strike), **Reset Combo** and **Next Auto Block**, and the outputs **Move**, **Phase**, **Free**, **Blockstun**, **Blocking**, **Blocked**, **Crouching**, **Held**, **Auto Block**, **Combo**, **Combo Hits**, **Combo Damage** and **Best Combo**; each move's `message` tells the rest of the game it started. A **Versus Match** has the actions **Rematch**, **Reset Positions** and **Toggle Hitboxes** and sends a message as each round starts, ends and the match is won, so a K.O. can open a door or a round start a boss's music; a **Character Select** takes input only while it has focus and picks by name with its **Pick** action; a **Computer Fighter** changes what it does with **Next Behavior**, and says it with its **Behavior** output.
