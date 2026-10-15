# Griff's free fonts

The free fonts that ship with Griff for games, listed in the Inspector's font picker as safe to ship. They sit beside the
editor (in `FreeFonts`), never inside the player: picking one copies the font and its license into the project's
`Assets/Fonts`, and the game ships it from there like any of the project's fonts, its export's
`THIRD-PARTY-NOTICES.txt` naming it with its license.

Each is under the SIL Open Font License 1.1, which lets it be embedded in and shipped with games, sold or not, and each
is the file as released by its official source, unmodified, so its Reserved Font Names (Press Start 2P's) are respected.
Its license text is beside it. `FreeFonts.cs` records the same, with each file's SHA-256, which the tests check.

| Font | Kind | File | Version | By | License | Source |
| --- | --- | --- | --- | --- | --- | --- |
| Press Start 2P | pixel | `PressStart2P-Regular.ttf` | 3.000 | CodeMan38 (Cody Boisclair) | SIL OFL 1.1, Reserved Font Name "Press Start 2P" (`PressStart2P-OFL.txt`) | [google/fonts `ofl/pressstart2p`](https://github.com/google/fonts/tree/67bc647ce1910e92f461bcc82aae30f21d21a9cc/ofl/pressstart2p) |
| Atkinson Hyperlegible Bold | sans | `AtkinsonHyperlegible-Bold.ttf` | 1.006 | Braille Institute of America | SIL OFL 1.1 (`AtkinsonHyperlegible-OFL.txt`) | [google/fonts `ofl/atkinsonhyperlegible`](https://github.com/google/fonts/tree/67bc647ce1910e92f461bcc82aae30f21d21a9cc/ofl/atkinsonhyperlegible) |
| Crimson Text SemiBold | serif | `CrimsonText-SemiBold.ttf` | 1.100 | Sebastian Kosch (The Crimson Text Project Authors) | SIL OFL 1.1 (`CrimsonText-OFL.txt`) | [google/fonts `ofl/crimsontext`](https://github.com/google/fonts/tree/67bc647ce1910e92f461bcc82aae30f21d21a9cc/ofl/crimsontext) |
| JetBrains Mono Bold | mono | `JetBrainsMono-Bold.ttf` | 2.304 | JetBrains (The JetBrains Mono Project Authors) | SIL OFL 1.1 (`JetBrainsMono-OFL.txt`) | [JetBrains/JetBrainsMono release v2.304](https://github.com/JetBrains/JetBrainsMono/releases/tag/v2.304), `fonts/ttf/JetBrainsMono-Bold.ttf` in `JetBrainsMono-2.304.zip` (the same file as the script editor's, in `src/Griff.Editor/Themes/Fonts`) |

The google/fonts files were downloaded at commit `67bc647ce1910e92f461bcc82aae30f21d21a9cc` (October 2026), each with
the `OFL.txt` beside it, renamed here after its font. To add another, it has to be licensed for embedding and
redistribution in games, taken unchanged from its official source with its license, and recorded here and in
`FreeFonts.cs`; keep the set small.
