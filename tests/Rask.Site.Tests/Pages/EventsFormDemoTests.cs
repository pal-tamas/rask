using System.Reflection;
using Rask.Core.Live;
using Rask.Site.Features;

namespace Rask.Site.Tests.Pages;

// EventsFormDemo (embedded in the Composition guide) collects a FormData on submit. The end-to-end
// fill → submit → echo path is exercised by the Composition guide walk; these unit tests pin the
// OnSubmit → FormData mapping (named value vs blank) directly.
public sealed class EventsFormDemoTests
{
    [Fact]
    public void Submitting_a_named_field_sets_submitted_to_its_value()
    {
        var demo = new EventsFormDemo();

        InvokeOnSubmit(demo, new FormData(new Dictionary<string, string> { ["name"] = "Ada" }));

        Assert.Equal("Ada", Submitted(demo));
    }

    [Fact]
    public void Submitting_a_blank_field_sets_submitted_to_the_blank_sentinel()
    {
        var demo = new EventsFormDemo();

        InvokeOnSubmit(demo, new FormData(new Dictionary<string, string> { ["name"] = "   " }));

        Assert.Equal("(blank)", Submitted(demo));
    }

    private static void InvokeOnSubmit(EventsFormDemo demo, FormData fd)
    {
        var mi = typeof(EventsFormDemo).GetMethod("OnSubmit", BindingFlags.Instance | BindingFlags.NonPublic)!;
        mi.Invoke(demo, [fd]);
    }

    private static string Submitted(EventsFormDemo demo)
    {
        var f = typeof(EventsFormDemo).GetField("_submitted", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (string)f.GetValue(demo)!;
    }
}
