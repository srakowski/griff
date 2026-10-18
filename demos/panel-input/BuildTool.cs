// A builder's tool: T picks it up, Esc puts it down. While it's in hand the script has Esc, so the game menu waits.
public class BuildTool : Behavior
{
    public bool Held { get; set; } = true;

    // The presses of Esc this script has seen: a press a panel used doesn't count.
    public int EscSeen { get; set; }

    protected override bool ClaimsBack => Held;

    protected override void Update()
    {
        if (Input.WasPressed(Key.Escape))
        {
            EscSeen++;
            Held = false;
        }

        if (Input.WasPressed(Key.T))
            Held = true;
        GetComponent<Text>()!.Value = (Held ? "Tool in hand (Esc puts it down)" : "No tool (T picks one up)") + $"\nEsc seen: {EscSeen}";
    }
}
