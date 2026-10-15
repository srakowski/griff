using Griff.Scenes;

namespace Griff.Modules.Tactics;

/// <summary>
/// A unit in a tactics game, with no code: on its side's turn you pick it, see the cells it can walk to (and the cells it
/// can hit from them), then click one to walk there and click an enemy in reach to attack it. Each turn it moves up to
/// <see cref="Move"/> cells, around walls and other units, then attacks something <see cref="MinRange"/> to
/// <see cref="Range"/> steps away for <see cref="Damage"/>, or waits. Give it a <see cref="TurnTaker"/> with 1 action
/// naming its side: attacking or waiting uses the action, so once every unit on a side has had its go, the turn passes.
/// A <see cref="TacticsControls"/> in the scene does the picking and draws the ranges. On a side the computer plays (the
/// turn order's Computer Sides), the computer takes each unit's go in turn, the way its <see cref="Stance"/> says: the
/// best attack it can make (a foe it can finish off first, then the weakest), or a walk toward the nearest foe.
/// </summary>
/// <remarks>
/// Its enemies are the units on other sides (with no turn takers, every other unit), or whatever with <see cref="Health"/>
/// is on its <see cref="Layers"/>. Ranges are counted in steps and reach over walls. Units block each other, friend or
/// foe. In a network game the host moves the units: a player's clicks are sent to it.
/// </remarks>
[Category("Gameplay")]
[Requires(typeof(GridMovement))]
[WorksWith(typeof(TurnTaker), Note = "its side, with 1 action: a go each turn")]
[WorksWith(typeof(Health), Note = "hit points, so it can be beaten")]
[WorksWith(typeof(TacticsControls), Note = "picks units and draws their ranges, one per scene")]
[ComponentName("Griff.TacticsUnit")]
public sealed class TacticsUnit : Component, IGridDriver, ITurnReady, IPlaytestReport
{
    /// <summary>How many cells it can walk in a turn, around walls and other units.</summary>
    public int Move { get; set; } = 4;

    /// <summary>How many steps away it can hit: 1 is next to it, more for an archer or a mage.</summary>
    [Group("Attack")]
    public int Range { get; set; } = 1;

    /// <summary>How close something has to be at least for it to hit it: 2 for an archer that can't hit what's next to it.</summary>
    public int MinRange { get; set; } = 1;

    /// <summary>Hit points each attack takes away.</summary>
    public float Damage { get; set; } = 1;

    /// <summary>
    /// Dice rolled for each hit's damage in place of <see cref="Damage"/>, e.g. "1d8+2"; empty hits for Damage every time.
    /// Table Values sets it from a dice column given for Damage.
    /// </summary>
    [RollsFor(nameof(Damage))]
    [Dice]
    public string? DamageRoll
    {
        get => _roll.Text;
        set => _roll.Text = value;
    }

    private DiceRoll _roll;

    /// <summary>The damage of one hit: <see cref="DamageRoll"/> rolled, or <see cref="Damage"/>.</summary>
    public float RollDamage() => _roll.Roll(Damage);

    /// <summary>
    /// The collision layers it attacks, e.g. "Enemy". Empty attacks the units on other sides (see <see cref="TurnTaker"/>).
    /// </summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>A sound played with each attack, e.g. "Sounds/hit.wav".</summary>
    public string? AttackSound { get; set; }

    /// <summary>A prefab spawned on what it hits, e.g. "Sparkle": a burst of particles, a damage number.</summary>
    [Names(NameKind.Prefab)]
    public string? HitEffect { get; set; }

    /// <summary>Freezes the game for this many seconds when an attack lands, so it hits hard. 0 for none.</summary>
    public float HitStop { get; set; } = 0.06f;

    /// <summary>
    /// How it plays when the computer plays its side (see <see cref="TurnOrder.ComputerSides"/>): Charge goes after the
    /// nearest foe, Guard stays put until a foe comes within a walk and a hit, Hold never walks and only attacks from
    /// where it stands.
    /// </summary>
    [Group("Computer")]
    public TacticsStance Stance { get; set; }

    /// <summary>
    /// True once it has walked this go and has yet to attack or wait. The game clears it as its go ends and as each of its
    /// side's turns starts.
    /// </summary>
    [Group("State")]
    public bool HasMoved { get; set; }

    /// <summary>True while it can take its go: it's its side's turn (if it takes turns) and it hasn't used its action.</summary>
    [DoNotSerialize]
    public bool CanAct { get; internal set; }

    /// <summary>True on its side's turn once it has had its go: drawn dimmed until its side's next turn.</summary>
    [DoNotSerialize]
    public bool IsDone { get; internal set; }

    /// <summary>True while it's walking to where it was sent.</summary>
    [DoNotSerialize]
    public bool IsWalking => Path.Count > 0 || Entity?.GetComponent<GridMovement>() is { IsMoving: true };

    /// <summary>
    /// The cells it can walk to on its go, column then row on its map (row 0 at the top), not counting the one it stands
    /// in. The game keeps it up to date while it can act and hasn't moved; empty otherwise.
    /// </summary>
    [DoNotSerialize]
    public IReadOnlyList<(int X, int Y)> Reachable { get; internal set; } = [];

    /// <summary>What it can attack from where it stands now. The game keeps it up to date while it can act.</summary>
    [DoNotSerialize]
    public IReadOnlyList<Entity> Targets { get; internal set; } = [];

    /// <summary>
    /// Walks to <paramref name="cell"/> (one of its <see cref="Reachable"/> cells) at the start of the next frame, as a click
    /// there does. It still has to attack or wait afterwards; with nothing in reach from there, its go ends by itself.
    /// </summary>
    public void MoveTo((int X, int Y) cell) => Requests.Add(new TacticsRequest(TacticsOrder.Move, cell, null));

    /// <summary>
    /// Attacks <paramref name="target"/> at the start of the next frame, as a click on it does: from where it stands if it's
    /// in reach, else walking first to the nearest cell it can hit it from (if it hasn't moved yet).
    /// </summary>
    public void Attack(Entity target) => Requests.Add(new TacticsRequest(TacticsOrder.Attack, null, target));

    /// <summary>Ends its go where it stands, without attacking, at the start of the next frame.</summary>
    public void Wait() => Requests.Add(new TacticsRequest(TacticsOrder.Wait, null, null));

    /// <summary>Takes back the walk it took this go, if it hasn't attacked or waited yet, putting it back where it was.</summary>
    public void UndoMove() => Requests.Add(new TacticsRequest(TacticsOrder.UndoMove, null, null));

    /// <summary>Raised as it sets off on a walk, with the cell it's going to.</summary>
    public event Action<TacticsUnit, (int X, int Y)>? Moved;

    /// <summary>Raised as it attacks, with what it attacks.</summary>
    public event Action<TacticsUnit, Entity>? Attacked;

    /// <summary>Raised as its go ends, after it attacks or waits.</summary>
    public event Action<TacticsUnit>? Done;

    /// <summary>True if it attacks things on collision layer <paramref name="layer"/> (always, with no <see cref="Layers"/>).</summary>
    public bool AttacksLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseMoved((int X, int Y) cell) => Moved?.Invoke(this, cell);

    internal void RaiseAttacked(Entity target) => Attacked?.Invoke(this, target);

    internal void RaiseDone() => Done?.Invoke(this);

    // What scripts have asked of it; the cells still to walk; what it attacks once it gets there; the cell it walked from
    // this go (for Undo Move). Not saved: a rewound unit stands wherever it was, with its go as HasMoved says.
    internal List<TacticsRequest> Requests { get; } = [];

    internal List<(int X, int Y)> Path { get; } = [];

    internal Entity? AttackAfter { get; set; }

    internal (int X, int Y)? MovedFrom { get; set; }

    // Its walk is all one go, used up as it attacks or waits.
    bool IGridDriver.StepTakesAction => false;

    // The next cell of its walk as a way to step (x right, y up), or null when it isn't walking.
    (int X, int Y)? IGridDriver.NextStep(GridMovement movement)
    {
        if (Path.Count == 0)
            return null;
        var next = Path[0];
        Path.RemoveAt(0);
        var grid = GridCells.Of(movement.FindMap());
        if (!grid.AreNeighbours(movement.Cell, next))
        {
            Path.Clear();
            return null;
        }

        return grid.WayBetween(movement.Cell, next);
    }

    // Its way is blocked: it stops where it is.
    void IGridDriver.StepBlocked() => Path.Clear();

    // A fresh go each turn: it can walk and attack again.
    void ITurnReady.TurnBegins()
    {
        Path.Clear();
        (HasMoved, MovedFrom, AttackAfter) = (false, null, null);
    }

    // Its cell, and whether it's walking, has had its go, waits to attack after walking, or can act (with how many cells
    // it reaches and what it can hit from where it stands), e.g. ", in cell (3, 1), can act: reaches 9 cells, can hit Orc".
    string IPlaytestReport.Report()
    {
        if (Entity?.GetComponent<GridMovement>() is not { } m)
            return "";
        var text = $", in cell ({m.Cell.X}, {m.Cell.Y})";
        var targets = Targets.Count > 0 ? $", can hit {string.Join(", ", Targets.Select(t => t.Name))}" : "";
        if (IsWalking)
            return text + ", walking";
        if (IsDone)
            return text + ", has had its go";
        if (!CanAct)
            return text + ", waiting for its side's turn";
        return HasMoved
            ? text + ", has moved" + (targets.Length > 0 ? targets + " (or click it to wait)" : "")
            : text + $", can act: reaches {Reachable.Count} cells" + targets;
    }
}

/// <summary>How a computer-played <see cref="TacticsUnit"/> plays.</summary>
public enum TacticsStance
{
    /// <summary>Attacks the best foe it can reach, else walks toward the nearest one.</summary>
    Charge,

    /// <summary>Attacks a foe it can reach with a walk and a hit, else stays where it is.</summary>
    Guard,

    /// <summary>Never walks: attacks a foe already in range, else waits.</summary>
    Hold,
}

internal enum TacticsOrder
{
    Move,
    Attack,
    Wait,
    UndoMove,
}

internal sealed record TacticsRequest(TacticsOrder Order, (int X, int Y)? Cell, Entity? Target);

/// <summary>
/// Lets players command their <see cref="TacticsUnit"/>s with the mouse, or with a cursor moved by the arrow keys or the
/// stick: click one of your units whose side's turn it is to pick it, and the cells it can walk to light up in
/// <see cref="MoveColor"/>, with the cells it can hit from them in <see cref="AttackColor"/>; hover a cell to see the way
/// it would go. Click a lit cell to walk there, then an enemy in reach to attack it (or click an enemy straight away to
/// walk up and hit it), or the unit itself to wait. Right-click (or Cancel) puts it back or lets it go. Hovering any unit
/// shows how far it reaches. Units that have had their go are dimmed. On the computer's turn, each of its units' reach
/// lights up for <see cref="ComputerPause"/> before it moves.
/// </summary>
/// <remarks>One per scene (the first with focus is used); it draws nothing itself. In a network game each player picks on their own screen.</remarks>
[Category("Gameplay")]
[WorksWith(typeof(TacticsUnit), Note = "the units it commands")]
[WorksWith(typeof(TurnOrder), Note = "whose turn it is")]
[ComponentName("Griff.TacticsControls")]
public sealed class TacticsControls : Component, IPlaytestReport
{
    /// <summary>The input action that picks or goes to the cell under the cursor, as a left click does: "Jump" is Space or the gamepad's A.</summary>
    [Names(NameKind.Action)]
    public string? ConfirmAction { get; set; } = "Jump";

    /// <summary>The input action that puts a unit back or lets it go, as a right click does: "Dash" is Shift or the gamepad's B.</summary>
    [Names(NameKind.Action)]
    public string? CancelAction { get; set; } = "Dash";

    /// <summary>
    /// Untick it to have the controls ignore the mouse, keys and gamepad (the computer's turns still play): another part of
    /// the scene has the player's input. Its Focus action ticks it and unticks it on everything else the player plays, and
    /// another's Focus takes it back.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>Hovering a unit nobody has picked shows how far it can walk and hit, fainter.</summary>
    public bool ShowOnHover { get; set; } = true;

    /// <summary>Draws a health bar under each unit with <see cref="Health"/>.</summary>
    public bool HealthBars { get; set; } = true;

    /// <summary>The cells a picked unit can walk to.</summary>
    [Group("Colors")]
    public Color MoveColor { get; set; } = Color.FromBytes(0x4C, 0x8D, 0xF0, 0x70);

    /// <summary>The cells it can hit from them, and what it can attack.</summary>
    public Color AttackColor { get; set; } = Color.FromBytes(0xE8, 0x4A, 0x4A, 0x70);

    /// <summary>The way it would walk to the hovered cell.</summary>
    public Color PathColor { get; set; } = Color.FromBytes(0xF4, 0xF4, 0xF4);

    /// <summary>The outline round the cell under the cursor.</summary>
    public Color CursorColor { get; set; } = Color.FromBytes(0xFF, 0xE0, 0x6A, 0xE0);

    /// <summary>Multiplies the color of units that have had their go.</summary>
    public Color DoneTint { get; set; } = Color.FromBytes(0x80, 0x80, 0x80);

    /// <summary>
    /// Seconds the computer takes over each of its units' goes before it moves, while the unit's reach lights up, so you
    /// can follow what it does.
    /// </summary>
    [Group("Computer")]
    public float ComputerPause { get; set; } = 0.5f;

    /// <summary>A sound played on picking a unit, e.g. "Sounds/select.wav".</summary>
    [Group("Sounds")]
    public string? SelectSound { get; set; }

    /// <summary>A sound played on letting a unit go or putting it back.</summary>
    public string? CancelSound { get; set; }

    /// <summary>The unit picked on this screen, or null. Set it from a script to pick one.</summary>
    [DoNotSerialize]
    public Entity? Selected { get; set; }

    /// <summary>The cell under the cursor (column then row on the units' map), or null when it's off the map.</summary>
    [DoNotSerialize]
    public (int X, int Y)? Cursor { get; internal set; }

    /// <summary>Raised when a unit is picked, with it.</summary>
    public event Action<Entity>? Picked;

    internal void RaisePicked(Entity unit) => Picked?.Invoke(unit);

    // Where the mouse is goes with the unit it has picked.
    string IPlaytestReport.Report() => "";

    string IPlaytestReport.MouseReport() => Selected is { } picked ? $", picked {picked.Name}" : "";
}
