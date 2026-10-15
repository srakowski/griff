// Takes the site's screenshots from a running Griff editor, over its MCP server, and saves them optimized to
// src/assets/screens/. See README.md ("Screenshots") for how to run it.
//
//   node scripts/screenshots.mjs [--port 7650] [--only editor,match-3]
//
// It makes a project from each template in a temporary folder, plays it, and saves what the editor_screenshot tool
// shows. The editor must be open and on screen (games only run while it is), with nothing else in its way.

import { mkdtemp, mkdir, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const args = process.argv.slice(2);
const option = (name, fallback) => {
	const i = args.indexOf(`--${name}`);
	return i >= 0 ? args[i + 1] : fallback;
};
const port = Number(option('port', process.env.GRIFF_MCP_PORT ?? 7650));
const only = option('only', '')?.split(',').filter(Boolean) ?? [];
const url = `http://127.0.0.1:${port}/mcp`;
const out = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'assets', 'screens');

// Each shot: the template to start from, what to do before the picture, and the picture.
//   view: "editor" (the whole editor window) or "game" (the game as the player sees it).
//   before: editor tools to call first ([name, arguments] pairs), to set the scene up; {assets} is the project's Assets folder.
//   play: seconds to play first; keys: what to press while playing (game_input steps), e.g. Enter past a menu.
//   width: the saved width (editor shots are shrunk; game shots keep their pixels).
const shots = [
	{ name: 'editor', template: 'Platformer', view: 'editor', width: 1600, before: [['Entity_Hide', { entity: 'Paused' }], ['select', { entities: ['Player'] }]] },
	{ name: 'editor-tilemap', template: 'Isometric', view: 'editor', width: 1600, before: [['Entity_Hide', { entity: 'Paused' }], ['select', { entities: ['Courtyard'] }]] },
	{ name: 'editor-script', template: 'Arcade', view: 'editor', width: 1600, before: [['ui_invoke', { target: 'ScriptEditors', path: 'Open', args: ['{assets}/Scripts/Ship.cs', 1] }]] },
	{ name: 'isometric', template: 'Isometric', view: 'game', play: 1 },
	{ name: 'match-3', template: 'Match-3', view: 'game', play: 1, keys: [{ tap: ['Enter'] }, { wait: 1 }] },
	{ name: 'strategy', template: 'Strategy', view: 'game', play: 2 },
	{ name: 'shooter', template: 'Shooter', view: 'game', play: 1.5 },
	{ name: 'sandbox', template: 'Sandbox', view: 'game', play: 1.5 },
	{ name: 'tactics', template: 'Tactics', view: 'game', play: 1, keys: [{ tap: ['Enter'] }, { wait: 1 }] },
	{ name: 'roguelike', template: 'Roguelike', view: 'game', play: 1, keys: [{ tap: ['Enter'] }, { wait: 1 }] },
	{ name: 'falling-blocks', template: 'Falling Blocks', view: 'game', play: 0.5, keys: [{ tap: ['Enter'] }, { wait: 1 }, { tap: ['Left'] }, { tap: ['Left'] }, { tap: ['Space'] }, { wait: 0.5 }, { tap: ['Right'] }, { tap: ['Right'] }, { tap: ['Space'] }, { wait: 0.5 }, { tap: ['Up'] }, { tap: ['Space'] }, { wait: 1 }] },
];

let nextId = 1;
async function rpc(method, params) {
	const response = await fetch(url, {
		method: 'POST',
		headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
		body: JSON.stringify({ jsonrpc: '2.0', id: nextId++, method, params }),
	});
	if (!response.ok) throw new Error(`${method}: HTTP ${response.status} ${await response.text()}`);
	const answer = await response.json();
	if (answer.error) throw new Error(`${method}: ${answer.error.message}`);
	return answer.result;
}

async function tool(name, args = {}) {
	const result = await rpc('tools/call', { name, arguments: args });
	const text = result.content.filter((c) => c.type === 'text').map((c) => c.text).join('\n');
	if (result.isError) throw new Error(`${name}: ${text}`);
	return { text, image: result.content.find((c) => c.type === 'image')?.data };
}

const sleep = (seconds) => new Promise((done) => setTimeout(done, seconds * 1000));

// Makes a project from the template in the folder and returns its Assets folder.
async function newProject(template, folder) {
	// Project_New opens the New Project window; fill it in and create, as a person would.
	const open = await tool('ui_inspect', { target: 'New Project' }).then(() => true, () => false);
	if (!open) await tool('Project_New');
	const name = template.replace(/[^A-Za-z0-9]/g, '');
	await tool('ui_set', { target: 'New Project', path: 'Name', value: name });
	await tool('ui_set', { target: 'New Project', path: 'Location', value: folder });
	await tool('ui_set', { target: 'New Project', path: 'SelectedTemplate', value: template });
	await tool('ui_invoke', { target: 'New Project', path: 'CreateCommand', answers: { 'Save changes': false }, wait: 60 });
	// Creating goes on after the window closes: wait for the project's start scene to be open.
	for (let i = 0; i < 120; i++) {
		const { text } = await tool('editor_state');
		if (text.includes(folder) && /Open scene: .*, [1-9]\d* entities/.test(text)) return join(folder, name, 'Assets').replaceAll('\\', '/');
		await sleep(0.5);
	}
	throw new Error(`The ${template} project didn't open.`);
}

async function save(shot, base64) {
	let image = sharp(Buffer.from(base64, 'base64'));
	if (shot.width) image = image.resize({ width: shot.width, withoutEnlargement: true, kernel: 'lanczos3' });
	// Lossless WebP keeps pixel art and the editor's text sharp, at a fraction of a PNG's size.
	const bytes = await image.webp({ lossless: true, effort: 6 }).toBuffer();
	await writeFile(join(out, `${shot.name}.webp`), bytes);
	const { width, height } = await sharp(bytes).metadata();
	console.log(`  ${shot.name}.webp  ${width}x${height}  ${(bytes.length / 1024).toFixed(0)} KB`);
}

await rpc('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'griff-site-screenshots', version: '1' } });
await mkdir(out, { recursive: true });
const projects = await mkdtemp(join(tmpdir(), 'griff-site-shots-'));
console.log(`Griff's editor on port ${port}; projects in ${projects}`);
for (const shot of shots.filter((s) => only.length === 0 || only.includes(s.name))) {
	const assets = await newProject(shot.template, join(projects, shot.name));
	if (shot.view === 'editor') {
		// The Game tab can be left in front (closing a script tab leaves it there); stopping a game brings the Scene back.
		await tool('Game_Play');
		await tool('Game_Stop');
	}
	for (const [name, args] of shot.before ?? []) await tool(name, JSON.parse(JSON.stringify(args).replaceAll('{assets}', assets)));
	if (shot.play) {
		await tool('Game_Play');
		if (shot.keys) await tool('game_input', { steps: shot.keys });
		await sleep(shot.play);
	}
	const { image } = await tool('editor_screenshot', { view: shot.view, max_width: 4096 });
	if (!image) throw new Error(`${shot.name}: editor_screenshot gave no picture`);
	await save(shot, image);
	if (shot.play) await tool('Game_Stop');
}
// The last project stays open in the editor, so its folder is left for Windows to clear from Temp.
console.log(`Done. The projects are in ${projects}.`);
