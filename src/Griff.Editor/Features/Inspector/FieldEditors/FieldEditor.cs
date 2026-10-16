using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// The view model of a field's own editor in the Inspector (see <see cref="FieldEditorAttribute"/>): a dice builder, a list
/// of points, tiles to click. One is made for each field marked with an attribute that has an editor: every non-abstract
/// <see cref="FieldEditor{TAttribute}"/> in the editor is found by its attribute's type, so there's no list to keep, and a
/// script's field marked the same gets the same editor. Its template is the DataTemplate for its type (in
/// FieldEditorTemplates.xaml), and agents reach it as the field's Editor through ui_inspect, ui_set and ui_invoke.
/// Every change it makes is an undo step of the field's value, set on each selected entity.
/// </summary>
public abstract class FieldEditor : ObservableObject
{
    private static readonly Lazy<IReadOnlyDictionary<Type, Type>> Found = new(Discover);

    protected FieldEditor(PropertyField field) => Field = field;

    /// <summary>The field it edits.</summary>
    public PropertyField Field { get; }

    /// <summary>The (primary selected entity's) component the field is on.</summary>
    protected Component Component => Field.Components[0];

    /// <summary>What the field edits through: its undo steps, the project's names and art.</summary>
    protected IPropertyHost Host => Field.Host;

    /// <summary>The field's text: empty when it's empty, or the selected entities' values differ.</summary>
    protected string Text => Field.IsMixed ? "" : Field.Value as string ?? "";

    /// <summary>The field's lines: none when it has none, or the selected entities' values differ.</summary>
    protected IReadOnlyList<string> Lines => Field.IsMixed ? [] : Field.Value as IReadOnlyList<string> ?? [];

    /// <summary>Sets the field's text as an undo step of its own (empty clears it).</summary>
    protected void SetText(string? text) => Field.TextValue = string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>
    /// Sets the field's text without ending the undo step, for a number being dragged or typed: the Inspector ends it when
    /// the edit's done, so a whole drag is one step.
    /// </summary>
    protected void SetTextLive(string? text) => Field.Value = string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>Sets the field's lines; <paramref name="seal"/> ends the undo step (not while a number's dragged).</summary>
    protected void SetLines(IEnumerable<string> lines, bool seal = true)
    {
        IReadOnlyList<string> list = [.. lines];
        if (Field.IsMixed || Field.Value is not IReadOnlyList<string> current || !current.SequenceEqual(list))
            Field.Value = list;
        if (seal)
            Host.Seal();
    }

    /// <summary>
    /// True for an editor that takes the Inspector section's whole width under the field's name, rather than the space beside
    /// it: rows of several cells, ticks.
    /// </summary>
    public virtual bool Stacks => false;

    /// <summary>Shows the value as it is now: the field calls it as its value changes (an undo, a drag in the Scene view).</summary>
    public virtual void Refresh() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// The editors there are, by the attribute that asks for each: every non-abstract <see cref="FieldEditor{TAttribute}"/>
    /// in the editor.
    /// </summary>
    public static IReadOnlyDictionary<Type, Type> Kinds => Concrete.Value;

    // The editors of attributes a field can be marked with: not those of the abstract ones others are made from (rows), which
    // Found keeps for them to fall back to.
    private static readonly Lazy<IReadOnlyDictionary<Type, Type>> Concrete = new(() => Found.Value.Where(k => !k.Key.IsAbstract).ToDictionary());

    /// <summary>The editor for <paramref name="field"/>, marked <paramref name="attribute"/>; null when no editor takes it.</summary>
    public static FieldEditor? Create(PropertyField field, FieldEditorAttribute attribute) =>
        EditorOf(attribute.GetType()) is { } type ? (FieldEditor)Activator.CreateInstance(type, field, attribute)! : null;

    // The editor for an attribute's type, or for the nearest type it's made from that has one (a script's own kind of rows
    // gets the rows' editor).
    private static Type? EditorOf(Type attribute)
    {
        for (var t = attribute; t is not null && t != typeof(FieldEditorAttribute); t = t.BaseType)
        {
            if (Found.Value.TryGetValue(t, out var editor))
                return editor;
        }

        return null;
    }

    private static Dictionary<Type, Type> Discover()
    {
        var editors = new Dictionary<Type, Type>();
        foreach (var type in typeof(FieldEditor).Assembly.GetTypes().Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            for (var t = type.BaseType; t is not null; t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(FieldEditor<>))
                {
                    var attribute = t.GetGenericArguments()[0];
                    if (type.GetConstructor(BindingFlags.Public | BindingFlags.Instance, [typeof(PropertyField), attribute]) is null)
                        throw new InvalidOperationException($"{type.Name} needs a public constructor taking (PropertyField, {attribute.Name}).");
                    if (!editors.TryAdd(attribute, type))
                        throw new InvalidOperationException($"{type.Name} and {editors[attribute].Name} both edit [{attribute.Name}].");
                    break;
                }
            }
        }

        // Griff's and its modules' attributes made from one an editor takes (each kind of rows) get its editor.
        foreach (var attribute in ModuleAssemblies.All.Prepend(typeof(FieldEditorAttribute).Assembly).SelectMany(a => a.GetTypes())
                     .Where(t => t is { IsAbstract: false } && t.IsSubclassOf(typeof(FieldEditorAttribute))))
        {
            for (var t = attribute.BaseType; t is not null && !editors.ContainsKey(attribute); t = t.BaseType)
            {
                if (editors.TryGetValue(t, out var editor))
                    editors[attribute] = editor;
            }
        }

        return editors;
    }

    /// <summary>
    /// Makes <paramref name="rows"/> hold <paramref name="count"/> rows, numbered from 1 and showing what they hold now,
    /// reusing those there so a box being typed in or dragged stays.
    /// </summary>
    protected static void Fit<TRow>(ObservableCollection<TRow> rows, int count, Func<TRow> make)
        where TRow : EditorRow
    {
        while (rows.Count > count)
            rows.RemoveAt(rows.Count - 1);
        while (rows.Count < count)
            rows.Add(make());
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Number = i + 1;
            rows[i].Refresh();
        }
    }
}

/// <summary>A field's own editor for fields marked <typeparamref name="TAttribute"/>; see <see cref="FieldEditor"/>.</summary>
public abstract class FieldEditor<TAttribute>(PropertyField field, TAttribute attribute) : FieldEditor(field)
    where TAttribute : FieldEditorAttribute
{
    /// <summary>The attribute that asked for it, with what it says about the field.</summary>
    public TAttribute Attribute { get; } = attribute;
}

/// <summary>A row of a list a field's editor shows (a point, a run of tiles, a clip), by its place in the list.</summary>
/// <param name="remove">Takes the row out of the list, by its <see cref="Number"/>.</param>
public abstract class EditorRow(Action<int> remove) : ObservableObject
{
    private IRelayCommand? _remove;

    /// <summary>Where it is in the list, from 1.</summary>
    public int Number { get; internal set; }

    /// <summary>Takes it out of the list, as one undo step (its × button).</summary>
    public IRelayCommand RemoveCommand => _remove ??= new RelayCommand(() => remove(Number));

    /// <summary>Shows what it holds now.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// Tags a part of a field editor's row for demos to find it: the converter parameter ("editor:remove:") with the row's
/// number or name after it ("editor:remove:2").
/// </summary>
public sealed class EditorTagConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        $"{parameter}{value}";

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
