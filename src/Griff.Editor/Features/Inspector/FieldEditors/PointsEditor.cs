using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;
using Griff.Editing;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A list of points (<see cref="PointsAttribute"/>), a row each with X and Y to set (whole cells for a grid's), and buttons
/// to add one past the last, take one out (down to the fewest it needs) and move one up the list. A path's start, where
/// the entity is placed, shows first and stays. The Scene view's knobs drag the same points.
/// </summary>
public sealed class PointsEditor : FieldEditor<PointsAttribute>
{
    public PointsEditor(PropertyField field, PointsAttribute attribute)
        : base(field, attribute)
    {
        Refresh();
    }

    /// <summary>The points, a row each, in order.</summary>
    public ObservableCollection<PointRow> Rows { get; } = [];

    /// <summary>True for a path from where the entity is placed: the start (0 0) shows first, and isn't one of <see cref="Rows"/>.</summary>
    public bool FromStart => Attribute.FromStart;

    /// <summary>True when one can be taken out: it has more than the fewest it needs.</summary>
    public bool CanRemove => Rows.Count > Attribute.Least;

    /// <summary>The points as they are.</summary>
    public IReadOnlyList<Vector2> Points => PointsAttribute.Read(Text);

    /// <summary>Adds a point after the last: a step on from it the way the last two went (or one to the right).</summary>
    public void Add()
    {
        var points = Points.ToList();
        var last = points.Count > 0 ? points[^1] : Vector2.Zero;
        var before = points.Count > 1 ? points[^2] : FromStart || points.Count == 0 ? Vector2.Zero : last - Vector2.UnitX;
        var step = last - before;
        if (step == Vector2.Zero)
            step = Vector2.UnitX;
        points.Add(Fit(last + step));
        Save(points, seal: true);
    }

    /// <summary>Sets point <paramref name="number"/> (from 1), as one undo step.</summary>
    public void Set(int number, double x, double y)
    {
        var points = Points.ToList();
        Check(number, points.Count);
        points[number - 1] = Fit(new Vector2((float)x, (float)y));
        Save(points, seal: true);
    }

    /// <summary>Takes point <paramref name="number"/> (from 1) out, unless it has only the fewest it needs.</summary>
    public void Remove(int number)
    {
        var points = Points.ToList();
        Check(number, points.Count);
        if (points.Count <= Attribute.Least)
            throw new InvalidOperationException($"It needs at least {Attribute.Least} point{(Attribute.Least == 1 ? "" : "s")}: set this one instead.");
        points.RemoveAt(number - 1);
        Save(points, seal: true);
    }

    /// <summary>Moves point <paramref name="number"/> (from 2) one place up the list.</summary>
    public void MoveUp(int number)
    {
        var points = Points.ToList();
        Check(number, points.Count);
        if (number == 1)
            throw new InvalidOperationException("It's first already.");
        (points[number - 2], points[number - 1]) = (points[number - 1], points[number - 2]);
        Save(points, seal: true);
    }

    public IRelayCommand AddCommand => _add ??= new RelayCommand(Add);

    private IRelayCommand? _add;

    public override void Refresh()
    {
        Fit(Rows, Points.Count, () => new PointRow(this));
        OnPropertyChanged(string.Empty);
    }

    // One axis of a point set from its row's box: while it's dragged, one undo step for the whole drag.
    internal void SetAxis(int number, int axis, double value)
    {
        var points = Points.ToList();
        if (number < 1 || number > points.Count)
            return;
        var p = points[number - 1];
        points[number - 1] = Fit(axis == 0 ? p with { X = (float)value } : p with { Y = (float)value });
        Save(points, seal: false);
    }

    private Vector2 Fit(Vector2 p) => Attribute.Cells ? new Vector2(MathF.Round(p.X), MathF.Round(p.Y)) : p;

    private void Save(List<Vector2> points, bool seal)
    {
        var text = FieldValues.WritePoints(points);
        if (seal)
            SetText(text);
        else
            SetTextLive(text);
    }

    private static void Check(int number, int count)
    {
        if (number < 1 || number > count)
            throw new ArgumentOutOfRangeException(nameof(number), count == 0 ? "There are no points yet: Add makes one." : $"The points are numbered 1 to {count}.");
    }
}

/// <summary>One point of a <see cref="PointsEditor"/>: its X and Y.</summary>
public sealed class PointRow(PointsEditor owner) : EditorRow(owner.Remove)
{
    private Vector2 Point => owner.Points.ElementAtOrDefault(Number - 1);

    /// <summary>Across: right is more.</summary>
    public double X
    {
        get => Point.X;
        set => owner.SetAxis(Number, 0, value);
    }

    /// <summary>Up is more.</summary>
    public double Y
    {
        get => Point.Y;
        set => owner.SetAxis(Number, 1, value);
    }

    /// <summary>Whole numbers only, for a grid's cells.</summary>
    public bool IsInteger => owner.Attribute.Cells;

    /// <summary>True when it can be taken out: there are more points than the fewest needed.</summary>
    public bool CanRemove => owner.CanRemove;

    /// <summary>True when it can move up: it isn't first.</summary>
    public bool CanMoveUp => Number > 1;

    /// <summary>Moves it one place up the list, as one undo step.</summary>
    public IRelayCommand MoveUpCommand => _up ??= new RelayCommand(() => owner.MoveUp(Number));

    private IRelayCommand? _up;
}
