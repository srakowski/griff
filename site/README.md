# griff.games

Griff's website: an [Astro Starlight](https://starlight.astro.build) site, built by the Site workflow
(`.github/workflows/site.yml`) and deployed to GitHub Pages on every push to main, at https://griff.games.

```
astro.config.mjs        The site: title, header, sidebar, the theme's CSS and component overrides
src/content/docs/       The pages: index.mdx is the landing page, docs/ the guides (Markdown, one page a file),
                        reference/ the generated reference (git-ignored; see Reference below)
src/components/         The header's logo and wordmark, the footer, the landing page's cards and screenshots
src/data/releases.mjs   The releases, read from GitHub when the site is built, for the Releases page
src/styles/griff.css    Griff's colours in light and dark, the wordmark's font, the landing page's layout
src/assets/screens/     The screenshots (made by scripts/screenshots.mjs; see Screenshots below)
public/                 Served as they are: the logo, the wordmark's font and its license, CNAME, and play/ (the
                        playable games, git-ignored; see Playable games below)
src/data/games.mjs      The words around each playable game: its tagline, paragraph and controls
tutorials/              The tutorials: a .demo script each (and the files it writes into its project)
scripts/                check-links.mjs, games.mjs, screenshots.mjs, tutorials.mjs and og.mjs
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
npm run games          # the playable games, exported from the templates (once, and when templates or the web player change)
npm run dev            # http://127.0.0.1:4321/, reloading as you edit
npm run build          # the site, into dist/ (the reference has to be there: it says so if not)
npm run build:all      # npm run reference, npm run games, then npm run build
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
  (`ComponentDescription` in `src/Griff.Agent`, from `ComponentRegistry` and the `///` comments), so the site and agents
  read the same words.
- `reference/commands.md`: every editor command by menu, with its id, shortcut and `///` summary (the
  `CommandRegistry` the MCP server turns into tools).
- `reference/api/`: the scripting API, a page a public type of the `Griff`, `Griff.Input`, `Griff.Runtime`,
  `Griff.Scenes` and `Griff.Modules.*` namespaces, with its public and protected members and their `///` comments.

`npm run reference` runs `tools/docs.cs` (`dotnet run -p:SkipPlayers=true tools/docs.cs -- --out <folder>`), which
builds the editor's real services the way `tests/Griff.Editor.Tests` does, so it runs on Windows. The Site workflow
does it in a `reference` job on a Windows runner (set up with `global.json`'s SDK), passes the pages on as an artifact
to the `build` job, and the link check then covers them with the guides.

## Playable games

Every template (Empty aside) is playable on the site: a page each at `/games/<slug>/`, a gallery of them on the landing page
and on `/games/`, and the games themselves at `/play/<slug>/`. They are exported by `tools/web.ps1 -Site` (the web export,
see the exporting guide) into `public/play/`, which is git-ignored: nothing built is committed. `npm run games` makes them
here (Windows, the .NET SDK, Edge or Chrome; about five minutes; `-- --only Platformer,Match3` for some, `-- --port` for the
poster browser's server), and `npm run build` writes their pages (`scripts/games.mjs pages`, into
`src/content/docs/games/`, git-ignored) and fails if the games aren't built, or a template has no entry in
`src/data/games.mjs`, or that file names a template that's gone.

- **One copy of .NET.** The exports share `public/play/_player/` (main.js, griff.js and `_framework`, 28 MB, about 10.5 MB
  gzipped): each game's page starts the shared main.js, and its files are fetched relative to the page, so a visitor
  downloads .NET once and the browser keeps it for the next game. `--site` checks every export's .NET files are the same
  before sharing them. A game's own files are 3 to 8 MB (its sounds, music, scenes and compiled scripts).
- **Compression.** GitHub Pages compresses what it serves with gzip (checked: it answers `Content-Encoding: gzip`
  to a browser that accepts Brotli, for HTML, JavaScript and WebAssembly) and doesn't use precompressed `.br` files, so
  none are published; the export leaves them out.
- **Posters.** The gallery's pictures are `poster.webp`, a screenshot of each game running in the headless browser
  (960 x 540), so they match the game. A game on its page is its poster with a Play button; clicking it starts the game
  in a frame (so nothing downloads until someone plays, and the click lets the page make sound).
- **The words.** `src/data/games.mjs` holds each game's tagline, paragraph and controls. The page also says sound starts
  on click, that saves stay in the browser and, for a game set up for network play (`NetworkWarnings`), that network
  play isn't available on the web. A new template needs an entry there (the build says so).
- **CI.** The Site workflow's `games` job (Windows, like `reference`) runs `tools/web.ps1 -Site site/public/play` and
  uploads the folder as the `games` artifact; the `build` job (Ubuntu) downloads it before `npm run build`, so the deployed
  site always has the games of the commit it was built from.

## Screenshots

The editor pictures on the landing page are real (the games' pictures are the posters `npm run games` takes): `scripts/screenshots.mjs` drives Griff's editor over its MCP server, makes a
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

## Tutorials

The tutorials (`tutorials/*.demo`, listed in the order in `scripts/tutorials.mjs`) are scripts in the format of the
repository's demos (`demos/README.md`): the demo player runs each in the real editor, and the page for it and every
picture on it come from that script and run, so a tutorial can't describe something the editor no longer does.

- `say:`, `explain:` and `chapter:` are the page's paragraphs and headings; the commands in between (`menu`, `create`,
  `select`, `set`, `add`, `key`, `play`...) become its numbered steps; `step: <words>` is a step the player ignores (for a
  click only the player has an id for); `agent: <tool> {json}` makes a call through the editor's MCP server, shows it as a
  code block and fails if it fails (`expectagent: <text>` checks the answer); `shot: <name> | <view> | <width> | <alt>`
  saves a picture (`editor` by default, or `game`, `scene` or a window's title).
- `npm run build` (and `dev`) first writes the pages, `src/content/docs/tutorials/*.mdx` (git-ignored), and fails if a
  script names a menu item, component or property the generated reference doesn't have, or a picture is missing. That
  runs in the Site workflow too, so a rename in the editor can't ship a stale tutorial.
- `npm run tutorials` plays them all in the editor (`tools/demo.ps1 -Check`: silent, at speed, with the mouse
  and keyboard driven for real, so leave them alone and give it a port of its own, `-- --port 7711`) and fails if one no
  longer gets to its end. Each run replaces that tutorial's pictures in `src/assets/tutorials/` with new ones, shrunk
  to 1600 wide at most and saved as lossless WebP. `-- --only first-platformer,puzzle` plays some. Commit the pictures.

A change that alters what a tutorial shows (a menu item, a component or field, a flow) updates that script and plays it.

## Setting up griff.games (maintainer)

Nothing below can be done from the repository: these are the steps on GitHub and at the domain's DNS provider, once. Until the first two
are done, the Site workflow's deploy job fails (its build and link check still run).

1. **Push** main to GitHub: `git push origin main`.
2. **Make the repository public** (Settings → General → Danger Zone → Change visibility), if it isn't yet. It's an
   open-source site, and GitHub Pages only publishes from a private repository on a paid plan.
3. **Verify the domain** (recommended before pointing DNS at GitHub, so no one else can claim it): your profile
   picture → **Settings** → **Pages** → **Add a domain** → `griff.games`. GitHub shows a TXT record: add it in
   your DNS provider (step 6) with the host `_github-pages-challenge-srakowski` and the value GitHub gives, then click
   **Verify**. Keep the TXT record afterwards.
4. **Pages from GitHub Actions**: the repository's **Settings** → **Pages** → **Build and deployment** → **Source**:
   **GitHub Actions**. Then run the Site workflow (**Actions** → **Site** → **Run workflow**, or push again) and check
   that it deploys.
5. **Custom domain**: the same Pages settings → **Custom domain**: `griff.games` → **Save**. (`public/CNAME` holds the
   domain too, but with Actions it's this setting that counts.) GitHub checks the DNS from step 6; it may say it's
   unchecked until the records are in.
6. **DNS records** (in the DNS settings wherever griff.games is managed). First delete any existing `A` record for
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
   `dig www.griff.games +nostats +nocomments +nocmd`. Nothing else (such as a parking page's address) should answer.
8. **HTTPS**: in the repository's Pages settings, once the DNS check passes, tick **Enforce HTTPS**. GitHub needs a
   certificate first, which can take up to 24 hours after the domain is set.
9. Open https://griff.games and https://www.griff.games (which should land on the first), and try the search, the
   theme switch and the Download and GitHub buttons.

## Cutting a release (maintainer)

Releases are made by `.github/workflows/release.yml` when a version tag is pushed. Nothing is released from the
repository's side: tagging and pushing is the maintainer's step.

**Versions** are `MAJOR.MINOR.PATCH` and the tag is the version with a `v`: `v0.1.0`. A **pre-release** adds a label after
a hyphen: `v0.1.0-alpha.1`, `v0.1.0-alpha.2`, then `v0.1.0-beta.1` and so on (SemVer: `0.1.0-alpha.2` comes after
`0.1.0-alpha.1` and before `0.1.0`). While Griff is pre-alpha its releases are pre-releases; the first is `v0.1.0-alpha.1`.
Below 1.0 a new minor version (`v0.2.0`) is a release with new features, and may change how projects and scripts work; a
patch (`v0.1.1`) only fixes. The workflow ignores any other tag.

1. **Check main is ready**: it is pushed, its CI run (Actions → CI) is green and its Site run deployed, and both test
   suites pass on it on your machine. The workflow runs no tests: CI's runner draws with software OpenGL, which refuses
   contexts now and then, so a CI run that fails only on "No OpenGL context could be made" doesn't stop a release.
   **Write the changelog entry**: `site/src/data/changelog.md` holds what changed in each release, in plain words, and
   its Unreleased section collects the changes since the last one (add a bullet to it with every user-visible change).
   Check it says what this release changes, run `powershell -NoProfile -ExecutionPolicy Bypass -File
   tools\changelog.ps1 -Version 0.1.0-alpha.1` (it makes Unreleased this version's entry, dated today, and starts a new
   empty one) and commit the result to main before tagging. The release's notes on GitHub start with that entry, and the
   Releases page lists every entry under Changelog.
2. **Tag and push** it (a pre-release here; `v0.1.0` the same way for a release):

   ```sh
   git switch main
   git tag v0.1.0-alpha.1
   git push origin v0.1.0-alpha.1
   ```

3. **Watch** Actions → Release (about 15 minutes). The build job builds the editor in Release on Windows with the tag's
   version (`-p:Version=0.1.0-alpha.1`: the editor's title bar says "Griff 0.1.0-alpha.1"), zips the editor
   with its game players, free fonts, LICENSE and `THIRD-PARTY-NOTICES.txt` as `Griff-0.1.0-alpha.1-win-x64.zip` (the
   portable copy), and packs the same with Velopack (`vpk`, a tool in `dotnet-tools.json`) on the tag's channel:
   `prerelease` for a pre-release tag, `stable` for a release. That makes `GriffEditor-prerelease-Setup.exe` (the
   installer: per user, no admin, installs the .NET 10 Desktop Runtime if it's missing), the full package
   `GriffEditor-0.1.0-alpha.1-prerelease-full.nupkg`, a delta package from the previous release on the channel (fetched
   first with `vpk download github`; none for the first), and the feed `releases.prerelease.json`. The publish job creates
   the GitHub Release as a draft with the zip and the notes (the commits since the previous tag, pre-releases included,
   after how to install it), marked **pre-release** for a pre-release tag, then `vpk upload github` adds the installer,
   the packages and the feed and publishes it. Then it starts the Site workflow, which lists the release on
   https://griff.games/releases/ with the installer as the download and the zip as the portable one. The landing page's
   **Download (pre-alpha)** button goes to that page, which shows the newest release, pre-releases included (GitHub's
   `releases/latest` skips pre-releases, so nothing links there). After the first release that isn't a pre-release, take
   "(pre-alpha)" off the button in `src/content/docs/index.mdx`.
4. **Look at it**: open the release on GitHub, edit its notes if you want to say more than the commit list does, and try
   the installer: it's not code-signed, so SmartScreen says "Windows protected your PC": **More info**, then **Run
   anyway**. It installs into `%LOCALAPPDATA%\GriffEditor` and starts Griff; Help → Check for Updates says it's up to date.
   An installed pre-release updates to newer pre-releases and to the first newer release; an installed release takes
   releases only. Each looks a little after it starts (unless Help → Check for Updates Automatically is off), downloads
   quietly and asks before restarting.

To redo a release that failed before it was created, delete the tag (`git push origin :refs/tags/v0.1.0-alpha.1`, then
`git tag -d v0.1.0-alpha.1`) and tag again after fixing main; a created release (or a draft left by a failed upload) has to
be deleted on GitHub first. Don't reuse a version that installed editors may have taken: tag the next one instead.

To try the release locally, without GitHub: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 0.1.0-alpha.1`
makes the same zip and Velopack files in `artifacts\release\` (`-NoInstaller` just the zip), and
`tools\release-notes.ps1 -Tag v0.1.0-alpha.1` writes the notes (it needs the tag). Packing a second version into the same
`-Out` folder makes a delta and a feed with both, to update from: [Building Griff](https://griff.games/docs/building/#releases)
says how to install the first and update it from that folder (`GRIFF_UPDATE_SOURCE`). `tools/release-notices.cs` writes
the notices from the files the editor folder holds and fails when a package has none on record: add its license to
`src/Griff.Exporting/Notices/` and `ThirdPartyNotices`.

**Code signing.** The installer, `Update.exe` and the editor aren't signed, so SmartScreen warns until a download has
built up a reputation. To sign them, `vpk pack` takes `--signParams` (signtool's arguments, for a certificate) or
`--azureTrustedSignFile` (Azure Trusted Signing); add one to `tools/release.ps1` with the secret in the workflow.

The Releases page (`src/content/docs/releases.mdx`, `src/components/Releases.astro`) reads GitHub's list of releases when
the site is built (`src/data/releases.mjs`): pre-releases too, labelled pre-alpha, each with its installer (the
`-Setup.exe`) and its portable zip (its other files are the installed editor's updates). It says "No release yet" when
there is none or GitHub can't be reached. Below the releases, the Changelog (`src/components/Changelog.astro`) lists
`src/data/changelog.md`, which is in the repository and so shows even offline.
`GRIFF_RELEASES_FILE=<json>` reads a saved copy of the API's answer instead, to see the page with releases offline.
