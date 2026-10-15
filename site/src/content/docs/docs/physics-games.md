---
title: "Physics games"
description: "A slingshot to pull back and let go, blocks that crack and break on hard knocks, levels won with stars, and pinball flippers, bumpers, a plunger and a tilt: physics games with no code, and the Physics template."
---

## The Physics template

**File → New Project → Physics** is a slingshot game to play straight away, and to take apart: three levels of wood, glass and stone with green grubs inside to knock out, and a menu to pick them from. It's all no-code components (a **Slingshot**, **Breakable** blocks, a **Level Goal**, **Stars**, counters and panels), its pictures and sounds made by Griff.

- **The menu** (`Main`) has a card for each level (**Timber**, **Glasshouse** and **Stone Keep**) with the best stars and score it's been won with. The first is open from the start, and each of the others once the one before has been won (its button **Needs Saved** the level before's stars). Arrows and Enter pick one, or click or tap it.
- **A level** (`Level1` to `Level3`) has a slingshot on a mound of rock on the left with its birds waiting in a row behind it (the last level's last shot is a heavy boulder, for stone), and the towers on the right: posts and planks, square blocks and triangular roofs, on the ground or on rock. Drag the bird back with the mouse (or a finger) and let go, or pull with the arrows (or a stick) and let go with Space (or A); the dotted line shows where it'll fly and the camera follows it, staying inside the level. Wood cracks and splits, glass shatters, stone crumbles, each with its own sound and its points rising where it broke, and a grub pops in a puff of green (it has a **Health** for three knocks' worth, flashing as it's hurt). In the third level a stone weight hangs on a **Rope** from a beam across two rocks, over a grub: break the beam and down it comes.
- **Every block's material** is a row of the **Materials** table (`Tables/Materials.table`): its **Strength**, **Min Hit**, **Points**, its pictures (a plank, a block and a wedge of it) and its break and hit sounds, set on each block by a **Table Values**. Change a cell and every block of that material changes, while the game runs too. A block's weight is its material's for its size.
- **The HUD** shows the level, the shots and grubs left (texts reading the slingshot's **Shots Left** and the level goal's **Targets Left**), the score (a **Counter** of every piece's Break Message) and the best, and a **Retry** button (R) that the arrow keys never pick and Space and A never press, since they're the slingshot's.
- **Winning**: once the last grub is down and things have settled, each bird left scores 10,000, counted one by one, and the **Complete** panel opens with the stars the score earned popping in one after another, the score and the best, and **Next**, **Again** and **Levels**. Running out of birds with grubs standing opens the **Failed** panel: **Try Again** or **Levels**. Esc (or Start) pauses.

The best stars and score of each level are kept in the save data (`Level1.Stars`, `Level1.Best`), from one run to the next, in the browser too. The scenes it plays the same every time: the same pulls topple the towers the same way, to the bit, and a rewind in the middle of a collapse plays it out again just as it went.

## A level to knock down

**Entity → Genres → Physics Games → Slingshot** puts a **Slingshot** on the left with three shots waiting behind it, and **Entity → Genres → Physics Games → Tower** a little tower on the right: two storeys of wooden posts and planks, a pane of glass and a stone slab, with a green target inside. Play, pull the slingshot's pouch back with the mouse and let go: the shot flies off the other way, the camera follows it, and whatever it hits hard enough cracks and breaks, the tower tumbling down as its pieces give way. Each piece broken adds its points to the score along the top. A scene with nothing solid to stand things on gets the ground too, and a camera with no **Follow** gets one following the slingshot. All of it works from the Inspector, with no code.

**Entity → Add Breakable** makes whatever's selected break on hard knocks: it gets a **Breakable**, a **Body** that rotates (so it tumbles and topples) and a box collider the size of its sprite if it has no collider.

## Slingshots

A **Slingshot** draws itself: a forked frame in its **Color** with elastic bands in its **Band Color**, the prongs **Fork Width** apart, and a pouch at its **Pouch** (from its position, its foot). Its shots are the bodies inside it, in order: the first sits in the pouch and the rest wait in a row behind its foot, held still and out of the way (nothing bumps into them, nor the shot in the pouch into them as it's pulled back past them). Once they're gone it makes copies of its **Shot** prefab (a bird, a boulder), **Shots** of them. A shot is any body: give it **Rotates** and it tumbles and rolls as it lands, a **Mass** for how hard it hits, **Bounciness** to bounce.

- **With the mouse** (or a finger on a touch screen): press within **Grab Radius** of the pouch, drag it back and let go. The further it's pulled (up to **Max Pull**), the faster the shot leaves, the other way: at its **Power** (world units a second) all the way back. Pulled less than **Min Pull**, it's let back without launching. It can't be pulled so low the shot would go into the ground under its foot.
- **With the keys or a gamepad**: the arrows (A, D, W, S, the d-pad or the stick) pull the pouch about at **Pull Speed**, the **Launch Action** (`Jump`: Space or A) lets go and the **Cancel Action** (`Dash`: Shift or B) lets it back.

While it's pulled a dotted line shows where the shot will go for **Preview** seconds, in **Preview Color**: the path the game will fly it on, step by step, so the shot passes through each dot (it doesn't see what's in the way). With **Camera Follows**, the camera's Follow follows each shot as it flies and goes back to what it followed once the shot has come to rest. **Auto Load** loads the next shot **Load Delay** seconds after the last has come to rest (or gone, or flown for ten seconds: off the edge of the world), and the first as the game starts.

Its actions, for a **Trigger**, a wire or a script: **Load** (the next shot into the pouch now) and **Launch** (what's in the pouch, as it's pulled, or with nobody pulling it at its **Default Pull**: a catapult a trigger fires on the beat, or a cannon a wire sets off). Its outputs: **Shots Left** (inside it, the loaded one too, and copies of its prefab still to come), **Stretch** (how far it's pulled, 0 to 1) and **Out Of Shots**, which turns on once it has none left and the last has come to rest: wire it to a panel that says the level's lost, or count it with a Counter. It plays its **Stretch Sound** as it's first pulled and its **Launch Sound** as it lets go, and sends its **Launch Message** (with the shot) and **Out Of Shots Message**.

## Breakable things

A **Breakable** breaks when it's hit hard enough. A hit is as hard as the push it takes to stop the two coming together, their speed towards each other times their weights shared: a crate of Mass 1 landing on the floor at 5 units a second hits 5, a heavy ball hits harder than a light one, and a fast one harder than a slow one. Hits softer than its **Min Hit** do nothing at all, so resting in a tall tower, nudges and landing gently don't wear it down; harder ones add up in its **Damage**, and once that reaches its **Strength** it breaks. Hits come from anything: a shot, a falling block, the floor it's knocked onto, another piece of the tower, so one thing breaking can bring down the next.

As its damage builds up it cracks: its **Stages** of cracking (3), one more for each share of its Strength, drawn over it in **Crack Color**, the same cracks every time. With a sprite sheet (a Sprite with Columns or Rows) its frames are the stages instead, from whole at frame 0 to most cracked.

As it breaks it splits into pieces (**Split**: 2 halves a plank and quarters a crate, 3 makes nine of a crate), each a part of its sprite (cut from its picture), moving and turning as it was, flying apart a little and fading away after **Pieces Last** seconds; the pieces knock into things too. With Split 0 it vanishes. Either way a puff of **Dust** in its color bursts out, or its **Effect** prefab (an explosion, a score popup) in its place, and its **Sound** plays; a hit that damages without breaking plays its **Hit Sound**. **Remove** takes it away; untick it to leave it turned off instead, with its **Broken** output on, until its **Repair** action brings it back (a wall a wire mends).

It sends its **Break Message** as it breaks, carrying its **Points** (a **Counter** adds them up into a score) or, with none, the entity (a Counter counts one), and its **Damage Message** with each hit's damage. With **Show Points** its points rise from where it broke and fade, in its **Points Color**: what knocking it out scored. Its actions: **Break**, **Damage** (as a hit that hard would) and **Repair**. Its outputs: **Damage** (out of its Strength, for a Bar) and **Broken**.

### Materials from a table

Wood, glass and stone are rows of a [table](/docs/data-tables/): make a **Materials** table with a row for each and columns named like the properties they set (**Strength**, **Min Hit**, **Mass** for the body, **Tint** for the sprite as `#8E929C`, **Sound**, **Split**), and give each block a **Table Values** on it with its **Row** (`Glass`). Balancing the table while the game runs changes every block of that material at once. Strengths to start from: glass 4 (Min Hit 1.5, light), wood 12 (Min Hit 3), stone 40 (Min Hit 6, heavy).

### Targets

A target to knock out (a pig in a tower) is just something breakable: a low Strength (3) and a high Points. Or give it a **Health** as well: each hit that counts takes its damage off that too (so its health bar, flash and **Hurt Sound** go with it), and it breaks as its health dies (Strength 0 leaves it to its Health; give the Health an **Invincible Time** of 0 so every hit counts). A Counter of the Break Messages keeps the score; a **Level Goal** says when the level's won or lost (below).

## Winning a level

A **Level Goal** (on an entity of its own, `Level`) is what a level is for: knock out every one of its **Targets** (everything called that, `Target`; a Breakable is gone once it's broken, anything else once it's removed or turned off, so they can be a platformer's enemies too) before its **Slingshot** (by name; empty is the scene's first) runs out of shots. It counts them as the game starts.

- Once the last is down it takes the slingshot's controls away and waits **Settle** seconds (2) for things to settle, then counts a **Shot Bonus** (10,000) for each shot left, one after another, each taken from the slingshot with its **Bonus Sound** and its points rising where it was, adding them to its **Score** (a Counter, by name: `Score`). Then it gives the level its stars, one for winning, two from **Two Stars** points and three from **Three Stars**, plays its **Win Sound** and sends its **Won Message** (`Level Complete`, with the stars): a **Panel** with that **Open Message** says so.
- Out of shots with targets still standing (once the last shot has come to rest), it waits **Settle** seconds as well (a target knocked out meanwhile still counts, and wins it), then plays its **Lose Sound** and sends its **Lost Message** (`Level Failed`, with how many are left).
- With **Save As** (`Level1`) it keeps the best stars the level has been won with in the save data as `Level1.Stars` and the best score as `Level1.Best`, from one run to the next: a level select menu's **Stars** show them, a Counter with **Save Best As** `Level1.Best` and the format `Best: {1}` shows the score, and the next level's button **Needs Saved** `Level1.Stars`, opening once it's won. A worse win keeps the best.

Its actions, for a trigger, a wire or a script: **Win** (now, counting the bonus) and **Lose**. Its outputs: **Targets Left** (out of how many there were), **Stars**, **Won**, **Lost**, **Best Stars** and **Best Score**, for texts (`Grubs: {0} / {1}`), bars and wires. Where the level has got to (playing, settling, counting the bonus, won or lost) is saved with the scene, so a rewind goes back into the middle of it.

### Stars

**Stars** (**Entity → UI → Stars**) draws a row of stars, **Count** of them (3), so many filled: an output (**Track** `Level` and **Output** `Stars`, on the panel a won level opens), a number kept in the save data (**Saved** `Level1.Stars`, on a level select menu's card), or its own **Value**. With a **Delay** they fill one after another, each popping in with its **Sound**, in real time, so they go on under a panel that pauses the game. It draws plump stars in its **Color** with a **Rim**, the empty ones in **Empty Color**, or its **Image** tinted so.

## Pinball

**Entity → Genres → Physics Games → Pinball Table** makes a whole little table to play straight away: walls round a playfield, a plunger in its lane on the right with the ball resting on it, two flippers over the drain, three pop bumpers, two slingshot kickers, three lanes along the top and a bank of three drop targets on the left, with the score and the ball shown beside it, pictures and sounds (in `Assets/Pinball`, to repaint and remake). The camera is moved and widened to see all of it. Play, hold **Space** (or the down arrow) and let go to shoot, flip with **Left Shift** or **Z** and **Right Shift** or **/** (a gamepad's shoulders or triggers), and nudge with **N** (or the up arrow, or **B**). The pieces are on the same menu one at a time too: **Flippers** (a pair), **Bumper**, **Kickers** (a pair), **Plunger** (with a ball on it), **Drop Targets** (a bank of three) and **Lanes** (three). Everything is ordinary physics underneath: the ball is a body with a circle collider that rotates, the walls are colliders (slanted ones have a frozen body that rotates, so their box turns with them), and every piece works from the Inspector.

### Flippers

A **Flipper** is a paddle on a **Hinge Joint**. While its button is held it swings up as fast as its **Speed** (2,000 degrees a second: up in a couple of hundredths of a second) and as hard as its **Strength**, to its hinge's upper limit, and stays there, cradling the ball; let go and it drops back to the lower one. How far it swings is the hinge's limits (drag the ends of the arc in the Scene view; a hinge with none is given its side's, resting as it starts and swinging 55 degrees up). Its **Side** says which way it swings: a **Left** flipper points right and swings up anticlockwise, a **Right** one the other way. Its body rotates and is heavier than the ball (Mass 3 against the ball's 1), so the ball comes off it hard and the same every time: the further along the paddle it is, the faster (about 40 units a second near the tip).

- Its **Action** is the input action that holds it up; empty, it's **Left Flipper** or **Right Flipper** by its side, two of Griff's own actions (rebind them in **Game → Controls...**).
- With **Touch**, the mouse or a finger held down on its side of the table's flippers (left of the middle between them, for a left one) holds it up too.
- A switch or a wire into its Power (**Raised**) holds it up, as do its **Raise** action (until **Lower**) and, for a moment, **Flip**: a flipper a Trigger, a wire or the beat flips. Its outputs: **Up**, and **Flips** (how many times it's swung up). It plays its **Sound** and sends its **Message** as it swings up.

A fast ball doesn't go through a flipper, swinging or not: the game works out where the two will meet within each step, and moves a fast body in parts small enough not to pass through anything.

### Bumpers and kickers

A **Bumper** knocks whatever touches it away at its **Strength** (16 units a second), straight away from its middle, as it flashes its sprites in its **Flash Color**, pops bigger by its **Pop**, plays its **Sound** and sends its **Message** (Score) with its **Points** (100), for a Counter keeping the score. After a knock it rests for its **Recover** time. Give it a **Direction** (in its own space, turned with it) and it's a slingshot kicker: only what touches its side facing that way is kicked, that way, so a ball rolling against its back does nothing. **Layers** limits what it knocks; untick **On** (or wire its Power) and it's just a wall. Its **Flash** action lights it up without a ball, and its outputs are **Hits** and **Lit** (on for a moment after each).

### The plunger

A **Plunger** is the tip the ball rests on at the bottom of its lane (give it a solid collider there; its position is the top of its tip). Hold its **Action** (**Plunger**: Space, the down arrow, the gamepad's A) to pull it back, further the longer it's held, all the way in **Pull Time** seconds, and let go: whatever rests on its tip is shot its **Direction** (up) at a speed from **Min Power** (a tap) to **Power** (all the way back). With **Analog**, a gamepad's right stick pulled down pulls it as far as it's pushed, and letting the stick spring back shoots at the pull it had (let back slowly, it doesn't shoot). The mouse or a finger held down within **Grab Radius** of its tip pulls it like the button. It draws itself, a knob on a rod in a spring that squeezes up as it's pulled (**Travel**, **Length**, **Width**, **Color**, **Spring Color**), plays its **Pull Sound** and **Launch Sound**, and sends its **Launch Message** with the ball. Its **Launch** action shoots at full power with nobody pulling it (an auto-plunger, or a kickback a Trigger fires); its outputs are **Pull**, **Loaded** (a ball rests on it) and **Launches**.

### Drop targets and lanes

A **Target** is something solid the ball hits: a hit harder than its **Min Hit** scores its **Points** with its **Message** and lights it (unlit targets are drawn darker); a drop target (**Drops**) falls out of the way as it's hit, drawn faint, and the ball goes through where it was. A **Lane** is a trigger the ball rolls through (at least a ball's width long): it scores and lights once each time a ball goes in. Targets and lanes come in sets, those inside the same entity: once every one of a set is lit, the set sends its **Complete Message** with its **Complete Points** (1,000), and **Reset After** seconds later they all come back up unlit, ready again. A lane with **Flipper Changes** moves the lit lanes of its set along each time a flipper of its table swings up (the left flipper to the left, the right to the right, round from the end), so the player lines the unlit one up with where the ball will go. Their actions are **Light** and **Reset**; their outputs **Lit**, **Down** and **Hits** (a target's) and **Lit** and **Passes** (a lane's).

### The drain, ball saves and extra balls

A **Drain** is a trigger below the flippers. A ball that falls into it is lost: it's taken off the table and **Serve Delay** seconds later put back on the **Plunger** (the one it names, or the nearest) as the next of its **Balls**, until the last is lost and the game's over (its **Game Over Message**, GameOver, for a panel that says so). A ball lost within **Ball Save** seconds of being shot from the plunger is given back instead: served again and shot by itself, with its **Save Message**. While other balls are still on the table (multiball), a ball that drains just goes and no ball is lost; **Within** says which table's balls it counts, by name. Its actions are **Extra Ball** (a ball more: a set of targets or a Counter's Reach can award one) and **Save Ball** (a ball save of its Ball Save's seconds); its outputs are **Ball** (which ball, out of its Balls), **Balls Left**, **Game Over** and **Saving**. The table's **Balls** Counter shows which ball it is, counting the drain's Ball output ("Ball {0} of {2}").

### Nudging and the tilt

A **Tilt** looks after a table: what's inside the entity its **Within** names (or stands in its area), or the whole scene. Its **Nudge Action** (**Nudge**: N, the up arrow, the gamepad's B) bumps the table: every ball on it gets a push of **Nudge Strength** its **Nudge Direction** (up the table), or that way and to the side while left or right is held, and the view shakes by its **Shake**. Each nudge adds one to its **Danger**, which falls back by one every **Settle** seconds: a nudge that takes it past one warns (its **Warning Message**, and "DANGER" over the table), and one that takes it past **Warnings** plus one tilts it ("TILT", its **Tilt Message**). Tilted, the flippers drop and stay down and the bumpers, kickers, targets and lanes go dead until the ball drains (a **Drain** it covers puts it right), or its **Reset** action. Its actions are **Nudge**, **Tilt** and **Reset**; its outputs **Danger**, **Tilted** and **Nudges**.

### Scoring

Every piece that scores sends a message with its points, Score by default, and a **Counter** whose Message is Score adds them up: the table's **Score** on the camera. Give pieces their own messages to count them apart (a Counter of drop target hits whose Reach awards an extra ball), and wire outputs anywhere: a bumper's **Hits** into a boss's Bar, a set of lanes' Complete Message into a door.

## Mixing it with other games

Untick a slingshot's **Controllable** (or **Focus** something else) and it ignores the mouse and keys while its actions still work, so a slingshot can sit in a platformer and take over the arrow keys only when the player walks up to it (see [Mixing genres](/docs/mashups/)). Give it a **Turn Taker** naming its side and it's pulled only on its side's turn, each launch using one of the turn taker's **Actions**: two slingshots taking turns at each other's towers. Flippers, plungers and tilts have **Controllable** too, so a pinball table in another game answers to its buttons only while it has focus, and with a **Turn Taker** (on them or the table) they're played only on their side's turn: two players taking turns at one table.

## Rewinds and network games

Everything a slingshot, a breakable and a level goal know (what's loaded and how far it's pulled, how many it has made, the shot in flight, damage, what's broken, where the level has got to) is saved with the scene, and a hit is worked out from the same physics every time, so a rewind replays a tower falling to the last bit, and so does playing it again. So is everything a pinball table knows (each flipper's hinge and whether it's up, the plunger's pull, which targets and lanes are lit, the danger and the tilt, which ball it is and the one waiting to be served), so a game played again with the same buttons ends with the same score, and a rewind goes back into the middle of one. Playing makes no garbage, scoring included. In a network game the host runs the physics and the breaking, and the pieces and dust come to everyone; a player pulls a slingshot on their own screen (with its line), and their pull and let-go are sent to the host, which launches the shot. With the **Multiplayer**'s **Teams**, only the players on a slingshot's Turn Taker side pull it. A pinball table is run by the host too: each player's buttons, stick and mouse reach it, and it flips, shoots and nudges for them. A table nobody owns answers to every player (with Teams and a Turn Taker, those of its side), and one a player owns (their own table, made for them by the Multiplayer) to them alone.

## Scripting

Physics tells scripts how hard things hit: `Game.Physics.Impacts` lists the last step's hard knocks, both ways round (`impact.Struck`, `impact.By`, `impact.Strength`, `impact.Point`). A breakable's `Hit` (with the damage and what hit it) and `Broke` events say what happened to it, and `Stage` how cracked it is; a slingshot's `Launched` event hands over each shot, `LoadedShot` is the one in its pouch, and `LaunchVelocity(pull)` how fast a pull would launch one. A level goal's `Completed` (with the stars) and `Failed` events say how a level ended, and `StarsFor(score)` what a score would earn. Pinball pieces have events too: a flipper's `Flipped`, a bumper's `Knocked` (with what it knocked), a plunger's `Launched` (with the ball) and `SpeedFor(pull)`, a target's `Hit`, a lane's `Passed`, a drain's `Drained` (with the ball and whether it was saved) and a tilt's `Warned` and `TiltedOver`.

```csharp
public class Shockwave : Behavior
{
    // Whatever this hits hard is shoved away as well.
    protected override void Update()
    {
        var at = Entity.Transform.WorldPosition;
        foreach (var impact in Game.Physics.Impacts)
        {
            if (impact.By == Entity && impact.Strength > 5 && impact.Struck.GetComponent<Body>() is { } body)
                body.Velocity += Vector2.Normalize(impact.Point - new Vector2(at.X, at.Y)) * 4;
        }
    }
}
```
