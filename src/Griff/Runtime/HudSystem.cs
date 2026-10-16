using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Lays out what's on the screen: each system's HUD, bars, stars, counters and text outputs showing what they read,
/// typewriters typing, and what's anchored to the screen.
/// </summary>
internal sealed class HudSystem : GameSystem
{
    public override string Name => "HUD";

    // Pins ScreenAnchor entities to their point on the camera's view, parents before children.
    internal void LayOutScreen()
    {
        var anchors = Game.Scene.ActiveNow<ScreenAnchor>();
        if (anchors.Count == 0)
            return;

        var camera = Views.FindCamera(Game.Scene);
        // What's behind the minigame that has the screen isn't seen, so it isn't pinned to its camera either.
        var shown = Game.Minigame;
        var component = camera?.GetComponent<Camera>();
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        center += component?.ShakeOffset ?? Vector2.Zero;
        var halfHeight = component?.Size ?? Views.DefaultCameraSize;
        var half = new Vector2(halfHeight * (Game.ViewAspect > 0 ? Game.ViewAspect : 1), halfHeight);

        foreach (var anchor in anchors)
        {
            // An entity's first anchor places it.
            if (anchor.Entity!.GetComponent<ScreenAnchor>() != anchor || MinigameRoot.IsBehind(anchor.Entity, shown))
                continue;
            var transform = anchor.Entity!.Transform;
            var world = center + ScreenAnchor.Direction(anchor.Anchor) * half + anchor.Offset;
            var target = new Vector3(world, transform.WorldPosition.Z);
            if (anchor.Entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
                target = Vector3.Transform(target, inverse);
            transform.Position = new Vector3(target.X, target.Y, transform.Position.Z);
        }
    }

    // Typewriters run on real time, so dialogue still types out while a talk holds the game still; a pause menu holds them
    // still too, all but those on an open panel.
    internal void RunTypewriters()
    {
        foreach (var typewriter in Game.Scene.RunningNow<Typewriter>())
        {
            if (Game.Time.IsPaused && !Game.PanelSystem.InOpenPanel(typewriter.Entity!))
                continue;
            if (typewriter.Advance(Game.Time.UnscaledDelta))
                Game.Audio.Play(typewriter.Sound!);
        }
    }

    // Bars and texts show how things are, so they go on showing it while they're paused.
    internal void RunBars()
    {
        foreach (var bar in Game.Present<Bar>())
            bar.Advance(Game.Time.Delta, Game.Scene);
    }

    // Stars show how a level went on every machine, filling one after another in real time, so they go on under a panel
    // that pauses the game.
    internal void RunStars()
    {
        foreach (var stars in Game.Showing<Stars>())
        {
            var saved = string.IsNullOrWhiteSpace(stars.Saved) ? 0 : Game.SaveData.GetInt(stars.Saved.Trim());
            if (stars.Advance(Game.Time.UnscaledDelta, saved) && !string.IsNullOrWhiteSpace(stars.Sound))
                Game.Audio.Play(stars.Sound.Trim());
        }
    }

    // Counters show their count, keep their best, and pop when the count changes.
    internal void RunCounters()
    {
        foreach (var counter in Game.Active<Counter>())
        {
            var transform = counter.Entity!.Transform;
            var key = counter.SaveBestAs?.Trim();
            if (!string.IsNullOrWhiteSpace(counter.Item))
                counter.Change(Game.ItemSystem.ItemCount(counter.Item, counter.Entity) - counter.Value);
            if (counter.CountsFromSource)
                CountFromSource(counter);
            if (!string.IsNullOrWhiteSpace(counter.Output))
                CountOutput(counter);

            if (counter.Shown is null)
            {
                counter.RestingScale = transform.Scale;
                counter.Best = string.IsNullOrEmpty(key) ? counter.Value : Game.SaveData.GetInt(key);
                if (counter.BestToBeat < 0 && !string.IsNullOrEmpty(key))
                    counter.BestToBeat = Game.SaveData.Has(key) ? counter.Best : 0;
            }

            if (counter.Value > counter.Best)
            {
                // Beating the best kept as the scene started is a new best, said once (and not when nothing was kept).
                if (counter.BestToBeat > 0 && counter.Best <= counter.BestToBeat && !string.IsNullOrWhiteSpace(counter.NewBestMessage) &&
                    Game.Runs(counter.Entity!))
                    Game.Send(counter.NewBestMessage.Trim(), counter.Value, from: counter.Entity);
                counter.Best = counter.Value;
                if (!string.IsNullOrEmpty(key))
                    Game.SaveData.Set(key, counter.Best);
            }

            // The text is only written again when what it shows has changed, so a steady count makes no garbage.
            if (counter.Entity.GetComponent<Text>() is { } text &&
                (counter.Displayed != (counter.Value, counter.Best, counter.Total, counter.Format) || text.Value != counter.DisplayedText))
            {
                var display = counter.Display();
                counter.Displayed = (counter.Value, counter.Best, counter.Total, counter.Format);
                counter.DisplayedText = display;
                if (text.Value != display)
                    text.Value = display;
            }

            ReachedOrNot(counter);

            // A change pops it bigger, springing back over a moment (in game time, held while it's paused), with no garbage:
            // a pinball score changes all the time.
            if (counter.RestingScale is { } rest)
            {
                if (counter.Shown is { } shown && shown != counter.Value && counter.Pop > 0 && counter.Pop != 1)
                {
                    transform.Scale = new Vector3(rest.X * counter.Pop, rest.Y * counter.Pop, rest.Z);
                    counter.Popping = (0, counter.Pop);
                }
                else if (counter.Popping is var (age, pop) && !counter.Entity.IsPaused)
                {
                    age += Game.Time.Delta;
                    var size = age >= Counter.PopTime ? 1 : pop + (1 - pop) * Ease.OutBack(age / Counter.PopTime);
                    transform.Scale = new Vector3(rest.X * size, rest.Y * size, transform.Scale.Z);
                    counter.Popping = age >= Counter.PopTime ? null : (age, pop);
                }
            }

            counter.Shown = counter.Value;
        }
    }

    // A counter showing what a component in the scene counts (see ICounterSource): the goals covered out of how many there
    // are, a move history's moves, a match board's swaps out of its Moves, its score out of its Target Score or the gems of
    // a kind cleared out of how many it asks for, a falling-block board's score, a dungeon's floor. The first that counts
    // what the counter asks for.
    private void CountFromSource(Counter counter)
    {
        foreach (var source in Game.Scene.ActiveNow<ICounterSource>())
        {
            if (source.CountFor(counter) is not { } count)
                continue;
            counter.Total = count.Total;
            counter.Change(count.Value - counter.Value);
            return;
        }
    }

    // Texts showing an output fill it into what they say, made again only when it (or what it's out of) changes.
    internal void ShowOutputs()
    {
        foreach (var text in Game.Present<Text>())
        {
            if (string.IsNullOrWhiteSpace(text.Output))
            {
                if (text.ReadFrom is not null)
                {
                    text.ReadFrom = null;
                    text.SetReadout(null);
                }

                continue;
            }

            var reader = text.Reader;
            var found = reader.Find(text.Entity!, text.Track, text.Output);
            var (number, name, outOf) = found ? (reader.Number, reader.Text, reader.OutOf) : (0f, "", 0f);
            if (text.ReadFrom is { } from && ReferenceEquals(from.Template, text.Value) && from.Number.Equals(number) && from.Name == name &&
                from.OutOf.Equals(outOf))
                continue;
            text.ReadFrom = (text.Value, number, name, outOf);
            text.SetReadout(Readout.Fill(text.Value, number, name, outOf, found ? reader.Output!.ValueType : typeof(string)));
        }
    }

    // A counter counting an output (a board's score, the dungeon's floor, a faction's gold), out of what it's out of.
    private static void CountOutput(Counter counter)
    {
        if (!counter.Reader.Find(counter.Entity!, counter.Track, counter.Output))
            return;
        counter.Total = (int)MathF.Round(counter.Reader.OutOf);
        counter.Change((int)MathF.Round(counter.Reader.Number) - counter.Value);
    }

    // As a counter reaches its value, it sends its Reach Message (where the game is run; a player's own counter, such as
    // one on their HUD, on their machine alone). Starting there, or being put back there by a rewind, isn't reaching it.
    private void ReachedOrNot(Counter counter)
    {
        var reached = counter.Reached;
        var was = counter.WasReached ?? reached;
        counter.WasReached = reached;
        if (!reached || was || string.IsNullOrWhiteSpace(counter.ReachMessage) || !Game.Runs(counter.Entity!))
            return;
        var message = counter.ReachMessage.Trim();
        if (Game.Net.IsOnline && NetLocalAttribute.IsOn(counter.Entity!))
            Game.MessageSystem.SendFromNetwork(message, counter.Value, relay: false, TriggerSystem.ScopeOf(counter.Entity));
        else
            Game.Send(message, counter.Value, from: counter.Entity);
    }

    internal void LayOutHuds()
    {
        // By index: a button pressed while laying out may do anything.
        for (var i = 0; i < Game.Systems.Count; i++)
            Game.Systems[i].RunLayOutHud();
    }
}
