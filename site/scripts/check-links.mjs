// Checks the built site (dist/) for broken links: every href, src and srcset in every page that points inside the site
// has to lead to a file that was built, and every #fragment to an id on that page. Run `npm run build` first.
//
//   node scripts/check-links.mjs              links inside the site (what CI runs)
//   node scripts/check-links.mjs --external   also asks every outside link's server (needs the network; the GitHub
//                                             links only answer once the repository is public)
//
// Links to https://griff.games/... count as inside the site. Exits with 1 if anything is broken.

import { readdir, readFile, stat } from 'node:fs/promises';
import { join, dirname, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const dist = join(dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const site = 'https://griff.games';
const external = process.argv.includes('--external');

async function* files(dir) {
	for (const entry of await readdir(dir, { withFileTypes: true })) {
		const path = join(dir, entry.name);
		if (entry.isDirectory()) yield* files(path);
		else yield path;
	}
}

const exists = (path) => stat(path).then((s) => s, () => null);

// The file a path inside the site is served from, as GitHub Pages serves it: a folder by its index.html.
async function served(path) {
	const file = join(dist, decodeURIComponent(path));
	const found = await exists(file);
	if (found?.isFile()) return file;
	if (found?.isDirectory() && (await exists(join(file, 'index.html')))) return join(file, 'index.html');
	if (!path.endsWith('/') && (await exists(file + '.html'))) return file + '.html';
	return null;
}

// The address a page is linked to by: /docs/x/index.html at /docs/x/, and 404.html at /404/.
const pretty = (page) => page.replace(/index.html$/, '').replace(/.html$/, '/');

const idCache = new Map();
async function ids(file) {
	if (!idCache.has(file)) {
		const html = await readFile(file, 'utf8');
		idCache.set(file, new Set([...html.matchAll(/\s(?:id|name)\s*=\s*["']([^"']+)["']/g)].map((m) => m[1])));
	}
	return idCache.get(file);
}

// Every link in a page: href and src, each URL in srcset, and url(...) in inline styles.
function links(html) {
	const found = [];
	const withoutScripts = html.replace(/<script\b[\s\S]*?<\/script>/gi, '');
	for (const m of withoutScripts.matchAll(/\s(href|src)\s*=\s*(?:"([^"]*)"|'([^']*)')/gi)) found.push(m[2] ?? m[3]);
	for (const m of withoutScripts.matchAll(/\ssrcset\s*=\s*(?:"([^"]*)"|'([^']*)')/gi))
		for (const candidate of (m[1] ?? m[2]).split(',')) found.push(candidate.trim().split(/\s+/)[0]);
	return found.filter(Boolean).map((link) => link.replaceAll('&amp;', '&'));
}

if (!(await exists(dist))) {
	console.error('No dist/ folder: run `npm run build` first.');
	process.exit(1);
}

const broken = [];
const outside = new Map();
let pages = 0;
let checked = 0;
for await (const file of files(dist)) {
	if (!file.endsWith('.html')) continue;
	pages++;
	const page = '/' + relative(dist, file).split(sep).join('/');
	for (const link of links(await readFile(file, 'utf8'))) {
		if (/^(mailto|tel|data|javascript|blob):/i.test(link)) continue;
		let url;
		try {
			url = new URL(link, site + page);
		} catch {
			broken.push(`${page}: ${link} (not a URL)`);
			continue;
		}
		if (url.origin !== site) {
			if (/^https?:$/.test(url.protocol)) outside.set(url.href.split('#')[0], page);
			continue;
		}
		checked++;
		// A page's link to itself (its canonical URL, or a #fragment on it) is to the file it's in: the 404 page's
		// canonical URL is /404/, which no file is.
		const self = url.pathname === page || url.pathname === pretty(page);
		const target = self ? file : await served(url.pathname);
		if (!target) {
			broken.push(`${page}: ${link} (nothing at ${url.pathname})`);
			continue;
		}
		const fragment = decodeURIComponent(url.hash.slice(1));
		if (fragment && target.endsWith('.html') && !(await ids(target)).has(fragment))
			broken.push(`${page}: ${link} (no #${fragment} on ${url.pathname})`);
	}
}

if (external) {
	for (const [href, page] of outside) {
		const ok = await fetch(href, { method: 'HEAD', redirect: 'follow' })
			.then((r) => (r.status === 405 || r.status === 403 ? fetch(href, { redirect: 'follow' }) : r))
			.then((r) => r.ok, () => false);
		if (!ok) broken.push(`${page}: ${href} (the server didn't answer OK)`);
	}
}

console.log(`Checked ${checked} links inside the site on ${pages} pages${external ? ` and ${outside.size} outside it` : ` (${outside.size} outside it not checked; --external checks them)`}.`);
if (broken.length > 0) {
	console.error(`${broken.length} broken:\n  ${broken.join('\n  ')}`);
	process.exit(1);
}
console.log('No broken links.');
