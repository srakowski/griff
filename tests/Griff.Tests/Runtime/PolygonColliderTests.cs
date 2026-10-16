using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Polygon colliders: their points fixed up and split into convex pieces, then met by everything: bodies that turn (tipping,
/// rolling over, resting, stacking and sleeping on boxes, circles, polygons, tile runs, slopes, hex and isometric walls),
/// bodies moved the old way (standing on and walking up a polygon hill), rays, overlaps, triggers, shadows and grid movers.
/// </summary>
public class PolygonColliderTests
{
    private const float Frame = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public PolygonColliderTests() => _game = new Game(_scene, _input);

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private static PolygonCollider Polygon(string points) => new() { Points = points };

    private Entity AddWall(string name, Vector2 position, Vector2 size)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<BoxCollider>().Size = size;
        _scene.Add(entity);
        return entity;
    }

    private Entity AddShape(string name, Vector2 position, string points)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<PolygonCollider>().Points = points;
        _scene.Add(entity);
        return entity;
    }

    private Body AddBody(string name, Vector2 position, string points, float degrees = 0)
    {
        var entity = AddShape(name, position, points);
        entity.Transform.Rotation = new Vector3(0, 0, degrees);
        var body = entity.AddComponent<Body>();
        body.Rotates = true;
        return body;
    }

    private static Vector2 At(Body body) => new(body.Entity!.Transform.Position.X, body.Entity.Transform.Position.Y);

    private static float Angle(Body body) => body.Entity!.Transform.Rotation.Z;

    // How far from lying on one of its sides a regular shape with `sides` sides is, in degrees.
    private static float OffFlat(float degrees, int sides)
    {
        var step = 360f / sides;
        var off = ((degrees % step) + step) % step;
        return MathF.Min(off, step - off);
    }

    private static float Area(PolygonCollider polygon)
    {
        var outline = polygon.Outline;
        var sum = 0f;
        for (var i = 0; i < outline.Count; i++)
            sum += outline[i].X * outline[(i + 1) % outline.Count].Y - outline[i].Y * outline[(i + 1) % outline.Count].X;
        return sum / 2;
    }

    private static string Regular(int sides, float radius, float turn = 0) => string.Join("; ", Enumerable.Range(0, sides).Select(i =>
    {
        var angle = turn + MathF.Tau * i / sides;
        return FormattableString.Invariant($"{radius * MathF.Cos(angle):0.####} {radius * MathF.Sin(angle):0.####}");
    }));

    // ----- Its shape -----

    [Fact]
    public void A_convex_shape_is_one_piece_going_round_anticlockwise_without_points_along_its_sides()
    {
        var polygon = Polygon("0 0; 0 1; 0.5 1; 1 1; 1 0");

        Assert.Null(polygon.Problem);
        Assert.Equal(1, polygon.PieceCount);
        Assert.Equal(4, polygon.Outline.Count);
        Assert.Equal(1, Area(polygon), 4);
        Assert.Equal(5, polygon.GivenPoints.Count);
    }

    [Fact]
    public void A_concave_shape_is_split_into_convex_pieces_that_cover_it()
    {
        var entity = new Entity("L");
        var polygon = entity.AddComponent<PolygonCollider>();
        polygon.Points = "0 0; 3 0; 3 1; 1 1; 1 3; 0 3";
        _scene.Add(entity);

        Assert.Null(polygon.Problem);
        Assert.Equal(2, polygon.PieceCount);
        Assert.Equal(5, Area(polygon), 4);
        Assert.True(polygon.Contains(new Vector2(2.5f, 0.5f)));
        Assert.True(polygon.Contains(new Vector2(0.5f, 2.5f)));
        Assert.False(polygon.Contains(new Vector2(2, 2)));
        Assert.Equal(new Box(new Vector2(1.5f, 1.5f), new Vector2(1.5f, 1.5f)), polygon.WorldBounds);
    }

    [Fact]
    public void A_shape_with_many_corners_is_split_into_pieces_of_no_more_than_eight()
    {
        var entity = new Entity("Gear");
        var polygon = entity.AddComponent<PolygonCollider>();
        polygon.Points = Regular(24, 2);
        _scene.Add(entity);

        Assert.Null(polygon.Problem);
        Assert.Equal(24, polygon.Outline.Count);
        Assert.True(polygon.PieceCount > 1);
        for (var piece = 0; piece < polygon.PieceCount; piece++)
            Assert.InRange(PieceOf(polygon, piece).Length, 3, 8);
        Assert.True(polygon.Contains(new Vector2(1.5f, 0.2f)));
        Assert.False(polygon.Contains(new Vector2(2.1f, 0)));
    }

    private static Vector2[] PieceOf(PolygonCollider polygon, int piece) => polygon.WorldPiece(piece).ToArray();

    [Theory]
    [InlineData("", "no points")]
    [InlineData("nonsense", "no points")]
    [InlineData("1 1", "only one point")]
    [InlineData("0 0; 2 0", "only two points")]
    [InlineData("0 0; 1 1; 2 2; 3 3", "in a line")]
    [InlineData("0 0; 1 1; 1 0; 0 1", "cross")]
    [InlineData("0 0; 2 0; 2 2; 2 3; 2 2; 0 2", "doubles back")]
    public void Points_that_dont_make_a_proper_shape_are_fixed_up_and_said_so(string points, string problem)
    {
        var entity = new Entity("Bad");
        var polygon = entity.AddComponent<PolygonCollider>();
        polygon.Points = points;
        _scene.Add(entity);

        Assert.Contains(problem, polygon.Problem);
        Assert.Equal(polygon.Problem, polygon.SetupNote());
        Assert.True(polygon.Outline.Count >= 3);
        Assert.True(Area(polygon) > 0);
        Assert.True(polygon.PieceCount >= 1);
        Assert.True(polygon.Contains(polygon.WorldCentroid));
    }

    [Fact]
    public void A_bow_tie_becomes_the_shape_round_its_points_and_more_than_sixty_four_points_are_cut_short()
    {
        var bowTie = Polygon("0 0; 2 2; 2 0; 0 2");
        Assert.Equal(4, Area(bowTie), 4);
        Assert.Equal(1, bowTie.PieceCount);

        var many = Polygon(Regular(70, 3));
        Assert.Contains("only the first 64", many.Problem);
        Assert.Equal(64, many.Outline.Count);
    }

    [Fact]
    public void Any_points_at_all_make_a_shape_of_convex_pieces_and_never_throw()
    {
        var random = new Random(7);
        for (var trial = 0; trial < 400; trial++)
        {
            var count = random.Next(0, 14);
            var points = Enumerable.Range(0, count).Select(_ => new Vector2(random.Next(-4, 5) * 0.5f, random.Next(-4, 5) * 0.5f)).ToList();
            var entity = new Entity("Random");
            var polygon = entity.AddComponent<PolygonCollider>();
            polygon.Points = string.Join("; ", points.Select(p => FormattableString.Invariant($"{p.X} {p.Y}")));
            _scene.Add(entity);

            Assert.True(Area(polygon) > 0, polygon.Points);
            var covered = 0f;
            for (var piece = 0; piece < polygon.PieceCount; piece++)
            {
                var corners = PieceOf(polygon, piece);
                Assert.InRange(corners.Length, 3, 8);
                for (var i = 0; i < corners.Length; i++)
                {
                    var (a, b, c) = (corners[i], corners[(i + 1) % corners.Length], corners[(i + 2) % corners.Length]);
                    Assert.True((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > -1e-5f, $"Piece {piece} of {polygon.Points} isn't convex.");
                    covered += (a.X * b.Y - a.Y * b.X) / 2;
                }
            }

            Assert.Equal(Area(polygon), covered, 3);
            _scene.Remove(entity);
        }
    }

    [Fact]
    public void Its_points_are_saved_with_the_scene()
    {
        var shape = AddShape("Rock", new Vector2(1, 2), "0 0; 2 0; 1 1.5");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene)).Find(shape.Id)!.GetComponent<PolygonCollider>()!;

        Assert.Equal("0 0; 2 0; 1 1.5", loaded.Points);
        Assert.Equal(PolygonCollider.DefaultPoints, new PolygonCollider().Points);
    }

    [Fact]
    public void It_stays_upright_unless_its_body_rotates_and_its_middle_and_inertia_are_where_it_balances()
    {
        var still = AddShape("Still", new Vector2(5, 0), "0 0; 3 0; 0 3");
        still.Transform.Rotation = new Vector3(0, 0, 90);
        var polygon = still.GetComponent<PolygonCollider>()!;
        Assert.Equal(new Vector2(5, 0), polygon.WorldPoints[0]);
        Assert.Equal(new Vector2(8, 0), polygon.WorldPoints[1]);

        var body = still.AddComponent<Body>();
        body.Rotates = true;
        Assert.True(Vector2.Distance(new Vector2(5, 3), polygon.WorldPoints[1]) < 1e-5f);
        Assert.True(Vector2.Distance(new Vector2(4, 1), body.CenterOfMass) < 1e-5f);

        // A square polygon turns as hard as a box the same size.
        var square = AddBody("Square", Vector2.Zero, "-1 -0.5; 1 -0.5; 1 0.5; -1 0.5");
        var box = new Entity("Box");
        box.AddComponent<BoxCollider>().Size = new Vector2(2, 1);
        var boxBody = box.AddComponent<Body>();
        _scene.Add(box);
        Assert.Equal(boxBody.Inertia, square.Inertia, 5);
    }

    // ----- Bodies that turn -----

    [Fact]
    public void A_triangle_dropped_on_its_point_tips_over_and_rests_on_a_side_asleep()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var triangle = AddBody("Triangle", new Vector2(0, 2), Regular(3, 0.8f, -MathF.PI / 2), degrees: 3);

        Run(4);

        Assert.True(triangle.Asleep);
        Assert.True(OffFlat(Angle(triangle) - 60, 3) < 0.5f, $"Resting at {Angle(triangle)} degrees.");
        // Lying on a side, its middle is a third of its height (1.2) up.
        Assert.Equal(0.4f, triangle.CenterOfMass.Y, 2);
        Assert.Equal("Floor", triangle.Ground?.Name);
    }

    [Fact]
    public void A_square_polygon_lands_and_rests_as_a_box_crate_does()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var square = AddBody("Square", new Vector2(-3, 2), "-0.5 -0.5; 0.5 -0.5; 0.5 0.5; -0.5 0.5", degrees: 25);
        var crate = new Entity("Crate");
        crate.Transform.Position = new Vector3(3, 2, 0);
        crate.Transform.Rotation = new Vector3(0, 0, 25);
        crate.AddComponent<BoxCollider>();
        var crateBody = crate.AddComponent<Body>();
        crateBody.Rotates = true;
        _scene.Add(crate);

        Run(4);

        Assert.True(square.Asleep && crateBody.Asleep);
        Assert.Equal(0.5f, At(square).Y, 2);
        Assert.Equal(crate.Transform.Position.Y, At(square).Y, 2);
        Assert.Equal(OffFlat(Angle(crateBody), 4), OffFlat(Angle(square), 4), 0);
        Assert.Equal(crate.Transform.Position.X - 3, At(square).X + 3, 1);
    }

    [Fact]
    public void An_octagon_rolls_down_a_slope_and_comes_to_rest_on_the_floor()
    {
        AddWall("Floor", new Vector2(10, -0.5f), new Vector2(40, 1));
        var ramp = AddShape("Ramp", new Vector2(-6, 0), "0 0; 6 0; 0 3");
        var octagon = AddBody("Octagon", new Vector2(-5.2f, 3.6f), Regular(8, 0.5f, MathF.PI / 8));

        Run(1.5f);
        Assert.True(octagon.Spin < -90, $"Spinning at only {octagon.Spin}.");
        Run(5);

        Assert.True(octagon.Asleep);
        Assert.True(At(octagon).X > 1, $"Stopped at {At(octagon)}.");
        Assert.True(OffFlat(Angle(octagon), 8) < 1, $"Resting at {Angle(octagon)} degrees.");
        Assert.Equal("Floor", octagon.Ground?.Name);
        Assert.NotNull(ramp);
    }

    [Fact]
    public void Polygons_stack_on_each_other_and_fall_asleep()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var hexagons = new List<Body>();
        for (var i = 0; i < 6; i++)
            hexagons.Add(AddBody($"Hexagon {i}", new Vector2(0.02f * (i % 2), 0.45f + i * 0.9f), Regular(6, 0.5f)));

        Run(5);

        Assert.All(hexagons, h => Assert.True(h.Asleep, $"{h.Entity!.Name} is still awake at {At(h)}."));
        for (var i = 0; i < hexagons.Count; i++)
        {
            Assert.InRange(At(hexagons[i]).X, -0.1f, 0.1f);
            Assert.Equal(0.4330127f + i * 0.8660254f, At(hexagons[i]).Y, 1);
        }
    }

    [Fact]
    public void An_l_shaped_body_lands_on_its_long_side_and_a_ball_settles_in_a_cup()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var el = AddBody("L", new Vector2(-6, 2), "0 0; 3 0; 3 1; 1 1; 1 3; 0 3", degrees: 10);
        var cup = AddShape("Cup", new Vector2(4, 0), "-2 0; 2 0; 2 2; 1.6 2; 1.6 0.4; -1.6 0.4; -1.6 2; -2 2");
        Assert.True(cup.GetComponent<PolygonCollider>()!.PieceCount > 1);
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(5, 3, 0);
        ball.AddComponent<CircleCollider>().Radius = 0.3f;
        var ballBody = ball.AddComponent<Body>();
        ballBody.Rotates = true;
        _scene.Add(ball);

        Run(5);

        Assert.True(el.Asleep);
        Assert.True(OffFlat(Angle(el), 4) < 1, $"The L rests at {Angle(el)} degrees.");
        Assert.True(el.Entity!.GetComponent<PolygonCollider>()!.WorldBounds.Min.Y > -0.02f);
        // The ball dropped in rolled down to the cup's floor (at y = 0.4) and stopped inside it.
        Assert.True(ballBody.Asleep);
        Assert.Equal(0.7f, ball.Transform.Position.Y, 2);
        Assert.InRange(ball.Transform.Position.X, 2.4f, 5.6f);
        Assert.Equal("Cup", ballBody.Ground?.Name);
    }

    [Fact]
    public void A_box_crate_slides_down_a_polygon_hill_and_a_polygon_rests_on_tile_floors_and_slopes()
    {
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.SlopeUpTiles = "2";
        map.Tiles = ["1..................1", "1..................1", "1...........2111...1", "11111111111111111111"];
        level.Transform.Position = new Vector3(0, 4, 0);
        _scene.Add(level);
        var rock = AddBody("Rock", new Vector2(5, 3), "-0.6 -0.4; 0.5 -0.5; 0.7 0.2; 0 0.6; -0.5 0.3", degrees: 40);
        var wedge = AddBody("Wedge", new Vector2(12.5f, 3), "-0.4 -0.3; 0.4 -0.3; 0 0.3");

        Run(4);

        Assert.True(rock.Asleep && wedge.Asleep);
        Assert.Equal("Level", rock.Ground?.Name);
        var rockShape = rock.Entity!.GetComponent<PolygonCollider>()!;
        Assert.Equal(1, rockShape.WorldBounds.Min.Y, 1);
        Assert.DoesNotContain(level, _game.Physics.OverlapPoint(rockShape.WorldCentroid));
        // The wedge slid down the slope tile onto the floor, or rests on it, but never in it.
        Assert.True(wedge.Entity!.GetComponent<PolygonCollider>()!.WorldBounds.Min.Y > 0.95f);
    }

    [Fact]
    public void A_polygon_lands_on_a_hex_floor()
    {
        var hex = new Entity("Hex");
        var hexMap = hex.AddComponent<Tilemap>();
        hexMap.Shape = TileShape.Hex;
        hexMap.Tiles = ["1.......1", "1.......1", "1.......1", "111111111", "111111111"];
        _scene.Add(hex);
        var pentagon = AddBody("Pentagon", new Vector2(4.5f, -0.5f), Regular(5, 0.4f), degrees: 10);

        Run(3);

        Assert.True(pentagon.IsGrounded);
        var shape = pentagon.Entity!.GetComponent<PolygonCollider>()!;
        // Row 3's hexagons' points reach up to y = -2.25: it rests on them (or in the notch between two), not in them.
        Assert.InRange(shape.WorldBounds.Min.Y, -2.75f, -2.2f);
        Assert.DoesNotContain(hex, _game.Physics.OverlapPoint(shape.WorldCentroid - new Vector2(0, 0.25f)));
    }

    [Fact]
    public void On_an_isometric_map_a_polygon_is_kept_off_the_walls_by_the_diamond_under_it()
    {
        var iso = new Entity("Iso");
        var isoMap = iso.AddComponent<Tilemap>();
        isoMap.Shape = TileShape.Isometric;
        isoMap.TileSize = 2;
        isoMap.Tiles = ["1111", "1..1", "1..1", "1111"];
        _scene.Add(iso);
        var floor = isoMap.PointAt(2, 2);
        var rock = AddBody("Rock", floor, "-0.3 -0.2; 0.3 -0.2; 0.2 0.2; -0.2 0.2", degrees: 30);
        rock.GravityScale = 0;
        rock.Velocity = new Vector2(6, 0);

        Run(2);

        Assert.InRange(Vector2.Distance(floor, rock.CenterOfMass), 0.3f, 2);
        Assert.DoesNotContain(iso, _game.Physics.OverlapPoint(rock.CenterOfMass));
        Assert.Equal(0, rock.Velocity.X, 2);
    }

    [Fact]
    public void A_mirrored_concave_body_still_meets_things_with_its_outside()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        var el = AddBody("L", new Vector2(0, 2), "0 0; 3 0; 3 1; 1 1; 1 3; 0 3");
        el.Entity!.Transform.Scale = new Vector3(-1, 1, 1);

        Run(4);

        Assert.True(el.Asleep);
        var shape = el.Entity.GetComponent<PolygonCollider>()!;
        Assert.Equal(0, shape.WorldBounds.Min.Y, 1);
        Assert.Equal(5, Area(shape), 4);
    }

    [Fact]
    public void A_jumbled_heap_of_polygons_or_crates_in_a_bin_falls_asleep()
    {
        static Scene Bin(Func<int, Entity> make)
        {
            var scene = new Scene();
            var floor = new Entity("Floor");
            floor.Transform.Position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider>().Size = new Vector2(40, 1);
            scene.Add(floor);
            foreach (var x in new[] { -5.5f, 5.5f })
            {
                var side = new Entity("Side");
                side.Transform.Position = new Vector3(x, 8, 0);
                side.AddComponent<BoxCollider>().Size = new Vector2(1, 16);
                scene.Add(side);
            }

            // Ten a row, each turned a different way, a little off true, so they tumble into a jumble.
            for (var i = 0; i < 60; i++)
            {
                var shape = make(i);
                shape.Transform.Position = new Vector3(-4.5f + i % 10 + i / 10 % 2 * 0.3f, 0.5f + i / 10 * 1.1f, 0);
                shape.Transform.Rotation = new Vector3(0, 0, i * 37 % 360);
                shape.AddComponent<Body>().Rotates = true;
                scene.Add(shape);
            }

            return scene;
        }

        Entity Shape(int i)
        {
            var entity = new Entity($"Shape {i}");
            entity.AddComponent<PolygonCollider>().Points = (i % 4) switch
            {
                0 => Regular(3, 0.5f),
                1 => Regular(5, 0.45f),
                2 => Regular(6, 0.45f),
                _ => "-0.45 -0.45; 0.45 -0.45; 0.45 -0.1; -0.1 -0.1; -0.1 0.45; -0.45 0.45",
            };
            return entity;
        }

        static Entity Crate(int i)
        {
            var entity = new Entity($"Crate {i}");
            entity.AddComponent<BoxCollider>().Size = new Vector2(0.9f, 0.9f);
            return entity;
        }

        foreach (var scene in new[] { Bin(Shape), Bin(Crate) })
        {
            var game = new Game(scene, new InputState());
            for (var frame = 0; frame < 180; frame++)
                game.Tick(Frame);
            var bodies = scene.Entities.Select(e => e.GetComponent<Body>()).OfType<Body>().ToList();
            Assert.All(bodies, b => Assert.True(b.Asleep, $"{b.Entity!.Name} is awake after 3 s."));

            // And they stay asleep, wedged as they are.
            var before = bodies.Select(b => b.Entity!.Transform.Position).ToList();
            for (var frame = 0; frame < 120; frame++)
                game.Tick(Frame);
            Assert.All(bodies, b => Assert.True(b.Asleep));
            Assert.Equal(before, bodies.Select(b => b.Entity!.Transform.Position).ToList());
        }
    }

    // ----- Everything else meeting it -----

    [Fact]
    public void A_player_walks_up_a_polygon_hill_and_down_the_other_side_and_lands_on_its_slant()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(60, 1));
        AddShape("Hill", new Vector2(4, 0), "0 0; 4 1.5; 8 0");
        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(0, 0.5f, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 1);
        var body = walker.AddComponent<Body>();
        _scene.Add(walker);
        Run(0.3f);

        var highest = 0f;
        var grounded = 0;
        for (var i = 0; i < 300 && walker.Transform.Position.X < 14; i++)
        {
            body.Velocity = body.Velocity with { X = 3 };
            Run(Frame);
            highest = MathF.Max(highest, walker.Transform.Position.Y);
            grounded += body.IsGrounded ? 1 : 0;
        }

        Assert.True(walker.Transform.Position.X >= 14, $"Stuck at {walker.Transform.Position}.");
        Assert.True(highest > 1.6f, $"Only got up to {highest}.");
        Assert.True(grounded > 250, $"Grounded only {grounded} steps of the walk.");

        // Dropped onto its slant, a box stands on it rather than on the box round it.
        var dropped = new Entity("Dropped");
        dropped.Transform.Position = new Vector3(6, 4, 0);
        dropped.AddComponent<BoxCollider>().Size = new Vector2(0.2f, 0.2f);
        var droppedBody = dropped.AddComponent<Body>();
        _scene.Add(dropped);
        Run(1.5f);
        Assert.True(droppedBody.IsGrounded);
        Assert.Equal(0.75f + 0.1f, dropped.Transform.Position.Y, 1);
    }

    [Fact]
    public void A_body_that_doesnt_rotate_with_a_polygon_falls_lands_and_is_stopped_by_walls_and_shoves_one_that_does()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddWall("Wall", new Vector2(6, 2), new Vector2(1, 4));
        var walker = AddShape("Walker", new Vector2(0, 3), "-0.3 -0.5; 0.3 -0.5; 0.4 0.5; -0.4 0.5");
        var body = walker.AddComponent<Body>();
        var rock = AddBody("Rock", new Vector2(2, 0.4f), Regular(6, 0.4f));
        Run(1);
        Assert.True(body.IsGrounded);
        Assert.Equal(0.5f, walker.Transform.Position.Y, 2);

        for (var i = 0; i < 150; i++)
        {
            body.Velocity = body.Velocity with { X = 3 };
            Run(Frame);
        }

        // It shoved the rock along to the wall and stopped at it.
        Assert.True(At(rock).X > 4.5f, $"The rock is only at {At(rock)}.");
        Assert.InRange(walker.Transform.Position.X, 3, 5.1f);
        Assert.True(walker.GetComponent<PolygonCollider>()!.WorldBounds.Max.X <= 5.5f + 1e-3f);
    }

    [Fact]
    public void Rays_overlaps_triggers_and_shadows_meet_it_as_it_is()
    {
        var cup = AddShape("Cup", Vector2.Zero, "-2 0; 2 0; 2 2; 1.6 2; 1.6 0.4; -1.6 0.4; -1.6 2; -2 2");
        _game.Tick(Frame);

        var down = _game.Physics.Raycast(new Vector2(0, 5), -Vector2.UnitY);
        Assert.NotNull(down);
        Assert.Equal(0.4f, down.Value.Point.Y, 4);
        Assert.Equal(Vector2.UnitY, down.Value.Normal);
        var across = _game.Physics.Raycast(new Vector2(-5, 1), Vector2.UnitX);
        Assert.Equal(-2, across!.Value.Point.X, 4);
        Assert.Equal(-Vector2.UnitX, across.Value.Normal);
        Assert.Null(_game.Physics.Raycast(new Vector2(0, 0.2f), Vector2.UnitX));

        Assert.Empty(_game.Physics.OverlapPoint(new Vector2(0, 1)));
        Assert.Single(_game.Physics.OverlapPoint(new Vector2(1.8f, 1.9f)));
        Assert.Empty(_game.Physics.OverlapBox(new Vector2(0, 1.2f), new Vector2(3, 1)));
        Assert.Single(_game.Physics.OverlapBox(new Vector2(0, 0.5f), new Vector2(3, 0.4f)));
        Assert.Empty(_game.Physics.OverlapCircle(new Vector2(0, 1.2f), 0.7f));
        Assert.Single(_game.Physics.OverlapCircle(new Vector2(2.5f, 1), 0.6f));

        // A polygon trigger tells what comes into it.
        var zone = AddShape("Zone", new Vector2(10, 0), "0 0; 2 0; 1 2");
        zone.GetComponent<PolygonCollider>()!.IsTrigger = true;
        var log = zone.AddComponent<TriggerLog>();
        var visitor = AddWall("Visitor", new Vector2(10.2f, 1.5f), new Vector2(0.2f, 0.2f));
        _game.Tick(Frame);
        Assert.Empty(log.Events);
        visitor.Transform.Position = new Vector3(11, 0.5f, 0);
        _game.Tick(Frame);
        Assert.Equal(["enter Visitor"], log.Events);

        // Its sides cast shadows, going round it clockwise.
        var edges = Shadows.Edges(_scene).Where(e => e.Owner == cup).ToList();
        Assert.Equal(8, edges.Count);
        Assert.All(edges, e => Assert.False(cup.GetComponent<PolygonCollider>()!.Contains((e.A + e.B) / 2 + e.Outward * 0.01f)));
    }

    [Fact]
    public void Grid_movers_count_cells_under_a_polygon_as_walls()
    {
        var room = new Entity("Room");
        room.AddComponent<Tilemap>().Tiles = ["11111111", "1......1", "11111111"];
        _scene.Add(room);
        AddShape("Rock", new Vector2(4.5f, -1.5f), "-0.4 -0.4; 0.4 -0.4; 0 0.4");
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(1.5f, -1.5f, 0);
        player.AddComponent<BoxCollider>().Size = new Vector2(0.8f);
        player.AddComponent<Body>().GravityScale = 0;
        var movement = player.AddComponent<GridMovement>();
        _scene.Add(player);
        Run(Frame);

        for (var i = 0; i < 4; i++)
        {
            _input.SetKey(Key.D, true);
            Run(Frame);
            _input.SetKey(Key.D, false);
            Run(0.3f);
        }

        Assert.Equal((3, 1), movement.Cell);
    }

    // ----- Every machine, every step -----

    [Fact]
    public void The_same_tumble_twice_ends_the_same_to_the_last_bit_and_a_rewind_replays_it()
    {
        static Scene Tumble()
        {
            var scene = new Scene();
            var floor = new Entity("Floor");
            floor.Transform.Position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider>().Size = new Vector2(40, 1);
            scene.Add(floor);
            var hill = new Entity("Hill");
            hill.AddComponent<PolygonCollider>().Points = "-8 0; -2 0; -8 3";
            scene.Add(hill);
            for (var i = 0; i < 5; i++)
            {
                var shape = new Entity($"Shape {i}");
                shape.Transform.Position = new Vector3(-6 + i * 1.3f, 4 + i * 0.7f, 0);
                shape.Transform.Rotation = new Vector3(0, 0, i * 17);
                shape.AddComponent<PolygonCollider>().Points = i % 2 == 0 ? Regular(5 + i, 0.5f) : "0 0; 1.2 0; 1.2 0.4; 0.4 0.4; 0.4 1; 0 1";
                var body = shape.AddComponent<Body>();
                (body.Rotates, body.Bounciness) = (true, 0.2f);
                scene.Add(shape);
            }

            return scene;
        }

        (Vector3, Vector3, float)[] Play(Scene scene)
        {
            var game = new Game(scene, new InputState());
            for (var frame = 0; frame < 150; frame++)
                game.Tick(Frame);
            return scene.Entities.Where(e => e.Name.StartsWith("Shape", StringComparison.Ordinal))
                .Select(e => (e.Transform.Position, e.Transform.Rotation, e.GetComponent<Body>()!.Spin)).ToArray();
        }

        Assert.Equal(Play(Tumble()), Play(Tumble()));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = Tumble();
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(serializer, capacity: 100, framesPerSnapshot: 10);
        for (var frame = 0; frame < 30; frame++)
        {
            game.Tick(Frame);
            timeline.Record(scene, game.Time.Total);
        }

        for (var frame = 0; frame < 100; frame++)
            game.Tick(Frame);
        var restored = timeline.Restore(1);
        var again = new Game(restored, new InputState());
        for (var frame = 0; frame < 110; frame++)
            again.Tick(Frame);

        foreach (var shape in scene.Entities.Where(e => e.Name.StartsWith("Shape", StringComparison.Ordinal)))
        {
            var copy = restored.Find(shape.Id)!;
            Assert.Equal(shape.Transform.Position, copy.Transform.Position);
            Assert.Equal(shape.Transform.Rotation, copy.Transform.Rotation);
        }
    }

    [Fact]
    public void In_a_network_game_the_host_tumbles_a_polygon_and_players_see_it_turn()
    {
        static Scene Slope()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var floor = new Entity("Floor");
            floor.Transform.Position = new Vector3(0, -0.5f, 0);
            floor.AddComponent<BoxCollider>().Size = new Vector2(60, 1);
            scene.Add(floor);
            var shape = new Entity("Shape");
            shape.Transform.Position = new Vector3(0, 3, 0);
            shape.Transform.Rotation = new Vector3(0, 0, 20);
            shape.AddComponent<PolygonCollider>().Points = "-0.8 -0.4; 0.8 -0.4; 0 0.6";
            var body = shape.AddComponent<Body>();
            (body.Rotates, body.Velocity, body.Spin) = (true, new Vector2(3, 0), 200);
            scene.Add(shape);
            return scene;
        }

        var world = new Networking.NetWorld(Slope);
        world.Run(4);

        var host = world.Host.Scene.Entities.Single(e => e.Name == "Shape");
        var client = world.Client.Scene.Entities.Single(e => e.Name == "Shape");
        Assert.True(host.GetComponent<Body>()!.Asleep);
        Assert.True(Vector3.Distance(host.Transform.Position, client.Transform.Position) < 1e-3f, $"{host.Transform.Position} on the host, {client.Transform.Position} for the player.");
        Assert.Equal(host.Transform.Rotation.Z, client.Transform.Rotation.Z, 2);
    }

    [Fact]
    public void Stepping_polygons_makes_no_garbage()
    {
        AddWall("Floor", new Vector2(0, -0.5f), new Vector2(40, 1));
        AddShape("Hill", new Vector2(-10, 0), "0 0; 6 0; 6 1; 3 2; 0 1");
        AddShape("Cup", new Vector2(6, 0), "-2 0; 2 0; 2 2; 1.6 2; 1.6 0.4; -1.6 0.4; -1.6 2; -2 2");
        for (var i = 0; i < 8; i++)
        {
            var body = AddBody($"Shape {i}", new Vector2(-8 + i * 2, 4 + i % 2), i % 2 == 0 ? Regular(5 + i, 0.5f) : "0 0; 1.2 0; 1.2 0.4; 0.4 0.4; 0.4 1; 0 1", i * 13);
            body.Velocity = new Vector2(2, 0);
        }

        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(-12, 3, 0);
        walker.AddComponent<BoxCollider>().Size = new Vector2(0.6f, 1);
        walker.AddComponent<Body>();
        _scene.Add(walker);
        var light = new Entity("Light");
        light.AddComponent<Light>().Shadows = true;
        _scene.Add(light);
        var edges = new List<ShadowEdge>(256);
        Run(0.5f);

        var perStep = Allocations.PerCall(() =>
        {
            _game.Tick(Frame);
            edges.Clear();
            Shadows.AddEdges(_scene, edges);
        }, 20);

        Assert.True(perStep == 0, $"A step allocated {perStep:N0} bytes.");
    }

    private sealed class TriggerLog : Behavior
    {
        [DoNotSerialize]
        public List<string> Events { get; set; } = [];

        protected override void OnTriggerEnter(Entity other) => Events.Add($"enter {other.Name}");
    }
}
