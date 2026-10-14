// Shows how the game keeps time: the steps it takes a second, and the frames the screen shows a second.
public class StepMeter : Behavior
{
    private long _steps, _frames;
    private double _since;

    protected override void Update()
    {
        var seconds = Time.RealTotal - _since;
        if (seconds < 0.5)
            return;
        GetComponent<Text>()!.Value =
            $"{(Time.Frame - _steps) / seconds:0} steps a second, {(Time.DrawnFrames - _frames) / seconds:0} frames a second";
        (_steps, _frames, _since) = (Time.Frame, Time.DrawnFrames, Time.RealTotal);
    }
}
