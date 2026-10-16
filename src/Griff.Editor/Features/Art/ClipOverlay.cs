using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;

namespace Griff.Editor.Features.Art;

/// <summary>
/// What a module adds to the Clip Editor for the frame showing: a fighting game's hitboxes and hurtboxes, drawn over the
/// frame and listed beside it. The Clip Editor knows no genre: it makes one overlay of each kind the editor has (an
/// <see cref="IClipOverlayMaker"/> a genre's editor module registers), shows each one's panel by its DataTemplate (the
/// genre's Templates.xaml, see <see cref="Core.Modules.GenreTemplates"/>) in the column beside the preview and its
/// <see cref="Drawing"/> over the frame, tells it when the frame, clip or entity changes, takes its keys first, and makes
/// what it changes when a clip is renamed or removed part of that undo step. Agents reach it as
/// <c>Overlays[Name]</c> on the ClipEditor.
/// </summary>
public abstract class ClipOverlay(ClipEditor clips) : ObservableObject
{
    /// <summary>The Clip Editor it's part of: the entity, the clip previewed and the frame showing.</summary>
    public ClipEditor Clips => clips;

    /// <summary>What it's called, as agents find it among the Clip Editor's overlays: "Boxes".</summary>
    public abstract string Name { get; }

    /// <summary>What's drawn over the frame: an object its genre's DataTemplate draws (and takes the mouse on), or null for nothing.</summary>
    public virtual object? Drawing => null;

    /// <summary>
    /// Shows what it has for the frame showing (<see cref="ClipEditor.Frame"/> of <see cref="ClipEditor.Selected"/>), after
    /// the frame, clip, entity, scene or onion skin changed; <paramref name="newEntity"/> is true when another entity is
    /// shown, so what was picked on the last one goes.
    /// </summary>
    protected internal abstract void Refresh(bool newEntity);

    /// <summary>The change to make with renaming clip <paramref name="from"/> to <paramref name="to"/>, in the same undo step; null for none.</summary>
    protected internal virtual IEdit? RenamingClip(string from, string to) => null;

    /// <summary>The change to make with removing clip <paramref name="name"/>, in the same undo step; null for none.</summary>
    protected internal virtual IEdit? RemovingClip(string name) => null;

    /// <summary>A key pressed in the Clip Editor, before it takes it itself: true if this took it (a tool's letter).</summary>
    protected internal virtual bool KeyDown(Key key, ModifierKeys modifiers) => false;

    /// <summary>Delete pressed: removes what's picked on the frame and says true, or says false to let the clip go instead.</summary>
    protected internal virtual bool Delete() => false;

    public override string ToString() => Name;
}

/// <summary>Makes a Clip Editor's <see cref="ClipOverlay"/>: a genre's editor module registers one as a singleton.</summary>
public interface IClipOverlayMaker
{
    /// <summary>The overlay for <paramref name="clips"/>, made once as the Clip Editor is.</summary>
    ClipOverlay Make(ClipEditor clips);
}
