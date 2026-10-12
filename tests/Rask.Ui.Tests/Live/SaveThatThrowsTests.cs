using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A kit form whose save throws, with <c>Ui.Toast</c> in the layout: the reader is told in a danger toast,
///     in their language, and the change is one the live diff ships.
/// </summary>
public partial class SaveThatThrowsTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("en-US", "The save failed. Please try again.")]
    [InlineData("hu-HU", "A mentés nem sikerült. Kérjük, próbálja újra.")]
    public async Task A_save_that_throws_shows_a_danger_toast_in_the_readers_language(string language, string sentence)
    {
        var page = Draw();

        await In(language, () => page.On("form").Submit());

        var toast = page.Find("[data-ui-toast-dialog]");
        Assert.Contains(sentence, toast.TextContent, StringComparison.Ordinal);
        Assert.Equal("danger", page.Find("[data-variant]").Attribute("data-variant"));
        Assert.DoesNotContain("hunter2", page.Html, StringComparison.Ordinal);
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task What_was_typed_is_still_in_the_field_and_the_button_can_be_pressed_again()
    {
        var page = Draw();
        await page.On("input").Input("Glock 17");

        await page.On("form").Submit();

        Assert.Equal("Glock 17", page.Find("input").Attribute("value"));
        Assert.DoesNotContain("disabled", page.Find("button[type=\"submit\"]").Attributes.Keys);
    }

    private static Page Draw()
    {
        var services = new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider();
        var pistol = new Pistol();

        return Page.Render(
            () => Div[
                Main[
                    Form.Model(pistol).OnSubmit(Save)[
                        Ui.Field[Ui.Label["Name"], Ui.Input.Bind(() => pistol.Name), Ui.Error],
                        Ui.Button.Primary.Submit["Save"]
                    ]
                ],
                Ui.Toast
            ],
            services);
    }

    private static Task Save(Pistol pistol) =>
        Task.FromException(new InvalidOperationException("Login failed for user 'sa' with password 'hunter2'."));

    private static async Task In(string language, Func<Task> act)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            await act();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    private sealed class Pistol
    {
        public string Name { get; set; } = "";
    }
}
