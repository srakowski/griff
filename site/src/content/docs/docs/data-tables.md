---
title: "Tables of game data"
description: "Items, spells, enemies and level curves in tables edited like a spreadsheet, pasted from Excel and Google Sheets, read by scripts and set on components with no code."
---

## Tables

A game's items, spells, enemies, races, classes, loot and level curves are tables: rows by name (Fireball, Frost Bolt) and columns of one type each (**number**, **whole number**, **text**, **yes/no**, a **choice** from a list, **dice** like `2d6+3`, a **sprite**, **sound** or **prefab**, or a **row** of another table, by its name). **Data → New Table...** makes one in `Assets/Tables` and shows it in the **Tables** panel (beside Assets and Output, or **Data → Tables**), a spreadsheet: pick a table at the top left, click a cell and type (F2 edits what's there; Enter keeps it), or pick from a list in cells that name choices, rows, pictures, sounds or prefabs; a yes/no cell is a tick. Text that doesn't fit its column (a word in a number column) is refused, saying what it takes, and a cell naming a picture, prefab or row that isn't there is flagged in yellow (a dice cell's tooltip says what it rolls: `Rolls 5 to 15, 10 on average`). The rows' names are the first column: type over one to rename it. Click a column's heading to pick it: the bar above the grid renames it and sets its type (its cells are converted, or the change is refused, saying which don't fit), a choice's choices and a row column's table; right-click the heading to sort, move, add or delete columns. **+ Row** (Ctrl+Enter) adds a row after the picked one, **+ Column** a column after the picked one, Alt+Up and Alt+Down move rows and Alt+Left and Alt+Right columns, **A-Z** and **Z-A** sort by the picked column (numbers by size, choices in their order, empty cells last), **Fill** (Ctrl+D) copies the top picked cell down the picked ones, Delete empties cells, and the filter box shows only the rows with something in them. Every change is saved straight away and undone with Ctrl+Z (Ctrl+Y redoes), each table with its own history; a change to its file from outside (an agent, a text editor, a git checkout) shows at once, and is a step that can be undone too.

## Copy and paste with spreadsheets

Copy and paste go both ways with Excel, Google Sheets and LibreOffice, as tab-separated text: select cells (drag, Shift-click, Ctrl+A), Ctrl+C, and paste them into a sheet; copy a block in a sheet and Ctrl+V pastes it at the picked cell, adding rows past the bottom (named by what's pasted into the names, or `Row 5`), with one value pasted into several picked cells filling them all. Copying every row copies the column headings too, and pasting a sheet with its headings into a table with no rows yet brings its columns, each getting the type its values all fit. **Data → Import CSV...** (also on the Assets panel's right-click menu) does the same with a CSV file (or tab- or semicolon-separated, as spreadsheets save them): a new table named after the file, or the rows of the table of that name replaced, its columns keeping their types (undo puts them back). **Data → Export CSV...** writes the table showing for a spreadsheet to open.

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

Add a **Table Values** to an entity and its other components take their values from a row with no code: its **Table** (`Enemies`, picked from the project's tables) and **Row** (`Slime`, picked from the table's rows; left empty, the row named like the entity, so every copy of a Slime prefab finds the Slime row), and **Sets**, a line for each property and the column it takes: `Health.Max = Health`, `DamageOnTouch.Amount = Damage`, or `Speed` for every Speed on the entity from the Speed column. With **Sets** empty, every property named like a column takes it (a column called `Patrol.Speed` sets that one). The values are set as the entity starts in the game (spawned ones straight away, so they play their first step with them) and again whenever the row changes while the game runs: edit a cell, or an agent writes the file, and the next step has it, never part way through a step, so a game plays the same given the same changes at the same steps. A number given dice rolls them, once (a Slime's health from `2d8`); rewinding doesn't roll them again, nor set a value the game has changed since, unless the row changed. In the Inspector the fields it sets carry a small table mark, whose tooltip says where they come from. A table or row that isn't there is flagged in the Inspector (the **Table** or **Row** field in yellow), a line of **Sets** naming a component or property the entity hasn't got is noted above its fields, and any of them, or a column the table hasn't got, is said in Output as the game starts, once, while the rest is set and the game plays on.

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

An empty cell is 0, "" or no. A table, row or column that isn't there throws, saying which there are (`The Spells table has no row "Firebal". Its rows: Fireball, Frost Bolt.`), so the game stops at the line in the editor; `Tables.Find("Spells", "Fireball")` gives null instead. A table changed while the game runs reaches scripts as the next step begins, and crowd scripts can read tables from any core. `Dice.Parse("2d6+3")` reads dice written anywhere.
