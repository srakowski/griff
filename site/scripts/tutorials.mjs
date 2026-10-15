// The website's tutorials. Each one is a .demo script in tutorials/ (the same format as the demos in the repository's
// demos/ folder: see demos/README.md), played in Griff's editor by the demo player. The page for it, and every
// screenshot on it, come from that run, so a tutorial can't describe something the editor no longer does.
//
//   node scripts/tutorials.mjs pages                 write src/content/docs/tutorials/*.mdx from the scripts (npm run build does)
//   node scripts/tutorials.mjs run [--only a,b]      play the scripts in the editor (tools/demo.ps1 -Check): each has to get to
//                                                    its end, and the pictures its `shot:` lines save are optimized to
//                                                    src/assets/tutorials/ (lossless WebP). `npm run tutorials` does this.
//   options for run: --no-build (the Debug editor is already built), --port 7711 (the editor's MCP port; GRIFF_MCP_PORT)
//
// What the page says is what the script does: `say:` and `explain:` are its paragraphs, `chapter:` its headings, the
// editor commands in between (menu, create, select, set, add, key, play...) its numbered steps, `step:` a step in words
// (the player ignores it), `shot:` a picture and `agent:` a tool call as a code block. `pages` also checks the script
// against the generated reference (the menu items, components and properties it names have to exist), so a rename in
// the editor fails the site's build before anyone looks at the pictures.

import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const site = join(dirname(fileURLToPath(import.meta.url)), '..');
const repo = join(site, '..');
const scripts = join(site, 'tutorials');
const pagesDir = join(site, 'src', 'content', 'docs', 'tutorials');
const shotsDir = join(site, 'src', 'assets', 'tutorials');
const reference = join(site, 'src', 'content', 'docs', 'reference');

// The tutorials, in the order the index and the sidebar list them. Each is tutorials/<name>.demo.
const order = ['first-platformer', 'top-down-adventure', 'puzzle', 'making-a-game-with-an-agent', 'balancing-with-tables'];

const args = process.argv.slice(2);
const mode = args[0] ?? 'pages';
const option = (name, fallback) => {
	const i = args.indexOf(`--${name}`);
	return i >= 0 ? args[i + 1] : fallback;
};

const tutorials = () => order.map((slug) => ({ slug, file: join(scripts, `${slug}.demo`) }));

function parse(path) {
	const lines = [];
	readFileSync(path, 'utf8')
		.split(/\r?\n/)
		.forEach((text, i) => {
			const line = text.trim();
			if (!line || line.startsWith('#')) return;
			const colon = line.indexOf(':');
			lines.push({ number: i + 1, command: (colon < 0 ? line : line.slice(0, colon)).trim().toLowerCase(), arg: colon < 0 ? '' : line.slice(colon + 1).trim() });
		});
	return lines;
}

const split = (text, sep = '|') => text.split(sep).map((s) => s.trim());
const humanize = (name) => (name.includes(' ') ? name : name.replace(/([a-z0-9])([A-Z])/g, '$1 $2'));
const flat = (name) => name.toLowerCase().replace(/[^a-z0-9]/g, '');

// ----- Checking a script against the generated reference -----

// The editor's menus and their commands, from reference/commands.md: { entity: Set of flattened item names }.
function menus() {
	const commands = join(reference, 'commands.md');
	if (!existsSync(commands)) return null;
	const found = new Map();
	let menu = null;
	for (const line of readFileSync(commands, 'utf8').split(/\r?\n/)) {
		const heading = line.match(/^## (.+) menu$/);
		if (heading) menu = flat(heading[1]);
		const row = line.match(/^\| ([^|]+?) \|/);
		if (menu && row && row[1] !== 'Command' && !row[1].startsWith('---')) {
			if (!found.has(menu)) found.set(menu, new Set());
			found.get(menu).add(flat(row[1]));
		}
	}
	return found;
}

function component(name) {
	const file = join(reference, 'components', `${flat(name)}.md`);
	return existsSync(file) ? readFileSync(file, 'utf8') : null;
}

function lint(tutorial, lines) {
	const index = menus();
	if (!index) return ['The reference pages are missing: run `npm run reference` first (it needs the .NET SDK).'];
	const problems = [];
	const complain = (line, text) => problems.push(`${basename(tutorial.file)} line ${line.number} (${line.command}: ${line.arg}): ${text}`);
	const menuHas = (menu, item) => index.get(flat(menu))?.has(flat(item)) ?? false;
	for (const line of lines) {
		if (line.command === 'menu') {
			const [menu, item] = split(line.arg, '>');
			if (!menuHas(menu, item ?? '')) complain(line, `no "${item}" in the ${menu} menu`);
		} else if (line.command === 'create') {
			if (!menuHas('Entity', line.arg.startsWith('Create ') ? line.arg : `Create ${line.arg}`)) complain(line, 'no such Entity > Create command');
		} else if (line.command === 'add' || line.command === 'expectcomponents') {
			const names = line.command === 'add' ? [line.arg] : split(line.arg.slice(line.arg.indexOf('=') + 1), ',');
			for (const name of names) if (flat(name) !== 'transform' && !component(name)) complain(line, `no component called ${name}`);
		} else if (line.command === 'set' || line.command === 'expectvalue') {
			const parts = split(line.arg, '=')[0].split('.');
			// "set: Component.Field", "expectvalue: Entity.Component.Field".
			const [comp, field] = line.command === 'set' ? [parts[0], parts[1]] : [parts[1], parts[2]];
			const page = comp && flat(comp) !== 'transform' && component(comp);
			if (comp && flat(comp) !== 'transform' && !page) complain(line, `no component called ${comp}`);
			else if (page && field && !page.toLowerCase().includes('`' + flat(field) + '`:') && !page.toLowerCase().includes('`' + field.toLowerCase().replace(/\s+/g, '') + '`:')) {
				complain(line, `${comp} has no property ${field}`);
			}
		}
	}
	return problems;
}

// ----- The page -----

const escape = (text) => text.replace(/[{}<>]/g, (c) => ({ '{': '&#123;', '}': '&#125;', '<': '&lt;', '>': '&gt;' })[c]);
const code = (text) => '`' + text.replace(/`/g, "'") + '`';
const arrows = (text) => split(text, '>').map((s) => `**${escape(s)}**`).join(' → ');

// A command that is something the reader does, in words; null for the ones that only check or show.
function stepText(line) {
	const a = line.arg;
	switch (line.command) {
		case 'step':
			return escape(a);
		case 'menu':
			return `Choose ${arrows(a)}.`;
		case 'create':
			return `Choose **Entity** → **Create ${escape(a.replace(/^Create /, ''))}**.`;
		case 'select':
			return `Select **${escape(a)}** in the Hierarchy.`;
		case 'rename':
			return `Rename it **${escape(a)}** (F2).`;
		case 'set': {
			const at = a.indexOf('=');
			const path = a.slice(0, at).trim().split('.').map(humanize);
			return `In the Inspector, set **${escape(path.join(' → '))}** to ${code(a.slice(at + 1).trim())}.`;
		}
		case 'add':
			return `Click **Add Component** and add **${escape(humanize(a))}**.`;
		case 'key':
			return `Press **${escape(a)}**.`;
		case 'play':
			return 'Press **F5** to play.';
		case 'stop':
			return 'Press **Shift+F5** to stop.';
		case 'tap':
			return `In the game, press **${escape(a)}**.`;
		case 'hold': {
			const [keys, seconds] = split(a, ' ');
			return `In the game, hold **${escape(keys)}** for ${seconds} second${seconds === '1' ? '' : 's'}.`;
		}
		default:
			return null;
	}
}

function page(tutorial, lines) {
	const title = lines.find((l) => l.command === 'title')?.arg ?? tutorial.slug;
	const intro = lines.find((l) => l.command === 'intro')?.arg ?? '';
	const summary = lines.find((l) => l.command === 'summary')?.arg ?? intro;
	const imports = [];
	const body = [];
	let steps = [];
	let chapter = title;
	const flush = () => {
		if (steps.length) body.push(steps.map((s, i) => `${i + 1}. ${s}`).join('\n'));
		steps = [];
	};

	for (const line of lines) {
		if (line.command === 'project') {
			const [template, name] = split(line.arg);
			flush();
			body.push(`Make a project: **File** → **New Project** (Ctrl+Shift+N), name it **${escape(name ?? template)}**, pick the **${escape(template)}** template and **Create**.`);
		} else if (line.command === 'chapter') {
			flush();
			chapter = split(line.arg)[0];
			body.push(`## ${escape(chapter)}`);
		} else if (line.command === 'say') {
			flush();
			body.push(escape(line.arg));
		} else if (line.command === 'explain') {
			flush();
			const [, label, text] = split(line.arg);
			body.push(`**${escape(label)}.** ${escape(text)}`);
		} else if (line.command === 'shot') {
			flush();
			const [name, view, , alt] = split(line.arg);
			const id = `shot${imports.length}`;
			imports.push(`import ${id} from '../../../assets/tutorials/${tutorial.slug}/${name}.webp';`);
			body.push(`<Shot src={${id}} alt=${JSON.stringify(alt ?? `${chapter} (${view === 'game' ? 'the game' : 'the editor'})`)} />`);
		} else if (line.command === 'agent') {
			flush();
			const space = line.arg.search(/\s/);
			const tool = space < 0 ? line.arg : line.arg.slice(0, space);
			const json = space < 0 ? '{}' : line.arg.slice(space).trim();
			let pretty = json;
			try {
				pretty = JSON.stringify(JSON.parse(json), null, 2);
			} catch {}
			body.push('```json title="' + tool + '"\n' + pretty + '\n```');
		} else {
			const text = stepText(line);
			if (text) steps.push(text);
		}
	}
	flush();

	// Pictures need the Shot component, so the page is MDX.
	const front = `---\ntitle: ${JSON.stringify(title)}\ndescription: ${JSON.stringify(summary)}\n---\n`;
	const note = `{/* Written by scripts/tutorials.mjs from tutorials/${basename(tutorial.file)}. Edit that, not this. */}`;
	return `${front}\n${note}\n\nimport Shot from '../../../components/Shot.astro';\n${imports.join('\n')}\n\n${intro ? escape(intro) + '\n\n' : ''}${body.join('\n\n')}\n`;
}

async function pages() {
	const problems = [];
	const built = [];
	for (const tutorial of tutorials()) {
		if (!existsSync(tutorial.file)) {
			problems.push(`${tutorial.file} is missing.`);
			continue;
		}
		const lines = parse(tutorial.file);
		problems.push(...lint(tutorial, lines));
		for (const line of lines.filter((l) => l.command === 'shot')) {
			const picture = join(shotsDir, tutorial.slug, `${split(line.arg)[0]}.webp`);
			if (!existsSync(picture)) problems.push(`${basename(tutorial.file)} line ${line.number}: ${picture} is missing. Run \`npm run tutorials -- --only ${tutorial.slug}\`.`);
		}
		built.push({ tutorial, lines });
	}
	if (problems.length) {
		console.error('The tutorials no longer match the editor (or have no pictures yet):\n  ' + problems.join('\n  '));
		process.exit(1);
	}

	await rm(pagesDir, { recursive: true, force: true });
	await mkdir(pagesDir, { recursive: true });
	for (const { tutorial, lines } of built) await writeFile(join(pagesDir, `${tutorial.slug}.mdx`), page(tutorial, lines));
	const rows = built.map(({ tutorial, lines }) => {
		const title = lines.find((l) => l.command === 'title').arg;
		const summary = lines.find((l) => l.command === 'summary')?.arg ?? lines.find((l) => l.command === 'intro')?.arg ?? '';
		return `- [${title}](/tutorials/${tutorial.slug}/): ${summary}`;
	});
	const intro =
		"Each tutorial builds something small, step by step. They are played in the real editor by the same player that runs Griff's demos, which also takes every picture on these pages, so the steps and screenshots are what the editor does today. New to Griff? Start with [Getting started](/docs/getting-started/), then pick one.";
	await writeFile(join(pagesDir, 'index.md'), `---\ntitle: Tutorials\ndescription: Step-by-step tutorials that build a small game in Griff's editor.\n---\n\n${intro}\n\n${rows.join('\n')}\n`);
	console.log(`Wrote ${built.length} tutorial pages.`);
}

// ----- Playing them -----

async function run() {
	const only = option('only', '')?.split(',').filter(Boolean) ?? [];
	const sharp = (await import('sharp')).default;
	const list = tutorials().filter((t) => only.length === 0 || only.includes(t.slug));
	if (list.length === 0) throw new Error(`No tutorial called ${only.join(', ')}.`);
	const failed = [];
	for (const tutorial of list) {
		const shots = await mkdtemp(join(tmpdir(), 'griff-tutorial-'));
		const demo = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', join(repo, 'tools', 'demo.ps1'), tutorial.file, '-Check'];
		if (args.includes('--no-build')) demo.push('-NoBuild');
		const env = { ...process.env, GRIFF_DEMO_SHOTS: shots, GRIFF_MUTE: '1' };
		const port = option('port', process.env.GRIFF_MCP_PORT);
		if (port) env.GRIFF_MCP_PORT = port;
		console.log(`Playing ${basename(tutorial.file)}...`);
		const result = spawnSync('powershell', demo, { env, stdio: 'inherit' });
		if (result.status !== 0) {
			failed.push(tutorial.slug);
			continue;
		}
		const out = join(shotsDir, tutorial.slug);
		await rm(out, { recursive: true, force: true });
		await mkdir(out, { recursive: true });
		const wanted = parse(tutorial.file)
			.filter((l) => l.command === 'shot')
			.map((l) => split(l.arg));
		for (const [name, view, width] of wanted) {
			const png = join(shots, `${name}.png`);
			if (!existsSync(png)) throw new Error(`${basename(tutorial.file)}: the run saved no picture called ${name}.`);
			let image = sharp(await readFile(png));
			// The editor's pictures are shrunk to 1600 wide at most; a game's keep their pixels. Lossless WebP keeps pixel art
			// and the editor's text sharp at a fraction of a PNG's size.
			if (!view || view === 'editor') image = image.resize({ width: Number(width) || 1600, withoutEnlargement: true, kernel: 'lanczos3' });
			const bytes = await image.webp({ lossless: true, effort: 6 }).toBuffer();
			await writeFile(join(out, `${name}.webp`), bytes);
			console.log(`  ${tutorial.slug}/${name}.webp  ${(bytes.length / 1024).toFixed(0)} KB`);
		}
		await rm(shots, { recursive: true, force: true });
	}
	if (failed.length) {
		console.error(`Out of date: ${failed.join(', ')}. The editor no longer does what the tutorial says; see the log in %TEMP%\\griff-demos\\demo.log.`);
		process.exit(1);
	}
	console.log(`${list.length} tutorials still match the editor.`);
}

if (mode === 'pages') await pages();
else if (mode === 'run') await run();
else throw new Error(`Unknown mode ${mode}: pages or run.`);
