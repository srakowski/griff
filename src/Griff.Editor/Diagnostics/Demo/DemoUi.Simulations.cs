#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Features.Data;

namespace Griff.Editor.Diagnostics.Demo;

// The Simulations panel: its settings, its Run button and its tabs of results, worked with the real mouse and keyboard.
internal sealed partial class DemoUi
{
    /// <summary>The Simulations panel's view model.</summary>
    public static SimulationsPanel Simulations => (SimulationsPanel)PanelContent("Simulations").DataContext;

    /// <summary>
    /// A part of the Simulations panel, by its tag: <c>simulation</c> (the picker), <c>new</c>, <c>run</c>, <c>stop</c>,
    /// <c>progress</c>, <c>undo</c>, <c>redo</c>, <c>export</c>, <c>summary</c>, <c>settings</c> and each setting
    /// (<c>arena</c>, <c>first:entity</c>, <c>first:table</c>, <c>first:rows</c>, the same for <c>second:</c>, <c>runs</c>,
    /// <c>seed</c>, <c>timelimit</c>, <c>rewards</c>, <c>track</c>, <c>sweeps</c>, <c>sweep:table</c>, <c>sweep:column</c>,
    /// <c>sweep:row</c>, <c>sweep:from</c>, <c>sweep:to</c>, <c>sweep:step</c>, <c>script</c>, and a scene run's
    /// <c>playsscene</c>, <c>scene</c>, <c>actevery</c>, <c>steprate</c>, <c>trackevery</c>, <c>endwhen</c>, <c>flags</c> and
    /// <c>closepanels</c>), <c>size</c>,
    /// <c>problems</c>, <c>findings</c>, <c>tabs</c>, <c>measure</c>, <c>step</c>, <c>pair</c>, <c>results</c> (the grid of
    /// pairs), <c>tabgrid</c> (the sweep's or run's grid), <c>matrix</c>, <c>chart</c>, <c>charttitle</c>,
    /// <c>chartsummary</c>, <c>message</c>, or <c>tab:Matrix</c> (a tab).
    /// </summary>
    public static FrameworkElement SimsPart(string part)
    {
        if (part.StartsWith("tab:", StringComparison.OrdinalIgnoreCase))
        {
            var name = part[4..].Trim();
            var tabs = (ListBox)SimsPart("tabs");
            return Descendants<ListBoxItem>(tabs).FirstOrDefault(i => string.Equals(i.DataContext as string, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The Simulations panel has no tab {name}.");
        }

        var tag = "sims:" + part;
        return Descendants<FrameworkElement>(PanelContent("Simulations")).FirstOrDefault(e => string.Equals(e.Tag as string, tag, StringComparison.OrdinalIgnoreCase) && e.IsVisible)
            ?? throw new InvalidOperationException($"No sims:{part} showing.");
    }

    // A setting's name as the demo writes it, and the panel's property for it.
    private static readonly Dictionary<string, string> SimProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arena"] = nameof(SimulationsPanel.Arena),
        ["first:entity"] = nameof(SimulationsPanel.FirstEntity),
        ["first:table"] = nameof(SimulationsPanel.FirstTable),
        ["first:rows"] = nameof(SimulationsPanel.FirstRows),
        ["second:entity"] = nameof(SimulationsPanel.SecondEntity),
        ["second:table"] = nameof(SimulationsPanel.SecondTable),
        ["second:rows"] = nameof(SimulationsPanel.SecondRows),
        ["runs"] = nameof(SimulationsPanel.Runs),
        ["seed"] = nameof(SimulationsPanel.Seed),
        ["timelimit"] = nameof(SimulationsPanel.TimeLimit),
        ["rewards"] = nameof(SimulationsPanel.Rewards),
        ["track"] = nameof(SimulationsPanel.Track),
        ["sweeps"] = nameof(SimulationsPanel.Sweeps),
        ["sweep:table"] = nameof(SimulationsPanel.SweepTable),
        ["sweep:column"] = nameof(SimulationsPanel.SweepColumn),
        ["sweep:row"] = nameof(SimulationsPanel.SweepRow),
        ["sweep:from"] = nameof(SimulationsPanel.SweepFrom),
        ["sweep:to"] = nameof(SimulationsPanel.SweepTo),
        ["sweep:step"] = nameof(SimulationsPanel.SweepStep),
        ["script"] = nameof(SimulationsPanel.Script),
        ["playsscene"] = nameof(SimulationsPanel.PlaysScene),
        ["scene"] = nameof(SimulationsPanel.Scene),
        ["actevery"] = nameof(SimulationsPanel.ActEvery),
        ["steprate"] = nameof(SimulationsPanel.StepRate),
        ["trackevery"] = nameof(SimulationsPanel.TrackEvery),
        ["endwhen"] = nameof(SimulationsPanel.EndWhen),
        ["flags"] = nameof(SimulationsPanel.Flags),
        ["closepanels"] = nameof(SimulationsPanel.ClosePanels),
        ["measure"] = nameof(SimulationsPanel.Measure),
        ["step"] = nameof(SimulationsPanel.StepIndex),
        ["pair"] = nameof(SimulationsPanel.RunPair),
    };

    /// <summary>What a setting reads now, as text (a step by its name: "Level 3").</summary>
    public static string SimSetting(string setting)
    {
        var panel = Simulations;
        if (string.Equals(setting, "step", StringComparison.OrdinalIgnoreCase))
            return panel.StepNames.ElementAtOrDefault(panel.StepIndex) ?? "";
        var property = typeof(SimulationsPanel).GetProperty(SimProperties.GetValueOrDefault(setting.Trim())
            ?? throw new InvalidOperationException($"The Simulations panel has no setting {setting}: {string.Join(", ", SimProperties.Keys)}."))!;
        return property.GetValue(panel) switch
        {
            null => "",
            bool on => on ? "true" : "false",
            double number => TablesPanel.Format(number),
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
        };
    }

    /// <summary>
    /// Sets one of the Simulations panel's settings as someone would: picks it from its list, types it into its box and
    /// presses Enter, or ticks its box.
    /// </summary>
    public async Task SimSet(string setting, string value)
    {
        var key = setting.Trim().ToLowerInvariant();
        var wanted = value.Trim();
        var part = SimsPart(key);
        switch (part)
        {
            case ComboBox box:
            {
                part.BringIntoView();
                await Settle(80);
                await Click(box);
                var item = await WaitFor(() => Find<ComboBoxItem>(i => string.Equals(i.Content as string ?? i.DataContext as string, wanted, StringComparison.OrdinalIgnoreCase) && i.IsVisible),
                    $"{wanted} in {setting}'s list", 3000);
                await Settle(80);
                await Click(item);
                await Settle(150);
                box.IsDropDownOpen = false;
                break;
            }

            case ToggleButton toggle:
            {
                var on = wanted.Equals("true", StringComparison.OrdinalIgnoreCase) || wanted.Equals("yes", StringComparison.OrdinalIgnoreCase);
                if (toggle.IsChecked != on)
                {
                    part.BringIntoView();
                    await Settle(150);
                    await Click(toggle);
                    await Settle(150);
                }

                break;
            }

            default:
            {
                var text = part as TextBox ?? Descendants<TextBox>(part).First(t => t.IsVisible);
                text.BringIntoView();
                await Settle(80);
                await Click(text);
                await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
                await input.Type(wanted);
                await input.Press(Key.Enter, showBadge: false);
                await Settle(150);
                break;
            }
        }

        if (!string.Equals(SimSetting(key), wanted, StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"FORCED simset {setting} = {value} (it read {SimSetting(key)})");
            var panel = Simulations;
            var property = typeof(SimulationsPanel).GetProperty(SimProperties[key])!;
            object? converted = key == "step"
                ? Math.Max(0, panel.StepNames.ToList().FindIndex(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase)))
                : property.PropertyType == typeof(bool) ? wanted.Equals("true", StringComparison.OrdinalIgnoreCase)
                : property.PropertyType == typeof(int) ? int.Parse(wanted, System.Globalization.CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(double) ? double.Parse(wanted, System.Globalization.CultureInfo.InvariantCulture)
                : wanted;
            property.SetValue(panel, converted);
            await Settle();
        }
    }

    /// <summary>Clicks one of the Simulations panel's tabs of results: Results, Matrix, Sweep or Over a Run.</summary>
    public async Task SimTab(string tab)
    {
        await Click(SimsPart("tab:" + tab));
        await Settle(150);
        if (!string.Equals(Simulations.Tab, tab.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"FORCED simtab {tab}");
            Simulations.Tab = tab.Trim();
            await Settle();
        }
    }

    /// <summary>Clicks Run and waits (up to a minute) for the simulation to finish.</summary>
    public async Task SimRun()
    {
        var panel = Simulations;
        await Click(SimsPart("run"));
        await Settle(150);
        if (!panel.IsRunning && !panel.HasResults)
        {
            Log?.Invoke("FORCED simrun");
            _ = panel.RunAsync();
        }

        var started = Environment.TickCount64;
        while (panel.IsRunning)
        {
            if (Environment.TickCount64 - started > 60_000)
                throw new TimeoutException("The simulation was still running after a minute.");
            await Settle(100);
        }

        await Settle(200);
        if (!panel.HasResults || panel.IsStale)
            throw new InvalidOperationException($"The simulation didn't run: {panel.Message}");
    }
}
#endif
