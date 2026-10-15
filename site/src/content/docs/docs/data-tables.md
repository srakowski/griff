---
title: "Tables of game data"
description: "Items, spells, enemies and level curves in tables edited like a spreadsheet, with formulas, curves and charts, pasted from Excel and Google Sheets, read by scripts, set on components with no code, and balanced by simulating thousands of fights."
---

## Tables

A game's items, spells, enemies, races, classes, loot and level curves are tables: rows by name (Fireball, Frost Bolt) and columns of one type each (**number**, **whole number**, **text**, **yes/no**, a **choice** from a list, **dice** like `2d6+3`, a **sprite**, **sound** or **prefab**, or a **row** of another table, by its name). **Data → New Table...** makes one in `Assets/Tables` and shows it in the **Tables** panel (beside Assets and Output, or **Data → Tables**), a spreadsheet: pick a table at the top left, click a cell and type (F2 edits what's there; Enter keeps it), or pick from a list in cells that name choices, rows, pictures, sounds or prefabs; a yes/no cell is a tick. Text that doesn't fit its column (a word in a number column) is refused, saying what it takes, and a cell naming a picture, prefab or row that isn't there is flagged in yellow (a dice cell's tooltip says what it rolls: `Rolls 5 to 15, 10 on average`). The rows' names are the first column: type over one to rename it. Click a column's heading to pick it: the bar above the grid renames it and sets its type (its cells are converted, or the change is refused, saying which don't fit), a choice's choices and a row column's table; right-click the heading to sort, move, add or delete columns. **+ Row** (Ctrl+Enter) adds a row after the picked one, **+ Column** a column after the picked one, Alt+Up and Alt+Down move rows and Alt+Left and Alt+Right columns, **A-Z** and **Z-A** sort by the picked column (numbers by size, choices in their order, empty cells last), **Fill** (Ctrl+D) copies the top picked cell down the picked ones, Delete empties cells, and the filter box shows only the rows with something in them. Every change is saved straight away and undone with Ctrl+Z (Ctrl+Y redoes), each table with its own history; a change to its file from outside (an agent, a text editor, a git checkout) shows at once, and is a step that can be undone too.

## Copy and paste with spreadsheets

Copy and paste go both ways with Excel, Google Sheets and LibreOffice, as tab-separated text: select cells (drag, Shift-click, Ctrl+A), Ctrl+C, and paste them into a sheet; copy a block in a sheet and Ctrl+V pastes it at the picked cell, adding rows past the bottom (named by what's pasted into the names, or `Row 5`), with one value pasted into several picked cells filling them all. Copying every row copies the column headings too, and pasting a sheet with its headings into a table with no rows yet brings its columns, each getting the type its values all fit. **Data → Import CSV...** (also on the Assets panel's right-click menu) does the same with a CSV file (or tab- or semicolon-separated, as spreadsheets save them): a new table named after the file, or the rows of the table of that name replaced, its columns keeping their types (undo puts them back). **Data → Export CSV...** writes the table showing for a spreadsheet to open.

## Formulas

A column can be worked out instead of typed in, as a spreadsheet's is: pick it and type a formula in **Formula** in the column bar, or type one starting with `=` into any of its cells (it becomes the whole column's, as in a spreadsheet's table; undo puts the values back). Its cells then show what it works out to, a shade quieter than typed ones, and change as soon as anything it reads does, in this table or another, while editing and while the game runs. Its type still says what it gives: a **whole number** column rounds, a **dice** column takes dice. Emptying the formula makes it a column of values again, keeping what it worked out.

```text
=Base * 1.15 ^ (Level - 1)              the row's Base and Level columns
=[Max Health] * 2                       a name with spaces or dots goes in brackets
=Class.Health + Level * 5               through a row column, a column of the row it names
=Classes.Warrior.Health                 a row of any table, by name (Levels.[10].XP for a row called 10)
=Lookup("Classes", Class, "Speed")      the same, the names worked out
=Prev(Total) + XP                       the row above's (0 in the first row): a running total
=Sum(Items.Price) / Count(Items)         a whole column added up, and a table's rows counted
=SumIf(Items.Weight, Items.Slot, "Armor")
=If(Level >= 10, "Veteran", "Rookie")
=Dice(Level, 6, 2)                      dice, for a dice column: Level d6 + 2
=Base Damage + Level                    dice plus a whole number stay dice: 1d8 + 3 is 1d8+3
```

A formula has numbers, text in quotes, `yes` and `no`, `+ - * / ^ %`, `&` to join text, `= <> < <= > >=`, `and`, `or` and `not`, and the functions If, Round, Floor, Ceiling, Abs, Sqrt, Pow, Exp, Ln, Log, Min, Max, Clamp, Lerp, Sum, Average, Count, SumIf, CountIf, AverageIf, Lookup, Prev, Row (the row's place, its level in a table of levels), Rows, Dice and Text. `Name` is the row's name. Renaming a column renames it in the table's formulas; a renamed row or column is still found by its old name elsewhere.

Formulas can't run code: Griff reads one into a small tree of these and works it out itself. Every host works the formulas out as it loads the tables, in the same order (the tables a formula reads first, then each table top to bottom and left to right), with its own powers and logarithms rather than the machine's maths library, so the editor, an exported game, every player of a network game and the web player get the same values to the last bit. A formula that doesn't read is refused as it's typed, saying what's wrong. One naming a column, row or table that isn't there (`#ref`), working itself out in a loop through other formulas (`#loop`), or with a sum that can't be done, like a division by 0 (`#error`), is kept: its cells are flagged in yellow (the tooltip says why), the line under the grid says how many problems there are (resting on it lists them), and a running game says each once in Output (or the exported game's log, or the browser's console) and plays on, the cells empty (0) until it's put right. The file keeps the formula, not its values:

```json
{ "name": "XP", "type": "whole number", "formula": "=Base * 1.15 ^ (Level - 1)" }
```

## Curves: levels, stats and prices

A table of levels (XP to the next level, a stat that grows, what things cost) can be generated from a curve rather than typed. Pick a number or whole number column and click **Curve** in the column bar: the curve editor opens beside the chart, with the curve drawn dashed over the column's values. Pick its kind, **linear** (the same step every level), **power** (slow, then fast; **Shape** is the power), **exponential** (the same share more every level, as XP usually is), **s-curve** (slow, quick through the middle, slow near the top; **Shape** is how sharp) or **points**, drawn by hand (click the chart to add one, drag one to move it, right-click to take it away; the curve goes smoothly through them without overshooting); its value at level 1 and at the last level; how many levels; and what to round to (10 gives 1,250 rather than 1,247). **Generate** (also **Data → Generate from Curve**) fills the column, a row a level: rows are added past the last (named by their level, `12`), rows still called `Row 3` take their level's name, and it's one step undo takes back. The column keeps its curve, in the file, so picking it later opens the curve as it was to change and generate again:

```json
{ "name": "XP", "type": "whole number", "curve": { "kind": "exponential", "first": 100, "last": 50000, "levels": 50, "roundTo": 10 } }
```

## Charts

The chart beside the grid (the chart button on the toolbar shows or hides it) draws the picked column: a number, whole number, yes/no or formula column as bars over the rows, or a line over the levels when the rows are levels (named 1, 2, 3, or generated from a curve). A dice column shows each row's average (a dot), range (a line from the least it rolls to the most) and spread (a box either side of the average, how far a roll usually lands from it). Resting the mouse on the chart says the row under it (`Ogre: 2d6+3 rolls 5 to 15, 10 on average, spread 2.42`), and the line under it sums the column up (`100 (1) to 50,000 (50), 9,870 on average`). It's drawn in the editor's theme.

## Table files

A table is a `.table` file in Assets, named after the file: JSON laid out a line per column and a line per row, each row listing only the cells it has, so a changed cell changes one line in git and two people changing different rows merge cleanly:

```json
{
  "columns": [
    { "name": "Health", "type": "whole number" },
    { "name": "Damage", "type": "dice" },
    { "name": "Element", "type": "choice", "choices": ["Fire", "Ice"] }
  ],
  "rows": {
    "Slime": { "Health": 6, "Damage": "1d4", "Element": "Ice" },
    "Fire Imp": { "Health": 12, "Damage": "2d6+1", "Element": "Fire" }
  },
  "renamedRows": { "Imp": "Fire Imp" }
}
```

A row or column renamed in the panel is listed under `renamedRows` or `renamedColumns`, and still answers to its old name: what names it by that name (a Table Values component, a script, another table's row column, in any scene or prefab, exported or not) still finds it. What the file holds that Griff doesn't know is kept when it's saved again.

## Table Values: a row's values with no code

Add a **Table Values** to an entity and its other components take their values from a row with no code: its **Table** (`Enemies`, picked from the project's tables) and **Row** (`Slime`, picked from the table's rows; left empty, the row named like the entity, so every copy of a Slime prefab finds the Slime row), and **Sets**, a line for each property and the column it takes: `Health.Max = Health`, `DamageOnTouch.Amount = Damage`, or `Speed` for every Speed on the entity from the Speed column. With **Sets** empty, every property named like a column takes it (a column called `Patrol.Speed` sets that one). The values are set as the entity starts in the game (spawned ones straight away, so they play their first step with them) and again whenever the row changes while the game runs: edit a cell, or an agent writes the file, and the next step has it, never part way through a step, so a game plays the same given the same changes at the same steps. A number given dice rolls them, once (a Slime's health from `2d8`), except a hit's damage (a Bump Attack's, a Tactics Unit's or a Damage On Touch's), which keeps the dice and rolls them for each hit, showing their average (its **Damage Roll** or **Amount Roll** holds them); rewinding doesn't roll them again, nor set a value the game has changed since, unless the row changed. In the Inspector the fields it sets carry a small table mark, whose tooltip says where they come from. A table or row that isn't there is flagged in the Inspector (the **Table** or **Row** field in yellow), a line of **Sets** naming a component or property the entity hasn't got is noted above its fields, and any of them, or a column the table hasn't got, is said in Output as the game starts, once, while the rest is set and the game plays on.

## Tables in scripts

`Tables` is the game's tables (see [Tables](#tables)). A row's cells turn into what they're used as, and a class of your own is filled from a row, each property taking the column of its name (ignoring case and spaces, so `MaxHealth` takes `Max Health`; a `Name` takes the row's name):

```csharp
float damage = Tables.Get("Spells", "Fireball")["Damage"];
int cost = Tables.Get("Spells", "Fireball")["Cost"];
Dice roll = Tables.Get("Spells", "Fireball")["Roll"];     // roll.Roll(), roll.Average, roll.Min, roll.Max, roll.Spread

public class Spell { public string Name { get; set; } = ""; public float Damage { get; set; } public Element Element { get; set; } }
var fireball = Tables.Get<Spell>("Spells", "Fireball");    // a choice column fills an enum by name
foreach (var spell in Tables.All<Spell>("Spells")) { ... }
var upgrade = Tables.Linked(Tables.Get("Spells", "Fireball"), "Upgrade");   // the row a row column names
```

An empty cell is 0, "" or no. A table, row or column that isn't there throws, saying which there are (`The Spells table has no row "Firebal". Its rows: Fireball, Frost Bolt.`), so the game stops at the line in the editor; `Tables.Find("Spells", "Fireball")` gives null instead. A table changed while the game runs reaches scripts as the next step begins, and crowd scripts can read tables from any core. Formula columns read as what they work out to (and the formulas reading a changed table are worked out again with it); `Tables.Problems` lists what's wrong with them. `Dice.Parse("2d6+3")` reads dice written anywhere, and `Dice.Of(2, 6, 3)` makes 2d6+3.

## Balance simulations

Whether a class is too strong at level 6, or an ogre too much for anyone below level 4, is found by playing the fight thousands of times, not by guessing. A balance simulation does that headless, as fast as the computer goes and across all its cores: **Data → New Simulation...** makes one in `Assets/Simulations` and shows it in the **Simulations** panel (beside Tables, or **Data → Simulations**), and **Run** plays it.

With no code, a simulation plays each row of one table against each row of another (each class against each enemy) in an **arena**: a scene of your game with an entity for each side (a Hero and an Enemy) and whatever they fight with. Each run is a game of its own, played by the game's own rules: the arena loads afresh, each side's entity takes its row's values through its **Table Values** (one is added, setting every property named like a column, if it has none), and its Health, attacks, turn order and dice play the fight out, step by step, until one side is beaten (every entity of that name dead or gone) or the **Time Limit** passes (a draw). Both sides have to play themselves: give each a Turn Taker on a Turn Order whose **Computer Sides** name both (with Grid Chase and Bump Attack, or Tactics Units), or real-time Attacks and Chases with their **Target**s set. New Simulation starts from the open scene: its first two entities with Table Values are the sides, each with the table it reads.

Its settings, down the side of the panel and saved to its file as they change (Ctrl+Z undoes them):

- **Arena**: the scene the fights are played in (the open one as it is in the editor, saved or not).
- **First side** and **Second side**: each an **Entity** of the arena, the **Table** its rows come from, and the **Rows** to play (empty plays them all). The first side's wins are what the results count.
- **Runs** a pair (200), the **Seed** the runs' random numbers come from, and the **Time Limit** in seconds of game time.
- **Rewards**: the second side's columns the first earns by beating it (`Gold, XP`), dice rolled.
- **Track**: outputs read through each fight, a line each, as a Counter reads them: `Gold = Purse.Count`, `XP = Hero.Amount:XP`.
- **Sweep a column by steps**: a **Table**'s **Column** (a number, not a formula) set to each value **From** one **to** another **by** a step, in every row or one **Row**, and the whole matchup played at each. A Classes table whose Health and Damage are formulas of its Level (`=[Base Health] + Growth * (Level - 1)`, `=Dice(1, Sides, Level)`) swept over Level from 1 to 10 plays each class at each level against each enemy.
- **Or a script**, for everything else (below).

The same seed plays the same fights, to the last bit, on every machine, in the web player and on any number of cores: each run's random numbers come from a seed of its own, made from the simulation's seed, the step, the two rows' names and the run's number, and the results are added up in the same order however the runs were shared out. Thousands of runs take a second or two (a small arena plays about 2,500 fights a second on a 16-thread desktop, 450 on one thread); the panel shows how far it's got, the editor carries on meanwhile, and **Stop** stops it, leaving the results as they were.

## Results: a table, a matrix, a sweep and a run

The panel's results come in four tabs, each beside a chart of the **Measure** picked: **Win %**, **Loss %**, **Draw %**, **Turns** and **Seconds** (and **Turns to Win** and **Seconds to Win**, of the fights won), **Damage Dealt** and **Damage Taken**, what was earned (**Gold**, **XP**) and the values tracked, each the average over a pair's runs.

- **Results**: every pair, a row each, with every measure, charted as bars.
- **Matrix**: a row for each of the first side's rows and a column for each of the second's, each cell coloured by how one-sided the pair is: the panel's own colour when each side wins as often, more of the accent the more the row wins, more of the blue the more the column does. Resting on a cell says how it went (`Warrior against Ogre: won 2 of 200, lost 198; 2.1 turns...`), and the chart beside it shows each row's measure over all the columns: which class is strongest.
- **Sweep**: a line for each of the first side's rows (and all of them together) over the sweep's steps, and its table.
- **Over a Run**: a pair's values as a fight goes, a point each turn (each second without a Turn Order): each side's health, the values tracked and what was earned.

With a sweep, **Step** (by the chart's title) picks which step the results, the matrix and the run show. Over the results, under their summary, what stands out is said in a line or two each: pairs where one side wins 95% of the time or more, a row far from the rest (`Rogue wins 88% of its fights, 31 points over the middle Classes row (57%): too strong?`, `Ogre is beaten in 0% of fights...: too strong?`), fights the time limit ends, and over a sweep, levels that change nothing (`Mage: Level 4 to 6 change nothing (30% to 30.5%): dead levels.`, `Warrior: every fight won from Level 7 on, so the levels after it change nothing.`) and levels that jump (`Level 4 to 5 jumps from 5% to 55%, where a step usually changes it 5 points.`). What the games said as they played (a Table Values naming a column the table hasn't got) is listed too. **Export** (the panel's export button) writes every pair at every step to a CSV file for a spreadsheet.

## Script simulations

A script plays what the matchup doesn't: a gauntlet of fights with gold and XP carried from one to the next, an economy, a dungeon run. Make a class deriving from `SimulationScript` and pick it as the simulation's **Script**; it's made afresh for each run and given the run's pair of rows (the sides' tables and rows as the matchup has them; a side with no table plays once). `Rand` and dice give the run's own numbers, so scripts are seeded and shared across the cores as the matchup is, and `run.Fight` plays the arena's fight between any two rows, by the game's own rules.

```csharp
public class Gauntlet : SimulationScript
{
    public override void Run(SimulationRun run)
    {
        foreach (var enemy in run.Tables["Enemies"].Rows)
        {
            var fight = run.Fight(run.First, enemy);   // the arena's fight: Won, Lost, Turns, Seconds, DamageDealt, DamageTaken
            run.Turns += fight.Turns;
            if (!fight.Won) { run.Lose(); return; }
            run.Earn("Gold", enemy["Gold"]);            // added up, and charted over the run
            run.Earn("XP", enemy["XP"]);
        }
        run.Win();
    }
}
```

`run.First` and `run.Second` are the rows, `run.Tables` the tables (with the sweep's step in them), `run.Step` the sweep's value, `run.Win()`, `run.Lose()` and `run.Draw()` say how it ended (a draw unless it says), `run.Turns`, `run.Seconds`, `run.DamageDealt` and `run.DamageTaken` are averaged like the matchup's, `run.Earn` adds to what the first side earned, and `run.Track("Health", hp)` puts a point on a value's chart over the run. A script that throws makes its run a draw, and what it threw is listed with the results. Breakpoints don't stop simulations: they run off the game's thread.

A game can play its own simulations too, the same as the editor does: `Game.Simulate("Balance")` gives the results (`results.Steps[0].Pair("Warrior", "Ogre")["Win %"]`, `results.Findings`), exported or in the browser, for a debug menu or a game that tunes itself.

## Simulation files

A simulation is a `.sim` file in Assets, a line a setting:

```json
{
  "arena": "Arena",
  "first": { "entity": "Hero", "table": "Classes" },
  "second": { "entity": "Enemy", "table": "Enemies", "rows": ["Slime", "Ogre"] },
  "runs": 200,
  "seed": 1,
  "timeLimit": 120,
  "rewards": ["Gold", "XP"],
  "track": ["Gold = Purse.Count"],
  "sweep": { "table": "Classes", "column": "Level", "from": 1, "to": 10, "step": 1 }
}
```

A scripted one has `"script": "Gauntlet"`. What the file holds that Griff doesn't know is kept. Agents write one with `asset_write` (the answer says what it plays, or what's missing for it to run), run it with `Data_RunSimulation` (which answers with the summary and what stands out) and read the rest from the Simulations panel (see [Agents](/docs/agents/)).
