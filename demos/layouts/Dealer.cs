// Deals cards onto the board it's on with Space, and takes the first one away with Backspace. It never sets a position:
// the board's Layout places every card.
public class Dealer : Behavior
{
    private int _dealt;

    protected override void Update()
    {
        if (Input.WasPressed(Key.Space))
        {
            var card = new Entity($"Card {++_dealt + 4}");
            var sprite = card.AddComponent<Sprite>();
            sprite.Size = new Vector2(1.6f, 2);
            sprite.Tint = new Color(0.9f, 0.75f - 0.1f * (_dealt % 4), 0.3f + 0.15f * (_dealt % 4));
            sprite.Order = 1;
            Game.Spawn(card, Entity);
        }

        if (Input.WasPressed(Key.Backspace) && Entity!.Children.Count > 0)
            Game.Destroy(Entity.Children[0]);
    }
}
