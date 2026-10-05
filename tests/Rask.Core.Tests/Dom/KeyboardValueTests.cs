using System.Reflection;
using System.Text.Json;

namespace Rask.Core.Tests.Dom;

/// <summary>
///     <c>Keys</c> and <c>Codes</c> are generated from the snapshot's <c>keys</c> and <c>codes</c>, which
///     <c>scripts/mdn/refresh.mjs</c> reads from UI Events' own tables.
/// </summary>
public class KeyboardValueTests
{
    private static readonly JsonElement Snapshot = MdnSnapshot.Root;

    [Fact]
    public void Every_key_value_in_the_snapshot_is_a_Keys_constant_spelled_as_the_spec_spells_it()
    {
        var snapshot = Names("keys");

        var constants = Constants(typeof(Keys));

        Assert.Equal(snapshot, constants.Keys.Order(StringComparer.Ordinal));
        Assert.All(constants, c => Assert.Equal(c.Key, c.Value));
    }

    [Fact]
    public void Every_code_value_in_the_snapshot_is_a_Codes_constant_spelled_as_the_spec_spells_it()
    {
        var snapshot = Names("codes");

        var constants = Constants(typeof(Codes));

        Assert.Equal(snapshot, constants.Keys.Order(StringComparer.Ordinal));
        Assert.All(constants, c => Assert.Equal(c.Key, c.Value));
    }

    [Fact]
    public void The_constants_are_the_strings_a_browser_reports()
    {
        var pressed = new[] { "Escape", "ArrowDown", "Enter", "KeyQ", "Space", "NumpadEnter" };

        var named = new[] { Keys.Escape, Keys.ArrowDown, Keys.Enter, Codes.KeyQ, Codes.Space, Codes.NumpadEnter };

        Assert.Equal(pressed, named);
    }

    [Fact]
    public void A_key_constant_matches_a_KeyboardEvent_as_a_pattern()
    {
        var e = new KeyboardEvent { Key = "Escape", Code = "Escape" };

        var dismissed = e.Key is Keys.Escape && e.Code is Codes.Escape;

        Assert.True(dismissed);
    }

    [Fact]
    public void Every_value_links_to_its_own_anchor_in_the_UI_Events_spec()
    {
        var entries = Snapshot.GetProperty("keys").EnumerateArray().Select(e => ("key", e))
            .Concat(Snapshot.GetProperty("codes").EnumerateArray().Select(e => ("code", e)));

        var wrong = entries
            .Where(x => x.e.GetProperty("spec").GetString()
                        != $"https://w3c.github.io/uievents-{x.Item1}/#{x.Item1}-{x.e.GetProperty("name").GetString()}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void The_snapshot_pins_the_UI_Events_commits_it_was_read_at()
    {
        var sources = Snapshot.GetProperty("sources");

        var pins = new[] { "w3c/uievents-key", "w3c/uievents-code" }.Select(p => sources.GetProperty(p).GetString()).ToList();

        Assert.All(pins, sha => Assert.Matches("^[0-9a-f]{40}$", sha!));
    }

    private static List<string> Names(string section) =>
        Snapshot.GetProperty(section).EnumerateArray().Select(e => e.GetProperty("name").GetString()!)
            .Order(StringComparer.Ordinal).ToList();

    private static Dictionary<string, string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);
}
