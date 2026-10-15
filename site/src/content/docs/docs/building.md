---
title: Building Griff
description: Build Griff from source, run its tests, and how the repository is laid out.
---

Building Griff takes the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```sh
dotnet build src/Griff.Editor/Griff.Editor.csproj
dotnet test tests/Griff.Tests
dotnet test tests/Griff.Editor.Tests
dotnet run --project src/Griff.Editor
```

Building the editor also publishes the game players it exports games with. Every project shares `Directory.Build.props` (nullable on, warnings are errors, the .NET analyzers' recommended rules), package versions live in `Directory.Packages.props`, and `.editorconfig` holds the code style. CI (`.github/workflows/ci.yml`) builds the editor and runs both test projects on Windows for every push and pull request, with Mesa's software OpenGL so the player smoke test can draw.

## The repository

```
src/
  Griff                    Engine core: scenes, serialization, editing/undo, runtime, physics,
                           render lists, audio, networking, projects and templates. No UI dependencies.
    Modules/               The genres, each in a folder of its own
  Griff.Scripting          Roslyn script compiler, hot reload, game exporter
  Griff.Graphics           OpenGL 3.3 sprite and text renderer
  Griff.Platform.Windows   WGL context, WPF game surface, frame pacing, input, sound output
  Griff.Editor             WPF editor: modules, commands, panels, design system
  Griff.Player             Runs exported games
  Griff.Web                Runs games in a browser (a proof so far)
tests/
  Griff.Tests              Unit and integration tests, including playing every template
  Griff.Editor.Tests       The editor's real services, the agent surface, and exported games run for real
site/                      This website (Astro Starlight), deployed to griff.games
```

Exported games are self-contained: they include the .NET runtime and run on any 64-bit Windows PC without installing anything. **BunnyMark** (`tools\bunnymark.ps1`) measures how many bouncing bunnies an exported game keeps up at 60 frames a second.

## This website

The site lives in `site/` and is built with [Astro Starlight](https://starlight.astro.build). With Node 24:

```sh
cd site
npm ci
npm run dev          # http://127.0.0.1:4321/
npm run build        # into site/dist
npm run check-links  # every link inside the built site has to lead somewhere
```

A GitHub Action builds it and deploys it to GitHub Pages on every push to main. [`site/README.md`](https://github.com/srakowski/griff/blob/main/site/README.md) has the rest, including how to take its screenshots again.

The architecture is written up in the [README](https://github.com/srakowski/griff#architecture).
