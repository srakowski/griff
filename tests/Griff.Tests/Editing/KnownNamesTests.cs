using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Griff.Editing;
using Griff.Modules.Strategy;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Editing;

public sealed class KnownNamesTests : IDisposable
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-names-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private PropertyDescriptor Property<T>(string name) => _registry.Get(typeof(T)).Properties.Single(p => p.Name == name);

    private static KnownNames Names(string[]? prefabs = null, string[]? scenes = null, string[]? layers = null, string[]? sent = null, string[]? heard = null) =>
        new(prefabs ?? [], scenes ?? [], ["Jump", "Fire", "Map"], layers ?? [], sent ?? [], heard ?? []);

    [Fact]
    public void FieldsNamingPrefabsScenesActionsLayersAndMessages_AreMarked()
    {
        Assert.Equal(NameKind.Prefab, Property<Spawner>("Prefab").Names!.Kind);
        Assert.Equal(NameKind.Prefab, Property<Shooter>("Prefab").Names!.Kind);
        Assert.Equal(NameKind.Prefab, Property<Pickup>("Effect").Names!.Kind);
        Assert.True(Property<Producer>("Trains").Names!.Many);
        Assert.Equal(NameKind.Scene, Property<Door>("Scene").Names!.Kind);
        Assert.Equal(NameKind.Action, Property<Attack>("Action").Names!.Kind);
        Assert.Equal(NameKind.Action, Property<Shooter>("Action").Names!.Kind);
        Assert.Equal(NameKind.Layer, Property<Pickup>("Layers").Names!.Kind);
        Assert.True(Property<Pickup>("Layers").Names!.Many);
        // Declared on the abstract collider, it holds for every kind of collider.
        Assert.True(Property<BoxCollider>("Layer").Names!.Defines);
        Assert.True(Property<CircleCollider>("IgnoreLayers").Names!.Many);
        Assert.Equal(NameKind.Message, Property<Pickup>("Message").Names!.Kind);
        Assert.True(Property<Counter>("Message").Names!.Hears);
        Assert.False(Property<Health>("DeathMessage").Names!.Hears);
        // Entity names and plain text aren't; items are.
        Assert.Null(Property<Door>("Target").Names);
        Assert.Equal(NameKind.Item, Property<Door>("Key").Names!.Kind);
    }

    [Fact]
    public void Unmatched_IsWhatTheProjectDoesntHave()
    {
        var names = Names(prefabs: ["Bullet"], scenes: ["Level2"], layers: ["Player"], sent: ["Score"], heard: ["Score", "Boom"]);

        Assert.Empty(names.Unmatched(Property<Spawner>("Prefab").Names!, "Bullet"));
        // As the game finds them: any capitals, or a path to the file.
        Assert.Empty(names.Unmatched(Property<Spawner>("Prefab").Names!, "bullet"));
        Assert.Empty(names.Unmatched(Property<Spawner>("Prefab").Names!, "Prefabs/Bullet.prefab"));
        Assert.Equal(["Bulet"], names.Unmatched(Property<Spawner>("Prefab").Names!, "Bulet"));
        Assert.Equal(["Level3"], names.Unmatched(Property<Door>("Scene").Names!, "Level3"));
        Assert.Empty(names.Unmatched(Property<Attack>("Action").Names!, "fire"));
        Assert.Equal(["Swing"], names.Unmatched(Property<Attack>("Action").Names!, "Swing"));
        // Layers: Default is always there, and a list is checked name by name.
        Assert.Equal(["Enemy"], names.Unmatched(Property<Pickup>("Layers").Names!, "Player, Default, Enemy"));
        // A collider's own layer is where layers come from: never flagged.
        Assert.Empty(names.Unmatched(Property<BoxCollider>("Layer").Names!, "Ghost"));
        // A counter hears what's sent; a pickup sends to whoever hears.
        Assert.Equal(["Coins"], names.Unmatched(Property<Counter>("Message").Names!, "Coins"));
        Assert.Empty(names.Unmatched(Property<Counter>("Message").Names!, "Score"));
        Assert.Empty(names.Unmatched(Property<Pickup>("Message").Names!, "Boom"));
        Assert.Equal(["Bang"], names.Unmatched(Property<Pickup>("Message").Names!, "Bang"));
        // Empty names nothing.
        Assert.Empty(names.Unmatched(Property<Spawner>("Prefab").Names!, ""));
    }

    [Fact]
    public void Unmatched_LeavesAComponentsOwnDefaultMessage_ButNotOneTyped()
    {
        var names = Names();
        var faction = new Faction();

        Assert.Empty(names.Unmatched(faction, Property<Faction>("DefeatMessage")));
        faction.DefeatMessage = "Lost";
        Assert.Equal(["Lost"], names.Unmatched(faction, Property<Faction>("DefeatMessage")));
        // A counter's default is still checked: it hears nothing nothing sends.
        Assert.Equal(["Score"], names.Unmatched(new Counter(), Property<Counter>("Message")));
    }

    [Fact]
    public void MessagesWithASidesName_MatchWhatTheyBecome()
    {
        var names = Names(sent: ["{0}Turn"]);

        Assert.Empty(names.Unmatched(Property<Counter>("Message").Names!, "EnemyTurn"));
        Assert.Equal(["Turned"], names.Unmatched(Property<Counter>("Message").Names!, "Turned"));
        // Not one to pick, and not checked itself.
        Assert.Empty(names.Choices(Property<Counter>("Message").Names!));
        Assert.Empty(names.Unmatched(Property<TurnOrder>("Message").Names!, "{0}Turn"));
    }

    [Fact]
    public void Choices_AreSorted_AndMessagesSayWhetherTheyreSentOrHeard()
    {
        var names = Names(prefabs: ["Zombie", "Bullet"], layers: ["Player"], sent: ["Score", "Won"], heard: ["Score", "Lost"]);

        Assert.Equal(["Bullet", "Zombie"], names.Choices(Property<Spawner>("Prefab").Names!).Select(c => c.Name));
        Assert.Equal(["Default", "Player"], names.Choices(Property<Pickup>("Layers").Names!).Select(c => c.Name));
        Assert.Equal(["Lost heard", "Score sent and heard", "Won sent"],
            names.Choices(Property<Pickup>("Message").Names!).Select(c => $"{c.Name} {c.Detail}"));
    }

    [Fact]
    public void Collect_FindsLayersAndMessages_InComponentsAndDialogue()
    {
        var player = new Entity("Player");
        player.AddComponent(new BoxCollider { Layer = "Hero" });
        player.AddComponent(new Health { DeathMessage = "PlayerDied" });
        var hud = new Entity("Hud");
        hud.AddComponent(new Counter { Message = "Coins" });
        var guide = new Entity("Guide");
        guide.AddComponent(new Dialogue { Lines = ["Hello!", "> [send OpenGate] Open it", "[send Waved]"] });
        var map = new Entity("Map");
        map.AddComponent(new Tilemap { Layer = "Ground" });

        var found = KnownNames.Collect([player, hud, guide, map], _registry);

        Assert.Equal(["Ground", "Hero"], found.Layers.Order());
        Assert.Equal(["OpenGate", "PlayerDied", "Waved"], found.Sent.Order());
        Assert.Equal(["Coins"], found.Heard);
    }

    [Fact]
    public void Scan_FindsWhatScriptsSendHearAndPutOnLayers()
    {
        const string code = """
            public class Boss : Behavior
            {
                protected override void Start()
                {
                    On("PlayerDied", () => { });
                    On<int>("Hurt", amount => { });
                    Game.Send("BossAppeared");
                    Game.Send("BossHurt", 3);
                    GetComponent<BoxCollider>().Layer = "Boss";
                    var text = "Send(\"NotThis\" + x)";
                }
            }
            """;

        var found = KnownNames.Scan(code);

        Assert.Equal(["BossAppeared", "BossHurt"], found.Sent.Order());
        Assert.Equal(["Hurt", "PlayerDied"], found.Heard.Order());
        Assert.Equal(["Boss"], found.Layers);
    }

    [Fact]
    public async Task Index_ReadsTheProjectsFiles_AndTheOpenSceneAsItIs()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var serializer = new SceneSerializer(_registry);
        var bullet = new Entity("Bullet");
        bullet.AddComponent(new CircleCollider { Layer = "Shot" });
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Bullet"))!);
        await File.WriteAllTextAsync(project.DefaultPrefabPath("Bullet"), serializer.SavePrefabToString(bullet));
        var scripts = Path.Combine(project.AssetsPath, "Scripts");
        Directory.CreateDirectory(scripts);
        await File.WriteAllTextAsync(Path.Combine(scripts, "Score.cs"), "class S { void Go() => Game.Send(\"Scored\"); }");
        var scenePath = project.FindScenes().First();
        var open = serializer.LoadFromString(await File.ReadAllTextAsync(scenePath));
        var coin = new Entity("Coin");
        coin.AddComponent(new Pickup { Message = "Gold" });
        open.Add(coin);

        var index = new KnownNameIndex(_registry);
        var names = index.Get(project, open.Entities, scenePath);

        Assert.Contains("Bullet", names.Prefabs);
        Assert.Contains(Path.GetFileNameWithoutExtension(scenePath), names.Scenes);
        Assert.Contains("Jump", names.Actions);
        Assert.Contains("Shot", names.Layers);
        Assert.Contains("Scored", names.Sent);
        // The open scene counts as it is, not as last saved.
        Assert.Contains("Gold", names.Sent);

        // A script saved again is read again.
        await File.WriteAllTextAsync(Path.Combine(scripts, "Score.cs"), "class S { void Go() => Game.Send(\"Tallied\"); }");
        File.SetLastWriteTimeUtc(Path.Combine(scripts, "Score.cs"), DateTime.UtcNow.AddMinutes(1));
        names = index.Get(project, open.Entities, scenePath);
        Assert.Contains("Tallied", names.Sent);
        Assert.DoesNotContain("Scored", names.Sent);
    }

    [Fact]
    public void ItemsFlagsAndKinds_ComeFromWhatGivesSetsAndSaysThem()
    {
        var gem = new Entity("Gem");
        gem.AddComponent(new Pickup { Item = "Key" });
        var lever = new Entity("Lever");
        lever.AddComponent(new Trigger { Do = "Set Flag", With = "PowerOn" });
        var talk = new Entity("Talk");
        talk.AddComponent(new Dialogue { Lines = ["[set MetKing] Hello.", "[clear Angry] Fine."] });
        var crate = new Entity("Crate");
        crate.AddComponent(new Pushable { Kind = "Red" });
        var door = new Entity("Door");
        door.AddComponent(new Door { Key = "Key", NeedsFlag = "not Lost" });
        var found = KnownNames.Collect([gem, lever, talk, crate, door], _registry);
        var script = KnownNames.Scan("""inventory.Add("Map"); inventory.Add("Coin", 3); Game.SetFlag("Opened", true);""");

        Assert.Equal(["Key"], found.Items);
        Assert.Equal(["Map", "Coin"], script.Items);
        Assert.Equal(["Angry", "MetKing", "PowerOn"], found.Flags.Order());
        Assert.Equal(["Opened"], script.Flags);
        Assert.Equal([("Crate", "Red")], found.Kinds);

        var names = new KnownNames([], [], [], [], [], [], items: found.Items.Concat(script.Items), flags: found.Flags.Concat(script.Flags), kinds: found.Kinds);
        var key = Property<Door>("Key").Names!;
        Assert.Equal(["Coin", "Key", "Map"], names.Choices(key).Select(c => c.Name));
        Assert.Empty(names.Unmatched(key, "key"));
        Assert.Equal(["Gold"], names.Unmatched(key, "Gold"));

        // A condition names its flag, or "not" it.
        var needs = Property<Door>("NeedsFlag").Names!;
        Assert.True(needs.Condition);
        Assert.Equal(["Angry", "not Angry", "MetKing", "not MetKing"], names.Choices(needs).Take(4).Select(c => c.Name));
        Assert.Empty(names.Unmatched(needs, "not PowerOn"));
        Assert.Empty(names.Unmatched(needs, "!opened"));
        Assert.Equal(["not Lost"], names.Unmatched(needs, "not Lost"));
        Assert.Equal("Nothing sets the flag \"Lost\": no trigger's Set Flag, dialogue's [set] or script's SetFlag", KnownNames.Problem(needs, ["not Lost"]));

        // Kinds go with kinds of their own scope.
        var accepts = Property<Griff.Modules.Puzzles.Goal>("Accepts").Names!;
        Assert.Equal(["Red"], names.Choices(accepts).Select(c => c.Name));
        Assert.Equal(["Blue"], names.Unmatched(accepts, "Red, Blue"));
        Assert.Equal("a crate kind", KnownNames.Noun(accepts));
        Assert.Empty(names.Unmatched(Property<Pushable>("Kind").Names!, "Green"));
    }

    [Fact]
    public void Problem_SaysWhatsMissing()
    {
        Assert.Equal("There's no prefab called \"Bulet\" in the project", KnownNames.Problem(Property<Spawner>("Prefab").Names!, ["Bulet"]));
        Assert.Equal("Nothing sends \"Coins\": no component's message, dialogue's [send] or script's Game.Send",
            KnownNames.Problem(Property<Counter>("Message").Names!, ["Coins"]));
        Assert.StartsWith("Nothing is on the layer \"Ghost\" or \"Bat\"", KnownNames.Problem(Property<Pickup>("Layers").Names!, ["Ghost", "Bat"]));
    }

    // A field's default is read from a sample of its component's type, held only while the type is: a script compiled
    // again goes, and its old assembly unloads, however often the Inspector asked about it.
    [Fact]
    public void ScriptTypes_AreHeldOnlyWhileTheyLive_SoAReloadedScriptUnloads()
    {
        var unloaded = AskAboutAScriptThenUnloadIt();
        for (var i = 0; i < 20 && unloaded.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(unloaded.IsAlive, "The script's assembly stayed loaded after the names were asked about one of its components.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AskAboutAScriptThenUnloadIt()
    {
        var compiled = ScriptCompiler.Compile([new ScriptSource("Pinger.cs", """
            public class Pinger : Behavior
            {
                [Names(NameKind.Message)]
                public string Sends { get; set; } = "Ping";
            }
            """)]);
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));
        var context = new AssemblyLoadContext("Scripts", isCollectible: true);
        var type = context.LoadFromStream(new MemoryStream(compiled.Assembly!)).GetType("Pinger")!;
        var property = new ComponentRegistry().Register(type).Properties.Single(p => p.Name == "Sends");

        // Its default needn't be heard by anything: the names asked for the type's sample to know it.
        Assert.Empty(new KnownNames([], [], [], [], [], []).Unmatched((Component)Activator.CreateInstance(type)!, property));
        context.Unload();
        return new WeakReference(context);
    }
}
