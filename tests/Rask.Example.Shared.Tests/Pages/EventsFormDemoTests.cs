using System.Reflection;
using Rask.Core.Live;
using Rask.Example.Shared.Features;

namespace Rask.Example.Shared.Tests.Pages;

// EventsFormDemo (embedded in the Composition guide) collects a FormData on submit. The end-to-end
// fill → submit → echo path is exercised by the Composition guide walk; these unit tests pin the
// OnSubmit → FormData mapping (named value vs blank) directly.
public sealed class EventsFormDemoTests
{
    [Fact]
    public void OnSubmit_WithNamedField_SetsSubmittedToTheValue()
    {
        var demo = new EventsFormDemo();
        InvokeOnSubmit(demo, new FormData(new Dictionary<string, string> { ["name"] = "Ada" }));
        Assert.Equal("Ada", Submitted(demo));
    }

    [Fact]
    public void OnSubmit_WithBlankField_SetsSubmittedToBlankSentinel()
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
