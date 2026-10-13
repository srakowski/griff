using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Rendering;

// A first-person game's view (see FirstPersonView): the top-down map stood up around the viewer by casting a ray for each
// strip of the screen. Solid tilemaps and Blocks are walls, sprites and particles stand in the world as billboards hidden
// behind whatever wall is nearer, and the weapon in hand, crosshair and minimap go over it. Everything is ordinary quads
// across the camera's view, so the renderer needs nothing new; the HUD (things pinned to the screen) still goes on top.
public static partial class SceneRenderer
{
    // How dark walls facing north and south are next to those facing east and west.
    private const float SideShade = 0.72f;

    // Floor and ceiling are drawn in this many bands each, darkening into the fog towards the horizon.
    private const int FloorBands = 40;

    // Nothing closer than this is drawn: it would fill the screen.
    private const float NearPlane = 0.08f;

    private static readonly Color MapBack = new(0.05f, 0.06f, 0.08f, 0.72f);
    private static readonly Color MapWall = new(0.75f, 0.78f, 0.84f, 0.9f);
    private static readonly Color MapEnemy = Color.FromBytes(0xE5, 0x53, 0x4B);
    private static readonly Color MapPickup = Color.FromBytes(0xE3, 0xB3, 0x41);
    private static readonly Color MapPlayer = Color.FromBytes(0x57, 0xD3, 0x6B);

    // What a ray hit: how far (straight ahead, so walls don't bulge), and how to draw that strip of wall.
    private readonly record struct WallHit(float Depth, Vector2 Point, string? Image, Vector4 Source, Color Tint, bool Shaded);

    // A billboard: its depth, its span across the screen (clip space), and what to draw.
    private readonly record struct Standee(float Depth, float Left, float Right, float Bottom, float Top, string? Image, Vector4 Source,
        Color Tint, Color Flash, Entity Entity);

    /// <summary>
    /// The first-person view through <paramref name="viewer"/>'s eyes, as quads over <paramref name="view"/> (the camera's
    /// view of the world, unrotated), back to front.
    /// </summary>
    private static List<SpriteDraw> FirstPersonDraws(Game game, FirstPersonView fp, Entity viewer, Entity? camera, View view, float aspect)
    {
        var scene = game.Scene;
        var draws = new List<SpriteDraw>();
        var screen = new ScreenSpace(view);

        var world = viewer.Transform.WorldMatrix;
        var eye = new Vector2(world.M41, world.M42);
        var ahead = Vector2.TransformNormal(Vector2.UnitX, world);
        var dir = ahead.LengthSquared() > 1e-8f ? Vector2.Normalize(ahead) : Vector2.UnitX;
        var right = new Vector2(dir.Y, -dir.X);
        var plane = MathF.Tan(Math.Clamp(fp.FieldOfView, 10, 170) * MathF.PI / 360);
        var focal = 1 / plane;
        var vertical = focal * aspect;

        // The walk's bob lifts and drops the eyes; looking up and down slides the horizon; a shake jiggles everything.
        var movement = viewer.GetComponent<FirstPersonMovement>();
        var stride = movement?.Stride ?? 0;
        var phase = (movement?.Walked ?? 0) * MathF.PI / 1.1f;
        var bob = MathF.Max(0, fp.Bob);
        var eyeHeight = fp.EyeHeight + MathF.Sin(phase * 2) * 0.025f * bob * stride;
        var shake = camera?.GetComponent<Camera>()?.ShakeOffset ?? Vector2.Zero;
        var horizon = -MathF.Tan((movement?.Pitch ?? 0) * MathF.PI / 180) * vertical + shake.Y / view.Half.Y;
        var nudge = shake.X / view.Half.X;
        var wallHeight = MathF.Max(0.01f, fp.WallHeight);
        var lighting = Lighting.For(scene, camera);

        float FogOf(float depth) => fp.Fog.A > 0 && fp.FogDistance > 0 ? Math.Clamp(depth / fp.FogDistance, 0, 1) * fp.Fog.A : 0;
        Color Fogged(float depth) => fp.Fog with { A = FogOf(depth) };

        // Ceiling and floor: bands, each the color it is at its distance, fading into the fog at the horizon.
        for (var band = 0; band < FloorBands; band++)
        {
            var (near, far) = (band / (float)FloorBands, (band + 1) / (float)FloorBands);
            var middle = (near + far) / 2;

            var floorTop = horizon - (1 + horizon) * (1 - far) * (1 - far);
            var floorBottom = horizon - (1 + horizon) * (1 - near) * (1 - near);
            var floorRow = horizon - (1 + horizon) * (1 - middle) * (1 - middle);
            if (floorTop > -1 && horizon - floorRow > 1e-4f)
            {
                var depth = eyeHeight * vertical / (horizon - floorRow);
                draws.Add(screen.Rect(-1, Math.Max(-1, floorBottom), 1, Math.Min(1, floorTop), Shade(fp.Floor, depth, eye + dir * depth)));
            }

            var ceilingBottom = horizon + (1 - horizon) * (1 - far) * (1 - far);
            var ceilingTop = horizon + (1 - horizon) * (1 - near) * (1 - near);
            var ceilingRow = horizon + (1 - horizon) * (1 - middle) * (1 - middle);
            if (ceilingBottom < 1 && ceilingRow - horizon > 1e-4f)
            {
                var depth = (wallHeight - eyeHeight) * vertical / (ceilingRow - horizon);
                draws.Add(screen.Rect(-1, Math.Max(-1, ceilingBottom), 1, Math.Min(1, ceilingTop), Shade(fp.Ceiling, depth, eye + dir * depth)));
            }
        }

        Color Shade(Color color, float depth, Vector2 at)
        {
            var light = lighting.At(at);
            var lit = new Color(color.R * light.R, color.G * light.G, color.B * light.B, color.A);
            return Color.Lerp(lit, fp.Fog with { A = lit.A }, FogOf(depth));
        }

        // Walls: one ray a strip, to the nearest tile or block.
        var columns = Math.Clamp(fp.Columns, 16, 2000);
        var depths = new float[columns];
        var maxDepth = fp.Fog.A >= 1 && fp.FogDistance > 0 ? fp.FogDistance * 1.05f : 96;
        var tilemaps = scene.Active<Tilemap>().Where(t => t.Solid && t.TileSize > 0).ToList();
        var blocks = scene.Entities.Where(e => e.IsActive)
            .Select(e => (Block: e.GetComponent<Block>(), Collider: e.GetComponent<BoxCollider>()))
            .Where(b => b.Block is not null && b.Collider is not null)
            .Select(b => (b.Block!, Physics.BoundsOf(b.Collider!), b.Block!.Entity!.GetComponent<Sprite>()))
            .ToList();
        var strip = 2f / columns;
        for (var column = 0; column < columns; column++)
        {
            var across = (column + 0.5f) * strip - 1;
            var ray = dir + right * (across * plane);
            WallHit? nearest = null;
            foreach (var tilemap in tilemaps)
            {
                if (CastTilemap(tilemap, eye, ray, maxDepth, fp.ShadeSides) is { } hit && (nearest is null || hit.Depth < nearest.Value.Depth))
                    nearest = hit;
            }

            foreach (var (block, box, sprite) in blocks)
            {
                if (CastBlock(block, box, sprite, eye, ray, wallHeight, fp.ShadeSides) is { } hit && (nearest is null || hit.Depth < nearest.Value.Depth))
                    nearest = hit;
            }

            depths[column] = nearest?.Depth ?? float.PositiveInfinity;
            if (nearest is not { } wall)
                continue;

            var top = horizon + (wallHeight - eyeHeight) * vertical / wall.Depth;
            var bottom = horizon - eyeHeight * vertical / wall.Depth;
            var left = -1 + column * strip + nudge;
            var light = wall.Shaded ? lighting.At(wall.Point) : Color.White;
            var tint = new Color(wall.Tint.R * light.R, wall.Tint.G * light.G, wall.Tint.B * light.B, wall.Tint.A);
            // A hair wider than the strip, so no seams show between strips.
            draws.Add(screen.Rect(left - 0.001f, bottom, left + strip + 0.001f, top, tint, wall.Image, wall.Source) with
            {
                Flash = wall.Shaded ? Fogged(wall.Depth) : Color.Transparent,
            });
        }

        // Things in the world, furthest first, each strip hidden where a wall is nearer.
        var standees = Standees(game, fp, viewer, camera, eye, dir, right, focal, vertical, horizon + 0, eyeHeight, lighting, FogOf);
        foreach (var standee in standees.OrderByDescending(s => s.Depth))
        {
            var first = Math.Max(0, (int)MathF.Floor((standee.Left + 1) / strip));
            var last = Math.Min(columns - 1, (int)MathF.Ceiling((standee.Right + 1) / strip) - 1);
            var width = standee.Right - standee.Left;
            if (width <= 0)
                continue;
            for (var column = first; column <= last; column++)
            {
                if (depths[column] <= standee.Depth)
                    continue;
                var run = column;
                while (run + 1 <= last && depths[run + 1] > standee.Depth)
                    run++;
                var from = Math.Max(standee.Left, -1 + column * strip);
                var to = Math.Min(standee.Right, -1 + (run + 1) * strip);
                if (to > from)
                {
                    var (u0, u1) = ((from - standee.Left) / width, (to - standee.Left) / width);
                    var source = standee.Source;
                    var part = new Vector4(source.X + source.Z * u0, source.Y, source.Z * (u1 - u0), source.W);
                    draws.Add(screen.Rect(from + nudge, standee.Bottom, to + nudge, standee.Top, standee.Tint, standee.Image, part, standee.Entity) with
                    {
                        Flash = standee.Flash,
                    });
                }

                column = run;
            }
        }

        draws.AddRange(WeaponDraws(game, fp, viewer, screen, aspect, stride, phase));
        if (fp.Crosshair.A > 0)
            draws.AddRange(CrosshairDraws(screen, fp.Crosshair));
        if (fp.Minimap)
            draws.AddRange(MinimapDraws(scene, viewer, eye, dir, tilemaps, blocks.Select(b => b.Item2), screen, aspect));
        return draws;
    }

    // ----- Rays -----

    // Steps a ray cell by cell across a tilemap's grid until it reaches a wall tile.
    private static WallHit? CastTilemap(Tilemap tilemap, Vector2 eye, Vector2 ray, float maxDepth, bool shadeSides)
    {
        if (!Matrix4x4.Invert(tilemap.Entity!.Transform.WorldMatrix, out var inverse))
            return null;
        var size = tilemap.TileSize;
        var local = Vector2.Transform(eye, inverse);
        var localRay = Vector2.TransformNormal(ray, inverse);
        // Grid space: X across, Y down the rows, one unit a cell.
        var (gx, gy) = (local.X / size, -local.Y / size);
        var (rx, ry) = (localRay.X / size, -localRay.Y / size);
        var (cellX, cellY) = ((int)MathF.Floor(gx), (int)MathF.Floor(gy));
        var deltaX = rx == 0 ? float.PositiveInfinity : MathF.Abs(1 / rx);
        var deltaY = ry == 0 ? float.PositiveInfinity : MathF.Abs(1 / ry);
        var stepX = rx < 0 ? -1 : 1;
        var stepY = ry < 0 ? -1 : 1;
        var sideX = rx < 0 ? (gx - cellX) * deltaX : (cellX + 1 - gx) * deltaX;
        var sideY = ry < 0 ? (gy - cellY) * deltaY : (cellY + 1 - gy) * deltaY;
        var steps = (int)((MathF.Abs(rx) + MathF.Abs(ry)) * maxDepth) + 4;
        for (var i = 0; i < steps; i++)
        {
            float depth;
            bool across;
            if (sideX < sideY)
            {
                depth = sideX;
                sideX += deltaX;
                cellX += stepX;
                across = true;
            }
            else
            {
                depth = sideY;
                sideY += deltaY;
                cellY += stepY;
                across = false;
            }

            if (depth > maxDepth)
                return null;
            if (!tilemap.IsBlocked(cellX, cellY))
                continue;

            // Where along the face it hit, 0 to 1 from the left as seen from outside.
            var (hx, hy) = (gx + rx * depth, gy + ry * depth);
            var u = across ? hy - MathF.Floor(hy) : hx - MathF.Floor(hx);
            if (across ? rx < 0 : ry > 0)
                u = 1 - u;
            var tile = tilemap.GetTile(cellX, cellY);
            var shown = tilemap.ShownTile(cellX, cellY, tile);
            var image = tilemap.ImageOf(shown);
            var source = image is null ? new Vector4(0, 0, 1, 1) : tilemap.TileRect(shown);
            var shade = shadeSides && !across ? SideShade : 1;
            var tint = new Color(tilemap.Tint.R * shade, tilemap.Tint.G * shade, tilemap.Tint.B * shade, tilemap.Tint.A);
            return new WallHit(depth, eye + ray * depth, image, Strip(source, u), tint, true);
        }

        return null;
    }

    // A ray against a block's box: the face it meets first.
    private static WallHit? CastBlock(Block block, Box box, Sprite? sprite, Vector2 eye, Vector2 ray, float wallHeight, bool shadeSides)
    {
        var (min, max) = (box.Min, box.Max);
        // Along each axis, where the ray is between the box's two sides (all the way, if it runs parallel to them and between).
        static (float Near, float Far) Slab(float from, float step, float low, float high)
        {
            if (step != 0)
            {
                var (a, b) = ((low - from) / step, (high - from) / step);
                return (MathF.Min(a, b), MathF.Max(a, b));
            }

            return from >= low && from <= high ? (float.NegativeInfinity, float.PositiveInfinity) : (float.PositiveInfinity, float.NegativeInfinity);
        }

        var (nearX, farX) = Slab(eye.X, ray.X, min.X, max.X);
        var (nearY, farY) = Slab(eye.Y, ray.Y, min.Y, max.Y);
        var near = MathF.Max(nearX, nearY);
        var far = MathF.Min(farX, farY);
        if (!(near > NearPlane) || near > far)
            return null;

        var hit = eye + ray * near;
        var across = nearX >= nearY;
        // How far along the face, from its left as seen from outside, and how long it is.
        var (along, length) = across
            ? (ray.X > 0 ? max.Y - hit.Y : hit.Y - min.Y, max.Y - min.Y)
            : (ray.Y > 0 ? hit.X - min.X : max.X - hit.X, max.X - min.X);
        // The picture repeats every wall-height or so along a long face, rather than stretching.
        var repeats = MathF.Max(1, MathF.Round(length / wallHeight));
        var u = length > 0 ? along / length * repeats : 0;
        u -= MathF.Floor(u);
        if (sprite is { FlipX: true })
            u = 1 - u;

        var image = sprite is null || string.IsNullOrEmpty(sprite.Image) ? null : sprite.Image;
        var source = image is null ? new Vector4(0, 0, 1, 1) : sprite!.FrameRect;
        var color = sprite?.Tint ?? Color.Gray;
        var shade = block.Shaded && shadeSides && !across ? SideShade : 1;
        var flash = sprite?.FlashOverlay ?? Color.Transparent;
        if (flash.A > 0)
            color = Color.Lerp(color, flash with { A = color.A }, flash.A);
        return new WallHit(near, hit, image, Strip(source, u), new Color(color.R * shade, color.G * shade, color.B * shade, color.A), block.Shaded);
    }

    // A one-texel-wide strip of an image part, at u (0 to 1) across it.
    private static Vector4 Strip(Vector4 source, float u) =>
        new(source.X + source.Z * Math.Clamp(u, 0, 0.9999f), source.Y, source.Z * 0.0005f, source.W);

    // ----- Things standing in the world -----

    private static List<Standee> Standees(Game game, FirstPersonView fp, Entity viewer, Entity? camera, Vector2 eye, Vector2 dir, Vector2 right,
        float focal, float vertical, float horizon, float eyeHeight, Lighting lighting, Func<float, float> fogOf)
    {
        var standees = new List<Standee>();
        var time = game.Time.Total;
        foreach (var entity in game.Scene.Entities)
        {
            if (!entity.IsActive || entity.IsSelfOrDescendantOf(viewer) || IsOnScreen(entity, camera) || entity.GetComponent<Block>() is not null)
                continue;
            var billboard = entity.GetComponent<Billboard>();
            var scale = billboard?.Scale ?? 1;
            var lift = (billboard?.Lift ?? 0) + (billboard is { Bob: not 0 } ? billboard.Bob * (0.5f + 0.5f * MathF.Sin((float)time * MathF.Tau * billboard.BobSpeed)) : 0);
            var matrix = entity.Transform.WorldMatrix;
            var at = new Vector2(matrix.M41, matrix.M42);

            if (entity.GetComponent<Sprite>() is { IsBlinkedOut: false } sprite)
            {
                var size = new Vector2(MathF.Abs(sprite.Size.X) * new Vector2(matrix.M11, matrix.M12).Length(),
                    MathF.Abs(sprite.Size.Y) * new Vector2(matrix.M21, matrix.M22).Length()) * scale;
                // A spinning pickup turns in place, edge-on and back.
                if (entity.GetComponent<Pickup>() is { Spin: not 0 })
                    size.X *= MathF.Max(0.08f, MathF.Abs(MathF.Cos(entity.Transform.Rotation.Z * MathF.PI / 180)));
                var flip = sprite.FlipX ^ (sprite.Size.X < 0);
                var source = string.IsNullOrEmpty(sprite.Image) ? new Vector4(0, 0, 1, 1) : sprite.FrameRect;
                if (flip)
                    source = new Vector4(source.X + source.Z, source.Y, -source.Z, source.W);
                var flash = sprite.FlashOverlay;
                Add(at, size, lift, string.IsNullOrEmpty(sprite.Image) ? null : sprite.Image, source, sprite.Tint, flash, entity);
            }

            // Particles stand at eye height (sparks off a wall, where it was hit), or the billboard's lift.
            if (entity.GetComponent<ParticleEmitter>() is { Particles.Count: > 0 } emitter)
            {
                var image = string.IsNullOrEmpty(emitter.Image) ? null : emitter.Image;
                foreach (var particle in emitter.Particles)
                {
                    var size = emitter.SizeAt(particle.Progress) * scale;
                    if (size <= 0)
                        continue;
                    var height = billboard is null ? eyeHeight - size / 2 : lift;
                    Add(particle.Position, new Vector2(size), height, image, new Vector4(0, 0, 1, 1), emitter.ColorAt(particle.Progress), Color.Transparent, entity);
                }
            }
        }

        return standees;

        void Add(Vector2 at, Vector2 size, float lift, string? image, Vector4 source, Color tint, Color flash, Entity entity)
        {
            var offset = at - eye;
            var depth = Vector2.Dot(offset, dir);
            if (depth < NearPlane || size.X <= 0 || size.Y <= 0)
                return;
            var middle = Vector2.Dot(offset, right) / depth * focal;
            var half = size.X / 2 / depth * focal;
            if (middle + half < -1 || middle - half > 1)
                return;
            var bottom = horizon + (lift - eyeHeight) * vertical / depth;
            var top = bottom + size.Y * vertical / depth;
            var light = lighting.At(at);
            var lit = new Color(tint.R * light.R, tint.G * light.G, tint.B * light.B, tint.A);
            var fog = fp.Fog with { A = fogOf(depth) };
            standees.Add(new Standee(depth, middle - half, middle + half, bottom, top, image, source, lit, flash.A > 0 ? flash : fog, entity));
        }
    }

    // ----- Weapon, crosshair, minimap -----

    private static IEnumerable<SpriteDraw> WeaponDraws(Game game, FirstPersonView fp, Entity viewer, ScreenSpace screen, float aspect, float stride, float phase)
    {
        if (game.CurrentWeapon is not { } weapon || weapon.Entity != viewer || !(weapon.Size > 0))
            yield break;

        // Swaying as the viewer walks, kicking back as it fires, coming up from below when it's taken out.
        var bob = MathF.Max(0, fp.Bob) * stride;
        var height = weapon.Size * 2;
        var width = height / aspect;
        var sway = MathF.Sin(phase) * 0.045f * bob;
        var dip = MathF.Abs(MathF.Cos(phase)) * 0.04f * bob;
        var kick = weapon.SinceFired < 0.15f ? (1 - weapon.SinceFired / 0.15f) * weapon.Kick * height : 0;
        var raise = game.Fps.Swapped < 0.2f ? (1 - game.Fps.Swapped / 0.2f) * height : 0;
        var middle = 0.12f + sway;
        var bottom = -1 - dip - kick - raise - height * 0.04f;
        var top = bottom + height;
        var firing = weapon.SinceFired < 0.08f;

        if (firing && weapon.MuzzleFlash.A > 0)
            yield return screen.Rect(-1, -1, 1, 1, weapon.MuzzleFlash with { A = 0.07f * weapon.MuzzleFlash.A });

        var image = firing && !string.IsNullOrWhiteSpace(weapon.FireImage) ? weapon.FireImage : weapon.Image;
        if (!string.IsNullOrWhiteSpace(image))
        {
            yield return screen.Rect(middle - width / 2, bottom, middle + width / 2, top, weapon.Color, image.Trim());
        }
        else
        {
            // A simple gun from behind: a body, a barrel, and a sight on top.
            var c = weapon.Color;
            var dark = new Color(c.R * 0.55f, c.G * 0.55f, c.B * 0.55f, c.A);
            yield return screen.Rect(middle - width * 0.17f, bottom, middle + width * 0.17f, bottom + height * 0.5f, dark);
            yield return screen.Rect(middle - width * 0.11f, bottom + height * 0.3f, middle + width * 0.11f, bottom + height * 0.72f, c);
            yield return screen.Rect(middle - width * 0.035f, bottom + height * 0.72f, middle + width * 0.035f, bottom + height * 0.78f, dark);
        }

        // The flash at the muzzle: a bright star where the barrel ends.
        if (firing && weapon.MuzzleFlash.A > 0 && string.IsNullOrWhiteSpace(weapon.FireImage))
        {
            var size = height * 0.28f * (1 - weapon.SinceFired / 0.08f * 0.4f);
            var at = new Vector2(middle, bottom + height * 0.8f);
            yield return screen.Rect(at.X - size / aspect / 2, at.Y - size / 2, at.X + size / aspect / 2, at.Y + size / 2, weapon.MuzzleFlash);
            yield return screen.Rect(at.X - size / aspect / 4, at.Y - size / 4, at.X + size / aspect / 4, at.Y + size / 4, Color.White);
        }
    }

    private static IEnumerable<SpriteDraw> CrosshairDraws(ScreenSpace screen, Color color)
    {
        var (gap, length, thickness) = (0.012f, 0.03f, 0.004f);
        var x = screen.Aspect;
        yield return screen.Rect(-(gap + length) / x, -thickness, -gap / x, thickness, color);
        yield return screen.Rect(gap / x, -thickness, (gap + length) / x, thickness, color);
        yield return screen.Rect(-thickness / x, gap, thickness / x, gap + length, color);
        yield return screen.Rect(-thickness / x, -gap - length, thickness / x, -gap, color);
    }

    // A small map in the top-right corner, north up, of the walls within reach, with the viewer, enemies and pickups.
    private static IEnumerable<SpriteDraw> MinimapDraws(Scene scene, Entity viewer, Vector2 eye, Vector2 dir, List<Tilemap> tilemaps,
        IEnumerable<Box> blocks, ScreenSpace screen, float aspect)
    {
        const float reach = 9;
        var half = 0.27f;
        var center = new Vector2(1 - 0.04f / aspect - half / aspect, 1 - 0.04f - half);
        var scale = new Vector2(half / aspect, half) / reach;
        yield return screen.Rect(center.X - half / aspect, center.Y - half, center.X + half / aspect, center.Y + half, MapBack);

        SpriteDraw? Square(Vector2 at, Vector2 size, Color color)
        {
            var offset = at - eye;
            var extent = size / 2;
            if (MathF.Abs(offset.X) + extent.X > reach || MathF.Abs(offset.Y) + extent.Y > reach)
                return null;
            var middle = center + offset * scale;
            var span = extent * scale;
            return screen.Rect(middle.X - span.X, middle.Y - span.Y, middle.X + span.X, middle.Y + span.Y, color);
        }

        foreach (var tilemap in tilemaps)
        {
            var matrix = tilemap.Entity!.Transform.WorldMatrix;
            var cell = new Vector2(tilemap.TileSize * new Vector2(matrix.M11, matrix.M12).Length(), tilemap.TileSize * new Vector2(matrix.M21, matrix.M22).Length());
            foreach (var (x, y, _) in tilemap.Cells())
            {
                if (tilemap.IsBlocked(x, y) && Square(Vector2.Transform(tilemap.CellCenter(x, y), matrix), cell, MapWall) is { } draw)
                    yield return draw;
            }
        }

        foreach (var box in blocks)
        {
            if (Square(box.Center, box.HalfSize * 2, MapWall with { A = 0.6f }) is { } draw)
                yield return draw;
        }

        foreach (var entity in scene.Entities.Where(e => e.IsActive))
        {
            if (entity.IsSelfOrDescendantOf(viewer))
                continue;
            var color = entity.GetComponent<Health>() is { IsDead: false } ? MapEnemy : entity.GetComponent<Pickup>() is { IsCollected: false } ? MapPickup : (Color?)null;
            if (color is null)
                continue;
            var at = entity.Transform.WorldPosition;
            if (Square(new Vector2(at.X, at.Y), new Vector2(0.45f), color.Value) is { } draw)
                yield return draw;
        }

        // The viewer: a dot with a line the way it looks.
        if (Square(eye, new Vector2(0.55f), MapPlayer) is { } me)
            yield return me;
        var tip = center + dir * 1.4f * scale;
        var from = center;
        var delta = (tip - from) * new Vector2(aspect, 1);
        var line = Matrix4x4.CreateScale(delta.Length(), 0.012f, 1) * Matrix4x4.CreateRotationZ(MathF.Atan2(delta.Y, delta.X)) *
                   Matrix4x4.CreateTranslation((from.X + tip.X) / 2 * aspect, (from.Y + tip.Y) / 2, 0);
        yield return screen.Draw(line, MapPlayer);
    }

    // Clip space (-1..1 across and up the screen) to the camera's view of the world, where draws are placed.
    private readonly record struct ScreenSpace(View View)
    {
        public float Aspect => View.Half.Y > 0 ? View.Half.X / View.Half.Y : 1;

        public SpriteDraw Rect(float left, float bottom, float right, float top, Color tint, string? image = null, Vector4? source = null, Entity? entity = null)
        {
            var world = Matrix4x4.CreateScale((right - left) * View.Half.X, (top - bottom) * View.Half.Y, 1) *
                        Matrix4x4.CreateTranslation(View.Center.X + (left + right) / 2 * View.Half.X, View.Center.Y + (top + bottom) / 2 * View.Half.Y, 0);
            return new SpriteDraw(world, image, tint, entity ?? ScreenEntity) { Source = source ?? new Vector4(0, 0, 1, 1) };
        }

        // A quad given in a space where one unit is half the screen's height both ways (so turning it keeps its shape).
        public SpriteDraw Draw(Matrix4x4 square, Color tint) =>
            new(square * Matrix4x4.CreateScale(View.Half.Y, View.Half.Y, 1) * Matrix4x4.CreateTranslation(View.Center.X, View.Center.Y, 0), null, tint, ScreenEntity);
    }

    // The light on a point of the floor plan: daylight, or in a dark scene, the ambient light and every light reaching it.
    private sealed class Lighting
    {
        private static readonly Lighting Daylight = new(Color.White, []);
        private readonly Color _ambient;
        private readonly List<LightDraw> _lights;

        private Lighting(Color ambient, List<LightDraw> lights) => (_ambient, _lights) = (ambient, lights);

        public static Lighting For(Scene scene, Entity? camera)
        {
            if (camera?.GetComponent<Camera>() is not { IsDark: true } view)
                return Daylight;
            // Without shadows: walls stand up out of the map, so its shadows wouldn't fall where they look right.
            var lights = scene.Active<Light>()
                .Where(l => l.Radius > 0 && l.CurrentIntensity > 0)
                .Select(l => new LightDraw(l.WorldCenter, l.Radius,
                    new Color(l.Color.R * l.CurrentIntensity, l.Color.G * l.CurrentIntensity, l.Color.B * l.CurrentIntensity), l.Softness))
                .ToList();
            return new Lighting(view.CurrentAmbient, lights);
        }

        public Color At(Vector2 point)
        {
            if (_lights.Count == 0)
                return _ambient;
            var sum = new Vector3(_ambient.R, _ambient.G, _ambient.B);
            foreach (var light in _lights)
                sum += light.At(point);
            sum = Vector3.Clamp(sum, Vector3.Zero, Vector3.One);
            return new Color(sum.X, sum.Y, sum.Z);
        }
    }
}
