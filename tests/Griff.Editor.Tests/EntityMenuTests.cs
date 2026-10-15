using System.Windows.Controls;
using System.Windows.Data;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.Hierarchy;
using Griff.Editor.Shell;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Entity menu laid out by category: each create command (and module preset) says its category with an attribute,
/// as components do; the menu and the Hierarchy's right-click menu have a submenu per category, and the Add Entity
/// palette lists them searchably with what was made lately at the top. Command ids, and so agents' tools, stay the same.
/// </summary>
public sealed class EntityMenuTests
{
    private static SceneDocument Open(IServiceProvider services, params Entity[] entities)
    {
        var scene = new Scene();
        foreach (var entity in entities)
            scene.Add(entity);
        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return document;
    }

    // What the Entity menu creates: the create commands (Entity.Create..., Create Entity's kinds) and the modules' presets.
    private static bool Creates(EditorCommand command) =>
        command.Id.StartsWith("Entity.Create", StringComparison.Ordinal) || command is CreateEntityCommand;

    // The architecture rule: a create command without a category would be lost from the menu and the palette, so a new
    // one says where it goes ([Category("...")], a module's preset its module's name), with an icon and a /// summary.
    [Fact]
    public void EveryCreateCommand_HasACategory_AnIcon_AndADescription() => EditorHost.Run(services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>().All.Where(Creates).ToList();
        Assert.True(commands.Count >= 35, $"Only {commands.Count} create commands found.");

        var missing = commands.Where(c => c.Category is null).Select(c => $"{c.DescribedBy.Name} ({c.Id})").ToList();
        Assert.True(missing.Count == 0, "These create commands have no [Category(\"...\")] on their class (or their preset's), so the Entity menu and the Add Entity palette can't place them:\n" + string.Join("\n", missing));
        foreach (var command in commands)
        {
            Assert.True(command.Icon is not null, $"{command.Id} has no icon.");
            Assert.False(DocComments.Summary(command.DescribedBy).IsEmpty, $"{command.Id} has no /// summary to describe it.");
            Assert.Equal(Menus.Entity, command.Placement?.Menu);
            Assert.Equal(0, command.Placement!.Group);
            // A module's are under its name; the core's under one of its own categories.
            var module = GameModule.Of(command.DescribedBy);
            Assert.Equal(module?.Name ?? command.Category, command.Category);
            if (module is null)
                Assert.Contains(command.Category, CommandCategories.Order);
        }

        // And nothing else has one: the categories are for what's made.
        Assert.All(services.GetRequiredService<CommandRegistry>().All.Where(c => c.Category is not null), c => Assert.True(Creates(c), c.Id));
    });

    [Fact]
    public void EachCategory_ListsItsThings_InOrder() => EditorHost.Run(services =>
    {
        var categories = services.GetRequiredService<CommandRegistry>().Categorized;
        var names = categories.Select(g => g.Key).ToList();

        Assert.Equal(["Basics", "Characters", "Gameplay", "Level", "Physics", "UI", "Story", "Audio", "Lights and Effects"], names.Take(9));
        // Then one per genre module that makes something, alphabetically.
        Assert.Equal(["Adventures", "Cards", "Dungeons", "Falling Blocks", "Fighting", "Match-3", "Physics Games", "Puzzles", "Tactics", "Visual Novels"], names.Skip(9));
        Assert.Equal(["Entity.CreateSparring"], Ids("Fighting"));
        Assert.Equal(["Entity.CreateSlingshot", "Entity.CreateTower"], Ids("Physics Games"));
        string[] Ids(string category) => categories.Single(g => g.Key == category).Select(c => c.Id).ToArray();
        Assert.Equal(["Entity.CreateEmpty", "Entity.CreateSprite", "Entity.CreateText", "Entity.CreateCamera"], Ids("Basics"));
        Assert.Equal(["Entity.CreatePlatformerPlayer", "Entity.CreateTopDownPlayer", "Entity.CreateGridPlayer", "Entity.CreateGridEnemy", "Entity.CreateHitbox"], Ids("Characters"));
        Assert.Equal(["Entity.CreateMusic", "Entity.CreateSoundSource", "Entity.CreateSoundPlayer", "Entity.CreateBeatClock", "Entity.CreateCalibration"], Ids("Audio"));
        Assert.Equal(["Entity.CreateMatchBoard"], Ids("Match-3"));
    });

    [Fact]
    public void TheEntityMenu_HasAddEntity_ThenASubmenuPerCategory_WithIconsAndDescriptions() => EditorHost.Run(services =>
    {
        var menu = new Menu();
        services.GetRequiredService<MenuBuilder>().Populate(menu);
        var entity = menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "E_ntity");
        var items = entity.Items.Cast<object>().ToList();

        var add = Assert.IsType<MenuItem>(items[0]);
        Assert.Equal("Entity.AddEntity", ((EditorCommand)add.Tag).Id);
        Assert.Equal("Ctrl+Shift+A", add.InputGestureText);

        var submenus = items.OfType<MenuItem>().Where(m => m.Tag is string).ToList();
        var categories = services.GetRequiredService<CommandRegistry>().Categorized.Select(g => g.Key).ToList();
        // The core's, then Genres with a submenu per genre module in it.
        Assert.Equal([.. categories.Where(c => !CommandCategories.IsModule(c)), CommandCategories.Genres], submenus.Select(m => (string)m.Tag));
        var genres = submenus[^1].Items.OfType<MenuItem>().ToList();
        Assert.Equal(categories.Where(CommandCategories.IsModule), genres.Select(m => (string)m.Tag));
        Assert.Equal("Match Board", ((string)genres.Single(m => (string)m.Tag == "Match-3").Items.OfType<MenuItem>().Single().Header).Replace("_", ""));

        var characters = submenus.Single(m => (string)m.Tag == "Characters").Items.OfType<MenuItem>().ToList();
        Assert.Equal(["_Platformer Player", "Top-down Pla_yer", "_Grid Player", "Grid _Enemy", "Add Atta_ck"], characters.Select(m => (string)m.Header));
        var enemy = characters[3];
        Assert.Equal(DocComments.Summary(typeof(CreateGridEnemyCommand)).FirstSentence, enemy.ToolTip);
        Assert.NotNull(enemy.Icon);
        Assert.Same(services.GetRequiredService<CommandRegistry>().Get("Entity.CreateGridEnemy"), enemy.Command);

        // What's done to the selection follows, as before.
        Assert.Contains(items.OfType<MenuItem>(), m => m.Tag is EditorCommand { Id: "Entity.Rename" });
    });

    // The flat menu grew taller than a screen, so items near its foot couldn't be clicked. Laid out by category, every
    // list a click opens stays short; a category that grows this long should be split.
    [Fact]
    public void EveryListTheEntityMenuOpens_FitsOnAScreen() => EditorHost.Run(services =>
    {
        const int Most = 20;
        var menu = new Menu();
        services.GetRequiredService<MenuBuilder>().Populate(menu);
        var entity = menu.Items.OfType<MenuItem>().Single(m => (string)m.Header == "E_ntity");

        // Things to make take one row per core category and one for Genres, whatever modules there are.
        var made = entity.Items.OfType<MenuItem>().Count(m => m.Tag is string);
        Assert.Equal(CommandCategories.Order.Count + 1, made);
        Assert.True(entity.Items.Count <= 32, $"The Entity menu has {entity.Items.Count} rows.");
        foreach (var submenu in Submenus(entity))
            Assert.True(submenu.Items.Count <= Most, $"{submenu.Tag} lists {submenu.Items.Count} things: split it so each list fits.");
        var hierarchy = HierarchyView.BuildMenu(services.GetRequiredService<CommandRegistry>());
        Assert.True(hierarchy.Items.Count <= 32, $"The Hierarchy's menu has {hierarchy.Items.Count} rows.");

        static IEnumerable<MenuItem> Submenus(MenuItem menu) =>
            menu.Items.OfType<MenuItem>().Where(m => m.Tag is string).SelectMany(m => Submenus(m).Prepend(m));
    });

    [Fact]
    public void TheHierarchysMenu_UsesTheSameCategories() => EditorHost.Run(services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();
        var menu = HierarchyView.BuildMenu(commands);
        var items = menu.Items.OfType<MenuItem>().ToList();

        Assert.Equal("Entity.AddEntity", ((EditorCommand)items[0].Tag).Id);
        Assert.Equal([.. commands.Categorized.Select(g => g.Key).Where(c => !CommandCategories.IsModule(c)), CommandCategories.Genres],
            items.Where(m => m.Tag is string).Select(m => (string)m.Tag));
        Assert.Contains(items.Single(m => m.Tag is CommandCategories.Genres).Items.OfType<MenuItem>(), m => m.Tag is "Tactics");
        var level = items.Single(m => m.Tag is "Level").Items.OfType<MenuItem>().Select(m => ((EditorCommand)m.Tag).Id);
        Assert.Equal(commands.Categorized.Single(g => g.Key == "Level").Select(c => c.Id), level);
        Assert.Contains(items, m => m.Tag is EditorCommand { Id: "Entity.Delete" });
    });

    [Fact]
    public void Agents_SeeEachCreateTool_ByItsCategory_UnderTheSameName() => EditorHost.Run(services =>
    {
        var tools = services.GetRequiredService<AgentServer>().Tools;

        var enemy = tools["Entity_CreateGridEnemy"].Description;
        Assert.StartsWith(DocComments.Summary(typeof(CreateGridEnemyCommand)).FirstSentence.TrimEnd('.'), enemy);
        Assert.Contains("(Menu: Entity > Characters > Grid Enemy)", enemy);
        Assert.Contains("(Menu: Entity > Genres > Match-3 > Match Board)", tools["Entity_CreateMatchBoard"].Description);
        Assert.Contains("(Menu: Entity > Basics > Empty, Ctrl+Alt+N)", tools["Entity_CreateEmpty"].Description);
        Assert.Contains("(Menu: Entity > Add Entity..., Ctrl+Shift+A)", tools["Entity_AddEntity"].Description);
    });

    [Fact]
    public void ThePalette_FindsThingsByName_CategoryOrWhatTheyDo_AndMakesThem() => EditorHost.Run(services =>
    {
        var document = Open(services);
        var hierarchy = services.GetRequiredService<HierarchyPanel>();

        Pump(services.GetRequiredService<CommandRegistry>().Get("Entity.AddEntity").ExecuteAsync());
        var palette = Assert.IsType<EntityPaletteViewModel>(hierarchy.Palette);
        Assert.True(hierarchy.IsAddOpen);
        // Nothing made yet: no Recent, and the first thing is selected.
        Assert.Equal("Basics", Groups(palette)[0]);
        Assert.Equal("Empty", palette.Selected!.Name);

        palette.Search = "grid enemy";
        Assert.Equal("Grid Enemy", palette.Selected!.Name);
        Assert.Equal(DocComments.Summary(typeof(CreateGridEnemyCommand)).FirstSentence, palette.PlanText);

        // By category: everything in it.
        palette.Search = "audio";
        Assert.Equal(["Music", "Sound Source", "Sound Player", "Beat Clock", "Calibration Screen"], palette.View.Cast<EntityPaletteItem>().Select(i => i.Name));

        // By what it does: a waterfall is a sound that loops.
        palette.Search = "waterfall";
        Assert.Equal("Sound Source", palette.Selected!.Name);

        palette.Search = "zzzz";
        Assert.False(palette.HasResults);

        palette.Search = "enemy";
        Assert.Equal("Grid Enemy", palette.Selected!.Name);
        hierarchy.AddPicked();
        Assert.False(hierarchy.IsAddOpen);
        var enemy = Assert.Single(document.Scene.Roots);
        Assert.Equal("Enemy", enemy.Name);
        Assert.NotNull(enemy.GetComponent<GridChase>());
        Assert.Equal(enemy, services.GetRequiredService<ISelection>().Entity);
    });

    [Fact]
    public void WhatWasMadeLately_IsAtTheTop_KeptInTheSettings() => EditorHost.Run(services =>
    {
        Open(services);
        var commands = services.GetRequiredService<CommandRegistry>();
        var hierarchy = services.GetRequiredService<HierarchyPanel>();
        var selection = services.GetRequiredService<ISelection>();

        // However it's made: the menu, the palette or an agent's tool.
        Pump(commands.Get("Entity.CreateLight").ExecuteAsync());
        selection.Select(null);
        Pump(commands.Get("Entity.CreateCrate").ExecuteAsync());
        selection.Select(null);
        Pump(commands.Get("Entity.CreateLight").ExecuteAsync());
        // Not other commands.
        Pump(commands.Get("Entity.Duplicate").ExecuteAsync());

        Assert.Equal(["Entity.CreateLight", "Entity.CreateCrate"], services.GetRequiredService<ISettingsStore>().Settings.RecentEntities);
        Assert.Equal(["Entity.CreateLight", "Entity.CreateCrate"], commands.Recent.Select(c => c.Id));

        hierarchy.IsAddOpen = true;
        var palette = hierarchy.Palette!;
        Assert.Equal(EntityPaletteViewModel.RecentGroup, Groups(palette)[0]);
        Assert.Equal(["Light", "Crate"], palette.View.Cast<EntityPaletteItem>().Where(i => i.IsRecent).Select(i => i.Name));
        Assert.Equal("Light", palette.Selected!.Name);

        // Searching lists each thing once, in its own category.
        palette.Search = "light";
        Assert.DoesNotContain(EntityPaletteViewModel.RecentGroup, Groups(palette));
        Assert.Single(palette.View.Cast<EntityPaletteItem>(), i => i.Name == "Light");

        // Only so many are kept.
        foreach (var command in commands.Categorized.SelectMany(g => g).Where(c => c is CreateEntityCommand).Take(12))
            Pump(command.ExecuteAsync());
        Assert.Equal(CommandRegistry.MaxRecent, commands.Recent.Count);
        Assert.True(services.GetRequiredService<ISettingsStore>().Settings.RecentEntities.Count <= CommandRegistry.MaxRecent * 2);
    });

    [Fact]
    public void ThePalette_WontMakeWhatNeedsASelection_UntilThereIsOne() => EditorHost.Run(services =>
    {
        var hero = new Entity("Hero");
        var document = Open(services, hero);
        var hierarchy = services.GetRequiredService<HierarchyPanel>();

        hierarchy.IsAddOpen = true;
        hierarchy.Palette!.Search = "attack";
        var attack = hierarchy.Palette.Selected!;
        Assert.Equal("Add Attack", attack.Name);
        Assert.False(attack.CanCreate);
        Assert.EndsWith("Select an entity first.", hierarchy.Palette.PlanText);
        hierarchy.AddPicked();
        Assert.True(hierarchy.IsAddOpen);

        hierarchy.IsAddOpen = false;
        services.GetRequiredService<ISelection>().Select(hero);
        hierarchy.IsAddOpen = true;
        hierarchy.Palette!.Search = "attack";
        hierarchy.AddPicked();
        Assert.Equal("Hitbox", Assert.Single(hero.Children).Name);
        Assert.NotNull(hero.GetComponent<Attack>());
        Assert.Same(document, services.GetRequiredService<IWorkspace>().Scene);
    });

    [Theory]
    [InlineData("Entity.CreateMusic", typeof(BackgroundMusic), "Music")]
    [InlineData("Entity.CreateSoundSource", typeof(SoundSource), "Sound Source")]
    [InlineData("Entity.CreateSoundPlayer", typeof(SoundPlayer), "Sound Player")]
    [InlineData("Entity.CreateBeatClock", typeof(BeatClock), "Beat Clock")]
    [InlineData("Entity.CreateCalibration", typeof(Calibration), "Calibrate")]
    public void TheAudioCategory_MakesSounds(string id, Type component, string name) => EditorHost.Run(services =>
    {
        var document = Open(services);
        Pump(services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync());

        var made = Assert.Single(document.Scene.Roots);
        Assert.Equal(name, made.Name);
        Assert.Contains(made.Components, c => c.GetType() == component);
        document.History.Undo();
        Assert.Empty(document.Scene.Roots);
    });

    private static List<string> Groups(EntityPaletteViewModel palette) =>
        palette.View.Groups.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList();

    private static void Pump(Task task) => EditorHost.Pump(task);
}
