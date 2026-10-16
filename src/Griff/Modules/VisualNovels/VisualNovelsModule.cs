using Griff.Runtime;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// Visual novels: a <see cref="Stage"/> with backgrounds and the characters on it (<see cref="Character"/>), at named
/// positions and with expressions, directed by the lines of a dialogue ("[show Ava left happy]", "[background Park
/// fade]"); the Visual Novel template, a short story with three endings; and Create Stage, Create Character and Create
/// Reading Menus in the Entity menu.
/// </summary>
public sealed class VisualNovelsModule : GameModule
{
    public override string Name => "Visual Novels";

    protected internal override void AddTo(GameSetup game)
    {
        var stages = game.AddSystem(new VisualNovelsSystem());
        // Around the dialogues' step: what a line says is done as it's said, and the advance action moves the talk on only
        // if Interact, a click or a choice didn't.
        game.StepBefore(Steps.Dialogues, "Stage Input", stages, stages.Note);
        game.StepAfter(Steps.Dialogues, "Stages", stages, stages.Run);
        foreach (var word in StageLines.TagWords)
            game.OnDialogueTag(word, (dialogue, args) => stages.Direct(dialogue, word, args));
    }
}
