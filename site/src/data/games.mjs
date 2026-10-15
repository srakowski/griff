// The playable games on the site: one for every template New Project offers (Empty aside), built by `npm run games`
// (tools/web.ps1 -Site) from the repository's code, so what you play is what the template is. This file holds the words
// around each one, which the pages (scripts/games.mjs) and the landing page's gallery use: a line for its card, a paragraph
// about it, and its controls. The title, the order and whether it plays over a network come from the template itself
// (games.json); the build fails when a template has no entry here, or an entry here has no template.
//
// Keys are the templates' names (ProjectTemplate.Name). Controls are `[keys, what they do]`; a game that takes the mouse
// says so in its tips.

export const games = {
	Platformer: {
		tagline: 'Run, jump and dash across platforms, swing a sword and come back at the last checkpoint.',
		about:
			'Collect coins, ride a lift, bounce off a spring and fight a slime with your sword. When it goes wrong you come back at the last checkpoint.',
		controls: [
			['Arrow keys or WASD', 'run'],
			['Space', 'jump'],
			['Shift', 'dash'],
			['X', 'swing the sword'],
			['E', 'read a sign'],
		],
	},
	TopDown: {
		tagline: 'Explore a torch-lit dungeon in eight directions, with a bat on your tail.',
		about:
			'Collect gems in a dim dungeon, fight a bat that chases you on sight, push a crate, step through portals and talk to the one who lives here.',
		controls: [
			['Arrow keys or WASD', 'walk in any of eight directions'],
			['Shift', 'dash'],
			['X', 'swing the sword'],
			['E', 'talk'],
		],
	},
	Arcade: {
		tagline: 'Blast waves of enemies in an arena, with a menu, options and a high score.',
		about:
			'A whole small arcade game: a title menu, an options screen, three hearts, waves of enemies and a game over that keeps your best score.',
		controls: [
			['WASD', 'fly'],
			['Mouse', 'aim'],
			['Click', 'fire'],
		],
	},
	Puzzle: {
		tagline: 'Push crates onto the marks in four Sokoban levels.',
		about: 'Each level opens once the one before is solved. Push, never pull, so think before the crate reaches a corner.',
		controls: [
			['Arrow keys or WASD', 'walk and push'],
			['Z', 'take a move back'],
			['R', 'start the level over'],
		],
	},
	Physics: {
		tagline: 'Pull back the slingshot and topple towers of wood, glass and stone onto the grubs inside.',
		about:
			'Three levels, each opening once the one before is won. Wood cracks, glass shatters and stone crumbles, every bird left over scores a bonus, and the best stars and score of each level are kept.',
		controls: [
			['Mouse or a finger', 'drag the bird back and let go'],
			['Arrow keys', 'pull the slingshot back'],
			['Space', 'let go'],
			['R', 'try the level again'],
			['Esc', 'pause'],
		],
	},
	Pinball: {
		tagline: 'Midnight Midway: a pinball table at a night carnival, with bumpers, a skyway and a bell to ring.',
		about:
			'Three balls to score with. Light M, I and D to raise the multiplier up to five times, knock the drop targets down twice for an extra ball, ride the orbit and the skyway, and ring the bell. A ball lost straight after it is shot is saved once; nudge the table to keep the ball alive, but not too often; and the best score is kept.',
		controls: [
			['Space or Down (held)', 'pull the plunger back, let go to shoot'],
			['Left and Right Shift, or Z and /', 'flip'],
			['Mouse or a finger', 'press either side to flip, hold the plunger to pull it'],
			['N or Up', 'nudge'],
			['Esc', 'pause'],
		],
	},
	Rhythm: {
		tagline: 'Neon Pulse: press each lane as its notes cross the line, to three songs at three difficulties.',
		about:
			'Pick a song (each plays as you pick it out) and a difficulty, then hit the notes on the beat: Perfect, Great, Good or Miss, holds held to their end, a combo, a score and a grade, with your best kept for every song and difficulty. Settings set the note speed, the volumes and your audio offset, which you can tap along to calibrate.',
		controls: [
			['D F J K, or the arrow keys', 'press the four lanes'],
			['Up, Down, Left and Right', 'pick a song and a difficulty'],
			['Enter or Space', 'play'],
			['A controller', 'the D-pad or X A Y B on the lanes'],
			['Mouse or fingers', 'press the lanes, several at once for a chord'],
			['Esc', 'pause'],
		],
	},
	Contraption: {
		tagline: 'Build machines in a room full of props: wheels, thrusters, ropes, hinges and logic gates.',
		about:
			'Grab and throw things, freeze them in the air, spawn crates, wheels, balloons, thrusters, lamps and buttons, weld, hinge and rope them together, and wire buttons to thrusters to make a lift.',
		controls: [
			['A and D', 'walk'],
			['Space', 'jump'],
			['1 to 7', 'pick a tool'],
			['Mouse', 'use the tool: grab, spawn, weld, hinge, rope, wire or remove'],
			['E', 'press buttons'],
			['Z', 'undo the last thing you built'],
		],
	},
	Multiplayer: {
		tagline: 'An arena shooter for up to eight players, on one network or over the internet.',
		about:
			'Shoot the slimes and, on a desktop, each other, grab hearts and top the scoreboard. Here in the browser you play alone against the slimes: the menu says so where it would host or join a game.',
		controls: [
			['WASD', 'move'],
			['Mouse', 'aim'],
			['Click', 'fire'],
			['Shift', 'dash'],
		],
	},
	BunnyMark: {
		tagline: 'A benchmark: how many bunnies can this computer bounce at 60 frames a second?',
		about:
			'Bunnies pour in and bounce about, more and more, until the game finds how many your computer holds. Each one is an ordinary entity, a sprite and a script. The count is shown and saved, with the best so far.',
		controls: [['Space', 'run it again']],
	},
	Isometric: {
		tagline: 'A sunny courtyard seen at a slant, with walls to walk behind and a crate to push.',
		about:
			'Collect gems in a ruined courtyard, walking behind and in front of its walls and hedges. A slime chases you on sight, a crate goes onto a plate and the gardener knows what is what.',
		controls: [
			['Arrow keys or WASD', 'walk'],
			['Shift', 'dash'],
			['X', 'swing the sword'],
			['E', 'talk'],
		],
	},
	Match3: {
		tagline: 'Swap gems into lines that clear, fall and refill in chains, over four levels.',
		about:
			'Every level has a move limit and goals to meet: a score to reach and gems to collect. Each level opens once the one before is won.',
		controls: [
			['Drag a gem onto its neighbor', 'swap them'],
			['Click a gem, then its neighbor', 'swap them'],
			['Arrow keys and Space', 'move the cursor, and pick a gem up'],
		],
	},
	Shooter: {
		tagline: 'A first-person shooter whose levels are painted top-down.',
		about:
			'Fight your way out of a dark base: grunts that charge, robots that throw fireballs, sliding doors, a blue key for the way out and a shotgun in the armory.',
		controls: [
			['WASD', 'move'],
			['Mouse or arrow keys', 'turn'],
			['Click', 'fire'],
			['1 and 2', 'change weapon'],
		],
		tips: ['The first click captures the mouse so you can look around; Esc lets it go.'],
	},
	Sandbox: {
		tagline: 'A block world made from a seed to dig up and build in, with caves, ores and crafting.',
		about:
			'Dig through grassy hills, caves, coal, iron and gold, build with what you find, and craft planks, torches, workbenches, furnaces and glass. Sand falls, and underground is dark without a torch.',
		controls: [
			['A and D', 'walk'],
			['Space', 'jump'],
			['Left mouse button', 'mine'],
			['Right mouse button', 'build'],
			['Click the hotbar', 'pick a block'],
			['C', 'craft'],
		],
	},
	Tactics: {
		tagline: 'Pick a unit, see how far it can walk and hit, and send it to fight.',
		about:
			'Lead two knights and two archers against an orc warband on a field of trees and rocks, side by side in turns. The computer plays the orcs.',
		controls: [
			['Click a unit', 'see how far it can walk and hit'],
			['Click a blue cell', 'walk there'],
			['Click a red foe', 'attack it'],
			['Right-click', 'take a walk back'],
			['Tab', 'end your turn'],
		],
	},
	Roguelike: {
		tagline: 'Dungeons made from a seed, floor after floor, where everything moves when you do.',
		about:
			'Go down through a crypt made anew every game: find the stairs on each floor, fight the slimes and bats by walking into them, and pick up gold and potions. The deepest floor you reach is kept.',
		controls: [
			['Arrow keys or WASD', 'walk and fight'],
			['1', 'drink a potion'],
			['Tab', 'wait a turn'],
		],
	},
	Strategy: {
		tagline: 'Real-time strategy: workers gather and build, barracks train armies, and a computer opponent comes out of the fog.',
		about:
			'Command an army against the computer: gather gold and wood with workers, put up farms, barracks and towers, train soldiers and archers, and destroy the red base.',
		controls: [
			['Click or drag', 'select'],
			['Right-click', 'send them there'],
			['Hotkeys on the command card', 'build and train'],
			['Arrow keys or the minimap', 'look around'],
		],
	},
	FallingBlocks: {
		tagline: 'Pieces of four slide, turn and drop; full rows clear and it all speeds up.',
		about:
			'Pieces of four blocks fall one after another: fill rows to clear them, and every ten rows they fall faster, until they stack up to the top. The best score is kept.',
		controls: [
			['Left and right', 'slide'],
			['Up', 'turn'],
			['Down', 'fall faster'],
			['Space', 'drop'],
		],
	},
	RpgStats: {
		tagline: 'A turn-based battle worked out from tables: classes, spells, items and enemies.',
		about:
			'Pick a class and a foe and fight it out by turns: attack, cast your spell or use your item. Races, classes, items, spells, levels and enemies are tables, with no code, and the editor ships a Balance simulation that plays every class at every level against every enemy.',
		controls: [
			['Arrow keys', 'choose in the menu'],
			['1', 'attack'],
			['2', 'cast your spell'],
			['3', 'use your item'],
			['Click', 'any of those, on its button'],
		],
	},
	Cards: {
		tagline: 'A card duel against the computer: creatures, spells and mana that grows every turn.',
		about:
			'Duel the lich: spend mana that grows each turn on creatures that strike every turn and spells that burn, heal and draw, and bring its health to nothing before it does the same to you. Every card is a row of a table, with no code.',
		controls: [
			['Drag a card', 'onto your side of the table to play it'],
			['Tap a card, then the table', 'play it on a touch screen'],
			['Drop a spell on what it hits', 'or aim it after with the dotted line'],
			['Arrow keys and Space', 'pick a card, play it and aim it'],
			['Shift', 'put a card back'],
			['Tab', 'end your turn'],
		],
	},
	Adventure: {
		tagline: "Where's Pip? A short point-and-click adventure: three rooms, a neighbour to talk to, and a cat up a tree.",
		about:
			"Pip the cat has been out all night, and he's stuck up the apple tree next door. Look at and use everything in the kitchen, the garden and the shed, talk to Mabel over the fence, and find, use and put together the things that will bring him down. Your progress is saved as you go.",
		controls: [
			['Click or tap', 'walk there, or use what you clicked'],
			['Right-click', 'look at it'],
			['Click an item, then something', 'use the item on it, or put two together'],
			['Arrow keys, Space, X', 'go from thing to thing, use, look'],
			['E', 'your items'],
		],
	},
	VisualNovel: {
		tagline: 'Starfall Café: a short visual novel about a star that fell into the flour.',
		about:
			'At closing time a little star falls into the café, and June and Felix have until sunrise to get it home. Pick how to warm it up and whether to let it go: three endings, with save and load slots, a log, skip and auto, all with no code.',
		controls: [
			['Click, tap or Space', 'read on'],
			['Arrows and Enter', 'pick a choice (or click it)'],
			['Ctrl', 'skip lines read before'],
			['A', 'Auto: read on by itself'],
			['H or the mouse wheel', 'the log of lines read'],
			['Esc', 'pause, save, load and settings'],
		],
	},
	Fighter: {
		tagline: 'Dojo Clash: a one-on-one fighting game for two at one keyboard or against the computer.',
		about:
			'Pick Kenta or Mira and fight best of three rounds against the clock on a dojo yard at dusk: quarter circles, a dragon punch, charges and an overhead, blocking high and low, and combos. Two can share the keyboard or play on two controllers; training has a dummy that stands, crouches, jumps or fights back.',
		controls: [
			['WASD, F and G', 'the left player: move, punch, kick'],
			['Arrows, K and L', 'the right player: move, punch, kick'],
			['A controller', 'a player of its own: X punch, A kick'],
			['Hold back', 'block (down-back to block low)'],
			['Tab', 'swap who has the keyboard and the controller'],
			['Esc', 'pause'],
		],
		tips: ['Against the computer, the keyboard is yours: the arrows or WASD to move, J to punch and K to kick.'],
	},
};
