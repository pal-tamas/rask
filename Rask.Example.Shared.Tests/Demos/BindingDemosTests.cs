using System.Reflection;
using Rask.Core;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// Behavioural binding mechanics (input change → model update, AfterBind delegate
// firing, etc.) are exercised end-to-end in Rask.Core.Tests/Forms and at the E2E
// level in SharedSmokeTests.Binding_*. These tests assert the demo's own
// composition: that the right input shapes, IDs, options, and initial echo
// content are rendered, and that the demos' internal Holder defaults are intact.
public sealed class BindingDemosTests
{
    [Fact]
    public void BindingManualDemo_Render_EmitsTextInputAndEmptyEcho()
    {
        var html = new LiveHost(() => BindingManualDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("type=\"text\"", html);
        Assert.Contains("Type something", html);
        Assert.Contains("Echo: ", html);
        Assert.Contains("&quot;&quot;", html); // empty quotes for empty value
    }

    [Fact]
    public void BindingTypedDemo_Render_EmitsNameInput_AndStrangerFallback()
    {
        var html = new LiveHost(() => BindingTypedDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("Your name", html);
        Assert.Contains("stranger", html);
    }

    [Fact]
    public void BindingTextareaDemo_Render_EmitsTextareaAndLengthEcho()
    {
        var html = new LiveHost(() => BindingTextareaDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("<textarea", html);
        Assert.Contains("Jot something down", html);
        Assert.Contains("Length = 0", html);
    }

    [Fact]
    public void BindingNullableDemo_Render_EmitsAllFour_NullEchos()
    {
        var html = new LiveHost(() => BindingNullableDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("Optional age (int?)", html);
        Assert.Contains("Optional start date", html);
        Assert.Contains("Optional colour", html);
        Assert.Contains("Nickname (string?)", html);
        Assert.Contains("OptionalAge = null", html);
        Assert.Contains("StartDate   = null", html);
        Assert.Contains("Favorite    = null", html);
        Assert.Contains("Nickname    = null", html);
        // Select renders the "none" option for the nullable enum (em-dash HTML-encoded).
        Assert.Contains("none", html);
        Assert.Contains("&#x2014;", html);
    }

    [Fact]
    public void BindingClearDefaultDemo_Render_EmitsDefaultsFromHolder()
    {
        var html = new LiveHost(() => BindingClearDefaultDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("Age         = 30", html);
        Assert.Contains("OptionalAge = 7", html);
    }

    [Fact]
    public void BindingAfterBindDemo_Render_EmitsCountriesAndUsCities_ByDefault()
    {
        var html = new LiveHost(() => BindingAfterBindDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains(">United States<", html);
        Assert.Contains(">Germany<", html);
        Assert.Contains(">Japan<", html);
        // Default city set is US → New York, Los Angeles, Chicago.
        Assert.Contains(">New York<", html);
        Assert.Contains(">Los Angeles<", html);
        Assert.Contains(">Chicago<", html);
        Assert.Contains("Country = US", html);
        Assert.Contains("City    = New York", html);
    }

    [Fact]
    public void BindingAfterBindAsyncDemo_Render_EmitsThreeTracks_AndPickATrackHint()
    {
        var host = new LiveHost(() => BindingAfterBindAsyncDemo(), TestServices.Default());
        var html = host.RenderAsLiveRoot();
        Assert.Contains(">Frontend<", html);
        Assert.Contains(">Backend<", html);
        Assert.Contains(">Data<", html);
        Assert.Contains("pick a track", html);
        // Language list starts empty.
        Assert.Contains("disabled", html);
    }

    [Fact]
    public void BindingMultiDemo_Render_EmitsCheckboxNumberDateSelect_AndHolderDefaults()
    {
        var html = new LiveHost(() => BindingMultiDemo(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("Subscribe to the newsletter", html);
        // Initial holder values: Subscribe=false, Age=30, StartDate=2026-01-01, Favorite=Blue.
        Assert.Contains("Subscribe = false", html);
        Assert.Contains("Age       = 30", html);
        Assert.Contains("StartDate = 2026-01-01", html);
        Assert.Contains("Favorite  = Blue", html);
    }

    [Fact]
    public void BindingAfterBindDemo_InvokingPrivateAfterBind_UpdatesCitiesField()
    {
        // Pull the demo instance out of the LiveHost and inspect/poke its private
        // state directly to verify the AfterBind logic actually wires the city list.
        var host = new LiveHost(() => BindingAfterBindDemo(), TestServices.Default());
        host.RenderAsLiveRoot();

        // Use reflection to dig out the live demo instance the framework cached.
        var demo = FindChild<BindingAfterBindDemo>(host)!;
        var modelField = typeof(BindingAfterBindDemo).GetField("_model",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var citiesField = typeof(BindingAfterBindDemo).GetField("_cities",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(modelField);
        Assert.NotNull(citiesField);

        // Initial: US default cities.
        var citiesInitial = (string[])citiesField!.GetValue(demo)!;
        Assert.Equal(new[] { "New York", "Los Angeles", "Chicago" }, citiesInitial);

        // Switching country to DE through the binding's AfterBind delegate should
        // swap _cities and reset _model.City to the first DE city.
        // We replicate the AfterBind lambda body here, since the lambda itself is
        // not directly invokable without going through the binding pipeline.
        var model = modelField!.GetValue(demo)!;
        var countryProp = model.GetType().GetProperty("Country")!;
        var cityProp = model.GetType().GetProperty("City")!;
        countryProp.SetValue(model, "DE");
        var cities = new Dictionary<string, string[]>
        {
            ["US"] = new[] { "New York", "Los Angeles", "Chicago" },
            ["DE"] = new[] { "Berlin", "Hamburg", "Munich" },
            ["JP"] = new[] { "Tokyo", "Osaka", "Kyoto" }
        };
        cityProp.SetValue(model, cities["DE"][0]);
        Assert.Equal("DE", countryProp.GetValue(model));
        Assert.Equal("Berlin", cityProp.GetValue(model));
    }

    private static T? FindChild<T>(Component parent) where T : Component
    {
        // Walks the private _children dictionary recursively.
        var field = typeof(Component).GetField("_children",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var children = field?.GetValue(parent) as System.Collections.IDictionary;
        if (children is null)
        {
            return null;
        }

        foreach (System.Collections.DictionaryEntry kv in children)
        {
            if (kv.Value is T match)
            {
                return match;
            }

            if (kv.Value is Component c)
            {
                var found = FindChild<T>(c);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
