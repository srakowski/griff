using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Rooms: a camera that keeps to the room its target is in and slides over to the next.</summary>
public class RoomTests
{
    private const float Step = 1 / 60f;
    private const float Aspect = 16f / 9;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly Entity _camera;
    private readonly Entity _player;
    private readonly List<string> _music = [];

    public RoomTests()
    {
        _camera = new Entity("Camera");
        _camera.AddComponent<Camera>().Size = 5;
        _camera.AddComponent<Follow>().Smoothing = 0;
        _scene.Add(_camera);
        _player = new Entity("Player");
        _scene.Add(_player);
        _game = new Game(_scene, new InputState()) { ViewAspect = Aspect, Audio = new MusicLog(_music) };
    }

    private Room AddRoom(string name, Vector2 center, Vector2? size = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(center, 0);
        _scene.Add(entity);
        var room = entity.AddComponent<Room>();
        room.Size = size ?? new Vector2(20, 10);
        return room;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
            _game.Tick(Step);
    }

    private void PlaceAt(Entity entity, Vector2 position) => entity.Transform.Position = new Vector3(position, 0);

    private Vector2 CameraAt => new(_camera.Transform.Position.X, _camera.Transform.Position.Y);

    [Fact]
    public void Camera_StaysInsideTheRoomTheTargetIsIn()
    {
        AddRoom("Hall", Vector2.Zero);
        PlaceAt(_player, new Vector2(9, 4));

        _game.Tick(Step);

        // The view is 17.8 by 10, so in a 20 by 10 room it can go 1.1 either side of the middle, and not up or down.
        Assert.Equal(20 / 2f - 5 * Aspect, CameraAt.X, 3);
        Assert.Equal(0, CameraAt.Y, 3);
        Assert.True(_scene.Entities.First(e => e.Name == "Hall").GetComponent<Room>()!.IsCurrent);
    }

    [Fact]
    public void CrossingIntoTheNextRoom_SlidesTheCameraOver_WithTheGameHeldStill()
    {
        var hall = AddRoom("Hall", Vector2.Zero);
        var cave = AddRoom("Cave", new Vector2(20, 0));
        cave.Transition = 0.5f;
        PlaceAt(_player, new Vector2(0, 0));
        Run(0.1f);
        var entered = new List<Entity>();
        cave.Entered += entered.Add;

        PlaceAt(_player, new Vector2(10.5f, 0));
        Run(0.25f);

        var follow = _camera.GetComponent<Follow>()!;
        Assert.Equal(cave, follow.CurrentRoom);
        Assert.False(hall.IsCurrent);
        Assert.True(follow.IsChangingRoom);
        Assert.True(_game.Time.IsFrozen);
        Assert.InRange(CameraAt.X, 2, 18);
        Assert.Equal([_player], entered);

        Run(0.35f);
        Assert.False(follow.IsChangingRoom);
        Assert.False(_game.Time.IsFrozen);
        Assert.Equal(20 - (10 - 5 * Aspect), CameraAt.X, 3);
    }

    [Fact]
    public void WithoutFreeze_TheGameKeepsGoing()
    {
        AddRoom("Hall", Vector2.Zero);
        AddRoom("Cave", new Vector2(20, 0)).Freeze = false;
        Run(0.1f);

        PlaceAt(_player, new Vector2(11, 0));
        Run(0.1f);

        Assert.False(_game.Time.IsFrozen);
        Assert.True(_camera.GetComponent<Follow>()!.IsChangingRoom);
    }

    [Fact]
    public void OutsideEveryRoom_TheCameraKeepsToTheLastOne()
    {
        var hall = AddRoom("Hall", Vector2.Zero);
        Run(0.1f);

        PlaceAt(_player, new Vector2(40, 0));
        Run(0.1f);

        Assert.Equal(hall, _camera.GetComponent<Follow>()!.CurrentRoom);
        Assert.Equal(10 - 5 * Aspect, CameraAt.X, 3);
    }

    [Fact]
    public void Rooms_SendTheirMessage_AndPlayTheirMusic_OnceForTheSameTune()
    {
        var hall = AddRoom("Hall", Vector2.Zero);
        hall.Music = "Music/town.mp3";
        hall.Message = "EnteredHall";
        var shop = AddRoom("Shop", new Vector2(20, 0));
        shop.Music = "Music/town.mp3";
        shop.Transition = 0;
        var cave = AddRoom("Cave", new Vector2(40, 0));
        cave.Music = "Music/cave.mp3";
        cave.Transition = 0;
        var heard = 0;
        _game.On("EnteredHall", _ => heard++);

        Run(0.1f);
        PlaceAt(_player, new Vector2(20, 0));
        Run(0.1f);
        PlaceAt(_player, new Vector2(40, 0));
        Run(0.1f);

        Assert.Equal(1, heard);
        Assert.Equal(["Music/town.mp3", "Music/cave.mp3"], _music);
    }

    [Fact]
    public void ADarkRoom_DarkensTheCamera_AndLeavingItBringsTheLightBack()
    {
        AddRoom("Hall", Vector2.Zero);
        var cave = AddRoom("Cave", new Vector2(20, 0));
        cave.Ambient = Color.FromBytes(0x10, 0x10, 0x20);
        cave.Transition = 0.2f;
        var camera = _camera.GetComponent<Camera>()!;
        Run(0.1f);
        Assert.False(camera.IsDark);

        PlaceAt(_player, new Vector2(20, 0));
        Run(0.1f);
        Assert.True(camera.IsDark);
        Assert.InRange(camera.CurrentAmbient.R, 0.1f, 0.9f);
        Run(0.3f);
        Assert.Equal(cave.Ambient, camera.CurrentAmbient);
        Assert.Equal(Color.White, camera.Ambient);

        PlaceAt(_player, new Vector2(0, 0));
        Run(0.6f);
        Assert.Equal(Color.White, camera.CurrentAmbient);
    }

    [Fact]
    public void Room_IsSavedWithTheScene()
    {
        var room = AddRoom("Cave", new Vector2(3, 4), new Vector2(30, 12));
        room.Transition = 0.8f;
        room.Freeze = false;
        room.Music = "Music/cave.mp3";
        room.Ambient = Color.Black;
        room.Message = "Cave";
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Cave").GetComponent<Room>()!;

        Assert.Equal((new Vector2(30, 12), 0.8f, false, "Music/cave.mp3", Color.Black, "Cave"),
            (copy.Size, copy.Transition, copy.Freeze, copy.Music, copy.Ambient, copy.Message));
    }

    private sealed class MusicLog(List<string> played) : IAudio
    {
        public float Volume { get; set; } = 1;
        public float MusicVolume { get; set; } = 1;
        public float SoundVolume { get; set; } = 1;
        public bool Muted { get; set; }

        public void Play(string sound, float volume = 1)
        {
        }

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => played.Add(music);

        public void StopMusic()
        {
        }

        public void StopAll()
        {
        }
    }
}
