using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Rewind for play mode: keeps periodic snapshots of the running scene so you can step back in time,
/// inspect or tweak, and resume from there. Snapshots use the scene file format, so they capture everything
/// a scene file does (public script properties included); private script fields restart from their defaults.
/// </summary>
public sealed class Timeline(SceneSerializer serializer, int capacity = 200, int framesPerSnapshot = 6)
{
    private readonly List<(double Time, string Json)> _snapshots = [];
    private int _framesSinceSnapshot;

    public int Count => _snapshots.Count;

    /// <summary>Game time of each snapshot, oldest first.</summary>
    public IEnumerable<double> Times => _snapshots.Select(s => s.Time);

    public double TimeAt(int index) => _snapshots[index].Time;

    /// <summary>Call once per game frame. Takes a snapshot every few frames and drops the oldest past capacity.</summary>
    public void Record(Scene scene, double time, bool force = false)
    {
        if (!force && ++_framesSinceSnapshot < framesPerSnapshot)
            return;

        _framesSinceSnapshot = 0;
        _snapshots.Add((time, serializer.SaveToString(scene)));
        if (_snapshots.Count > capacity)
            _snapshots.RemoveAt(0);
    }

    /// <summary>A fresh scene in the state of snapshot <paramref name="index"/>.</summary>
    public Scene Restore(int index) => serializer.LoadFromString(_snapshots[index].Json);

    /// <summary>Forgets everything after <paramref name="index"/>, so resuming from it starts a new future.</summary>
    public void TruncateAfter(int index)
    {
        if (index + 1 < _snapshots.Count)
            _snapshots.RemoveRange(index + 1, _snapshots.Count - index - 1);
        _framesSinceSnapshot = 0;
    }

    public void Clear()
    {
        _snapshots.Clear();
        _framesSinceSnapshot = 0;
    }
}
