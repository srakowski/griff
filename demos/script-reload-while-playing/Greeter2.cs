// Puts up a sign over itself when the game starts, with a friendlier greeting.
public class Greeter : Behavior
{
    protected override void Start()
    {
        var sign = new Entity("Sign");
        sign.AddComponent<Text>().Value = "Welcome back!";
        sign.Transform.Position = Transform.Position + new Vector3(0, 2, 0);
        Game.Spawn(sign);
    }
}
