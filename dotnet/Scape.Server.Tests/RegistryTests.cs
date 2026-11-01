namespace Scape.Server.Tests;

public class RegistryTests
{
    [Fact]
    public void FindsAKindByTheNameItWasRegisteredUnder()
    {
        var walk = new ActionKind("walk");
        var registry = new Registry();
        registry.AddKind(walk);
        Assert.Same(walk, registry.KindNamed("walk"));
        Assert.Null(registry.KindNamed("run"));
    }

    [Fact]
    public void RefusesASecondKindWithTheSameName()
    {
        var registry = new Registry();
        registry.AddKind(new ActionKind("walk"));
        Assert.Throws<ArgumentException>(() => registry.AddKind(new ActionKind("walk")));
    }

    [Fact]
    public void KeepsTheSystemsOfAPhaseInTheOrderTheyWereAdded()
    {
        var order = new List<int>();
        var registry = new Registry();
        registry.AddSystem(Phase.Movement, () => order.Add(1));
        registry.AddSystem(Phase.Combat, () => order.Add(9));
        registry.AddSystem(Phase.Movement, () => order.Add(2));
        foreach (var system in registry.SystemsFor(Phase.Movement))
        {
            system();
        }

        Assert.Equal([1, 2], order);
        Assert.Empty(registry.SystemsFor(Phase.Npcs));
    }
}
