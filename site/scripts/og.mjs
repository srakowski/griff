// Renders the social card (public/og.png, 1200x630) and the apple-touch-icon from the logo, the wordmark font and the tagline's (Atkinson Hyperlegible, one of Griff's free fonts), in
// headless Edge or Chrome, and saves them optimized. `npm run og`. Commit the results.
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import sharp from 'sharp';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const pub = join(root, 'public');
const browser = [
	process.env.BROWSER,
	'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
	'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
	'C:/Program Files/Google/Chrome/Application/chrome.exe',
	'/usr/bin/google-chrome',
	'/usr/bin/chromium',
].find((p) => p && existsSync(p));
if (!browser) throw new Error('No Edge or Chrome found; set BROWSER to its path.');

const font = pathToFileURL(join(pub, 'fonts', 'JetBrainsMono-ExtraBold.woff2')).href;
// The tagline's font is one of the free fonts Griff ships (SIL OFL, unmodified), never one of the computer's own.
const tagFont = pathToFileURL(join(root, '..', 'src', 'Griff.Fonts', 'Free', 'AtkinsonHyperlegible-Bold.ttf')).href;
const logo = pathToFileURL(join(pub, 'griff.png')).href;
const html = `<!doctype html><meta charset="utf-8"><style>
@font-face { font-family: 'JetBrains Mono'; src: url('${font}') format('woff2'); font-weight: 800; }
@font-face { font-family: 'Atkinson Hyperlegible'; src: url('${tagFont}') format('truetype'); font-weight: 700; }
* { margin: 0; box-sizing: border-box; }
body { width: 1200px; height: 630px; background: #16171b; color: #fff; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 36px; font-family: 'JetBrains Mono', monospace; position: relative; overflow: hidden; }
body::before { content: ''; position: absolute; inset: 0; background: radial-gradient(ellipse at 50% 40%, #2a1b0e 0%, #16171b 65%); }
.row { display: flex; align-items: center; gap: 48px; position: relative; }
img { width: 256px; height: 256px; image-rendering: pixelated; }
.word { font-weight: 800; letter-spacing: -0.03em; font-size: 240px; line-height: 1; }
.tag { position: relative; font-family: 'Atkinson Hyperlegible'; font-weight: 700; font-size: 44px; color: #f5a453; }
.bar { position: absolute; left: 0; right: 0; bottom: 0; height: 12px; background: #d4761c; }
</style><body><div class="row"><img src="${logo}"><span class="word">griff</span></div>
<div class="tag">A game engine for game jams</div><div class="bar"></div></body>`;

const dir = await mkdtemp(join(tmpdir(), 'griff-og-'));
try {
	const page = join(dir, 'card.html');
	const shot = join(dir, 'card.png');
	await writeFile(page, html);
	const r = spawnSync(browser, ['--headless=new', '--disable-gpu', '--hide-scrollbars', '--allow-file-access-from-files',
		'--force-device-scale-factor=1', '--window-size=1200,630', '--virtual-time-budget=3000', `--screenshot=${shot}`,
		`--user-data-dir=${join(dir, 'profile')}`, pathToFileURL(page).href], { stdio: 'inherit' });
	if (r.status !== 0 || !existsSync(shot)) throw new Error('The browser did not produce a screenshot.');
	await sharp(shot).resize(1200, 630).png({ compressionLevel: 9, palette: true, quality: 90, effort: 10, dither: 0.5 }).toFile(join(pub, 'og.png'));
} finally {
	await rm(dir, { recursive: true, force: true });
}
await sharp(join(pub, 'griff.png')).resize(180, 180, { kernel: 'nearest' }).png({ compressionLevel: 9 }).toFile(join(pub, 'apple-touch-icon.png'));
console.log('Wrote public/og.png and public/apple-touch-icon.png');
