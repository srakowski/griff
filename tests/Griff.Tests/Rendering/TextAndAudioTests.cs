using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

public class TextAndAudioTests
{
    [Fact]
    public void Text_IsDrawnInOrder_AboveItsSprite()
    {
        var scene = new Scene();
        var background = new Entity("Background");
        background.AddComponent<Sprite>().Order = -1;
        var label = new Entity("Score");
        var text = label.AddComponent<Text>();
        text.Value = "Score: 3";
        text.Size = 2;
        label.Transform.Position = new Vector3(1, 2, 0);
        var button = new Entity("Button");
        button.AddComponent<Sprite>();
        button.AddComponent<Text>().Value = "Play";
        scene.Add(background);
        scene.Add(label);
        scene.Add(button);

        var draws = SceneRenderer.Build(scene, 1).Sprites;

        Assert.Equal(["Background", "Score", "Button", "Button"], draws.Select(d => d.Entity.Name));
        Assert.Equal("Score: 3", draws[1].Text);
        Assert.Null(draws[2].Text);
        Assert.Equal("Play", draws[3].Text);
        Assert.Equal(new Vector3(1, 3, 0), Vector3.Transform(new Vector3(0, 0.5f, 0), draws[1].World));
    }

    [Fact]
    public void EmptyText_IsNotDrawn()
    {
        var scene = new Scene();
        var entity = new Entity();
        entity.AddComponent<Text>().Value = "";
        scene.Add(entity);

        Assert.Empty(SceneRenderer.Build(scene, 1).Sprites);
    }

    [Fact]
    public void Behaviors_CanPlaySounds_AndStopStopsAudio()
    {
        var scene = new Scene();
        var entity = new Entity();
        entity.AddComponent<Jumper>();
        scene.Add(entity);
        var audio = new RecordingAudio();
        var game = new Game(scene, new InputState()) { Audio = audio };

        game.Tick(0.016);
        game.Stop();

        Assert.Equal(["play Sounds/jump.wav", "music Music/theme.mp3", "stop all"], audio.Calls);
    }

    public class Jumper : Behavior
    {
        protected override void Start()
        {
            Audio.Play("Sounds/jump.wav");
            Audio.PlayMusic("Music/theme.mp3");
        }
    }

    private sealed class RecordingAudio : IAudio
    {
        public List<string> Calls { get; } = [];

        public void Play(string sound, float volume = 1f) => Calls.Add($"play {sound}");

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => Calls.Add($"music {music}");

        public void StopMusic() => Calls.Add("stop music");

        public void StopAll() => Calls.Add("stop all");
    }
}
