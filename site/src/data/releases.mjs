// Griff's releases for the Releases page, read from GitHub when the site is built. Nothing here may fail the build:
// before the first release, offline, or when GitHub answers with anything else, the list is empty and the page says so.
//
// GRIFF_RELEASES_FILE=<json> reads a saved copy of the API's answer instead (for trying the page offline). In CI,
// GITHUB_TOKEN raises the API's rate limit; the repository is public, so it needs no permission for this.
import { readFile } from 'node:fs/promises';

export const repo = 'srakowski/griff';
export const repoUrl = `https://github.com/${repo}`;
/** GitHub redirects this to the newest release, or to the releases page while there is none. */
export const latestUrl = `${repoUrl}/releases/latest`;

let cached;

/**
 * Published releases, newest first: { tag, version, name, date, prerelease, url, notes (HTML), assets: [{ name, url, size }] }.
 * Drafts never show; neither does a release with no files to download.
 */
export function getReleases() {
	cached ??= load();
	return cached;
}

async function load() {
	try {
		let raw;
		if (process.env.GRIFF_RELEASES_FILE) {
			raw = JSON.parse(await readFile(process.env.GRIFF_RELEASES_FILE, 'utf8'));
		} else {
			const headers = { Accept: 'application/vnd.github.html+json', 'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'griff-site' };
			if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
			const response = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=30`, { headers, signal: AbortSignal.timeout(15000) });
			if (!response.ok) throw new Error(`GitHub answered ${response.status}`);
			raw = await response.json();
		}
		if (!Array.isArray(raw)) throw new Error('the answer is not a list');

		return raw
			.filter((r) => !r.draft && /^v\d+\.\d+\.\d+$/.test(r.tag_name ?? ''))
			.map((r) => ({
				tag: r.tag_name,
				version: r.tag_name.slice(1),
				name: r.name || `Griff ${r.tag_name.slice(1)}`,
				date: (r.published_at ?? r.created_at ?? '').slice(0, 10),
				prerelease: !!r.prerelease,
				url: r.html_url,
				notes: r.body_html ?? '',
				assets: (r.assets ?? []).map((a) => ({ name: a.name, url: a.browser_download_url, size: a.size })),
			}))
			.filter((r) => r.assets.length > 0)
			.sort((a, b) => b.date.localeCompare(a.date) || b.tag.localeCompare(a.tag, 'en', { numeric: true }));
	} catch (error) {
		console.warn(`Releases: not read (${error instanceof Error ? error.message : error}); the page will point to GitHub.`);
		return [];
	}
}
