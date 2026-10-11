using Griff.Agent;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Agent;

public class PlaytestingTests
{
    [Theory]
    [InlineData("Space", Key.Space)]
    [InlineData("leftshift", Key.LeftShift)]
    [InlineData("3", Key.D3)]
    [InlineData("D3", Key.D3)]
    [InlineData("0", Key.D0)]
    public void KeyNamed_TakesNamesInAnyCase_AndDigitsAsTheirNumberKeys(string name, Key key) =>
        Assert.Equal(key, Playtesting.KeyNamed(name));

    [Theory]
    [InlineData("12")]
    [InlineData("Jump")]
    [InlineData("")]
    public void KeyNamed_IsNull_ForAnythingElse(string name) => Assert.Null(Playtesting.KeyNamed(name));

    [Fact]
    public void Talking_SaysWhoIsTalking_TheLine_AndTheChoicesWithThePickedOne()
    {
        var scene = new Scene();
        var box = new Entity("Dialogue Box");
        box.AddComponent<Text>();
        scene.Add(box);
        var shopkeeper = new Entity("Shopkeeper");
        shopkeeper.AddComponent<BoxCollider>().IsTrigger = true;
        var dialogue = shopkeeper.AddComponent<Dialogue>();
        dialogue.Lines = ["Want a sword?", "> Yes", "> No", "Off you go."];
        scene.Add(shopkeeper);
        var game = new Game(scene, new InputState());
        game.Tick(1 / 60f);
        Assert.Null(Playtesting.Talking(game));

        game.StartDialogue(dialogue);
        Assert.Equal(
            "Talking: Shopkeeper says \"Want a sword?\". Choices: 1 \"Yes\" (picked), 2 \"No\"; tap a number, or Up or Down and Interact, to pick.",
            Playtesting.Talking(game));

        game.ChooseDialogue(dialogue, 1);
        Assert.Equal("Talking: Shopkeeper says \"Off you go.\".", Playtesting.Talking(game));
    }
}
