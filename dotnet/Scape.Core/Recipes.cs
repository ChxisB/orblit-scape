namespace Scape.Core;

/// <summary>What a player does to begin a recipe.</summary>
public abstract class RecipeStart
{
    // Closed to this assembly, so the cases below are every start.
    private protected RecipeStart()
    {
    }
}

/// <summary><see cref="Item"/> used on <see cref="Target"/>, in either order. Both are in the inventory.</summary>
public sealed class OnItem(int item, int target) : RecipeStart
{
    public int Item { get; } = item;

    public int Target { get; } = target;

    /// <summary>The same value for either order of the two items.</summary>
    public (int Low, int High) Key => Item < Target ? (Item, Target) : (Target, Item);
}

/// <summary><see cref="Item"/> used on a placed object, the recipe's station.</summary>
public sealed class OnObject(int item, int objectId) : RecipeStart
{
    public int Item { get; } = item;

    /// <summary>The id of the <see cref="ObjectDef"/> the item is used on.</summary>
    public int ObjectId { get; } = objectId;
}

/// <summary>What a make gives.</summary>
public abstract class Product
{
    // Closed to this assembly, so the cases below are every product.
    private protected Product()
    {
    }
}

/// <summary>One stack in the inventory.</summary>
public sealed class Gives(ItemStack stack) : Product
{
    public ItemStack Stack { get; } = stack;
}

/// <summary>
/// A fire that stands where the player does for <see cref="Ticks"/> ticks.
/// <see cref="ObjectId"/> is the id of its <see cref="ObjectDef"/>.
/// </summary>
public sealed class Lights : Product
{
    public Lights(int objectId, int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(objectId, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(objectId, ObjectDef.MaxId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        ObjectId = objectId;
        Ticks = ticks;
    }

    public int ObjectId { get; }

    public int Ticks { get; }
}

/// <summary>
/// What a failed make costs. The inputs are used up and the player gets
/// <see cref="Item"/>, or nothing when it is null. The chance is <see cref="Chance"/> at
/// the recipe's level and falls in a line to zero at <see cref="SafeLevel"/>.
/// </summary>
public sealed class Failure
{
    public Failure(double chance, int safeLevel, int? item = null)
    {
        if (double.IsNaN(chance) || chance <= 0 || chance > 1)
        {
            throw new ArgumentException(FormattableString.Invariant($"{chance} is outside 0 to 1."), nameof(chance));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(safeLevel, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(safeLevel, SkillDef.MaxLevel);
        Chance = chance;
        SafeLevel = safeLevel;
        Item = item;
    }

    public double Chance { get; }

    public int SafeLevel { get; }

    public int? Item { get; }
}

/// <summary>
/// One thing a player can make: the inputs it uses up, the tool it needs in
/// hand, the skill and level it asks for, and what it gives.
/// </summary>
public sealed class Recipe
{
    /// <summary>The most ticks one make may take.</summary>
    public const int MaxTicks = 100;

    // F2: a row of content names its fields, and most take their defaults.
    public Recipe(
        string name,
        string skill,
        int level,
        int xp,
        RecipeStart start,
        IReadOnlyList<ItemStack> inputs,
        Product product,
        int? tool = null,
        int ticks = 3,
        Failure? failure = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, SkillDef.MaxLevel);
        ArgumentOutOfRangeException.ThrowIfLessThan(xp, 1);
        if (inputs.Count == 0)
        {
            throw new ArgumentException("The inputs are empty.", nameof(inputs));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(ticks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticks, MaxTicks);
        if (failure is not null && failure.SafeLevel <= level)
        {
            throw new ArgumentException("The failure is safe below its level.", nameof(failure));
        }

        Name = name;
        Skill = skill;
        Level = level;
        Xp = xp;
        Start = start;
        Inputs = [.. inputs];
        Product = product;
        Tool = tool;
        Ticks = ticks;
        Failure = failure;
    }

    /// <summary>A verb phrase that finishes "You fail to": <c>cook the fish</c>.</summary>
    public string Name { get; }

    public string Skill { get; }

    public int Level { get; }

    /// <summary>Experience for each make that works.</summary>
    public int Xp { get; }

    public RecipeStart Start { get; }

    public IReadOnlyList<ItemStack> Inputs { get; }

    public Product Product { get; }

    /// <summary>An item held or worn and not used up, or null.</summary>
    public int? Tool { get; }

    /// <summary>Ticks for each make.</summary>
    public int Ticks { get; }

    public Failure? Failure { get; }

    /// <summary>The chance that one make fails for a player of <paramref name="skillLevel"/>.</summary>
    public double FailChanceAt(int skillLevel)
    {
        if (Failure is not { } failure || skillLevel >= failure.SafeLevel)
        {
            return 0;
        }

        if (skillLevel <= Level)
        {
            return failure.Chance;
        }

        var along = (double)(skillLevel - Level) / (failure.SafeLevel - Level);
        return failure.Chance * (1 - along);
    }
}

/// <summary>
/// The recipes a game has. Each must name items, an object and a skill that
/// exist and a level the skill reaches, so a typo fails when the game starts
/// and not when a player tries. A start names one recipe, so a second recipe
/// for the same two items would never run.
/// </summary>
public sealed class RecipeSet
{
    public RecipeSet(IReadOnlyList<Recipe> recipes, ItemSet items, ObjectSet objects, SkillSet skills)
    {
        Recipes = [.. recipes];
        var starts = new HashSet<object>();
        foreach (var recipe in Recipes)
        {
            Check(recipe, items, objects, skills);
            if (!starts.Add(StartKey(recipe.Start)))
            {
                throw new ArgumentException($"{recipe.Name} repeats a start.", nameof(recipes));
            }
        }
    }

    public IReadOnlyList<Recipe> Recipes { get; }

    private static object StartKey(RecipeStart start) => start switch
    {
        OnItem onItem => onItem.Key,
        OnObject onObject => ("object", onObject.Item, onObject.ObjectId),
        _ => throw new InvalidOperationException($"{start.GetType().Name} is not a recipe start."),
    };

    private static void Check(Recipe recipe, ItemSet items, ObjectSet objects, SkillSet skills)
    {
        var skill = skills.Find(recipe.Skill)
            ?? throw new ArgumentException($"The skill {recipe.Skill} of {recipe.Name} is unknown.", nameof(skills));
        if (recipe.Level > skill.LevelCap)
        {
            throw new ArgumentException($"{recipe.Name} is above the cap.", nameof(recipe));
        }

        HashSet<int> held = [.. recipe.Inputs.Select(s => s.Item)];
        if (recipe.Tool is { } tool)
        {
            held.Add(tool);
        }

        foreach (var id in ItemsNamedBy(recipe, held))
        {
            items.Require(id);
        }

        CheckStart(recipe, held, objects);
        if (recipe.Product is Lights lights)
        {
            objects.Require(lights.ObjectId);
        }
    }

    private static List<int> ItemsNamedBy(Recipe recipe, HashSet<int> held)
    {
        List<int> named = [.. held];
        if (recipe.Product is Gives gives)
        {
            named.Add(gives.Stack.Item);
        }

        if (recipe.Failure?.Item is { } failed)
        {
            named.Add(failed);
        }

        return named;
    }

    private static void CheckStart(Recipe recipe, HashSet<int> held, ObjectSet objects)
    {
        switch (recipe.Start)
        {
            case OnItem onItem:
                CheckHeld(recipe, [onItem.Item, onItem.Target], held);
                break;
            case OnObject onObject:
                CheckHeld(recipe, [onObject.Item], held);
                objects.Require(onObject.ObjectId);
                break;
        }
    }

    private static void CheckHeld(Recipe recipe, int[] used, HashSet<int> held)
    {
        if (!used.All(held.Contains))
        {
            throw new ArgumentException($"{recipe.Name} starts with an item that is neither an input nor the tool.", nameof(recipe));
        }
    }
}
