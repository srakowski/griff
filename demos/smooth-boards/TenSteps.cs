// Slows the game to ten steps a second, so it's easy to see what moves once a step and what's drawn between steps.
public class TenSteps : Behavior
{
    protected override void Start() => Time.StepRate = 10;
}
