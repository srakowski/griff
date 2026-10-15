// The site's playable games: every template New Project offers, exported for the web by tools/web.ps1 -Site into
// public/play/ (git-ignored: built by the Site workflow and here on demand), and a page for each.
//
//   node scripts/games.mjs pages    write src/content/docs/games/*.mdx from public/play/games.json and src/data/games.mjs
//                                   (npm run build does); fails if the games aren't built, or a template has no words
//                                   in src/data/games.mjs (or the file names a template that's gone)
//   node scripts/games.mjs build    export the games (needs Windows and the .NET SDK global.json pins): `npm run games`
//                                   options: --only Platformer,Match3 (some templates), --port 8091 (the poster browser's server)
//
// public/play/ holds .NET and the web player once (_player/), then for each game <slug>/ with its page, its files and its
// poster (poster.webp), and games.json. Astro copies it as it is into dist/, so a game is at /play/<slug>/ and its page
// beside the guides is /games/<slug>/.

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { mkdir, rm, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { games as words } from '../src/data/games.mjs';

const site = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(site, '..');
const playDir = join(site, 'public', 'play');
const pagesDir = join(site, 'src', 'content', 'docs', 'games');

const args = process.argv.slice(2);
const mode = args[0] ?? 'pages';
const option = (name, fallback) => {
	const i = args.indexOf(`--${name}`);
	return i >= 0 ? args[i + 1] : fallback;
};

const escape = (text) => text.replace(/[{}<>]/g, (c) => ({ '{': '&#123;', '}': '&#125;', '<': '&lt;', '>': '&gt;' })[c]);
const megabytes = (bytes) => (bytes / 1048576).toFixed(1);

/** The built games: games.json, or a clear message if they haven't been built. */
function listing() {
	const file = join(playDir, 'games.json');
	if (!existsSync(file)) {
		console.error('The playable games are missing (public/play/). Run `npm run games` (Windows, the .NET SDK) or use `npm run build:all`.');
		process.exit(1);
	}
	return JSON.parse(readFileSync(file, 'utf8'));
}

function page(game, list, position) {
	const text = words[game.name];
	const note = [];
	note.push(
		'**Sound starts when you click or press a key.** A browser keeps a page silent until you have done one of them, so the game plays quietly for a moment first and says so at the bottom.',
	);
	if (game.network)
		note.push(
			"**Network play isn't available on the web.** A browser can't open the connections it needs, so this game plays alone here, and where it would host or join a game it says so. The desktop game does it all.",
		);
	note.push('**Saves stay in this browser**, for this game only: your best score or progress is still here when you come back.');
	note.push('**Controllers work too**: plug one in and press a button. On a phone or tablet a touch acts as the mouse.');
	for (const tip of text.tips ?? []) note.push(escape(tip));
	const front = {
		title: game.title,
		description: `Play the ${game.title} template in your browser: ${text.tagline}`,
		sidebar: { label: game.title, order: position + 1 },
	};
	return `---
title: ${JSON.stringify(front.title)}
description: ${JSON.stringify(front.description)}
sidebar:
  label: ${JSON.stringify(front.sidebar.label)}
  order: ${front.sidebar.order}
---

import PlayGame from '../../../components/PlayGame.astro';

<PlayGame slug=${JSON.stringify(game.slug)} title=${JSON.stringify(game.title)} />

${escape(text.about)}

## Controls

${text.controls.map(([keys, what]) => `- **${escape(keys)}**: ${escape(what)}`).join('\n')}

## Good to know

${note.map((n) => `- ${n}`).join('\n')}
- The first game you open downloads .NET and Griff's player (about ${megabytes(list.playerGzipBytes)} MB, kept by your browser and shared by every game here); this game's own files are ${megabytes(game.bytes)} MB.
- The licenses of what the game carries (.NET and the other software and fonts it is built with) are in its [THIRD-PARTY-NOTICES.txt](/play/${game.slug}/THIRD-PARTY-NOTICES.txt).

## Make it yours

This is the **${escape(game.title)}** template as it comes: in Griff's editor, pick it in New Project and change it into your game. Everything in it is an ordinary scene, component and script you can open, and **Build → Build for Web** makes a page like this one from your own game (see [Exporting](/docs/exporting/)). New to Griff? Start with [Getting started](/docs/getting-started/).
`;
}

async function pages() {
	const list = listing();
	const names = list.games.map((g) => g.name);
	const problems = [
		...names.filter((n) => !words[n]).map((n) => `The ${n} template has no entry in src/data/games.mjs (its tagline, paragraph and controls).`),
		...Object.keys(words)
			.filter((n) => !names.includes(n))
			.map((n) => `src/data/games.mjs has an entry for ${n}, which isn't a template (or isn't built: run \`npm run games\`).`),
	];
	if (problems.length) {
		console.error('The games no longer match the templates:\n  ' + problems.join('\n  '));
		process.exit(1);
	}

	const ordered = [...list.games].sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));
	await rm(pagesDir, { recursive: true, force: true });
	await mkdir(pagesDir, { recursive: true });
	for (const [i, game] of ordered.entries()) await writeFile(join(pagesDir, `${game.slug}.mdx`), page(game, list, i));
	const rows = ordered.map((g) => `- [${escape(g.title)}](/games/${g.slug}/): ${escape(words[g.name].tagline)}`);
	const intro =
		"Every template Griff comes with is a whole game, and every one is playable here, in your browser: the same game the editor opens, exported with **Build → Build for Web**, which runs Griff's engine on .NET for WebAssembly. Open one, click it, and play. They are built from Griff's code every time this site is, so they are always the current templates.";
	await writeFile(
		join(pagesDir, 'index.mdx'),
		`---\ntitle: Play\ndescription: Every Griff template, playable in your browser.\nsidebar:\n  label: All games\n  order: 0\n---\n\nimport Gallery from '../../../components/Gallery.astro';\n\n${intro}\n\n<Gallery />\n\nAll of them play with a keyboard and mouse, with a controller, and most with touch; each page says how. Sound starts once you click or press a key, and network play isn't available in a browser.\n\n${rows.join('\n')}\n`,
	);
	console.log(`Wrote ${ordered.length} game pages.`);
}

function build() {
	const only = option('only');
	const script = join(repo, 'tools', 'web.ps1');
	const run = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', script, '-Site', playDir, '-Port', option('port', '8091')];
	if (only) run.push('-Template', only);
	const result = spawnSync('powershell', run, { stdio: 'inherit', cwd: repo });
	if (result.error || result.status !== 0) {
		console.error(`Building the games failed (${result.error?.message ?? 'exit ' + result.status}). It needs Windows, the .NET SDK and Edge or Chrome.`);
		process.exit(result.status || 1);
	}
}

if (mode === 'pages') await pages();
else if (mode === 'build') build();
else {
	console.error(`Unknown mode "${mode}": use pages or build.`);
	process.exit(1);
}
