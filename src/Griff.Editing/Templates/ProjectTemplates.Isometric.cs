using System.Numerics;
using Griff.Audio;
using Griff.Scenes;

namespace Griff.Templates;

public static partial class ProjectTemplates
{
    private sealed class IsometricTemplate : ProjectTemplate
    {
        public override string Description => "Explore a sunny ruined courtyard seen at a slant, walking behind and in front of its walls and hedges, collecting gems. A slime chases you on sight, a crate goes onto a plate, and the gardener knows what's what. Arrow keys or WASD, Shift to dash, X to swing, E to talk.";

        public override int Order => 25;

        protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 5) with { Volume = 0.7f };

        protected override Scene BuildStartScene() => Isometric();

        protected override Dictionary<string, Entity> BuildPrefabs() => new() { ["Prefabs/Sparkle.prefab"] = Sparkle() };

        public override IReadOnlyDictionary<string, byte[]> Images() => TemplateArt.IsometricFiles();
    }

    // Where the courtyard's top corner is, so its middle is at the world's origin.
    private static readonly Vector2 CourtyardAt = new(0, 6);

    // The courtyard's ground, a row of cells a line, the grid's columns running down to the right and its rows down to
    // the left: grass (0), flowers (1), a paved path (2) and dirt (3).
    private static readonly string[] CourtyardGround =
    [
        "000000000000",
        "001000010000",
        "003330200100",
        "003333200000",
        "003333200100",
        "003333200000",
        "222222222222",
        "000100200010",
        "010000200000",
        "000001200000",
        "001000200100",
        "000000200000",
    ];

    // What stands on it: stone (4) and mossy stone (5) along the back and in an old shed, a hedge (6), and low walls (7)
    // along the front, so they don't hide what's just behind them.
    private static readonly string[] CourtyardWalls =
    [
        "445444454444",
        "4..........7",
        "4.44.4.....7",
        "4.4....66..7",
        "4.4..4.66..7",
        "5.4444.6...7",
        "4..........7",
        "4..........7",
        "4.77.......7",
        "4..........7",
        "4..........7",
        "477777777777",
    ];

    // An isometric courtyard: a floor that doesn't collide, a layer of walls that does and sorts each diagonal among the
    // sprites, and characters whose feet (their pictures' bottoms, and the flat boxes they collide with) stand on the diamonds.
    private static Scene Isometric()
    {
        var scene = new Scene();

        var follow = AddCameraWithScore(scene);
        var camera = follow.Entity!.GetComponent<Camera>()!;
        camera.SortByY = true;
        camera.Background = Color.FromBytes(0x23, 0x3A, 0x3E);
        follow.Entity.Transform.Position = new Vector3(Cell(6, 7), 0);

        var courtyard = new Entity("Courtyard");
        courtyard.Transform.Position = new Vector3(CourtyardAt, 0);
        var ground = courtyard.AddComponent<Tilemap>();
        ground.Image = TemplateArt.IsoTiles;
        (ground.Columns, ground.Rows) = (TemplateArt.IsoTileColumns, TemplateArt.IsoTileRows);
        ground.Shape = TileShape.Isometric;
        ground.TileSize = 2;
        ground.TileHeight = 1;
        ground.Tiles = CourtyardGround;
        ground.Solid = false;
        ground.Order = -10;
        scene.Add(courtyard);

        // A layer of the courtyard's map (no Image of its own): solid, and drawn a diagonal at a time among the sprites.
        var walls = new Entity("Walls");
        var wallTiles = walls.AddComponent<Tilemap>();
        wallTiles.Tiles = CourtyardWalls;
        wallTiles.SortByY = true;
        scene.Add(walls, courtyard);

        // Top-down Movement's Isometric walks over the diamonds: up and down go half as fast on the screen.
        var player = Person("Player", 6, 7, TemplateArt.IsoHero);
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>().GravityScale = 0;
        var movement = player.AddComponent<TopDownMovement>();
        movement.Isometric = true;
        movement.Dash = true;
        player.AddComponent<Speaker>().Name = "You";
        AddDashTrail(player, minSpeed: 9);
        AddRespawningHealth(player);
        scene.Add(player);
        AddSword(scene, player);

        // One in the shed, one behind the hedge, and three in the far corners.
        var gems = new Entity("Gems");
        scene.Add(gems);
        foreach (var (x, y) in new[] { (3, 4), (7, 2), (1, 1), (10, 1), (1, 10) })
            scene.Add(Gem("Gem", x, y, Color.FromBytes(0x4C, 0xD0, 0xE0)), gems);

        // A slime that comes after the player once it sees them, finding its way round the walls.
        var slime = new Entity("Slime");
        slime.Transform.Position = new Vector3(Cell(10, 2) + new Vector2(0, 0.3f), 0);
        var slimeSprite = slime.AddComponent<Sprite>();
        (slimeSprite.Image, slimeSprite.Size) = (TemplateArt.IsoSlime, new Vector2(0.9f, 0.7f));
        var slimeCollider = slime.AddComponent<BoxCollider>();
        (slimeCollider.Size, slimeCollider.Offset, slimeCollider.Layer) = (new Vector2(0.7f, 0.35f), new Vector2(0, -0.17f), "Enemy");
        slime.AddComponent<Body>().GravityScale = 0;
        var chase = slime.AddComponent<Chase>();
        chase.Range = 5;
        chase.Speed = 2;
        chase.NeedsSight = true;
        chase.Around = "Walls";
        var bite = slime.AddComponent<DamageOnTouch>();
        bite.Layers = "Player";
        bite.Knockback = 6;
        AddEnemyHealth(slime, hits: 2);
        scene.Add(slime);

        // A crate to push straight up into the hedge's corner, onto the plate, which brings out a bonus gem while it's
        // pressed. Only the crate presses the plate.
        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(Cell(9, 6) + new Vector2(0, 0.35f), 0);
        var crateSprite = crate.AddComponent<Sprite>();
        (crateSprite.Image, crateSprite.Size) = (TemplateArt.IsoCrate, new Vector2(1.4f, 1.4f));
        var crateCollider = crate.AddComponent<BoxCollider>();
        (crateCollider.Size, crateCollider.Offset, crateCollider.Layer) = (new Vector2(1.4f, 0.7f), new Vector2(0, -0.35f), "Crate");
        crate.AddComponent<Body>().GravityScale = 0;
        crate.AddComponent<Pushable>();
        scene.Add(crate);

        var plate = new Entity("Plate");
        plate.Transform.Position = new Vector3(Cell(8, 5), 0);
        var plateSprite = plate.AddComponent<Sprite>();
        (plateSprite.Image, plateSprite.Size, plateSprite.Order) = (TemplateArt.IsoPlate, new Vector2(1.4f, 0.7f), -5);
        var plateCollider = plate.AddComponent<BoxCollider>();
        (plateCollider.Size, plateCollider.IsTrigger) = (new Vector2(0.5f, 0.25f), true);
        var plateSwitch = plate.AddComponent<Switch>();
        plateSwitch.Mode = SwitchMode.Plate;
        plateSwitch.Layers = "Crate";
        plateSwitch.Targets = "Bonus Gem";
        scene.Add(plate);
        var bonus = Gem("Bonus Gem", 9, 9, Color.FromBytes(0xE3, 0xB3, 0x41));
        bonus.GetComponent<Pickup>()!.Amount = 3;
        scene.Add(bonus);

        // The gardener, just above where the player starts, answers what they ask about. MetGardener is kept in the save
        // data, so the greeting changes from the second time on.
        var gardener = AddSign(scene, Cell(5, 6),
            "[if MetGardener] Back again? The courtyard's all yours.",
            "[if not MetGardener] [set MetGardener] Hello there! You can walk right behind the walls and hedges here.",
            "-> Ask",
            "# Ask",
            "What would you like to know about?",
            "> The gems -> Gems",
            "> The slime -> Slime",
            "> The crate -> Crate",
            "> Nothing, thanks -> End",
            "# Gems",
            "Five gems about: one in the old shed, one behind the hedge, and three in the far corners.",
            "-> Ask",
            "# Slime",
            "It comes after you once it sees you, and the walls block its view. Or swing your sword at it with X.",
            "-> Ask",
            "# Crate",
            "Push the crate up into the hedge's corner, onto the red plate, and see what turns up.",
            "-> Ask");
        gardener.Name = "Gardener";
        gardener.Transform.Position = new Vector3(Cell(5, 6) + new Vector2(0, 0.5f), 0);
        var gardenerSprite = gardener.GetComponent<Sprite>()!;
        (gardenerSprite.Image, gardenerSprite.Size, gardenerSprite.Tint) = (TemplateArt.IsoGardener, new Vector2(0.8f, 1.2f), Color.White);
        // Talked to from the diamonds round them.
        var reach = gardener.GetComponent<BoxCollider>()!;
        (reach.Size, reach.Offset) = (new Vector2(3, 2), new Vector2(0, -0.45f));
        gardener.Children[0].Transform.Position = new Vector3(0, 1.05f, 0);
        var gardenerSpeaker = gardener.AddComponent<Speaker>();
        (gardenerSpeaker.Name, gardenerSpeaker.PortraitImage) = ("Gardener", TemplateArt.IsoGardenerPortrait);

        AddDialogueBox(scene);
        AddPauseMenu(scene, menuScene: null);
        return scene;
    }

    // The middle of the courtyard's cell (x, y) in the world: where something standing in it has its feet.
    private static Vector2 Cell(int x, int y) => CourtyardAt + new Vector2((x - y) * 1f, -(x + y + 1) * 0.5f);

    // A gem floating over the middle of cell (x, y), picked up by walking onto its diamond: its collider is at the
    // middle, where the player's feet go.
    private static Entity Gem(string name, int x, int y, Color color)
    {
        var gem = Collectible(name, Cell(x, y) + new Vector2(0, 0.35f), color);
        var collider = gem.GetComponent<CircleCollider>()!;
        (collider.Radius, collider.Offset) = (0.4f, new Vector2(0, -0.35f));
        return gem;
    }

    // Someone standing in cell (x, y): a picture with its feet on the cell's middle, and a flat box at its feet that it
    // collides with (on an isometric map, the diamond inside that box is what meets the walls).
    private static Entity Person(string name, int x, int y, string image)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(Cell(x, y) + new Vector2(0, 0.5f), 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size) = (image, new Vector2(0.8f, 1.2f));
        var collider = entity.AddComponent<BoxCollider>();
        (collider.Size, collider.Offset) = (new Vector2(0.6f, 0.3f), new Vector2(0, -0.45f));
        return entity;
    }
}
