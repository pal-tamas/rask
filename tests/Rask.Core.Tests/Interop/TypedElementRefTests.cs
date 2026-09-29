using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core.Components;

namespace Rask.Core.Tests.Interop;

// A ref typed to its element's MDN interface carries that interface's members, generated from MDN, and each reaches the
// element through one generic helper (__raskEl.call/get/set) under the member's MDN name.
public partial class TypedElementRefTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task An_operation_calls_the_element_member_of_its_MDN_name()
    {
        var js = new FakeJsRuntime();
        var dialog = new ElementRef<HTMLDialogElement>();
        _ = Dialog.Ref(dialog);

        using (Services(js))
        {
            await dialog.ShowModal();
        }

        Assert.Equal(["__raskEl.call"], js.Calls.Select(c => c.Identifier));
        Assert.Equal([dialog, "showModal"], js.ArgsFor("__raskEl.call")!);
    }

    [Fact]
    public async Task An_attribute_is_an_awaitable_read_of_the_live_element()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskEl.get", true);
        var dialog = new ElementRef<HTMLDialogElement>();
        _ = Dialog.Ref(dialog);

        bool open;
        using (Services(js))
        {
            open = await dialog.Open;
        }

        Assert.True(open);
        Assert.Equal([dialog, "open"], js.ArgsFor("__raskEl.get")!);
    }

    [Fact]
    public async Task A_state_the_render_does_not_own_is_written_through_a_setter()
    {
        var js = new FakeJsRuntime();
        var video = new ElementRef<HTMLVideoElement>();
        _ = Video.Ref(video);

        using (Services(js))
        {
            await video.SetCurrentTime(12);
        }

        Assert.Equal([video, "currentTime", 12.0], js.ArgsFor("__raskEl.set")!);
    }

    [Fact]
    public async Task A_typed_ref_carries_the_members_of_its_interfaces_bases()
    {
        var js = new FakeJsRuntime();
        var dialog = new ElementRef<HTMLDialogElement>();
        _ = Dialog.Ref(dialog);

        using (Services(js))
        {
            await dialog.Focus();
            await dialog.ScrollIntoView(new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth, Block = ScrollLogicalPosition.Nearest });
        }

        Assert.Equal(["focus", "scrollIntoView"], js.Calls.Select(c => (string)c.Args![1]!));
    }

    [Fact]
    public void A_typed_ref_crosses_to_the_browser_as_the_same_marker_an_untyped_one_does()
    {
        var dialog = new ElementRef<HTMLDialogElement>();

        // As the runtime sends an argument: by its runtime type, through an object[].
        var json = JsonSerializer.Serialize<object[]>([dialog]);

        Assert.Equal($$"""[{"__raskRef__":"{{dialog.Id}}"}]""", json);
    }

    [Fact]
    public void Options_cross_to_the_browser_in_MDNs_own_shape()
    {
        var options = new ScrollIntoViewOptions { Behavior = ScrollBehavior.Smooth, Block = ScrollLogicalPosition.Nearest };

        var json = JsonSerializer.Serialize<object>(options, RaskDomJsonContext.Default.Options);

        Assert.Equal("""{"block":"nearest","behavior":"smooth"}""", json);
    }

    [Fact]
    public void A_rectangle_comes_back_from_the_browser_as_a_DOMRect()
    {
        const string fromBrowser = """{"x":1,"y":2,"width":30,"height":40,"top":2,"right":31,"bottom":42,"left":1}""";

        var rect = JsonSerializer.Deserialize(fromBrowser, RaskDomJsonContext.Default.DOMRect)!;

        Assert.Equal((30.0, 42.0), (rect.Width!.Value, rect.Bottom!.Value));
    }

    [Fact]
    public void An_attribute_the_render_owns_has_no_setter()
    {
        var members = typeof(ElementRefMembers).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();

        var setters = (members.Contains("SetOpen"), members.Contains("SetId"), members.Contains("SetClassName"), members.Contains("SetCurrentTime"));

        Assert.Equal((false, false, false, true), setters);
    }

    [Fact]
    public void No_member_rewrites_what_the_render_owns()
    {
        var members = typeof(ElementRefMembers).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => m.Name).ToHashSet();

        string[] mutators = ["SetInnerHTML", "SetInnerText", "SetOuterHTML", "SetTextContent", "SetAttribute", "RemoveAttribute", "Append", "Remove", "ReplaceChildren", "InsertAdjacentHTML", "SetHTMLUnsafe"];

        var present = mutators.Where(members.Contains).ToList();

        Assert.Equal([], present);
    }

    [Fact]
    public void A_ref_typed_to_another_element_throws_where_it_is_put()
    {
        var video = new ElementRef<HTMLVideoElement>();

        var error = Assert.Throws<InvalidOperationException>(() => Div.Ref(video));

        Assert.Contains("ElementRef<HTMLVideoElement> is on a <div>", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ref_typed_to_a_base_interface_goes_on_any_element_of_it()
    {
        var anyHtml = new ElementRef<HTMLElement>();

        var html = Section.Ref(anyHtml)["x"].ToHtml();

        Assert.Contains($"data-rask-ref=\"{anyHtml.Id}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ref_used_before_its_element_is_on_a_page_says_so()
    {
        var dialog = new ElementRef<HTMLDialogElement>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await dialog.ShowModal());

        Assert.Contains("before the element was on a page", error.Message, StringComparison.Ordinal);
    }

    private static IDisposable Services(IJSRuntime js) =>
        Rask.Core.Forms.DispatchServicesScope.Push(new ServiceCollection().AddSingleton(js).BuildServiceProvider());
}
