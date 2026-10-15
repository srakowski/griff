# griff.games

Griff's website: an [Astro Starlight](https://starlight.astro.build) site, built by the Site workflow
(`.github/workflows/site.yml`) and deployed to GitHub Pages on every push to main, at https://griff.games.

```
astro.config.mjs        The site: title, header, sidebar, the theme's CSS and component overrides
src/content/docs/       The pages: index.mdx is the landing page, docs/ the guides (Markdown, one page a file),
                        reference/ the generated reference (git-ignored; see Reference below)
src/components/         The header's logo and wordmark, the footer, the landing page's cards and screenshots
src/styles/griff.css    Griff's colours in light and dark, the wordmark's font, the landing page's layout
src/assets/screens/     The screenshots (made by scripts/screenshots.mjs; see Screenshots below)
public/                 Served as they are: the logo, the wordmark's font and its license, CNAME
scripts/                check-links.mjs and screenshots.mjs
```

The tool that writes `reference/` is `tools/docs.cs` in the repository's root.

It depends on Astro and Starlight only (and what they bring: `sharp`, which Astro uses for images, makes the
screenshots). The wordmark is JetBrains Mono ExtraBold, served from `public/fonts/` with its license
(`JetBrainsMono-OFL.txt`, the SIL Open Font License); nothing is loaded from another site.

## Preview it

With Node 24 (and npm 11) and the .NET SDK `global.json` pins (Windows), in this folder:

```sh
npm ci                 # once, and after package-lock.json changes
npm run reference      # the reference pages, written from the code (once, and when components or commands change)
npm run dev            # http://127.0.0.1:4321/, reloading as you edit
npm run build          # the site, into dist/ (the reference has to be there: it says so if not)
npm run build:all      # npm run reference, then npm run build
npm run preview        # dist/ at http://127.0.0.1:4321/
npm run check-links    # after a build: every link inside the site has to lead somewhere
```

The servers listen on 127.0.0.1 only. `npm run check-links` checks every `href`, `src` and `srcset` on every built page
that points inside the site (and `#fragments`), and fails if one leads nowhere; the Site workflow runs it before
deploying. `node scripts/check-links.mjs --external` also asks the servers of the links that leave the site (the
GitHub links only answer once the repository is public).

Guides are Markdown files in `src/content/docs/docs/`; a new one goes in the sidebar in `astro.config.mjs`.

## Reference

Three sets of pages are written from the code, so a new component or command shows up by itself and none of it is
typed by hand or kept in git (`src/content/docs/reference/` is git-ignored):

- `reference/components/`: every component, a page each, by category. A page is `component_describe`'s text for it
  (`ComponentDescription` in `src/Griff/Agent`, from `ComponentRegistry` and the `///` comments), so the site and agents
  read the same words.
- `reference/commands.md`: every editor command by menu, with its id, shortcut and `///` summary (the
  `CommandRegistry` the MCP server turns into tools).
- `reference/api/`: the scripting API, a page a public type of the `Griff`, `Griff.Input`, `Griff.Runtime`,
  `Griff.Scenes` and `Griff.Modules.*` namespaces, with its public and protected members and their `///` comments.

`npm run reference` runs `tools/docs.cs` (`dotnet run -p:SkipPlayers=true tools/docs.cs -- --out <folder>`), which
builds the editor's real services the way `tests/Griff.Editor.Tests` does, so it runs on Windows. The Site workflow
does it in a `reference` job on a Windows runner (set up with `global.json`'s SDK), passes the pages on as an artifact
to the `build` job, and the link check then covers them with the guides.

## Screenshots

The pictures on the landing page are real: `scripts/screenshots.mjs` drives Griff's editor over its MCP server, makes a
project from each template in a temporary folder, plays it, and saves what the `editor_screenshot` tool shows to
`src/assets/screens/` as lossless WebP (editor shots shrunk to 1600 wide at most, game shots at their own size), so
pixel art and the editor's text stay sharp at a fraction of a PNG's size. To take them again:

1. Build the editor (`dotnet build src/Griff.Editor/Griff.Editor.csproj` from the repository's root) and start it,
   on screen and with nothing over it: games only run while the editor is showing. With other editors open, give it a
   port of its own and settings of its own, so its window starts at the usual size:

   ```powershell
   $env:GRIFF_MCP_PORT = '7711'; $env:GRIFF_SETTINGS_PATH = "$env:TEMP\griff-site-settings.json"
   src\Griff.Editor\bin\Debug\net10.0-windows\Griff.Editor.exe
   ```

2. In `site/`: `npm run screenshots -- --port 7711` (the default port is 7650, or `GRIFF_MCP_PORT`). It takes about
   two minutes; `--only editor,match-3` retakes some. Leave the mouse and keyboard alone while it runs.
3. Look at them, `npm run build`, and commit `src/assets/screens/`.

Which shots there are, and what each does first (the entity it selects, the keys it presses), is the `shots` list at
the top of the script. A new shot needs a line there and a place on the landing page (`src/content/docs/index.mdx`).

## Setting up griff.games (for Shawn)

Nothing below can be done from the repository: these are the steps on GitHub and GoDaddy, once. Until the first two
are done, the Site workflow's deploy job fails (its build and link check still run).

1. **Push** main to GitHub: `git push origin main`.
2. **Make the repository public** (Settings → General → Danger Zone → Change visibility), if it isn't yet. It's an
   open-source site, and GitHub Pages only publishes from a private repository on a paid plan.
3. **Verify the domain** (recommended before pointing DNS at GitHub, so no one else can claim it): your profile
   picture → **Settings** → **Pages** → **Add a domain** → `griff.games`. GitHub shows a TXT record: add it in
   GoDaddy (step 6) with the host `_github-pages-challenge-srakowski` and the value GitHub gives, then click
   **Verify**. Keep the TXT record afterwards.
4. **Pages from GitHub Actions**: the repository's **Settings** → **Pages** → **Build and deployment** → **Source**:
   **GitHub Actions**. Then run the Site workflow (**Actions** → **Site** → **Run workflow**, or push again) and check
   that it deploys.
5. **Custom domain**: the same Pages settings → **Custom domain**: `griff.games` → **Save**. (`public/CNAME` holds the
   domain too, but with Actions it's this setting that counts.) GitHub checks the DNS from step 6; it may say it's
   unchecked until the records are in.
6. **GoDaddy DNS** (My Products → griff.games → **DNS** → **DNS Records**). First delete GoDaddy's own `A` record for
   `@` (the "Parked" one) and any `CNAME` for `www`, and turn off domain forwarding if it's on. Then add:

   | Type  | Name                               | Value                         | TTL    |
   | ----- | ---------------------------------- | ----------------------------- | ------ |
   | A     | @                                  | 185.199.108.153               | 1 hour |
   | A     | @                                  | 185.199.109.153               | 1 hour |
   | A     | @                                  | 185.199.110.153               | 1 hour |
   | A     | @                                  | 185.199.111.153               | 1 hour |
   | AAAA  | @                                  | 2606:50c0:8000::153           | 1 hour |
   | AAAA  | @                                  | 2606:50c0:8001::153           | 1 hour |
   | AAAA  | @                                  | 2606:50c0:8002::153           | 1 hour |
   | AAAA  | @                                  | 2606:50c0:8003::153           | 1 hour |
   | CNAME | www                                | srakowski.github.io           | 1 hour |
   | TXT   | _github-pages-challenge-srakowski  | (the value from step 3)       | 1 hour |

   These are GitHub's addresses for Pages as its docs give them ("Managing a custom domain for your GitHub Pages site",
   checked 2026-10-14). The `www` CNAME points at `srakowski.github.io`, without the repository's name; GitHub sends
   `www.griff.games` on to `griff.games`.
7. **Check the DNS** once it has spread (minutes, up to a day). In PowerShell:

   ```powershell
   Resolve-DnsName griff.games -Type A          # the four 185.199.x.153 addresses
   Resolve-DnsName griff.games -Type AAAA       # the four 2606:50c0:800x::153 addresses
   Resolve-DnsName www.griff.games -Type CNAME  # srakowski.github.io
   Resolve-DnsName _github-pages-challenge-srakowski.griff.games -Type TXT
   ```

   or with `dig` (Git Bash, macOS, Linux): `dig griff.games +noall +answer -t A`, the same with `-t AAAA`, and
   `dig www.griff.games +nostats +nocomments +nocmd`. Nothing else (a GoDaddy parking address) should answer.
8. **HTTPS**: in the repository's Pages settings, once the DNS check passes, tick **Enforce HTTPS**. GitHub needs a
   certificate first, which can take up to 24 hours after the domain is set.
9. Open https://griff.games and https://www.griff.games (which should land on the first), and try the search, the
   theme switch and the Download and GitHub buttons.
