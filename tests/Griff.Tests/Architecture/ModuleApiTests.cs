
namespace Griff.Tests.Architecture;

/// <summary>
/// Guards what a module may use: only what a script's system could too. A module is code like any other that adds a
/// system to a game, so everything it asks of the game (<c>Game</c>, the components, the services around them) is public
/// and documented, or a system it asks for by type. Each module is an assembly of its own that Griff opens no internals to
/// (ModuleTests), so the compiler keeps that; these keep how its steps are placed.
/// </summary>
public sealed class ModuleApiTests
{
    // Griff's steps are named by ids: a module can't name one that isn't there, and no two share an id.
    [Fact]
    public void Steps_AreDistinctMembersOfTheirPhase()
    {
        var steps = typeof(Griff.Runtime.Steps).GetFields().Select(f => (Griff.Runtime.StepId)f.GetValue(null)!).ToList();

        Assert.Equal(steps.Count, steps.Distinct().Count());
        Assert.Equal(steps.Count, steps.Select(s => s.ToString()).Distinct().Count());
    }

    // A step is placed by an id: one that is not a step of the frame (another game's, say) fails here, not when a game starts.
    [Fact]
    public void AStep_NamingAnIdThatIsNotInTheFrame_Fails()
    {
        var setup = new Griff.Runtime.GameSetup(new Griff.Runtime.Game(new Griff.Scenes.Scene(), new Griff.Runtime.InputState()));
        setup.Module = new Probe();
        var system = setup.AddSystem(new ProbeSystem());
        setup.StepAfter(new Griff.Runtime.StepId(Griff.Runtime.FramePhase.Logic, "Nowhere"), "Late", system, () => { });

        var error = Assert.Throws<InvalidOperationException>(() => setup.Place([]));

        Assert.Contains("isn't a step of this frame", error.Message);
    }

    // A module's system is scheduled by its steps alone, not by Update as well.
    [Fact]
    public void AModuleSystem_OverridingUpdate_IsRefused()
    {
        var setup = new Griff.Runtime.GameSetup(new Griff.Runtime.Game(new Griff.Scenes.Scene(), new Griff.Runtime.InputState()));

        Assert.Throws<InvalidOperationException>(() => setup.AddSystem(new UpdatingSystem()));
    }

    private sealed class Probe : Griff.Runtime.GameModule
    {
        public override string Name => "Probe";

        protected internal override void AddTo(Griff.Runtime.GameSetup game)
        {
        }
    }

    private sealed class ProbeSystem : Griff.Runtime.GameSystem;

    private sealed class UpdatingSystem : Griff.Runtime.GameSystem
    {
        protected override void Update()
        {
        }
    }
}
