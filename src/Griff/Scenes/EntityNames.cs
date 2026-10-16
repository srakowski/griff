using System.Numerics;

namespace Griff.Scenes;

/// <summary>Where <see cref="EntityNames.Find"/> looks.</summary>
public enum NameScope
{
    /// <summary>What is inside the holder first, then the scene: how a field names an entity.</summary>
    InsideThenScene,

    /// <summary>Only the scene, whatever is inside the holder.</summary>
    Scene,

    /// <summary>Only the holder and what is inside it (an attack's Hitbox).</summary>
    Inside,
}

/// <summary>How <see cref="EntityNames.Find"/> narrows a name down.</summary>
/// <param name="From">The entity that holds the name (a field's owner, a camera following one): what is inside it is meant first, and the part of the scene it is in.</param>
/// <param name="ActiveOnly">Only entities that are on (see <see cref="Entity.IsActive"/>).</param>
/// <param name="Scope">Where to look: inside <paramref name="From"/> first and then the scene (the rule), only the scene, or only inside <paramref name="From"/>.</param>
/// <param name="ExceptFrom">Never <paramref name="From"/> itself (a weld to the nearest other part).</param>
/// <param name="Where">Only entities this accepts (one with a component, not the holder itself).</param>
/// <param name="Centre">The component that wants the nearest says so, giving where an entity is: of the ones with the name, the nearest to <paramref name="Around"/> wins (and a network game's own player does not).</param>
/// <param name="Around">Where nearest is measured from; without it, <paramref name="From"/>'s centre.</param>
public readonly record struct NameLookup(
    Entity? From = null, bool ActiveOnly = false, NameScope Scope = NameScope.InsideThenScene, bool ExceptFrom = false, Func<Entity, bool>? Where = null, Func<Entity, Vector2>? Centre = null, Vector2? Around = null);

/// <summary>
/// The one way a name finds an entity, in the running game, in renaming, in the Inspector's flags and for agents, so the
/// same text means the same entity everywhere. The rule: the name is trimmed and matched exactly; what is inside the
/// entity that holds it comes first (a lever's own "Hint"), then the scene; in the scene, one in the same part of it as the
/// holder (the minigame being played, or outside them; without a holder, the minigame that has the screen) before another,
/// and in a network game this machine's player's own before another; of what is left the first in the scene's order, or the
/// nearest (to the holder, not preferring the local player) when the component says so. A name that several match where the rule needs one is ambiguous
/// (<see cref="Ambiguous"/>), and the Inspector, scene_tree and Output say so.
/// </summary>
public static class EntityNames
{
    /// <summary>The entity called <paramref name="name"/> as <paramref name="how"/> says, or null. Makes no garbage without <c>Where</c> or <c>NearestTo</c>.</summary>
    public static Entity? Find(Scene scene, ReadOnlySpan<char> name, in NameLookup how = default)
    {
        name = name.Trim();
        if (name.IsEmpty)
            return null;
        if (how.From is { } from && how.Scope != NameScope.Scene)
        {
            if (InsideOf(from, name, in how) is { } inside)
                return inside;
            if (how.Scope == NameScope.Inside)
                return null;
        }

        var worlds = scene.ActiveNow<MinigameRoot>().Count > 0;
        var world = !worlds ? null : how.From is { } holder ? MinigameRoot.Of(holder) : MinigameRoot.Shown(scene);
        var prefer = scene.PreferNamed;
        var here = how.Around ?? (how.Centre is { } centre && how.From is { } at ? centre(at) : default);
        Entity? best = null;
        var bestRank = -1;
        var bestDistance = float.PositiveInfinity;
        var entities = scene.EntitiesNow;
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (!name.SequenceEqual(entity.Name) || (how.ExceptFrom && entity == how.From) || (how.ActiveOnly && !entity.IsActive) || (how.Where is { } where && !where(entity)))
                continue;
            var rank = (worlds && MinigameRoot.Of(entity) != world ? 0 : 2) + (prefer is null || how.Centre is not null || prefer(entity) ? 1 : 0);
            if (how.Centre is { } centreOf)
            {
                var distance = Vector2.DistanceSquared(centreOf(entity), here);
                if (rank > bestRank || (rank == bestRank && distance < bestDistance))
                    (best, bestRank, bestDistance) = (entity, rank, distance);
            }
            else if (rank == 3)
                return entity;
            else if (rank > bestRank)
                (best, bestRank) = (entity, rank);
        }

        return best;
    }

    /// <summary><see cref="Find(Scene, ReadOnlySpan{char}, in NameLookup)"/> for text.</summary>
    public static Entity? Find(Scene scene, string? name, in NameLookup how = default) => Find(scene, name.AsSpan(), in how);

    /// <summary>Every entity in the scene called <paramref name="name"/> (trimmed), for a field that acts on all of them.</summary>
    public static IReadOnlyList<Entity> Every(Scene scene, string? name) =>
        string.IsNullOrWhiteSpace(name) ? [] : [.. scene.Entities.Where(e => e.Name == name.Trim())];

    /// <summary>Whether <paramref name="entity"/> answers to <paramref name="name"/> (trimmed, exact).</summary>
    public static bool Is(Entity entity, string? name) => name.AsSpan().Trim().SequenceEqual(entity.Name);

    /// <summary>How many entities in the scene are called <paramref name="name"/> (trimmed) and <paramref name="where"/> accepts, without making garbage.</summary>
    public static int Count(Scene scene, string? name, Func<Entity, bool> where)
    {
        var wanted = name.AsSpan().Trim();
        if (wanted.IsEmpty)
            return 0;
        var count = 0;
        var entities = scene.EntitiesNow;
        for (var i = 0; i < entities.Length; i++)
        {
            if (wanted.SequenceEqual(entities[i].Name) && where(entities[i]))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Every entity the name can mean for a field on <paramref name="owner"/>: those inside it (itself too, unless
    /// <paramref name="skipOwner"/>) if there are any, otherwise, unless <paramref name="insideOnly"/>, those in the scene
    /// called that, in the owner's part of it if any are. A switch acts on all of them; a field that needs one has an
    /// ambiguous name when there are several.
    /// </summary>
    public static IReadOnlyList<Entity> All(Entity owner, string name, bool skipOwner = false, bool insideOnly = false)
    {
        name = name.Trim();
        var inside = owner.SelfAndDescendants().Skip(skipOwner ? 1 : 0).Where(e => e.Name == name).ToList();
        if (inside.Count > 0 || insideOnly || owner.Scene is null)
            return inside;
        var found = owner.Scene.Entities.Where(e => e.Name == name).ToList();
        if (found.Count > 1 && found.Any(e => Scene.SamePart(e, owner)))
            found = [.. found.Where(e => Scene.SamePart(e, owner))];
        return found;
    }

    /// <summary>
    /// The entity a field on <paramref name="owner"/> means, when the name is not ambiguous: the one <see cref="All"/>
    /// gives. Null when nothing has the name or several do.
    /// </summary>
    public static Entity? Resolve(Entity owner, string name, bool skipOwner = false, bool insideOnly = false) =>
        All(owner, name, skipOwner, insideOnly) is [var only] ? only : null;

    /// <summary>
    /// The entities <paramref name="name"/> can mean for a field on <paramref name="owner"/> that needs one, when there are
    /// several (see <see cref="All"/>); empty when it is unambiguous or matches nothing.
    /// </summary>
    public static IReadOnlyList<Entity> Ambiguous(Entity owner, string name, bool skipOwner = false, bool insideOnly = false) =>
        All(owner, name, skipOwner, insideOnly) is { Count: > 1 } several ? several : [];

    private static Entity? InsideOf(Entity entity, ReadOnlySpan<char> name, in NameLookup how)
    {
        if (name.SequenceEqual(entity.Name) && !(how.ExceptFrom && entity == how.From) && (!how.ActiveOnly || entity.IsActive) && (how.Where is not { } where || where(entity)))
            return entity;
        foreach (var child in entity.ChildList)
        {
            if (InsideOf(child, name, in how) is { } found)
                return found;
        }

        return null;
    }
}
