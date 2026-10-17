// The changelog the Releases page shows, read from changelog.md (tracked beside this file; tools/changelog.ps1 adds each
// release's entry). Returns [{ title, version, date, bullets: [html] }], newest first, "Unreleased" (version null) first.
import changelog from './changelog.md?raw';

const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const inline = (text) => escape(text).replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');

export function getChangelog() {
	const text = changelog.replace(/<!--[\s\S]*?-->/g, '');
	const sections = [];
	for (const line of text.split(/\r?\n/)) {
		const heading = /^##\s+(.+?)\s*$/.exec(line);
		if (heading) {
			const [, version, date] = /^(\S+)(?:\s+-\s+(\S+))?$/.exec(heading[1]) ?? [, heading[1], null];
			const unreleased = version.toLowerCase() === 'unreleased';
			sections.push({ title: unreleased ? 'Unreleased' : version, version: unreleased ? null : version, date: date ?? null, bullets: [] });
		} else if (/^-\s+/.test(line) && sections.length) {
			sections.at(-1).bullets.push(inline(line.replace(/^-\s+/, '')));
		}
	}
	return sections;
}
