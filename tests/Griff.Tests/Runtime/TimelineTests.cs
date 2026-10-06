using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class TimelineTests
{
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());

    [Fact]
    public void Rewind_RestoresEarlierState_AndResumingBranches()
    {
        var scene = new Scene();
        var ball = new Entity("Ball");
        ball.AddComponent<BoxCollider>();
        var body = ball.AddComponent<Body>();
        body.GravityScale = 0;
        body.Velocity = new Vector2(1, 0);
        scene.Add(ball);
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(_serializer, capacity: 100, framesPerSnapshot: 10);

        for (var frame = 0; frame < 60; frame++)
        {
            game.Tick(1 / 60.0);
            timeline.Record(scene, game.Time.Total);
        }

        Assert.Equal(6, timeline.Count);
        Assert.Equal(1f, ball.Transform.Position.X, 2);

        // Go back to the snapshot taken at about a third of a second.
        var restored = timeline.Restore(1);
        var restoredBall = restored.Find(ball.Id)!;
        Assert.Equal(0.333f, restoredBall.Transform.Position.X, 2);
        Assert.Equal(new Vector2(1, 0), restoredBall.GetComponent<Body>()!.Velocity);

        // Change the past and resume: the old future is gone.
        timeline.TruncateAfter(1);
        Assert.Equal(2, timeline.Count);
        restoredBall.GetComponent<Body>()!.Velocity = new Vector2(0, 2);
        var resumed = new Game(restored, new InputState());
        for (var frame = 0; frame < 30; frame++)
            resumed.Tick(1 / 60.0);

        Assert.Equal(0.333f, restoredBall.Transform.Position.X, 2);
        Assert.Equal(1f, restoredBall.Transform.Position.Y, 2);
    }

    [Fact]
    public void Capacity_DropsOldestSnapshots()
    {
        var scene = new Scene();
        var timeline = new Timeline(_serializer, capacity: 3, framesPerSnapshot: 1);

        for (var i = 0; i < 10; i++)
            timeline.Record(scene, i);

        Assert.Equal([7.0, 8.0, 9.0], timeline.Times);
    }
}
