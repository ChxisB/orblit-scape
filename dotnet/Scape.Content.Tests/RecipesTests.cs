using Scape.Core;

namespace Scape.Content.Tests;

public class RecipesTests
{
    private static Recipe Named(string name) => DefaultRecipes.All.Recipes.First(recipe => recipe.Name == name);

    [Fact]
    public void TheFirstGameHasACookingFiremakingFletchingAndSmithing()
    {
        Assert.Equal(
            new HashSet<string> { "cooking", "firemaking", "fletching", "smithing" },
            DefaultRecipes.All.Recipes.Select(r => r.Skill).ToHashSet());
    }

    [Fact]
    public void CookingCanFailAtLevel1AndNeverFromLevel30()
    {
        var cook = Named("cook the fish");

        Assert.True(cook.FailChanceAt(1) > 0);
        Assert.Equal(0, cook.FailChanceAt(30));
    }

    [Fact]
    public void AToolIsNeverAnInput()
    {
        foreach (var recipe in DefaultRecipes.All.Recipes)
        {
            if (recipe.Tool is { } tool)
            {
                Assert.DoesNotContain(tool, recipe.Inputs.Select(stack => stack.Item));
            }
        }
    }

    [Fact]
    public void LightingLogsPlacesTheFireObject()
    {
        var light = Named("light the logs");

        Assert.Equal(ObjectId.Fire, ((Lights)light.Product).ObjectId);
    }
}
