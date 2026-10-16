// Griff's releases for the Releases page, read from GitHub when the site is built. Nothing here may fail the build:
// before the first release, offline, or when GitHub answers with anything else, the list is empty and the page says so.
//
// GRIFF_RELEASES_FILE=<json> reads a saved copy of the API's answer instead (for trying the page offline). In CI,
// GITHUB_TOKEN raises the API's rate limit; the repository is public, so it needs no permission for this.
import { readFile } from 'node:fs/promises';

export const repo = 'srakowski/griff';
export const repoUrl = `https://github.com/${repo}`;
/**
 * Every release on GitHub, pre-releases too. (GitHub's releases/latest skips pre-releases, so it can't be the newest
 * while Griff is pre-alpha.)
 */
export const releasesUrl = `${repoUrl}/releases`;

// A release tag: v0.1.0, or a pre-release's, v0.1.0-alpha.1.
const releaseTag = /^v\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$/;

let cached;

/**
 * Published releases and pre-releases, newest first: { tag, version, name, date, prerelease, url, notes (HTML), installer,
 * portable, assets }. installer is the Setup.exe (installs Griff and keeps it up to date) and portable the zip, each
 * { name, url, size } or null; assets is those two, the installer first. (A release's other files, the installed editor's
 * update packages and feed, aren't for downloading by hand.) Drafts never show; neither does a release with nothing to
 * download.
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
			.filter((r) => !r.draft && releaseTag.test(r.tag_name ?? ''))
			.map((r) => {
				const files = (r.assets ?? []).map((a) => ({ name: a.name, url: a.browser_download_url, size: a.size }));
				const installer = files.find((a) => a.name.endsWith('-Setup.exe')) ?? null;
				const portable = files.find((a) => a.name.endsWith('.zip')) ?? null;
				return {
					tag: r.tag_name,
					version: r.tag_name.slice(1),
					name: r.name || `Griff ${r.tag_name.slice(1)}`,
					date: (r.published_at ?? r.created_at ?? '').slice(0, 10),
					prerelease: !!r.prerelease,
					url: r.html_url,
					notes: r.body_html ?? '',
					installer,
					portable,
					assets: [installer, portable].filter(Boolean),
				};
			})
			.filter((r) => r.assets.length > 0)
			.sort((a, b) => b.date.localeCompare(a.date) || b.tag.localeCompare(a.tag, 'en', { numeric: true }));
	} catch (error) {
		console.warn(`Releases: not read (${error instanceof Error ? error.message : error}); the page will point to GitHub.`);
		return [];
	}
}
