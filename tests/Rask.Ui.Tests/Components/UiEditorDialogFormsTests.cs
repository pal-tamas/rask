using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Wire;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A list whose editor holds a form over an operation and a Delete: the generated steps' shape, by hand.</summary>
[Route("/ui-editor-dialog/notes")]
public sealed partial class EditorDialogNotesPage : Component
{
    /// <summary>What the delete is refused for, as its dry run says it.</summary>
    public static IReadOnlyList<FieldFailure> Refused { get; set; } = [];

    /// <summary>Whether the form finds the note its key names.</summary>
    public static bool Stored { get; set; } = true;

    /// <summary>What the form says once saved; null leaves the dialog its own word.</summary>
    public static string? SavedMessage { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var form = OperationForms.Of<NoteInput>(
            "7",
            static _ => Task.FromResult(Stored ? new NoteInput { Title = "Shopping" } : null),
            static (_, _) => Task.CompletedTask,
            static () => true).Id("note-form");
        var dialog = Ui.EditorDialog.In("edit").Heading("Note")[
                (SavedMessage is { } saved ? form.Saved(saved) : form)[f => [Input.Bind(() => f.Title).Id("title")]]
            ];
        dialog.Deleting(static (_, _) => Task.FromResult(Refused), static (_, _) => Task.CompletedTask, static () => true);
        return Main[dialog];
    }

    /// <summary>The form's one field.</summary>
    public sealed class NoteInput
    {
        /// <summary>The note's title.</summary>
        public string Title { get; set; } = "";
    }
}

/// <summary>
///     <c>Ui.EditorDialog</c> as the host of a form over an operation: the stand-in for a record that is gone, and a
///     Delete that shows what its dry run refuses instead of asking.
/// </summary>
[Collection(EditorDialogNotesCollection.Name)]
public sealed partial class UiEditorDialogFormsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_form_over_a_record_that_is_gone_shows_the_editors_stand_in()
    {
        EditorDialogNotesPage.Stored = false;

        var page = Page.Visit("/ui-editor-dialog/notes?edit=7");

        Assert.Equal("It is not there any more.", page.TextOf("#editor-missing"));
        Assert.False(page.Exists("#title"));
        EditorDialogNotesPage.Stored = true;
    }

    [Fact]
    public void A_stored_record_has_its_form_and_a_Delete_and_a_new_one_no_Delete()
    {
        var stored = Page.Visit("/ui-editor-dialog/notes?edit=7");
        var fresh = Page.Visit("/ui-editor-dialog/notes?edit=new");

        Assert.Equal("Shopping", stored.Find("#title").Attribute("value"));
        Assert.Equal("Delete", stored.TextOf("#editor-delete"));
        Assert.False(fresh.Exists("#editor-delete"));
    }

    [Fact]
    public async Task A_Delete_its_dry_run_refuses_shows_why_instead_of_asking()
    {
        EditorDialogNotesPage.Refused = [new FieldFailure("A pinned note is kept.", [])];
        var page = Page.Visit("/ui-editor-dialog/notes?edit=7");

        await page.On("#editor-delete").Click();

        Assert.Equal("A pinned note is kept.", page.TextOf("#editor-delete-refused"));
        EditorDialogNotesPage.Refused = [];
    }

    [Theory]
    [InlineData(null, "Saved")]
    [InlineData("Note saved.", "Note saved.")]
    public async Task A_saved_form_has_the_dialog_toast_its_own_message_or_the_dialogs_word(string? message, string toasted)
    {
        EditorDialogNotesPage.SavedMessage = message;
        using var toasts = Toast.Fake();
        var page = Page.Visit("/ui-editor-dialog/notes?edit=7");

        await page.On("#note-form").Submit();

        toasts.Shown(toasted).Once();
        EditorDialogNotesPage.SavedMessage = null;
    }
}

/// <summary>The notes page's switches are static: its tests run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class EditorDialogNotesCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "editor-dialog-notes";
}
