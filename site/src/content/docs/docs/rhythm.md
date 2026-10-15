---
title: "Rhythm games"
description: "Note lanes that scroll a chart of notes to a hit line in time with the music and judge every press, Perfect, Great, Good or Miss, by what each player actually hears and sees: their audio and visual offsets, and a calibration screen to tap along to."
---

A rhythm game lives or dies by timing, and timing means what the player *hears*, not what the game sends to the sound card. A **Beat Clock** (see [Keeping the beat](/docs/audio/#keeping-the-beat)) already counts what's reached the speakers, allowing for the sound card's own delay. Some speakers add a delay of their own that no computer can see: Bluetooth headphones are often 150 to 250 milliseconds late, a TV's speakers or a sound bar a little less, and a TV's picture can lag behind too. Each player sets theirs once, by tapping along, and every beat clock allows for it from then on. **Note lanes** play a chart of notes against it with no code: the notes scroll to a hit line, and each press is judged by the beat as the player hears it.

## Note lanes

**Entity → Genres → Rhythm → Create Note Lanes** makes a rhythm game you can play straight away: four lanes in the middle of the view with a **Beat Clock** and **Note Lanes** on them, a song made in Griff (`Assets/Music/Groove.wav`, 16 bars at 120 beats a minute, which opens in the Sound Maker to tweak) and its chart (the `Groove` table, at Easy, Normal and Hard), and the score, accuracy and best combo beside them. Play, and as each note crosses the line press its lane: **D**, **F**, **J** and **K**, the arrow keys, the controller's D-pad or face buttons (X, A, Y and B, left to right), a click on the lane, or on a touch screen a finger on it, several at once for a chord. A long note is a hold: press as it reaches the line and keep it down until its end goes by.

Each press is judged by how far it is from the note's beat as the player hears it, early or late, in milliseconds:

- **Perfect** within the **Perfect Window** (45 ms): 300 points.
- **Great** within the **Great Window** (90 ms): 200 points.
- **Good** within the **Good Window** (135 ms): 100 points.
- **Miss**: a note let go past the Good Window, a press earlier than that but within the **Miss Window** (180 ms; further off, a press isn't taken for the note at all, so tapping between notes costs nothing), or a hold let go of before its end (let go within the Good Window of its end, it counts as held). A Miss scores nothing and starts the combo again.

A hold is judged twice: as it's pressed, and at its end (Perfect if it's held to there, Miss if not). The judgment pops up over the lanes in its color, with the combo under it; the pad at the hit line lights while its lane is held, and a hit flashes there.

What the lanes know are outputs, for a **Text**, a **Counter** or a **Bar** to show and anything to read: **Score**, **Combo** (hits in a row since the last Miss), **Best Combo**, **Perfects**, **Greats**, **Goods**, **Misses**, **Notes** (in the chart being played), **Accuracy** (the points scored out of what Perfects all along would have scored, 0 to 100: `{0:0.00}%`), **Grade** (S from 95, A from 90, B from 80, C from 70, D below), **Judgment** (the last one, `Perfect`), **Timing** (how far the last press was from its beat, in milliseconds: below 0 early, above late, for an "Early" or "Late" under the judgment, or to see a player's offset needs setting) and **Is Playing**. Scripts can follow its `Judged` event (each judgment and its lane) and `Completed`.

## Charts

A chart is a table (see [Tables of game data](/docs/data-tables/)): a row a note, with these columns.

- **Beat**: when it's to be hit, counting beats as a **Beat Clock** does: 0 is the music's first beat (where the clock's **Offset** puts it), 4 the first of the second bar in 4/4, 6.5 halfway through the seventh beat. A formula works too: `=Bar * 4 + Step`.
- **Lane**: which lane, from 1.
- **Hold**: how many beats a hold note is held for. Empty (or 0) is a tap.
- **Difficulty**: which difficulties it's in: `Hard`, or several, `Normal, Hard`. Empty is in every one. A **choice** column (Easy, Normal, Hard) picks from a list.

The rows' names are anything (`Hard 12`, `Chorus 3`), and they're in any order: the lanes sort the notes by beat (then lane). A chart is a `.table` file like any other, a line a note in git, so two people charting different parts of a song merge cleanly, and it's edited in the **Tables** panel: type the notes in, paste them from a spreadsheet, or work them out with formulas. A note changed while the game plays reaches it at once.

A song picks its chart by name: the lanes' **Song** `Music/Groove.wav` plays the `Groove` table, unless their **Chart** names another. Their **Difficulty** says which notes to play (`Normal`: those naming Normal, and those naming none); empty plays only the notes that name none, so a chart without a Difficulty column plays whole. A menu picks it with no code: a **Choice** whose **Save As** is `difficulty` (its options Easy, Normal and Hard), and the lanes' **Saved Difficulty** `difficulty`; or a button that does the lanes' **Play** action **With** `Hard`, which starts them again at that difficulty. A difficulty the chart doesn't name, or a note in a lane the lanes haven't got, is said in Output as the game plays.

## The lanes' settings

- **Song**: the music they play as they start, from the top, once through (not looping), at their **Volume**. Empty keeps time with the music playing (a Background Music's), or with none, clicks a metronome at the clock's tempo, so a chart can be tried before it has a song.
- **Chart**, **Difficulty** and **Saved Difficulty**: see [Charts](#charts).
- **Clock**: the entity whose **Beat Clock** times them; empty is their own, or else the scene's first. The clock says the tempo: music made in Griff says its own, and other music needs the clock's **Tempo** set (and its **Offset**, where the first beat is).
- **Start On Show** (on): they start as they're shown (as the scene starts, or a panel they're in opens). Off, they wait for their **Start** action. **Stop** stops them and their song.
- **Lanes** (4), **Direction** (which way the notes travel to the hit line: **Down**, **Up**, **Left** or **Right**, the lanes one above another for the last two), **Speed** (how fast the notes come, in world units a second: faster spreads them out), **Length** (from where notes come in to the far end), **Lane Width** and **Hit Line** (how far the line is from the end the notes travel to).
- **Actions**: the input actions that press the lanes, the first lane's first, separated by commas. Every game has **Lane1** to **Lane4** (D F J K, the arrows, the D-pad, and X A Y B), which **Game → Controls** rebinds like any other; for more lanes, add actions of the game's own. A lane without one is pressed by touch alone (the Inspector says which).
- **Clicks** (on): a click or a tap on a lane presses it, and on a touch screen each finger does, so chords and holds play by touch.
- **Controllable** and its **Focus** action: whether they take the player's input (see [Mixing genres](/docs/mashups/)). Like a menu, they listen while they're on the top panel (or none is open).
- The four windows, in milliseconds.
- **Colors** (the notes', a lane each, as hex colors separated by spaces), **Lane Color**, **Pop Size** (how big the judgment pops up; 0 shows none) and **Order**.
- **Hit Sound** and **Miss Sound** (none to begin with: the music is the sound), and the messages below.

Each player can make the notes come faster or slower still with their own **note speed** (`Game.NoteSpeed`, 1 to begin with, from 0.25 to 4), kept with their other settings like the offsets: a slider whose **Setting** is **NoteSpeed** (say 0.5 to 3 in steps of 0.25, with a text showing `{0:0.00}x`) sets it in an options menu. It changes only how far apart the notes are drawn, never when they're to be hit.

## Messages: a note can fire a weapon

The lanes send a message on each judgment, for anything else in the game to act on, the way every genre's pieces drive each other (see [Mixing genres](/docs/mashups/#genres-driving-each-other)):

- **Hit Message** (`Hit`) on each Perfect, Great and Good, with the points as its data: a **Counter** listening for it adds up the score, and a **Trigger** ("when `Hit` arrives from Note Lanes, Fire the Cannon") makes each hit do something. With **Use Message Number**, the points are the value: a Perfect does 300 damage.
- **Perfect Message** (`Perfect`) on each Perfect as well, for a bonus.
- **Miss Message** (`Miss`) on each Miss: a Health that loses a heart, a crowd that boos.
- **Complete Message** (`Song Complete`) once the song is over (every note judged, and their own song played to its end; keeping time with other music, a bar after the last note), with the score as its data: a results panel with it as its **Open Message**, showing the **Grade**, **Accuracy** and **Best Combo**.

## Pauses, rewinds and replays

The lanes pause with the game (a pause menu) or their entity (a **Pause Group**): their song stops where it was, the notes hold still, and as the game goes on the song carries on from there. A rewind in the editor does the same: the lanes keep what was judged, the score and where the song was (their **Is Playing**, **Song Time** and **Played**, saved with the scene), and the song carries on from that moment. So does a game held still while its song plays on (paused in the editor, stopped at a breakpoint, a long stall): the song goes back to where the lanes had got to, rather than the notes going by unplayed. Notes that went by while the lanes weren't listening (lanes started part way through music already playing, a chart changed under them) are left out rather than missed.

A press counts on the game step it arrives in, and the judging reads nothing but the clock, so the same presses at the same moments of the music judge the same, every time, on every machine. Without music, a test or a replay counts the music's time the same way.

## Audio and visual offsets

Each player has two settings, kept with their other settings (volumes, fullscreen) in the game's save data, so they come back every time the game runs:

- **Audio offset** (`Game.AudioOffset`, in milliseconds): how late they hear the sound. Beat clocks count the music's beats this much later, so their **Beat**, **Bar**, **Beat Message**, **Bar Message**, the things that step to them (spawners, shooters, movers, switches) and the timing of a press (`SecondsFromBeat`, `SecondsFrom(beat)`) all go by the beat as the player hears it: a tap on the beat they hear is on time.
- **Visual offset** (`Game.VisualOffset`, in milliseconds): how late they see the screen. What's drawn to the beat is drawn this much ahead, so it's seen as the beat is heard: a **Beat Pulse** bops early by that much, and so does anything drawn from a clock's `ShownBeats`. It changes only how things are drawn.

This is the split rhythm games usually make (audio calibration for the timing, video calibration for the picture), and it's how the two add up: a press is judged by when it's heard, and what's drawn is placed where the music will be when the player sees it. Both go from -1000 to 1000 and start at 0. A beat clock's own **Offset** is something else: it's the music's (where its first beat is), the same for every player.

## A calibration screen

**Entity → Audio → Create Calibration Screen** makes one, ready to open from an options menu: a panel (closed while the game runs until something opens it) called `Calibrate`, with a **Calibration**, a heading, the calibration's prompt, a slider to fine-tune the offset by hand (with how many milliseconds it is beside it), and **Apply**, **Again** and **Back** buttons. Give the options menu a button whose **Action** is **Open Panel** and **Panel** is `Calibrate`; a text on it with **Track** `Calibrate` and **Output** `Current` (`Audio Offset: {0:0} ms`) says the offset in use, as the Arcade template's options do. For a screen that sets the visual offset, set its calibration's **Measures** to **Visual** and its slider's **Setting** to **VisualOffset** (the heading follows by itself).

Opening it starts the clicks: a count-in of four beats to listen to, then sixteen to tap along to, with **Space** (its **Tap Action**, Jump: the gamepad's A), a click, or a touch. A row of dots under its light fills in as each tap lands. The light only lights as the player taps, so nothing on the screen gives away when the click comes. Then it works out how long after each beat the taps came, leaves out the odd ones (a missed beat, a double tap, a late start: anything further from the middle tap than the taps typically are, or than 40 ms), and offers the average: "Your taps came 182 ms after the clicks (give or take 9 ms). Apply it?" **Apply** makes it the player's audio offset; **Again** starts over. With fewer than half the beats tapped steadily it says to try again instead. A tap counts on the game step it arrives in (each a 60th of a second), as every press in the game does, so what that adds on average is in the offset too, and a hit timed the same way comes out on time. The music that was playing stops for the clicks and comes back when it's done, closed, or stopped.

A **Calibration**'s settings:

- **Measures**: **Audio** taps along to clicks (setting the audio offset); **Visual** taps along to its light flashing in silence (setting the visual offset).
- **Tempo** (90 beats a minute: slow enough that even a very late tap isn't taken for the next beat's), **Count In** (4) and **Beats** (16).
- **Start On Show** (on): starts as it's shown. Off, it waits for its **Start** action.
- **Volume** of the clicks, on top of the player's sound effect volume (not the music's, so a player who has turned the music off still hears them).
- **Tap Action** (`Jump`) and **Clicks** (a left click or a touch taps too).
- **Color**, **Size** and **Order** of its light and dots.

While it runs it hides the buttons and sliders inside it, so a tap can't press one, and shows them again when it's done. Like a menu's controls, it listens while it's on the top panel (or no panel is open), and it runs in real time, under a pause menu too, so it can sit in a pause menu's options. Its actions are **Start**, **Stop** and **Apply** (faded on a button until there's a result to apply), its outputs **Is Running**, **Taps** (out of **Beats**), **Result**, **Spread** (how far the steady taps were from their average), **Applied**, **Current** (the offset in use), **Title** (`Audio Offset` or `Visual Offset`, which the screen's heading shows) and **Prompt** (what to tell the player now, naming the tap's key or button the way they play), and scripts can follow its `Finished` event. Its **State** says where it's got to.

## Timing a hit in a script

The lanes do all this with no code. A script judging hits of its own (a rhythm fight, a dance-off) gets what it needs from the beat clock, with the player's offsets already allowed for:

```csharp
var clock = Game.Find("Music")!.GetComponent<BeatClock>()!;
if (Input.WasPressed("Jump"))
{
    // How far the music they hear is past the note's beat, in seconds: negative if early.
    var off = MathF.Abs(clock.SecondsFrom(note.Beat));
    if (off < 0.05f) Perfect();
    else if (off < 0.12f) Good();
}

// Notes coming down a lane are drawn where they'll be when the player sees them.
var y = hitLine + (note.Beat - clock.ShownBeats) * speed;
```

`Beats` is the count with its fraction, as heard; `ShownBeats` is the same ahead by the visual offset, for drawing; `SecondsFromBeat` is how far it is from the nearest beat. `Audio.PlayMetronome(tempo, beatsPerBar, volume)` plays a click track as the music (a count-in, or a practice mode), which beat clocks keep time with like any other music.

## Network games and replays

An offset belongs to a player and their speakers, so it stays on their machine: it's kept in their own save data, never sent to the other players, and never saved with a scene. Each machine's beat clocks count beats as that machine's player hears them. In a network game the host runs the game, so its clocks' messages, and what steps to them, go by the host player's hearing (as they've always gone by the host's sound card); each player's own clock, with their own offset, drives what they see bop and when their own taps land. A hit is best judged on the machine of the player who tapped, by their own clock, and the result sent on: that way nobody's headphones shift anyone else's timing, and two players with different offsets each get "on time" when they tap with what they hear. Note lanes do exactly that: each machine keeps its own lanes (like a HUD) and plays them as its own player hears the song, judging their presses there, and sends each judgment to the host, which sends the lanes' messages (from its copy of them, to every machine, so the host's triggers act on everyone's hits, `Game.Net.Sender` saying whose) and adds the points to that player's score, which a scoreboard shows everyone. A calibration is each machine's own (its buttons work on that machine at once), and so is the setting it applies.

The offsets never touch anything else in the game: they only shift when this machine's music counts as heard and where things are drawn. A test or a replay without music counts the same with the same offsets (0 unless a test sets them), and rewinding in the editor leaves them as they are.

## In the browser

A game exported to the web calibrates and plays the same way: its music plays through Web Audio, and what's been heard counts what the browser says it still holds and its own output delay, so notes are judged by the beat the player hears there too (the web check plays the lanes' song in a headless browser and presses notes on the beat as Web Audio plays it, for Perfects). Keys, the mouse, a controller and touch all play, every finger on a phone or tablet a press of its own, so chords and holds play by touch. A browser makes no sound until the player has clicked or pressed something, so start the song from a button (a menu's Play), or the lanes keep time silently until then. The offsets and the note speed are kept with the page's other saves.
