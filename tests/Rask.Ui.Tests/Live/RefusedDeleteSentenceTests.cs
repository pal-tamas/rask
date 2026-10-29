using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Core;
using Rask.Core.Messaging;
using Rask.Data;
using Rask.Testing;
using Rask.TestSupport;

namespace Rask.UiTests.Live;

/// <summary>Referenced by a table the model does not map, and says what a refused delete means.</summary>
public sealed class Hitch : Aggregate<int>, ITimestamps, IVersioned, IFormModel, IDeletable
{
    public const string InUse = "A pótkocsi nem törölhető, mert még hivatkoznak rá.";

    private Hitch() { }

    public string Plate { get; private set; } = "";

    public static Hitch Plated(string plate) => new() { Plate = plate };

    public static void Configure(EntityTypeBuilder<Hitch> builder) => builder.WhenReferenced(InUse);
}

/// <summary>What a form over a hitch edits.</summary>
public sealed class HitchForm
{
    public string Plate { get; set; } = "";
}

/// <summary>
///     The seam between the two halves: the generated <c>Hitch.Delete(id)</c> meets a row SQLite will not let
///     go, in a handler that catches nothing — and the sentence the aggregate declared is the toast. No part of
///     the page says "refused"; the store's exception is what the handler let out.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed partial class RefusedDeleteSentenceTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private const string SaveFailed = "The save failed. Please try again.";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-ui-refused-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_generated_delete_the_database_refuses_ends_as_the_declared_sentence_in_a_toast(bool insideAForm)
    {
        await using var database = await TestDatabase.Start(
            o => o.UseSqlite($"Data Source={_dbPath}"), cancellationToken: TestContext.Current.CancellationToken);
        var hitch = await ReferencedHitchAsync(database.Context);
        var page = new HitchPage(hitch.Id, insideAForm);
        using var toasts = Toast.Fake();

        await page.ClickDelete();

        toasts.Shown(Hitch.InUse).Once();
        toasts.Shown(Hitch.InUse).As(ToastLevel.Error);
        toasts.Shown(SaveFailed).Never();
        Assert.DoesNotContain("boundary-fallback", page.Html, StringComparison.Ordinal);
        Assert.Equal(1, await database.Context.Set<Hitch>().CountAsync(TestContext.Current.CancellationToken));
    }

    // A hitch, and a row pointing at it in a table only the database knows about.
    private static async Task<Hitch> ReferencedHitchAsync(DbContext context)
    {
        var hitch = Hitch.Plated("ABC-123");
        await hitch.Save(cancellationToken: TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlAsync(
            $"""
             CREATE TABLE "TripLog" ("Id" INTEGER PRIMARY KEY, "HitchId" INTEGER NOT NULL REFERENCES "Hitch" ("Id"));
             INSERT INTO "TripLog" ("HitchId") VALUES ({hitch.Id});
             """,
            TestContext.Current.CancellationToken);
        return hitch;
    }

    // A delete button as an app writes it — alone, or as a form's own "Törlés" beside its save.
    private sealed class HitchPage
    {
        private readonly StubComponent _view;

        internal HitchPage(int id, bool insideAForm)
        {
            var bin = new Bin(id);
            _view = new StubComponent(() => ErrorBoundary.Fallback(static (_, _) => P.Id("boundary-fallback")["failed"])[
                insideAForm ? Markup.Form.Model(new HitchForm()).OnSubmit(static _ => { })[bin] : bin
            ]);
            Html = _view.RenderAsLiveRoot();
        }

        internal string Html { get; private set; }

        internal async Task ClickDelete()
        {
            using var payload = JsonDocument.Parse("{}");

            try
            {
                await _view.TryInvokeHandlerAsync(MarkupAssert.Attr(Html, "data-rask-on-click")!, payload.RootElement);
            }
            finally
            {
                Html = _view.RenderAsLiveRoot();
            }
        }
    }

    private sealed class Bin(int id) : Component
    {
        protected override Component? Render() => Button.Type(ButtonType.Button).OnClick(Delete)["Törlés"];

        private Task Delete() => Hitch.Delete(id);
    }
}
