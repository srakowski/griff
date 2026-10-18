/// <summary>
/// A little workshop: Space takes an order, kept as a plain object in a private list, and the worker makes them one at a
/// time, the one under way kept in another field (the same object). S saves the game, X ships everything, L loads.
/// </summary>
public class Workshop : Behavior
{
    private class Order
    {
        public int Number;
        public int Done;
    }

    private readonly List<Order> _orders = new();
    private Order? _current;
    private int _next = 1;

    /// <summary>How many orders the workshop holds.</summary>
    public int Orders => _orders.Count;

    /// <summary>The number of the order being made, or 0.</summary>
    public int Making => _current?.Number ?? 0;

    protected override void Update()
    {
        if (Input.WasPressed(Key.Space))
            _orders.Add(new Order { Number = _next++ });
        if (Input.WasPressed(Key.S))
            Game.SaveGame("1");
        if (Input.WasPressed(Key.L))
            Game.LoadGame("1");
        if (Input.WasPressed(Key.X))
        {
            StopCoroutines();
            _orders.Clear();
            _current = null;
        }

        if (_current is null && _orders.Count > 0)
        {
            _current = _orders[0];
            StartCoroutine(Make(_current));
        }

        GetComponent<Text>()!.Value = $"{_orders.Count} orders" + (_current is null ? "" : $", making #{_current.Number}: {_current.Done}%");
    }

    // A coroutine isn't saved, so the work under way starts again from what the fields say.
    protected override void OnLoaded()
    {
        if (_current is not null)
            StartCoroutine(Make(_current));
    }

    private IEnumerator Make(Order order)
    {
        while (order.Done < 100)
        {
            yield return Wait(0.5f);
            order.Done += 10;
        }

        _orders.Remove(order);
        _current = null;
    }
}
