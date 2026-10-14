using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// Every event slot whose frame carries a typed payload — a value, the picked values, the posted form, the chosen
// files — runs a handler written without that argument (`() => …`, `async () => …`), fed the frame a browser sends.
// A parameterless delegate in such a slot used to be refused as a stale id and silently never ran.
public partial class ArgumentlessHandlerSlotTests : global::Rask.Core.RaskMarkup
{
    // Fed through Callback.Invoke by their component rather than registered as a handler, so the argument-less
    // shape runs there: Callback<T>.Invoke has its own arm for it.
    private static readonly string[] InvokedThroughTheCallback =
    [
        "DragDrop.OnDrop",
        "EyeDropperTrigger.OnColor",
        "GestureTrigger.OnResult",
        "InstallTrigger.OnOutcome",
        "MediaCaptureTrigger.OnResult",
        "MediaCaptureTrigger.OnStream",
    ];

    private int _runs;

    public static TheoryData<string, bool> Slots()
    {
        var data = new TheoryData<string, bool>();
        foreach (var slot in DispatchedSlots)
        {
            data.Add(slot, false);
            data.Add(slot, true);
        }

        return data;
    }

    private static readonly string[] DispatchedSlots =
    [
        "HTMLFormElement.OnAnySubmit",
        "HTMLFormElement.OnInvalidSubmit",
        "HTMLFormElement.OnSubmit",
        "HTMLInputElement.AfterBind",
        "HTMLInputElement.OnChange",
        "HTMLInputElement.OnFiles",
        "HTMLInputElement.OnInput",
        "HTMLSelectElement.AfterBind",
        "HTMLSelectElement.OnChange",
        "HTMLSelectElement.OnSelect",
        "HTMLTextAreaElement.AfterBind",
        "HTMLTextAreaElement.OnChange",
        "HTMLTextAreaElement.OnInput",
    ];

    [Fact]
    public void Every_typed_event_slot_is_covered_by_the_argument_less_handler_test()
    {
        var expected = DispatchedSlots.Concat(InvokedThroughTheCallback).Order(StringComparer.Ordinal);

        var found = TypedEventSlots();

        Assert.Equal(expected, found);
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public async Task An_argument_less_handler_runs_in_every_typed_event_slot(string slot, bool asynchronous)
    {
        var (page, selector, domEvent, frame) = Arrange(slot, asynchronous);

        var handled = await page.TryInvoke(page.HandlerIdFor(selector, domEvent), frame);

        Assert.True(handled, $"{slot} refused its own frame");
        Assert.Equal(1, _runs);
    }

    // Every public Callback<T> property of a Rask.Core class whose T is not a DOM event: a DOM event slot is judged
    // by its interface, which a parameterless handler always satisfies. Interfaces only restate their implementers'.
    private static IEnumerable<string> TypedEventSlots() =>
        typeof(Component).Assembly.GetExportedTypes()
            .Where(type => !type.IsInterface)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.PropertyType.IsGenericType
                            && p.PropertyType.GetGenericTypeDefinition() == typeof(Callback<>)
                            && !typeof(Event).IsAssignableFrom(p.PropertyType.GetGenericArguments()[0]))
                .Select(p => $"{TypeName(type)}.{p.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    private static string TypeName(Type type) =>
        type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name;

    private (Page Page, string Selector, string Event, string Frame) Arrange(string slot, bool asynchronous)
    {
        Action sync = () => _runs++;
        Func<Task> async = async () =>
        {
            await Task.Yield();
            _runs++;
        };
        var age = new AgeModel();
        var named = new NameModel();
        var color = new NameModel();
        var required = new RequiredModel();
        var files = new TestFileBackend();
        files.Add("provider.csv", "a;b");
        const string Value = """{ "type": "change", "value": "42" }""";
        const string Submit = """{ "type": "submit", "form": {} }""";

        return slot switch
        {
            "HTMLInputElement.OnInput" => (Page.Render(() => asynchronous
                    ? Input.Value<string>(null).OnInput(async)
                    : Input.Value<string>(null).OnInput(sync)),
                "input", "input", """{ "type": "input", "value": "x" }"""),
            "HTMLInputElement.OnChange" => (Page.Render(() => asynchronous
                    ? Input.Value<string>(null).OnChange(async)
                    : Input.Value<string>(null).OnChange(sync)),
                "input", "change", Value),
            "HTMLInputElement.OnFiles" => (Page.Render(() => asynchronous
                    ? Input.Value<string>(null).OnFiles(async)
                    : Input.Value<string>(null).OnFiles(sync), TestServiceProvider.With<IBrowserFileBackend>(files)),
                "input", "files", "{\"type\":\"files\"," + files.Payload()[1..]),
            "HTMLInputElement.AfterBind" => (Page.Render(() => Form.Model(age)[asynchronous
                    ? Input.Bind(() => age.Age).AfterBind(async)
                    : Input.Bind(() => age.Age).AfterBind(sync)]),
                "input", "change", Value),
            "HTMLTextAreaElement.OnInput" => (Page.Render(() => asynchronous
                    ? Textarea.Value<string>(null).OnInput(async)
                    : Textarea.Value<string>(null).OnInput(sync)),
                "textarea", "input", """{ "type": "input", "value": "x" }"""),
            "HTMLTextAreaElement.OnChange" => (Page.Render(() => asynchronous
                    ? Textarea.Value<string>(null).OnChange(async)
                    : Textarea.Value<string>(null).OnChange(sync)),
                "textarea", "change", Value),
            "HTMLTextAreaElement.AfterBind" => (Page.Render(() => Form.Model(named)[asynchronous
                    ? Textarea.Bind(() => named.Name).AfterBind(async)
                    : Textarea.Bind(() => named.Name).AfterBind(sync)]),
                "textarea", "change", Value),
            "HTMLSelectElement.OnChange" => (Page.Render(() => asynchronous
                    ? Select.Value<string>(null).OnChange(async)[Option.Value("42")]
                    : Select.Value<string>(null).OnChange(sync)[Option.Value("42")]),
                "select", "change", Value),
            "HTMLSelectElement.OnSelect" => (Page.Render(() => asynchronous
                    ? Select.Value<string>(null).OnSelect(async)[Option.Value("42")]
                    : Select.Value<string>(null).OnSelect(sync)[Option.Value("42")]),
                "select", "change", """{ "type": "change", "values": ["42"] }"""),
            "HTMLSelectElement.AfterBind" => (Page.Render(() => Form.Model(color)[asynchronous
                    ? Select.Bind(() => color.Name).AfterBind(async)[Option.Value("42")]
                    : Select.Bind(() => color.Name).AfterBind(sync)[Option.Value("42")]]),
                "select", "change", Value),
            "HTMLFormElement.OnAnySubmit" => (Page.Render(() => asynchronous
                    ? Form.Model(named).OnAnySubmit(async)
                    : Form.Model(named).OnAnySubmit(sync)),
                "form", "submit", Submit),
            "HTMLFormElement.OnSubmit" => (Page.Render(() => asynchronous
                    ? Form.Model(named).OnSubmit(async)
                    : Form.Model(named).OnSubmit(sync)),
                "form", "submit", Submit),
            "HTMLFormElement.OnInvalidSubmit" => (Page.Render(() => asynchronous
                    ? Form.Model(required).OnInvalidSubmit(async)
                    : Form.Model(required).OnInvalidSubmit(sync)),
                "form", "submit", Submit),
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "no arrangement for this slot"),
        };
    }

    private sealed class AgeModel
    {
        public int Age { get; set; }
    }

    private sealed class NameModel
    {
        public string? Name { get; set; }
    }

    private sealed class RequiredModel
    {
        [Required]
        public string? Name { get; set; }
    }
}
