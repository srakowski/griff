// @ts-check
import { existsSync } from 'node:fs';
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

const repo = 'https://github.com/srakowski/griff';

// The reference pages (components, commands, the scripting API) are written from the code by `npm run reference` and
// aren't in git: say so rather than failing on a sidebar link to a page that isn't there.
if (!existsSync(new URL('./src/content/docs/reference/components/index.md', import.meta.url))) {
	throw new Error('The reference pages are missing. Run `npm run reference` first (it needs the .NET SDK), or use `npm run build:all`.');
}

// griff.games: the landing page is src/content/docs/index.mdx, the docs are src/content/docs/docs/.
// See README.md for building, previewing, the link check and deploying.
export default defineConfig({
	site: 'https://griff.games',
	// The dev and preview servers stay on this machine.
	server: { host: '127.0.0.1', port: 4321 },
	integrations: [
		starlight({
			title: 'Griff',
			description: 'Griff is a game engine and editor for game jams: the shortest path from an idea to something people can play by Sunday night.',
			favicon: '/griff.png',
			social: [{ icon: 'github', label: 'GitHub', href: repo }],
			editLink: { baseUrl: `${repo}/edit/main/site/` },
			customCss: ['./src/styles/griff.css'],
			components: {
				SiteTitle: './src/components/SiteTitle.astro',
				Footer: './src/components/Footer.astro',
			},
			// Starlight emits og:title, og:type, og:url, og:site_name, og:description and twitter:card on every page;
			// the card image (npm run og) and the touch icon are ours.
			head: [
				{ tag: 'link', attrs: { rel: 'apple-touch-icon', href: '/apple-touch-icon.png' } },
				{ tag: 'meta', attrs: { property: 'og:image', content: 'https://griff.games/og.png' } },
				{ tag: 'meta', attrs: { property: 'og:image:type', content: 'image/png' } },
				{ tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
				{ tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
				{ tag: 'meta', attrs: { property: 'og:image:alt', content: 'The Pixel Griffin and the griff wordmark: a game engine for game jams' } },
				{ tag: 'meta', attrs: { name: 'twitter:image', content: 'https://griff.games/og.png' } },
				{ tag: 'meta', attrs: { name: 'twitter:image:alt', content: 'The Pixel Griffin and the griff wordmark: a game engine for game jams' } },
				{
					tag: 'link',
					attrs: { rel: 'preload', href: '/fonts/JetBrainsMono-ExtraBold.woff2', as: 'font', type: 'font/woff2', crossorigin: '' },
				},
			],
			sidebar: [
				{
					label: 'Start here',
					items: [
						{ label: 'What is Griff', slug: 'docs/what-is-griff' },
						{ label: 'Getting started', slug: 'docs/getting-started' },
						{ label: 'Releases', slug: 'releases' },
					],
				},
				{
					label: 'Tutorials',
					items: [
						{ label: 'All tutorials', slug: 'tutorials' },
						{ label: 'Your first platformer', slug: 'tutorials/first-platformer' },
						{ label: 'A top-down adventure', slug: 'tutorials/top-down-adventure' },
						{ label: 'A crate-pushing puzzle', slug: 'tutorials/puzzle' },
						{ label: 'Making a game with an agent', slug: 'tutorials/making-a-game-with-an-agent' },
						{ label: 'Balancing with data tables', slug: 'tutorials/balancing-with-tables' },
					],
				},
				{
					// One page a template, written by scripts/games.mjs into src/content/docs/games/ (the index first).
					label: 'Play',
					collapsed: true,
					items: [{ autogenerate: { directory: 'games' } }],
				},
				{
					label: 'Guides',
					items: [
						{ label: 'The editor', slug: 'docs/editor' },
						{ label: 'Scenes and components', slug: 'docs/scenes-and-components' },
						{ label: 'Tilemaps', slug: 'docs/tilemaps' },
						{ label: 'Players and movement', slug: 'docs/players' },
						{ label: 'Rooms, light and atmosphere', slug: 'docs/atmosphere' },
						{ label: 'Menus, HUDs and pickups', slug: 'docs/ui' },
						{ label: 'Health, enemies and combat', slug: 'docs/combat' },
						{ label: 'Puzzles and boards', slug: 'docs/puzzles' },
						{ label: 'Roguelikes, turns and tactics', slug: 'docs/turns' },
						{ label: 'Card games', slug: 'docs/cards' },
						{ label: 'Dialogue, switches and items', slug: 'docs/story' },
						{ label: 'Point-and-click adventures', slug: 'docs/adventures' },
						{ label: 'Visual novels', slug: 'docs/visual-novels' },
						{ label: 'Tables of game data', slug: 'docs/data-tables' },
						{ label: 'Audio', slug: 'docs/audio' },
						{ label: 'Text and fonts', slug: 'docs/text-and-fonts' },
						{ label: 'Strategy, shooters, worlds', slug: 'docs/genres' },
						{ label: 'Mixing genres', slug: 'docs/mashups' },
						{ label: 'Scripting', slug: 'docs/scripting' },
						{ label: 'Multiplayer', slug: 'docs/multiplayer' },
						{ label: 'Exporting', slug: 'docs/exporting' },
						{ label: 'Agents (MCP)', slug: 'docs/agents' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'Components', slug: 'reference/components' },
						{ label: 'Editor commands', slug: 'reference/commands' },
						{ label: 'Scripting API', slug: 'reference/api' },
					],
				},
				{
					label: 'About Griff',
					items: [
						{ label: 'Architecture', slug: 'docs/architecture' },
						{ label: 'Building Griff', slug: 'docs/building' },
						{ label: 'Known gaps', slug: 'docs/status' },
					],
				},
			],
		}),
	],
});
