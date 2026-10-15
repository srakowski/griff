using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Weather: rain, snow, leaves and embers over the view, and storms' lightning.
public sealed partial class Game
{
    // Weather is scenery, so it has its own randomness and never changes what Rand gives the game.
    private readonly Random _weatherRandom = new();

    private void RunWeather()
    {
        var camera = SceneRenderer.FindCamera(Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;

        // Room for the widest window and a little more, so nothing pops in at the edges.
        var area = new Vector2(size * 2 * Math.Max(ViewAspect, 1) + 2, size * 2 + 2);
        foreach (var weather in Scene.Running<Weather>())
        {
            if (Vector2.Distance(weather.Area, area) > 0.5f)
            {
                weather.Area = area;
                weather.Drops.Clear();
            }

            var wanted = weather.CountFor(weather.Area);
            while (weather.Drops.Count < wanted)
                weather.Drops.Add(NewDrop(weather));
            if (weather.Drops.Count > wanted)
                weather.Drops.RemoveRange(wanted, weather.Drops.Count - wanted);

            foreach (var drop in weather.Drops)
                MoveDrop(weather, drop, Time.Delta);
            if (Time.Delta > 0)
                weather.MovedIn = Scene.Step;
            RunStorm(weather);
        }
    }

    private Weather.Drop NewDrop(Weather weather)
    {
        float Range(float from, float to) => from + _weatherRandom.NextSingle() * (to - from);
        var drop = new Weather.Drop
        {
            Position = new Vector2(Range(0, weather.Area.X), Range(0, weather.Area.Y)),
            Phase = Range(0, MathF.Tau),
            Angle = Range(0, MathF.Tau),
        };
        (drop.From, drop.PhaseFrom, drop.AngleFrom) = (drop.Position, drop.Phase, drop.Angle);
        switch (weather.Kind)
        {
            case WeatherKind.Rain:
                drop.Velocity = new Vector2(0, -Range(16, 22));
                drop.Size = Range(0.35f, 0.6f);
                drop.Color = new Color(0.68f, 0.76f, 0.9f, Range(0.35f, 0.6f));
                break;
            case WeatherKind.Snow:
                drop.Velocity = new Vector2(0, -Range(0.8f, 1.8f));
                drop.Size = Range(0.05f, 0.13f);
                drop.Color = new Color(1, 1, 1, Range(0.7f, 1));
                break;
            case WeatherKind.Leaves:
                drop.Velocity = new Vector2(0, -Range(0.9f, 1.6f));
                drop.Size = Range(0.14f, 0.22f);
                drop.Spin = Range(-3, 3);
                drop.Color = _weatherRandom.Next(4) switch
                {
                    0 => Color.FromBytes(0xE0, 0x7A, 0x2C),
                    1 => Color.FromBytes(0xC4, 0x3E, 0x26),
                    2 => Color.FromBytes(0xE8, 0xB0, 0x3A),
                    _ => Color.FromBytes(0x9C, 0x5A, 0x2A),
                };
                break;
            default:
                drop.Velocity = new Vector2(0, Range(0.8f, 2.2f));
                drop.Size = Range(0.04f, 0.09f);
                drop.Color = _weatherRandom.Next(3) switch
                {
                    0 => Color.FromBytes(0xFF, 0xB3, 0x47),
                    1 => Color.FromBytes(0xFF, 0x7A, 0x2E),
                    _ => Color.FromBytes(0xFF, 0xDE, 0x7A),
                };
                break;
        }

        return drop;
    }

    // Falls (or rises) at its own speed, pushed by the wind; snow sways, leaves flutter and tumble, embers waver.
    // Positions wrap round the area, so the view never runs out.
    private static void MoveDrop(Weather weather, Weather.Drop drop, float seconds)
    {
        (drop.From, drop.PhaseFrom, drop.AngleFrom) = (drop.Position, drop.Phase, drop.Angle);
        drop.Phase += seconds * (weather.Kind == WeatherKind.Embers ? 7 : 1.7f);
        var velocity = drop.Velocity * weather.Speed + new Vector2(weather.Wind, 0);
        velocity.X += weather.Kind switch
        {
            WeatherKind.Snow => MathF.Sin(drop.Phase) * 0.5f,
            WeatherKind.Leaves => MathF.Sin(drop.Phase * 1.3f) * 1.4f,
            WeatherKind.Embers => MathF.Sin(drop.Phase * 0.4f) * 0.6f,
            _ => 0,
        };
        drop.Position += velocity * seconds;
        drop.Angle += drop.Spin * seconds;
        drop.Position = new Vector2(Wrap(drop.Position.X, weather.Area.X), Wrap(drop.Position.Y, weather.Area.Y));
    }

    private static float Wrap(float value, float length) => length > 0 ? value - MathF.Floor(value / length) * length : value;

    // A storm flashes every 4 to 11 seconds, twice in quick succession, with thunder rolling in a moment later.
    private void RunStorm(Weather weather)
    {
        if (!weather.Storm || !(Time.Delta > 0))
        {
            weather.NextLightning = -1;
            return;
        }

        if (weather.NextLightning < 0)
            weather.NextLightning = 2 + _weatherRandom.NextSingle() * 4;
        weather.NextLightning -= Time.Delta;
        if (weather.ThunderIn >= 0)
        {
            weather.ThunderIn -= Time.Delta;
            if (weather.ThunderIn < 0 && !string.IsNullOrWhiteSpace(weather.ThunderSound))
                Audio.Play(weather.ThunderSound.Trim(), 1, 0, 0.85f + _weatherRandom.NextSingle() * 0.3f);
        }

        if (weather.FlickerIn >= 0)
        {
            weather.FlickerIn -= Time.Delta;
            if (weather.FlickerIn < 0)
                FlashScreen(new Color(0.92f, 0.95f, 1, 0.7f), 0.45f);
        }

        if (weather.NextLightning <= 0)
        {
            FlashScreen(new Color(0.92f, 0.95f, 1, 0.45f), 0.12f);
            weather.FlickerIn = 0.14f;
            weather.ThunderIn = 0.4f + _weatherRandom.NextSingle() * 0.9f;
            weather.NextLightning = 4 + _weatherRandom.NextSingle() * 7;
        }
    }
}
