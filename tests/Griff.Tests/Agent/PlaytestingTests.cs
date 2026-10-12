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

    [Fact]
    public void Talking_SaysWhoIsSpeaking_WhenItsSomeoneElse()
    {
        var scene = new Scene();
        var box = new Entity("Dialogue Box");
        box.AddComponent<Text>();
        scene.Add(box);
        var hero = new Entity("Player");
        hero.AddComponent<Speaker>().Name = "You";
        scene.Add(hero);
        var guide = new Entity("Guide");
        guide.AddComponent<BoxCollider>().IsTrigger = true;
        guide.AddComponent<Speaker>();
        var dialogue = guide.AddComponent<Dialogue>();
        dialogue.Lines = ["Hello.", "You: Hi!"];
        scene.Add(guide);
        var game = new Game(scene, new InputState());
        game.Tick(1 / 60f);

        game.StartDialogue(dialogue);
        Assert.Equal("Talking: Guide says \"Hello.\".", Playtesting.Talking(game));
        game.AdvanceDialogue(dialogue);
        Assert.Equal("Talking: Guide (You) says \"Hi!\".", Playtesting.Talking(game));
    }

    [Fact]
    public void Talking_SaysWhenTheLineIsStillTyping()
    {
        var scene = new Scene();
        var box = new Entity("Dialogue Box");
        box.AddComponent<Text>();
        box.AddComponent<Typewriter>().Speed = 5;
        scene.Add(box);
        var sign = new Entity("Sign");
        sign.AddComponent<BoxCollider>().IsTrigger = true;
        var dialogue = sign.AddComponent<Dialogue>();
        dialogue.Lines = ["A long line, typed slowly."];
        scene.Add(sign);
        var game = new Game(scene, new InputState());
        game.Tick(1 / 60f);

        game.StartDialogue(dialogue);
        game.Tick(1 / 60f);
        Assert.Contains("still typing", Playtesting.Talking(game));
        game.AdvanceDialogue(dialogue);
        Assert.DoesNotContain("still typing", Playtesting.Talking(game));
    }

    [Fact]
    public void Flags_ListsThoseSetAndCleared()
    {
        Assert.Null(Playtesting.Flags(new Dictionary<string, bool>()));
        Assert.Equal("Flags: set MetGuide, Asked; cleared HasKey.",
            Playtesting.Flags(new Dictionary<string, bool> { ["MetGuide"] = true, ["HasKey"] = false, ["Asked"] = true }));
    }

    [Fact]
    public void FlagsSet_ListsWhatsSavedAsTrue()
    {
        var game = new Game(new Scene(), new InputState());
        Assert.Null(Playtesting.FlagsSet(game));

        game.SetFlag("MetGuide");
        game.SaveData.Set("difficulty", "Hard");
        game.SaveData.Set("Muted", false);
        Assert.Equal("Flags set: MetGuide.", Playtesting.FlagsSet(game));
    }
}
