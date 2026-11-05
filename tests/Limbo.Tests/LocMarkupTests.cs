using System.Reflection;
using Limbo.Console.Interactive;
using Spectre.Console;

namespace Limbo.Tests;

public class LocMarkupTests
{
    // Every interactive string is rendered as Spectre markup; one stray '[' throws at
    // runtime and takes down whichever screen shows it. Help texts are long and
    // hand-written, so check them all, in both languages, once here.
    [Fact]
    public void EveryTranslation_IsValidMarkup()
    {
        var map = (IDictionary<string, (string En, string Tr)>)typeof(Loc)
            .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        var failures = new List<string>();
        foreach (var (key, (en, tr)) in map)
        {
            foreach (var text in new[] { en, tr })
            {
                try
                {
                    _ = new Markup(text);
                }
                catch (Exception ex)
                {
                    failures.Add($"{key}: {ex.Message}");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void EveryTranslation_HasBothLanguages()
    {
        var map = (IDictionary<string, (string En, string Tr)>)typeof(Loc)
            .GetField("Map", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

        Assert.DoesNotContain(map, kv => string.IsNullOrWhiteSpace(kv.Value.En) || string.IsNullOrWhiteSpace(kv.Value.Tr));
    }
}
