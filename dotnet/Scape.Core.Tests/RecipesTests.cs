using static Scape.Core.Tests.ItemSupport;

namespace Scape.Core.Tests;

public class RecipesTests
{
    private const int Fire = 1;
    private const int Anvil = 2;

    private static readonly ObjectSet Objects = new([new ObjectDef(Fire, "Fire"), new ObjectDef(Anvil, "Anvil", solid: true)]);

    // F2: each test changes one field of the row and names it.
    private static Recipe Make(
        string skill = "fighting",
        int level = 5,
        RecipeStart? start = null,
        IReadOnlyList<ItemStack>? inputs = null,
        Product? product = null,
        int? tool = null,
        int ticks = 3,
        Failure? failure = null) =>
        new(
            "cook the logs",
            skill,
            level,
            10,
            start ?? new OnObject(Logs, Fire),
            inputs ?? [new ItemStack(Logs, 1)],
            product ?? new Gives(new ItemStack(Helmet, 1)),
            tool,
            ticks,
            failure);

    private static RecipeSet SetOf(params Recipe[] recipes) => new(recipes, Items(), Objects, Skills);

    // Recipe.

    [Fact]
    public void AFailureChanceFallsInALineToZeroAtTheSafeLevel()
    {
        var def = Make(failure: new Failure(0.5, 15));

        Assert.Equal(0.5, def.FailChanceAt(1));
        Assert.Equal(0.5, def.FailChanceAt(5));
        Assert.Equal(0.25, def.FailChanceAt(10), 1e-9);
        Assert.Equal(0, def.FailChanceAt(15));
        Assert.Equal(0, def.FailChanceAt(50));
    }

    [Fact]
    public void ARecipeWithNoFailureNeverFails()
    {
        Assert.Equal(0, Make().FailChanceAt(1));
    }

    [Fact]
    public void RejectsALevelTicksOrInputsOutOfRange()
    {
        Assert.ThrowsAny<ArgumentException>(() => Make(level: 0));
        Assert.ThrowsAny<ArgumentException>(() => Make(level: SkillDef.MaxLevel + 1));
        Assert.ThrowsAny<ArgumentException>(() => Make(ticks: 0));
        Assert.ThrowsAny<ArgumentException>(() => Make(ticks: Recipe.MaxTicks + 1));
        Assert.ThrowsAny<ArgumentException>(() => Make(inputs: []));
    }

    [Fact]
    public void RejectsExperienceBelowOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Recipe(
            "cook the logs",
            "fighting",
            5,
            0,
            new OnObject(Logs, Fire),
            [new ItemStack(Logs, 1)],
            new Gives(new ItemStack(Helmet, 1))));
    }

    [Fact]
    public void RejectsAFailureThatIsSafeAtOrBelowTheLevel()
    {
        Assert.ThrowsAny<ArgumentException>(() => Make(failure: new Failure(0.5, 5)));
    }

    [Fact]
    public void RejectsAFailureChanceOutside0To1()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Failure(0, 9));
        Assert.ThrowsAny<ArgumentException>(() => new Failure(1.5, 9));
        Assert.ThrowsAny<ArgumentException>(() => new Failure(0.5, 1));
    }

    [Fact]
    public void AFireNeedsAnObjectAndALifetime()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Lights(0, 5));
        Assert.ThrowsAny<ArgumentException>(() => new Lights(Fire, 0));
    }

    // OnItem.

    [Fact]
    public void HasTheSameKeyInEitherOrder()
    {
        Assert.Equal(new OnItem(3, 7).Key, new OnItem(7, 3).Key);
    }

    // RecipeSet.

    [Fact]
    public void AcceptsRecipesWhoseNamesAllExist()
    {
        var set = SetOf(
            Make(failure: new Failure(0.5, 9, Arrows)),
            Make(start: new OnItem(Sword, Logs), tool: Sword, product: new Lights(Fire, 50)));

        Assert.Equal(2, set.Recipes.Count);
    }

    [Fact]
    public void RejectsAnItemObjectOrSkillThatDoesNotExist()
    {
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(inputs: [new ItemStack(99, 1)])));
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(start: new OnObject(Logs, 9))));
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(product: new Lights(9, 5))));
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(skill: "baking")));
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(failure: new Failure(0.5, 9, 99))));
    }

    [Fact]
    public void RejectsAProductThatDoesNotExist()
    {
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(product: new Gives(new ItemStack(99, 1)))));
    }

    [Fact]
    public void RejectsALevelAboveTheSkillCap()
    {
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(level: 60)));
    }

    [Fact]
    public void RejectsAStartThatUsesAnItemTheRecipeDoesNotHold()
    {
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(start: new OnObject(Sword, Fire))));
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(start: new OnItem(Logs, Sword))));
    }

    [Fact]
    public void AToolCountsAsHeldForTheStart()
    {
        Assert.Single(SetOf(Make(start: new OnItem(Logs, Sword), tool: Sword)).Recipes);
    }

    [Fact]
    public void RejectsTwoRecipesThatStartTheSameWay()
    {
        Assert.ThrowsAny<ArgumentException>(() => SetOf(Make(), Make()));
        Assert.ThrowsAny<ArgumentException>(() => SetOf(
            Make(start: new OnItem(Logs, Sword), tool: Sword),
            Make(start: new OnItem(Sword, Logs), tool: Sword)));
    }

    [Fact]
    public void AnItemPairAndAnItemOnAnObjectAreDifferentStarts()
    {
        var set = SetOf(
            Make(start: new OnItem(Coins, Logs), inputs: [new ItemStack(Coins, 1), new ItemStack(Logs, 1)]),
            Make(start: new OnObject(Coins, Anvil), inputs: [new ItemStack(Coins, 1)]));

        Assert.Equal(2, set.Recipes.Count);
    }

    [Fact]
    public void TheSameItemOnDifferentStationsIsTwoRecipes()
    {
        Assert.Equal(2, SetOf(Make(), Make(start: new OnObject(Logs, Anvil))).Recipes.Count);
    }
}
