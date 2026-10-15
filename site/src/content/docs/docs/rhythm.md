---
title: "Rhythm games"
description: "Keeping time with what each player actually hears and sees: their audio and visual offsets, a calibration screen to tap along to, and how a hit's timing allows for them."
---

A rhythm game lives or dies by timing, and timing means what the player *hears*, not what the game sends to the sound card. A **Beat Clock** (see [Keeping the beat](/docs/audio/#keeping-the-beat)) already counts what's reached the speakers, allowing for the sound card's own delay. Some speakers add a delay of their own that no computer can see: Bluetooth headphones are often 150 to 250 milliseconds late, a TV's speakers or a sound bar a little less, and a TV's picture can lag behind too. Each player sets theirs once, by tapping along, and every beat clock allows for it from then on.

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

## Timing a hit

The beat clock gives what a hit judgment needs, with the player's offsets already allowed for:

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

An offset belongs to a player and their speakers, so it stays on their machine: it's kept in their own save data, never sent to the other players, and never saved with a scene. Each machine's beat clocks count beats as that machine's player hears them. In a network game the host runs the game, so its clocks' messages, and what steps to them, go by the host player's hearing (as they've always gone by the host's sound card); each player's own clock, with their own offset, drives what they see bop and when their own taps land. A hit is best judged on the machine of the player who tapped, by their own clock, and the result sent on: that way nobody's headphones shift anyone else's timing, and two players with different offsets each get "on time" when they tap with what they hear. A calibration is each machine's own (its buttons work on that machine at once), and so is the setting it applies.

The offsets never touch anything else in the game: they only shift when this machine's music counts as heard and where things are drawn. A test or a replay without music counts the same with the same offsets (0 unless a test sets them), and rewinding in the editor leaves them as they are.

## In the browser

A game exported to the web calibrates the same way: the clicks play through Web Audio (counting what the browser says it holds and its own delay), and keys, the mouse, touch and a controller all tap. A browser makes no sound until the player has clicked or pressed something, so open the calibration from a button, as an options menu does. The offsets are kept with the page's other saves.
