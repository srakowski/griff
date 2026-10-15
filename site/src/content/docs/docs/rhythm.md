---
title: "Rhythm games"
description: "The Rhythm template, a whole rhythm game with no code; note lanes that scroll a chart of notes to a hit line in time with the music and judge every press, Perfect, Great, Good or Miss, by what each player actually hears and sees; a song select with the best scores; their audio and visual offsets, and a calibration screen to tap along to."
---

A rhythm game lives or dies by timing, and timing means what the player *hears*, not what the game sends to the sound card. A **Beat Clock** (see [Keeping the beat](/docs/audio/#keeping-the-beat)) already counts what's reached the speakers, allowing for the sound card's own delay. Some speakers add a delay of their own that no computer can see: Bluetooth headphones are often 150 to 250 milliseconds late, a TV's speakers or a sound bar a little less, and a TV's picture can lag behind too. Each player sets theirs once, by tapping along, and every beat clock allows for it from then on. **Note lanes** play a chart of notes against it with no code: the notes scroll to a hit line, and each press is judged by the beat as the player hears it.

## The Rhythm template

**File → New Project → Rhythm** is *Neon Pulse*, a whole rhythm game to play straight away and take apart, with no code, its songs, pictures and sounds all made by Griff:

- **The title** (Play, Settings, Quit), the theme playing and the speakers bopping to it.
- **The song select**: three songs, easiest first: *First Light* (chill, 100 beats a minute), *Neon Avenue* (bright, 120) and *Overdrive* (tense, 140). Up and down pick one out, which plays (so you hear it before you choose), left and right pick **Easy**, **Normal** or **Hard**, and Enter, Space or a controller's A plays it; or click (or tap) a song, and again to play it, and the arrows beside the difficulty. Each card shows the best grade at the difficulty picked out, and beside them the song's notes, best score and grade.
- **The song**, on four lanes in a city at night: the judgments pop up with the combo, the score, accuracy and best for this song along the top with how far through it is, the combo big on the left, and each lane's key under it (`D F J K`, or a controller's buttons as you play with one). A button in the corner pauses for fingers.
- **The results** as the song ends: the grade, big, the score, accuracy, best combo and the Perfects, Greats, Goods and Misses, the best score, **NEW BEST!** and **FULL COMBO**, and **Retry**, **Songs** and **Title**. The best score and grade of each song at each difficulty are kept.
- **The pause menu** (Esc, P or Start, or the corner button): Resume, Restart, Settings and Songs. The song stops where it is and carries on from there.
- **Settings** (from the title and the pause menu): the **note speed**, the **audio offset** and **visual offset**, the music and effects volumes, and **Calibrate**, the calibration screen to tap along to. They're the player's own, kept with their other settings.

Keys, a controller, the mouse and fingers all play it, every finger a press of its own on a touch screen, so chords and holds play by touch; in the browser too.

How it's put together: the song select is a **Song Select** (see [A song select](#a-song-select)) whose cards are its children, keeping the song and difficulty played as `Song` and `Difficulty`; the `Play` scene's **Note Lanes** play them (their **Saved Song** and **Saved Difficulty**), keep their bests as `Best.<song>.<difficulty>` (**Save Best As**), send **Song Complete** as the song ends, which opens the `Results` panel, and every number on the screen is a **Text** showing one of their outputs. The theme plays only on the title: the songs play on the song select and the lanes. Each song is a tune made in Griff (`Music/First Light.wav`, which opens in the Sound Maker) and its chart the table named like it (`Tables/First Light.table`), at Easy, Normal and Hard, which the **Chart** panel opens to record or edit.

The charts were written from the songs' own notes, the way a charter plays along by ear: the melody's notes are the notes, each in the lane its pitch reaches for (higher to the right), with the drums filling its gaps, a phrase's last note held through the rest after it, and the first beat of each phrase a chord. Easy keeps to the main beats (a note every beat or two), Normal follows the melody's eighth notes with chords and holds, and Hard plays all of it, its sixteenths and the snare under it as chords. Every chart is fair: a note in a lane comes at least 0.36 seconds after the last there (so no press can be taken for the wrong note), never more than two lanes at once (one on Easy), and nothing is played in a lane while it's held; each is cleared to an S with perfect timing in Griff's tests, and gets busier from Easy to Hard and from one song to the next. A script can write a chart the same way for a tune of its own, `ChartWriter.Chart("My Song", recipe)` (a table to save, at the three difficulties), from the notes `MusicSynth.Notes(recipe)` lists.

To add a song: make the music (**Audio → New Music**, a WAV in `Music/`), chart it (**Data → New Table** named like it, then record it in the Chart panel), add its path to the Song Select's **Songs**, and copy a card for it (a child of the `Songs` entity, its **Grade** text showing the select's `Grade:<song>`).

## Note lanes

**Entity → Genres → Rhythm → Create Note Lanes** makes a rhythm game you can play straight away: four lanes in the middle of the view with a **Beat Clock** and **Note Lanes** on them, a song made in Griff (`Assets/Music/Groove.wav`, 16 bars at 120 beats a minute, which opens in the Sound Maker to tweak) and its chart (the `Groove` table, at Easy, Normal and Hard), and the score, accuracy and best combo beside them. Play, and as each note crosses the line press its lane: **D**, **F**, **J** and **K**, the arrow keys, the controller's D-pad or face buttons (X, A, Y and B, left to right), a click on the lane, or on a touch screen a finger on it, several at once for a chord. A long note is a hold: press as it reaches the line and keep it down until its end goes by.

Each press is judged by how far it is from the note's beat as the player hears it, early or late, in milliseconds:

- **Perfect** within the **Perfect Window** (45 ms): 300 points.
- **Great** within the **Great Window** (90 ms): 200 points.
- **Good** within the **Good Window** (135 ms): 100 points.
- **Miss**: a note let go past the Good Window, a press earlier than that but within the **Miss Window** (180 ms; further off, a press isn't taken for the note at all, so tapping between notes costs nothing), or a hold let go of before its end (let go within the Good Window of its end, it counts as held). A Miss scores nothing and starts the combo again.

A hold is judged twice: as it's pressed, and at its end (Perfect if it's held to there, Miss if not). The judgment pops up over the lanes in its color, with the combo under it; the pad at the hit line lights while its lane is held, and a hit flashes there.

What the lanes know are outputs, for a **Text**, a **Counter** or a **Bar** to show and anything to read: **Score**, **Combo** (hits in a row since the last Miss), **Best Combo**, **Perfects**, **Greats**, **Goods**, **Misses**, **Notes** (in the chart being played), **Accuracy** (the points scored out of what Perfects all along would have scored, 0 to 100: `{0:0.00}%`), **Grade** (S from 95, A from 90, B from 80, C from 70, D below), **Judgment** (the last one, `Perfect`), **Timing** (how far the last press was from its beat, in milliseconds: below 0 early, above late, for an "Early" or "Late" under the judgment, or to see a player's offset needs setting), **Is Playing**, **Progress** (how far through the chart, 0 to 1, for a **Bar**), **Title** (the chart's name, its song's), **Playing** (the difficulty it plays), **Full Combo** (no Miss yet), and **Best** and **New Best**: the best score kept for the song at that difficulty, and whether this play beat it once it's over. **Save Best As** (`Best`) keeps each song's best score at each difficulty in the save data as `Best.<chart>.<difficulty>`, with its grade (`Best.Groove.Normal.Grade`), for a song select to show; empty keeps none. Scripts can follow its `Judged` event (each judgment and its lane) and `Completed`.

## Charts

A chart is a table (see [Tables of game data](/docs/data-tables/)): a row a note, with these columns.

- **Beat**: when it's to be hit, counting beats as a **Beat Clock** does: 0 is the music's first beat (where the clock's **Offset** puts it), 4 the first of the second bar in 4/4, 6.5 halfway through the seventh beat. A formula works too: `=Bar * 4 + Step`.
- **Lane**: which lane, from 1.
- **Hold**: how many beats a hold note is held for. Empty (or 0) is a tap.
- **Difficulty**: which difficulties it's in: `Hard`, or several, `Normal, Hard`. Empty is in every one. A **choice** column (Easy, Normal, Hard) picks from a list.

The rows' names are anything (`Hard 12`, `Chorus 3`), and they're in any order: the lanes sort the notes by beat (then lane). A chart is a `.table` file like any other, a line a note in git, so two people charting different parts of a song merge cleanly, and it's edited in the **Tables** panel: type the notes in, paste them from a spreadsheet, or work them out with formulas. A note changed while the game plays reaches it at once.

A song picks its chart by name: the lanes' **Song** `Music/Groove.wav` plays the `Groove` table, unless their **Chart** names another. Their **Difficulty** says which notes to play (`Normal`: those naming Normal, and those naming none); empty plays only the notes that name none, so a chart without a Difficulty column plays whole. A menu picks it with no code: a **Choice** whose **Save As** is `difficulty` (its options Easy, Normal and Hard), and the lanes' **Saved Difficulty** `difficulty`; or a button that does the lanes' **Play** action **With** `Hard`, which starts them again at that difficulty. A difficulty the chart doesn't name, or a note in a lane the lanes haven't got, is said in Output as the game plays.

The song works the same way: the lanes' **Saved Song** names where a menu kept the song to play (`Song`), which they play in place of their **Song**, with its own chart. Played from their own scene in the editor, with nothing saved there, they play their Song.

## A song select

A **Song Select** is a song select with no code, as the Rhythm template's: its **Songs** (`Music/First Light.wav`, a path each) are the entity's children in order, a card each, and the player picks one out with up and down (the arrow keys, W and S, a controller's D-pad or stick, held to run through them), the difficulty with left and right (its **Difficulties**, Easy, Normal and Hard), and plays it with its **Play Actions** (Jump and Interact: Space, Enter, E and a controller's A and Y); a click or a tap on a card picks it out, and another on that card plays it. **Back** names the scene Esc, Backspace or a controller's B go back to. As it plays a song it keeps the song and difficulty in the save data (**Save As** `Song`, **Save Difficulty As** `Difficulty`) and loads its **Scene** after **Delay** (0.5 seconds, its **Pick Sound** playing), where Note Lanes whose Saved Song and Saved Difficulty name the same play them. Coming back, the song and difficulty played last are picked out again. In the Inspector its **Songs** are rows of music picked from the project's, each saying which table is its chart and its notes, and its **Difficulties** and a Note Lanes' **Difficulty** are picked from the ones the charts have.

With **Preview** on, the song picked out plays (from **Preview From** seconds in, over and over, at **Preview Volume**), so the player hears each as they move through them; give that scene no Background Music. Its **Cursor** (an entity, `Cursor`) glides over the card picked out, and **Move Sound** plays as the song or difficulty changes.

What it knows are outputs for texts to show: **Title** (the song picked out's, its file's name), **Difficulty**, **Notes** (in its chart at that difficulty), **Best** and **Best Grade** (the best score kept for it at that difficulty, by Note Lanes whose Save Best As is its **Best From**, `Best`), and read by a song's name, **Grade:First Light** and **Best Score:First Light** for a badge on each card. Its actions, for buttons beside it and agents, are **Next**, **Previous**, **Next Difficulty**, **Previous Difficulty**, **Play** and **Pick** (a song by name, played). It listens like a menu, while it's on the top panel (or none is open), and is each player's own in a network game.

## Recording a chart

The quickest way to chart a song is to play along to it. Choose **Entity → Edit Chart** and the chart (the Groove table, say), select the Note Lanes, or pick a chart in the **Chart** panel (beside Tables at the bottom): the chart is drawn on a timeline, a row a lane in the lanes' colors, the bars numbered across the top and the song's shape behind the notes. The difficulty picked is drawn bright, and the other difficulties' notes show faintly behind it, so a Hard chart can be built on top of Normal. The song, its tempo and its first beat are the lanes' own (their **Song** and their **Beat Clock**; with no lanes in the scene, the music named like the chart, at the tempo it says), so what you chart is what the lanes will judge.

Pick the **difficulty** to record into, the **Snap** (the grid the notes land on: 1/4 a note a beat, 1/8, 1/8 triplets, 1/16 or 1/16 triplets) and whether a take goes **Overdub** (added to the notes there) or **Replace** (the difficulty's notes from where it started to where it stopped taken out first). Click the ruler to put the playhead where to start (on the grid; Shift for anywhere), and press **Record** (or **R**). The song plays from a bar before the playhead, so you're in time when it gets there; at the very start, clicks count you in instead. Tap the lanes' keys along to it, as the game binds them: **D**, **F**, **J** and **K** (or the arrows), or a controller's buttons. Each tap is a note at the beat you heard it on, snapped to the grid, and shows on the timeline as you play; hold a key down for half a beat or more and it's a hold, to where you let go. Taps before the playhead are only getting ready, and left out. **Stop** (Space, or Record again) puts the take into the chart as **one undo step** and says what went in ("Take 3: 24 notes in Hard from bar 5 to bar 12, in place of 20"), and the playhead goes back to where the take started, ready for another go. **Metronome** clicks every beat as it plays, for a song whose beat is hard to hear.

Taps are timed by what you hear, not by what the computer sent: the sound card's own delay is allowed for, and so is your **audio offset** (**Offset ms** on the toolbar). It's the same setting a **Calibration** measures (see [A calibration screen](#a-calibration-screen)) when you play the game in the editor, so calibrate once and your takes land on the beat even through Bluetooth headphones; changing it here changes it there.

Then tidy it up by hand, on the same grid:

- **Click** an empty spot in a lane to add a note there; **drag along** the lane as you click to make it a hold.
- **Drag** a note to move it along the grid or to another lane; **drag the end** of a hold to make it longer or shorter (back to its start makes it a tap).
- **Right-click** a note (or pick it and press **Delete**) to take it out. The arrow keys move the note picked a step along the grid or a lane up or down.
- **Play** (Space) plays from the playhead with a tick on each note, to hear how it sits on the music; double-click the ruler to play from there. Stop leaves the playhead where it got to. The wheel scrolls along the song and Ctrl+wheel zooms.

Each change is one undo step of the chart's table (**Undo** and **Redo** on the toolbar, Ctrl+Z and Ctrl+Y, and the Tables panel's undo too), saved straight away and reaching a running game at once. A note that's in several difficulties (a row naming none, or `Normal, Hard`) changes only in the one being edited: the row stays for the others, and the edited note becomes a row of its own. New notes are rows named for their difficulty (`Hard 61`), put in after the notes before them, so the chart reads in order in the Tables panel and in git. A chart whose **Beat** or **Lane** is worked out by a formula is shown but not edited here: the panel says to change the formula.

Agents chart the same way through the [MCP](/docs/agents/): `Rhythm_EditChart` picks a chart, `ui_set` on `Chart` sets `Difficulty`, `Snap`, `RecordMode`, `AudioOffset` and `Playhead`, `ui_invoke` `RecordTake` records a take of taps given as moments in the song (`"1@4.0, 2@4.5, 4@5.0-5.8"`: a lane at so many seconds in, as it reached the speakers, held to a later moment for a hold) as one undo step with your offset and the snap allowed for, and `AddNote`, `MoveNote`, `SetHold`, `RemoveNote` and `PlayFrom` do the rest; `ui_inspect` reads `NoteLines` (each note of the difficulty), `Summary` and `Message`. `Rhythm_RecordChart` and `Rhythm_PlayChart` start and stop a take or playback.

## The lanes' settings

- **Song**: the music they play as they start, from the top, once through (not looping), at their **Volume**. Empty keeps time with the music playing (a Background Music's), or with none, clicks a metronome at the clock's tempo, so a chart can be tried before it has a song.
- **Saved Song**, **Chart**, **Difficulty** and **Saved Difficulty**: see [Charts](#charts).
- **Save Best As**: where each song's best score is kept (see [Note lanes](#note-lanes)); a [Song Select](#a-song-select) shows them.
- **Clock**: the entity whose **Beat Clock** times them; empty is their own, or else the scene's first. The clock says the tempo: music made in Griff says its own, and other music needs the clock's **Tempo** set (and its **Offset**, where the first beat is).
- **Start On Show** (on): they start as they're shown (as the scene starts, or a panel they're in opens). Off, they wait for their **Start** action. **Stop** stops them and their song.
- **Lanes** (4), **Direction** (which way the notes travel to the hit line: **Down**, **Up**, **Left** or **Right**, the lanes one above another for the last two), **Speed** (how fast the notes come, in world units a second: faster spreads them out), **Length** (from where notes come in to the far end), **Lane Width** and **Hit Line** (how far the line is from the end the notes travel to).
- **Actions**: the input actions that press the lanes, the first lane's first, separated by commas. Every game has **Lane1** to **Lane4** (D F J K, the arrows, the D-pad, and X A Y B), which **Game → Controls** rebinds like any other; for more lanes, add actions of the game's own. A lane without one is pressed by touch alone (the Inspector says which). Built-in actions share keys between genres, as the keys under the fingers are few: J and K are also **Fire** and **Dash**, and a fighter's **Punch** and **Kick** (U I J K), and the arrows also walk. Each genre's pieces take the player's input only while they have focus (see [Mixing genres](/docs/mashups/)), so in a game that switches between a fight and the lanes the same keys do each in turn. A game where the lanes and a fighter listen at the same moment gives one of them other keys in **Game → Controls** (the lanes on the arrows alone, say) or actions of its own.
- **Clicks** (on): a click or a tap on a lane presses it, and on a touch screen each finger does, so chords and holds play by touch.
- **Controllable** and its **Focus** action: whether they take the player's input (see [Mixing genres](/docs/mashups/)). Like a menu, they listen while they're on the top panel (or none is open).
- The four windows, in milliseconds.
- **Colors** (the notes', a lane each, as hex colors separated by spaces), **Lane Color**, **Pop Size** (how big the judgment pops up; 0 shows none) and **Order**. The Inspector lists them a color a row.
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
