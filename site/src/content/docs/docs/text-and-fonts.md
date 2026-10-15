---
title: "Text and fonts"
description: "The Text component, its fonts, and typewriter text."
---

## Text and fonts

A **Text** draws its **Value** (several lines are fine) at its entity, **Size** world units a line, in its **Color**, lined up by its **Align** (centered on the entity, starting at it or ending at it). Its **Font** is a font file in Assets (`.ttf`, `.otf` or `.ttc`, such as `Fonts/pixel.ttf`), which ships with the game and is read again when the file changes, or an installed font's name (`Consolas`), or empty for the built-in font, Selawik Semibold (Microsoft's open-source stand-in for Segoe UI, which Griff's text used before, with the same metrics). Griff reads fonts and rasterizes their glyphs itself, in plain .NET (`Griff.Fonts`, with StbTrueTypeSharp), so text draws the same in the editor, an exported game and a browser: each glyph is rasterized once, at 64 pixels a line, onto an atlas page, and text is drawn as one smoothed quad a glyph, so a score or a timer that changes every frame makes no garbage. A character the font hasn't got comes from the built-in font, then from a part of DejaVu Sans Bold (arrows, shapes, symbols such as ♥, Greek and Cyrillic); one none has (Chinese, Japanese, emoji) draws as the font's missing-glyph box, so for those choose a font file that has them. An installed font is drawn from the player's own computer and never copied into a game: on a computer without it, and always in a browser, the built-in font draws instead (the web export warns of each), so prefer font files.

## Typewriter text

Add a **Typewriter** next to a **Text** and the text types itself out, **Speed** letters a second, pausing briefly at punctuation, with an optional blip **Sound**. Whenever a script changes the text, the new line types out from the start. It runs on real time, so it works in a paused game.
