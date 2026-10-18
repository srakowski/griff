// Puts up a sign over itself when the game starts.
public class Greeter : Behavior
{
    protected override void Start()
    {
        var sign = new Entity("Greeting");
        sign.AddComponent<Text>().Value = "Welcome!";
        sign.Transform.Position = Transform.Position + new Vector3(0, 2, 0);
        Game.Spawn(sign);
    }
}
