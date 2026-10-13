using System.IO;
using System.Numerics;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Projects;
using Griff.Scenes;

namespace Griff.Editor.Features.Assets;

/// <summary>
/// Puts an asset into the open scene (or the prefab whose tab is showing): a prefab as a linked copy, an image as a
/// sprite sized to its shape. The Assets panel's Add to Scene and dropping an asset on the Scene view both come here.
/// </summary>
public sealed class AssetPlacement(IWorkspace workspace, ISelection selection)
{
    /// <summary>The prefabs and images in Assets, by their path there ("Prefabs/Coin.prefab", "Sprites/hero.png").</summary>
    public IReadOnlyList<string> Placeable()
    {
        if (workspace.Project is not { } project || !Directory.Exists(project.AssetsPath))
            return [];
        return Directory.EnumerateFiles(project.AssetsPath, "*.*", SearchOption.AllDirectories)
            .Where(p => AssetNode.KindOf(p) is AssetKind.Prefab or AssetKind.Image)
            .Select(p => PrefabFiles.AssetPathOf(project, p))
            .Where(p => !p.StartsWith('.') && !p.StartsWith("Build/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Adds a copy of a prefab, or a sprite showing an image, given by its path in Assets, as one undo step, and selects
    /// it. A prefab goes where it was saved from and an image at 0, 0, unless <paramref name="at"/>
    /// (in world units) says where. Inside a prefab's tab it goes inside the prefab.
    /// </summary>
    /// <returns>The new entity.</returns>
    public Entity AddToScene(string assetPath, Vector2? at = null)
    {
        if (workspace.Scene is not { } document || workspace.Project is not { } project)
            throw new InvalidOperationException("Open a scene to add things to it.");

        var path = assetPath.Trim().Replace('\\', '/');
        var full = Path.Combine(project.AssetsPath, path.Replace('/', Path.DirectorySeparatorChar));
        Entity entity;
        if (AssetNode.KindOf(full) == AssetKind.Image && File.Exists(full))
        {
            entity = new Entity(Path.GetFileNameWithoutExtension(path));
            var sprite = entity.AddComponent<Sprite>();
            sprite.Image = path;
            sprite.Size = new Vector2(Aspect(full), 1);
        }
        else if (project.FindPrefab(path) is { } file)
        {
            path = PrefabFiles.AssetPathOf(project, file);
            if (PrefabFiles.IsOwnTab(workspace, document, path))
                throw new InvalidOperationException($"{Path.GetFileNameWithoutExtension(path)} can't go inside itself.");
            entity = PrefabFiles.Instantiate(workspace, path)
                ?? throw new InvalidOperationException($"{path} couldn't be read as a prefab.");
        }
        else
        {
            throw new ArgumentException($"There's no prefab or image \"{assetPath}\" in Assets.", nameof(assetPath));
        }

        var parent = document.ParentFor(null);
        if (at is { } world)
        {
            // In a prefab's tab, where it lands is turned into a place inside the prefab.
            var position = world;
            if (parent is not null && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var toParent))
                position = Vector2Of(Vector3.Transform(new Vector3(world, 0), toParent));
            entity.Transform.Position = new Vector3(position, entity.Transform.Position.Z);
        }

        document.Execute(new AddEntityEdit(document.Scene, entity, parent));
        selection.Select(entity);
        return entity;
    }

    private static Vector2 Vector2Of(Vector3 v) => new(v.X, v.Y);

    // Width over height, read from the file's header; 1 if it can't be read.
    private static float Aspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            return frame.PixelHeight > 0 ? (float)frame.PixelWidth / frame.PixelHeight : 1f;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException or UnauthorizedAccessException)
        {
            return 1f;
        }
    }
}
