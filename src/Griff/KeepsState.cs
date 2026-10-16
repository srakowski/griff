using Griff.Runtime;

namespace Griff;

/// <summary>
/// A part of a running game whose fields are its state: every <see cref="GameSystem"/>, the game's clock and its random
/// numbers. A rewind snapshot keeps each field's value as it was (numbers, text, lists, dictionaries, the entities and
/// components it points to, and the timers, tweens, coroutines and closures it holds, with what they hold), and putting
/// the game back puts them back into the same part, so nothing it kept is lost. A field that isn't state (a link to
/// another part of the game, a list reused to save garbage) says so with <see cref="NotStateAttribute"/>. A system of a
/// script's is kept the same way.
/// </summary>
/// <example><code>
/// public class Waves : GameSystem
/// {
///     private int _wave;                    // kept: a rewind goes back to the wave it was on
///     private readonly List&lt;Entity&gt; _alive = []; // kept, as the entities they were then
///
///     [NotState("filled again each step")]
///     private readonly List&lt;Entity&gt; _scratch = [];
/// }
/// </code></example>
public interface IKeepsState
{
    /// <summary>
    /// Called once a rewind has put the fields back (every part's, so other parts are back too), to work out again what
    /// fields marked <see cref="NotStateAttribute"/> hold from the rest. Nothing by default.
    /// </summary>
    void Restored()
    {
    }
}

/// <summary>
/// On a field of a system (or of anything a rewind snapshot keeps, see <see cref="IKeepsState"/>): it isn't the game's
/// state, so the snapshot leaves it out and a rewind leaves it as it is: a link to another part of the game, a cache it
/// can work out again (in <see cref="IKeepsState.Restored"/>, or as it next looks), a list kept only to reuse. On a
/// class or interface: what it is belongs to the game rather than its moving state (its input, sound, save data and the
/// like) or never changes once made, so something pointing to one keeps pointing to the same one.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct)]
public sealed class NotStateAttribute(string reason) : Attribute
{
    /// <summary>Why it isn't state.</summary>
    public string Reason { get; } = reason;
}
