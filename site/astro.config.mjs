// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

const repo = 'https://github.com/srakowski/griff';

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
			head: [
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
						{ label: 'Agents (MCP)', slug: 'docs/agents' },
						{ label: 'Building Griff', slug: 'docs/building' },
					],
				},
			],
		}),
	],
});
