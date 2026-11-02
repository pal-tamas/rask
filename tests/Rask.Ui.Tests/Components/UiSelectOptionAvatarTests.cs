using System.Reflection;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>avatar</c> and <c>avatar:*</c> on <c>flux:select.option</c>: an image's address drawn as the
///     extra-small round avatar Flux draws there, or an avatar of the page's own whose props win.
/// </summary>
public partial class UiSelectOptionAvatarTests : global::Rask.Core.RaskMarkup
{
    private const string Url = "https://example.com/ana.png";

    [Fact]
    public void An_address_is_drawn_as_the_extra_small_round_avatar_named_by_the_option()
    {
        var select = Ui.Select.Of<string>().Listbox[Ui.SelectOption.Value("ana").Label("Ana Lima").Avatar(Url)];

        var page = Page.Render(select);

        var avatar = page.Find("[data-ui-option] [data-ui-avatar]");
        Assert.Equal("xs", avatar.Attribute("data-size"));
        Assert.Equal("true", avatar.Attribute("data-circle"));
        Assert.True(avatar.HasClass("me-2") && avatar.HasClass("isolate"));
        Assert.Equal("Ana Lima", page.Find("[data-ui-option] [data-ui-avatar] img").Attribute("alt"));
    }

    [Fact]
    public void An_avatar_of_the_pages_own_keeps_its_props_and_takes_the_options_size_shape_and_name()
    {
        var select = Ui.Select.Of<string>().Listbox[Ui.SelectOption.Value("ana").Label("Ana Lima").Avatar(Ui.Avatar.ColorAuto())];
        var expected = Page.Render(Ui.Avatar.Xs.Circle().Name("Ana Lima").ColorAuto().Class("me-2 isolate")).Find("[data-ui-avatar]");

        var avatar = Page.Render(select).Find("[data-ui-option] [data-ui-avatar]");

        Assert.Equal(expected.Attributes, avatar.Attributes);
        Assert.Equal("AL", avatar.TextContent.Trim());
    }

    [Fact]
    public void What_the_pages_avatar_states_wins_over_the_options_own()
    {
        var select = Ui.Select.Of<string>().Listbox[
            Ui.SelectOption.Value("ana").Label("Ana Lima").Avatar(Ui.Avatar.Sm.Circle(false).Initials("A").Color(Ui.Color.Teal).Class("ring-1"))
        ];

        var avatar = Page.Render(select).Find("[data-ui-option] [data-ui-avatar]");

        Assert.Equal("sm", avatar.Attribute("data-size"));
        Assert.Null(avatar.Attribute("data-circle"));
        Assert.Equal("A", avatar.TextContent.Trim());
        Assert.True(avatar.HasClass("me-2") && avatar.HasClass("ring-1"));
    }

    [Fact]
    public void The_picked_options_avatar_is_drawn_again_in_the_button()
    {
        var select = Ui.Select.Value("ana").Listbox[Ui.SelectOption.Value("ana").Label("Ana Lima").Avatar(Ui.Avatar.ColorAuto())];

        var page = Page.Render(select);

        Assert.Equal(2, page.FindAll("[data-ui-avatar]").Count);
        Assert.Equal("xs", page.Find("[data-ui-select-button] [data-ui-avatar]").Attribute("data-size"));
    }

    [Fact]
    public void Every_prop_of_the_pages_avatar_reaches_the_one_the_option_draws()
    {
        var own = Ui.Avatar;
        var props = typeof(UiAvatar).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(prop => prop is { CanWrite: true, SetMethod.IsPublic: true } && prop.Name != nameof(UiAvatar.Class))
            .ToList();
        props.ForEach(prop => prop.SetValue(own, Sample(prop.PropertyType)));

        var drawn = Assert.IsType<UiAvatar>(UiOptionAvatar.Draw(own, "Ana Lima"));

        Assert.All(props, prop => Assert.Equal(prop.GetValue(own), prop.GetValue(drawn)));
        Assert.Equal("me-2 isolate", drawn.Class);
        Assert.NotSame(own, drawn);
    }

    // A value no prop of the avatar holds unless it is set: the last member of an enum, true, words.
    private static object Sample(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        if (plain.IsEnum)
        {
            return Enum.GetValues(plain).GetValue(Enum.GetValues(plain).Length - 1)!;
        }

        return plain switch
        {
            _ when plain == typeof(bool) => true,
            _ when plain == typeof(string) => "sample",
            _ when plain == typeof(Rask.Core.Routing.RouteUrl) => (Rask.Core.Routing.RouteUrl)"/people/ana",
            _ when plain == typeof(Component) => (Component)"3",
            _ => throw new InvalidOperationException($"No sample for {type.Name}: add one, so the new prop is held to reach the copy."),
        };
    }
}
