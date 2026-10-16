using System.Collections.Generic;
using System.Numerics;

/// <summary>A shop, its stock, prices and sign, opened with a key.</summary>
public class Shop : Behavior
{
    /// <summary>What it sells.</summary>
    public IReadOnlyList<string> Stock { get; set; } = ["Sword", "Shield"];

    /// <summary>What each thing costs, in gold.</summary>
    public IReadOnlyList<string> Prices { get; set; } = ["Sword: 30", "Shield: 20"];

    /// <summary>The colors its sign cycles through.</summary>
    public IReadOnlyList<string> Colors { get; set; } = ["#F2607E", "#F2BC4B"];

    /// <summary>The key that opens it.</summary>
    public Key OpenKey { get; set; } = Key.E;

    /// <summary>Where leaving the shop goes.</summary>
    public string? NextScene { get; set; }

    /// <summary>Which way the shopkeeper faces.</summary>
    public Vector2 FacingDirection { get; set; } = Vector2.UnitX;

    /// <summary>What the shopkeeper says when you come in.</summary>
    [PlainText("the shopkeeper's own words")]
    public string? Greeting { get; set; } = "Welcome!";
}
