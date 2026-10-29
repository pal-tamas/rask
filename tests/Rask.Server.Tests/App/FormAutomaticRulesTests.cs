using System.Data.Common;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Data;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.App;

/// <summary>A name that is a value object, and states its own rule where the value is.</summary>
public sealed record BerthName(string Value)
{
    public const int MaxLength = 255;
    public const string Required = "Kötelező kitölteni.";
    public const string TooShort = "A berth's name has at least two characters.";
    public const string TooLong = "A berth's name is at most 255 characters.";

    // Everything about the name, the empty one included: no attribute says any of it.
    public static IEnumerable<string> Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return Required;
        }
        else if (value.Trim().Length < 2)
        {
            yield return TooShort;
        }
        else if (value.Length > MaxLength)
        {
            yield return TooLong;
        }
    }
}

/// <summary>An existing table whose unique rule is over a value object's column.</summary>
public sealed class Berth : Aggregate<int>, IFormModel, IPerTenant
{
    public const string NameTaken = "Ilyen néven már létezik kikötőhely.";

    private Berth() { }

    public int? TenantId { get; private set; }

    public BerthName Name { get; private set; } = new("");

    public static void Configure(EntityTypeBuilder<Berth> builder)
    {
        builder.ToTable("Berths");
        builder.Property(b => b.Name).HasMaxLength(255);
        builder.HasIndex(b => new { b.Name, b.TenantId }).IsUnique(NameTaken);
    }
}

/// <summary>An aggregate of plain properties: nothing but their types says what must be filled.</summary>
public sealed class Trailer : Aggregate<int>, IFormModel
{
    private Trailer() { }

    public int Weight { get; private set; }

    public string Plate { get; private set; } = "";

    public int? Axles { get; private set; }

    public static void Configure(EntityTypeBuilder<Trailer> builder) => builder.ToTable("Trailers");
}

public sealed class LegacyTrailer
{
    public int Id { get; set; }

    public int Weight { get; set; }

    public string Plate { get; set; } = "";

    public int? Axles { get; set; }
}

public sealed class LegacyBerth
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public int? TenantId { get; set; }
}

/// <summary>The context the app already had. It owns the schema, indexes included.</summary>
public sealed class LegacyQuayContext(DbContextOptions<LegacyQuayContext> options) : DbContext(options)
{
    public DbSet<LegacyBerth> Berths => Set<LegacyBerth>();

    public DbSet<LegacyCrane> Cranes => Set<LegacyCrane>();

    public DbSet<LegacyTrailer> Trailers => Set<LegacyTrailer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyTrailer>().ToTable("Trailers");

        modelBuilder.Entity<LegacyBerth>(b =>
        {
            b.ToTable("Berths");
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(255);
            b.HasIndex(x => new { x.Name, x.TenantId }).IsUnique();
        });

        modelBuilder.Entity<LegacyCrane>(b =>
        {
            b.ToTable("Cranes");
            b.HasIndex(c => new { c.Pier, c.Slot, c.TenantId }).IsUnique();
        });
    }
}

public sealed class QuayDomainContext(DbContextOptions<QuayDomainContext> options) : RaskDbContext(options);

/// <summary>
///     The same form with a field sent at every keystroke: the one binding whose value reaches the model before
///     it is committed, so a submit can find a name the store was never asked about.
/// </summary>
public sealed partial class BerthEveryKeyFormApp : Component
{
    private readonly BerthModel _model = new();
    private int _saves;

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(Save)[f =>
        [
            Markup.P[$"fault={f.Error?.GetType().Name ?? "none"}"],
            Markup.P[$"saves={_saves}"],
            Input.Bind(() => _model.Name).Debounce(TimeSpan.Zero).Id("name"),
            Validation.Message.Template(messages => Markup.P.Class("name-error")[string.Join(" + ", messages)]).For(() => _model.Name),
            Button["save"],
        ]];

    private async Task Save(BerthModel berth)
    {
        _saves++;
        await Berth.Create(berth);
    }
}

/// <summary>A form over the aggregate's GENERATED model: a bound field, no rule written, a save that just saves.</summary>
public sealed partial class BerthFormApp : Component
{
    private readonly BerthModel _model = new();
    private int _saves;

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(Save)[f =>
        [
            Markup.P[$"fault={f.Error?.GetType().Name ?? "none"}"],
            Markup.P[$"saves={_saves}"],
            Input.Bind(() => _model.Name).Id("name"),
            Validation.Message.Template(messages => Markup.P.Class("name-error")[string.Join(" + ", messages)]).For(() => _model.Name),
            Button["save"],
        ]];

    private async Task Save(BerthModel berth)
    {
        _saves++;
        await Berth.Create(berth);
    }
}

/// <summary>A model somebody wrote by hand: the same field, and nothing that ties it to the aggregate.</summary>
public sealed class BerthDraft
{
    public string? Name { get; set; }
}

public sealed partial class BerthDraftFormApp : Component
{
    private readonly BerthDraft _model = new();

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(_ => Task.CompletedTask)[
            Input.Bind(() => _model.Name).Id("name"),
            Validation.Message.Template(messages => Markup.P.Class("name-error")[string.Join(" + ", messages)]).For(() => _model.Name),
            Button["save"]
        ];
}

/// <summary>A create form that starts blank: a weight nobody has typed yet, and nothing validating it.</summary>
public sealed partial class TrailerFormApp : Component
{
    private readonly TrailerModel _model = new() { Weight = null, Plate = "ABC-123" };
    private int _saves;

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(Save)[
            Markup.P[$"saves={_saves}"],
            Input.Bind(() => _model.Weight).Id("weight"),
            Validation.Message.Template(messages => Markup.P.Class("weight-error")[string.Join(" + ", messages)]).For(() => _model.Weight),
            Input.Bind(() => _model.Axles).Id("axles"),
            Validation.Message.Template(messages => Markup.P.Class("axles-error")[string.Join(" + ", messages)]).For(() => _model.Axles),
            Button["save"]
        ];

    private async Task Save(TrailerModel trailer)
    {
        _saves++;
        await Trailer.Create(trailer);
    }
}

/// <summary>The same form with a rule of its own written on the field.</summary>
public sealed partial class BerthWrittenRuleFormApp : Component
{
    public const string NoDigits = "A berth's name has no digits.";

    private readonly BerthModel _model = new();

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(_ => Task.CompletedTask)[
            Input.Bind(() => _model.Name).Validate(name => name is not null && name.Any(char.IsDigit) ? [NoDigits] : []).Id("name"),
            Validation.Message.Template(messages => Markup.P.Class("name-error")[string.Join(" + ", messages)]).For(() => _model.Name),
            Button["save"]
        ];
}

/// <summary>An edit form: the model is filled from the row it will save over.</summary>
public sealed partial class BerthEditFormApp : Component
{
    public const int Edited = 1;

    private BerthModel? _model;

    protected override async Task OnMount() => _model = await Berth.Model(Edited);

    protected override Component? Render() =>
        _model is null
            ? Markup.P["no such berth"]
            : Form.Model(_model).OnSubmit(berth => Berth.Update(Edited, berth))[
                Input.Bind(() => _model.Name).Id("name"),
                Validation.Message.Template(messages => Markup.P.Class("name-error")[string.Join(" + ", messages)]).For(() => _model.Name),
                Button["save"]
            ];
}

/// <summary>A generated model whose aggregate has a unique rule over two fields the form binds.</summary>
public sealed partial class CraneGeneratedFormApp : Component
{
    private readonly CraneModel _model = new();

    protected override Component? Render() =>
        Form.Model(_model).OnSubmit(crane => Crane.Create(crane))[
            Input.Bind(() => _model.Pier).Id("pier"),
            Validation.Message.Template(messages => Markup.P.Class("pier-error")[messages[0]]).For(() => _model.Pier),
            Input.Bind(() => _model.Slot).Id("slot"),
            Validation.Message.Template(messages => Markup.P.Class("slot-error")[messages[0]]).For(() => _model.Slot),
            Button["save"]
        ];
}

/// <summary>
///     The rules a form bound to an aggregate's generated model asks BY ITSELF, with no step written on the
///     field: the value object's own rule, and the aggregate's unique rules asked of the store before the save.
///     Driven as a browser drives it — over the socket, on a hand-wired host, with the tenant from the request.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed partial class FormAutomaticRulesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-form-rules-{Guid.NewGuid():N}.db");
    private readonly StoreQuestions _questions = new();

    public void Dispose()
    {
        Db.Reset();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_value_objects_rule_is_told_under_the_field_with_no_validate_step_written()
    {
        using var host = await StartAsync<BerthFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var tooShort = await CommitAsync(ws, page, "name", "A");
        var fine = await CommitAsync(ws, tooShort, "name", "Almasfuzito");

        Assert.Contains($"<p class=\"name-error\">{Shown(BerthName.TooShort)}</p>", tooShort, StringComparison.Ordinal);
        Assert.DoesNotContain("name-error", fine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_written_validate_step_is_asked_after_the_value_objects_rule_has_let_the_value_through()
    {
        using var host = await StartAsync<BerthWrittenRuleFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var breaksBoth = await CommitAsync(ws, page, "name", "7");
        var breaksTheWrittenOne = await CommitAsync(ws, breaksBoth, "name", "Pier 7");
        var fine = await CommitAsync(ws, breaksTheWrittenOne, "name", "Pier seven");

        // The value object speaks first, and what it rejects is not put to the rule written on the field.
        Assert.Contains($"<p class=\"name-error\">{Shown(BerthName.TooShort)}</p>", breaksBoth, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"name-error\">{Shown(BerthWrittenRuleFormApp.NoDigits)}</p>", breaksTheWrittenOne, StringComparison.Ordinal);
        Assert.DoesNotContain("name-error", fine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_taken_name_is_told_when_the_field_is_committed_not_while_it_is_typed_and_a_correction_clears_it()
    {
        using var host = await StartAsync<BerthFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var typed = await TypeAsync(ws, page, "name", "Budapest");
        var askedWhileTyping = _questions.Count;
        var committed = await CommitAsync(ws, typed, "name", "Budapest");
        var corrected = await CommitAsync(ws, committed, "name", "Tata");

        Assert.DoesNotContain("name-error", typed, StringComparison.Ordinal);
        Assert.Equal(0, askedWhileTyping);
        Assert.Contains($"<p class=\"name-error\">{Shown(Berth.NameTaken)}</p>", committed, StringComparison.Ordinal);
        Assert.DoesNotContain("name-error", corrected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_taken_name_never_committed_is_told_on_submit_and_does_not_reach_the_save()
    {
        using var host = await StartAsync<BerthFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var refused = await TypeThenSubmitAsync(ws, page, "name", "Budapest");

        Assert.Contains($"<p class=\"name-error\">{Shown(Berth.NameTaken)}</p>", refused, StringComparison.Ordinal);
        Assert.Contains("saves=0<", refused, StringComparison.Ordinal);
        Assert.Contains("fault=none", refused, StringComparison.Ordinal);
        Assert.Equal(TypedThenSubmittedQuestions, _questions.Count);
    }

    [Fact]
    public async Task A_refused_save_of_a_name_typed_and_never_sent_sets_the_message_once_in_its_last_frame()
    {
        using var host = await StartAsync<BerthFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var renders = Renders(await TypeThenSubmitFramesAsync(ws, page, "name", "Budapest"));

        // The submit repaints while the store is asked (a form in flight says so), and then with the answer.
        // The message is in the answer alone: it is not set by the value, taken back and set again.
        var told = renders.Where(frame => frame.Contains("name-error", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, renders.Count);
        Assert.Same(renders[^1], Assert.Single(told));
        Assert.Contains(Shown(Berth.NameTaken), LastHtml([told[0]]), StringComparison.Ordinal);
        Assert.Equal(1, _questions.Count);
    }

    [Fact]
    public async Task A_name_committed_alone_is_still_put_to_the_store_and_the_submit_after_it_asks_afresh()
    {
        using var host = await StartAsync<BerthFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var committed = await CommitAsync(ws, page, "name", "Budapest");
        var askedByTheField = _questions.Count;
        var refused = await SubmitAsync(ws, committed);

        Assert.Contains($"<p class=\"name-error\">{Shown(Berth.NameTaken)}</p>", committed, StringComparison.Ordinal);
        Assert.Equal(1, askedByTheField);
        Assert.Equal(2, _questions.Count);
        Assert.Contains("saves=0<", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_taken_name_typed_into_a_field_sent_at_every_key_is_told_on_submit_and_does_not_reach_the_save()
    {
        using var host = await StartAsync<BerthEveryKeyFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var typed = await TypeAsync(ws, page, "name", "Budapest");
        var refused = await SubmitAsync(ws, typed);

        Assert.Contains($"<p class=\"name-error\">{Shown(Berth.NameTaken)}</p>", refused, StringComparison.Ordinal);
        Assert.Contains("saves=0<", refused, StringComparison.Ordinal);
        Assert.Contains("fault=none", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_only_another_tenant_holds_is_free_and_saves()
    {
        using var host = await StartAsync<BerthFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var committed = await CommitAsync(ws, page, "name", "Szeged");
        var saved = await SubmitAsync(ws, committed);

        // Tenant 8 holds "Szeged". Tenant 7's form is told nothing about it, before the save or by it.
        Assert.DoesNotContain("name-error", committed, StringComparison.Ordinal);
        Assert.True(_questions.Count > 0);
        Assert.Contains("saves=1<", saved, StringComparison.Ordinal);
        Assert.Contains("fault=none", saved, StringComparison.Ordinal);
        Assert.Equal(["Budapest|7", "Gyor|7", "Szeged|7", "Szeged|8"], await BerthsAsync(host));
    }

    [Fact]
    public async Task An_edit_form_does_not_collide_with_the_row_it_edits_and_does_with_another()
    {
        using var host = await StartAsync<BerthEditFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var itsOwnName = await CommitAsync(ws, page, "name", "Budapest");
        var anotherRows = await CommitAsync(ws, itsOwnName, "name", "Gyor");

        Assert.Contains("value=\"Budapest\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("name-error", itsOwnName, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"name-error\">{Shown(Berth.NameTaken)}</p>", anotherRows, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rule_over_two_bound_fields_is_told_under_both_from_one_question_and_correcting_one_clears_both()
    {
        using var host = await StartAsync<CraneGeneratedFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var half = await CommitAsync(ws, page, "pier", "North");
        var askedWithOneMemberFilled = _questions.Count;
        var both = await CommitAsync(ws, half, "slot", "3");
        var asked = _questions.Count;
        var corrected = await CommitAsync(ws, both, "slot", "4");

        Assert.DoesNotContain("-error", half, StringComparison.Ordinal);
        Assert.Equal(0, askedWithOneMemberFilled);
        Assert.Contains($"<p class=\"pier-error\">{Shown(Crane.PlaceTaken)}</p>", both, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"slot-error\">{Shown(Crane.PlaceTaken)}</p>", both, StringComparison.Ordinal);
        Assert.Equal(1, asked);
        Assert.DoesNotContain("-error", corrected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_written_by_hand_gets_neither_the_value_objects_rule_nor_the_question_to_the_store()
    {
        using var host = await StartAsync<BerthDraftFormApp>();
        var page = await OpenAsync(host, tenant: 7);
        using var ws = await AttachAsync(host, page);

        var tooShort = await CommitAsync(ws, page, "name", "A");
        var taken = await CommitAsync(ws, tooShort, "name", "Budapest");

        Assert.DoesNotContain("name-error", tooShort, StringComparison.Ordinal);
        Assert.DoesNotContain("name-error", taken, StringComparison.Ordinal);
        Assert.Equal(0, _questions.Count);
    }

    [Fact]
    public async Task With_the_automatic_validators_off_and_no_attribute_anywhere_the_value_object_still_says_what_is_required_and_what_is_too_long()
    {
        var automatic = Rask.Core.Forms.RaskValidation.AutoValidate;
        Rask.Core.Forms.RaskValidation.AutoValidate = false;
        try
        {
            using var host = await StartAsync<BerthFormApp>();
            var page = await OpenAsync(host, tenant: 7);
            using var ws = await AttachAsync(host, page);

            var untouched = await SubmitAsync(ws, page);
            var tooLong = await CommitAsync(ws, untouched, "name", new string('x', BerthName.MaxLength + 1));
            var submittedTooLong = await SubmitAsync(ws, tooLong);

            Assert.Contains($"<p class=\"name-error\">{Shown(BerthName.Required)}</p>", untouched, StringComparison.Ordinal);
            Assert.Contains("saves=0<", untouched, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"name-error\">{Shown(BerthName.TooLong)}</p>", tooLong, StringComparison.Ordinal);
            Assert.Contains("saves=0<", submittedTooLong, StringComparison.Ordinal);
            Assert.Contains("fault=none", submittedTooLong, StringComparison.Ordinal);
        }
        finally
        {
            Rask.Core.Forms.RaskValidation.AutoValidate = automatic;
        }
    }

    [Theory]
    [InlineData("hu", "Kötelező kitölteni.")]
    [InlineData("en", GeneratedModelRules.RequiredMessage)]
    public async Task An_unfilled_number_that_cannot_be_null_says_the_catalogs_sentence_in_the_readers_language_with_the_automatic_validators_off(
        string language, string sentence)
    {
        var automatic = Rask.Core.Forms.RaskValidation.AutoValidate;
        var strings = Rask.Core.Globalization.RaskStrings.Source;
        Rask.Core.Forms.RaskValidation.AutoValidate = false;
        Rask.Core.Globalization.RaskStrings.UseSource(new HungarianRequired());
        try
        {
            using var host = await StartAsync<TrailerFormApp>(language);
            var page = await OpenAsync(host, tenant: 7);
            using var ws = await AttachAsync(host, page);

            var refused = await SubmitAsync(ws, page);

            // The weight is required by its type; the axles are optional, and nothing is said about them.
            Assert.Contains($"<p class=\"weight-error\">{Shown(sentence)}</p>", refused, StringComparison.Ordinal);
            Assert.DoesNotContain("axles-error", refused, StringComparison.Ordinal);
            Assert.Contains("saves=0<", refused, StringComparison.Ordinal);
        }
        finally
        {
            Rask.Core.Globalization.RaskStrings.UseSource(strings!);
            Rask.Core.Forms.RaskValidation.AutoValidate = automatic;
        }
    }

    [Fact]
    public async Task A_field_that_is_required_by_its_type_says_so_to_assistive_technology_and_an_optional_one_does_not()
    {
        using var host = await StartAsync<TrailerFormApp>();
        using var berths = await StartAsync<BerthFormApp>();

        var trailer = await OpenAsync(host, tenant: 7);
        var berth = await OpenAsync(berths, tenant: 7);

        // A plain int, and a value object's text: both from the type, with no attribute on either model.
        Assert.Contains("aria-required=\"true\"", Field(trailer, "weight"), StringComparison.Ordinal);
        Assert.Contains("aria-required=\"true\"", Field(berth, "name"), StringComparison.Ordinal);
        Assert.DoesNotContain("aria-required", Field(trailer, "axles"), StringComparison.Ordinal);
        Assert.DoesNotContain(" required", Field(trailer, "weight"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_create_with_no_form_names_the_unfilled_plain_properties_and_asks_the_database_nothing()
    {
        using var host = await StartAsync<TrailerFormApp>();
        var commandsBefore = _questions.Commands;

        var refused = await Assert.ThrowsAsync<Rask.Cqrs.RaskValidationException>(
            () => Trailer.Create(new TrailerModel { Weight = null, Plate = " " }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            [("Weight", GeneratedModelRules.RequiredMessage), ("Plate", GeneratedModelRules.RequiredMessage)],
            refused.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal(commandsBefore, _questions.Commands);
    }

    [Theory]
    [InlineData(null, BerthName.Required)]
    [InlineData("", BerthName.Required)]
    [InlineData("A", BerthName.TooShort)]
    public async Task A_create_with_no_form_behind_it_is_refused_by_the_same_rule_under_the_fields_name_before_the_database_is_asked_anything(
        string? name, string message)
    {
        using var host = await StartAsync<BerthFormApp>();
        var commandsBefore = _questions.Commands;

        Rask.Cqrs.RaskValidationException refused;
        using (Tenant.Use(7))
        {
            refused = await Assert.ThrowsAsync<Rask.Cqrs.RaskValidationException>(
                () => Berth.Create(new BerthModel { Name = name }, cancellationToken: TestContext.Current.CancellationToken));
        }

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(["Name"], failure.Fields);
        Assert.Equal(message, failure.Message);
        Assert.Equal(commandsBefore, _questions.Commands);
        Assert.Equal(["Budapest|7", "Gyor|7", "Szeged|8"], await BerthsAsync(host));
    }

    // The message as the page carries it: text is encoded on its way into markup, accents included.
    private static string Shown(string message) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message);

    private static async Task<string> OpenAsync(RaskTestHost host, int tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/start");
        request.Headers.Add(CurrentCustomer.Header, tenant.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    // The socket carries the tenant as the page's request did: each event asks the resolver again.
    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string page)
    {
        var sessionId = MarkupAssert.SessionId(page);
        host.WebSockets.ConfigureRequest = request => request.Headers[CurrentCustomer.Header] = "7";
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        return ws;
    }

    // A keystroke, where the field writes every one. A field that waits for the reader sends nothing yet,
    // and then there is nothing to send here either.
    private static async Task<string> TypeAsync(WebSocket ws, string page, string id, string value)
    {
        if (InputHandler().Match(Field(page, id)) is not { Success: true } handler)
        {
            return page;
        }

        await ws.SendJsonAsync(new { id = handler.Groups[1].Value, type = "input", value }, ct: TestContext.Current.CancellationToken);
        return LastHtml(await ws.SettledAsync()) ?? page;
    }

    // The reader stopped: the field's value is committed, which is when its rules — and the store's — are asked.
    // What a browser sends for that is the keystrokes and then a change, which carries the value again.
    private static async Task<string> CommitAsync(WebSocket ws, string page, string id, string value)
    {
        page = await TypeAsync(ws, page, id, value);
        var handler = ChangeHandler().Match(Field(page, id)).Groups[1].Value;
        await ws.SendJsonAsync(new { id = handler, type = "change", value }, ct: TestContext.Current.CancellationToken);

        return LastHtml(await ws.SettledAsync()) ?? page;
    }

    // The value arrives in the frame of its form's submit, so its own question is left to the submit, which
    // asks the store about the whole model: once.
    private const int TypedThenSubmittedQuestions = 1;

    // What the browser sends for a field that waits, typed into and never left, when Save is pressed: one
    // frame, the value first and the submit behind it.
    private static async Task<string> TypeThenSubmitAsync(WebSocket ws, string page, string id, string value) =>
        LastHtml(await TypeThenSubmitFramesAsync(ws, page, id, value)) ?? page;

    private static async Task<List<string>> TypeThenSubmitFramesAsync(WebSocket ws, string page, string id, string value)
    {
        var change = ChangeHandler().Match(Field(page, id)).Groups[1].Value;
        var submit = new { id = MarkupAssert.RequireAttr(page, "data-rask-on-submit"), type = "submit", form = new { } };
        await ws.SendTypedThenAsync(change, value, submit, TestContext.Current.CancellationToken);

        return await ws.SettledAsync();
    }

    // The frames that draw the page: an ack and a script carry no markup.
    private static List<string> Renders(List<string> frames) =>
        frames.Where(frame => LastHtml([frame]) is not null).ToList();

    private static async Task<string> SubmitAsync(WebSocket ws, string page)
    {
        var handler = MarkupAssert.RequireAttr(page, "data-rask-on-submit");
        await ws.SendJsonAsync(new { id = handler, type = "submit", form = new { } }, ct: TestContext.Current.CancellationToken);

        return LastHtml(await ws.SettledAsync()) ?? page;
    }

    // The one <input …> tag with this id.
    private static string Field(string page, string id)
    {
        var start = page.IndexOf($"<input id=\"{id}\"", StringComparison.Ordinal);
        return page[start..(page.IndexOf('>', start) + 1)];
    }

    private static string? LastHtml(List<string> frames)
    {
        for (var i = frames.Count - 1; i >= 0; i--)
        {
            using var frame = JsonDocument.Parse(frames[i]);
            if (frame.RootElement.TryGetProperty("html", out var html))
            {
                return html.GetString();
            }
        }

        return null;
    }

    private static async Task<List<string>> BerthsAsync(RaskTestHost host)
    {
        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyQuayContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        var rows = await legacy.Berths.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        return [.. rows.Select(b => $"{b.Name}|{b.TenantId}").Order(StringComparer.Ordinal)];
    }

    // The documented hand-wired host over a schema somebody else created, unique indexes included.
    private async Task<RaskTestHost> StartAsync<TApp>(string? language = null)
        where TApp : Component
    {
        Action<DbContextOptionsBuilder> database = o => o.UseSqlite($"Data Source={_dbPath};Pooling=False");

        var host = RaskTestHost.Create<TApp>(
            configureServices: services =>
            {
                services.AddHttpContextAccessor();
                services.AddSingleton<CurrentCustomer>();
                services.AddDbContextFactory<LegacyQuayContext>(database);
                services.AddRaskTenant(sp => sp.GetRequiredService<CurrentCustomer>().TenantId);
                services.AddRaskData<QuayDomainContext>(o =>
                {
                    database(o);
                    o.AddInterceptors(_questions);
                });
            },
            configureMiddleware: app => app.UseRaskData(),
            diffMode: LiveDiffMode.DisabledFull,
            configureCulture: language is null
                ? null
                : c =>
                {
                    c.SupportedCultures.Add("en");
                    c.SupportedCultures.Add("hu");
                });

        // The reader's language rides on the culture cookie, on the page request and on the socket alike.
        if (language is not null)
        {
            var cookie = $".AspNetCore.Culture=c%3D{language}%7Cuic%3D{language}";
            host.Http.DefaultRequestHeaders.Add("Cookie", cookie);
            host.WebSockets.ConfigureRequest = r => r.Headers["Cookie"] = cookie;
        }

        await using var legacy = await host.Services.GetRequiredService<IDbContextFactory<LegacyQuayContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await legacy.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        legacy.AddRange(
            new LegacyBerth { Id = BerthEditFormApp.Edited, Name = "Budapest", TenantId = 7 },
            new LegacyBerth { Id = 2, Name = "Gyor", TenantId = 7 },
            new LegacyBerth { Id = 3, Name = "Szeged", TenantId = 8 },
            new LegacyCrane { Pier = "North", Slot = "3", TenantId = 7 });
        await legacy.SaveChangesAsync(TestContext.Current.CancellationToken);

        return host;
    }

    /// <summary>An app's own catalog: it says the sentence in Hungarian, and nothing else.</summary>
    private sealed class HungarianRequired : Rask.Core.Globalization.IRaskStringSource
    {
        public string? Get(Rask.Core.Globalization.RaskString key, string cultureTag) =>
            key == Rask.Core.Globalization.RaskString.ValidationRequired && cultureTag.StartsWith("hu", StringComparison.Ordinal)
                ? "Kötelező kitölteni."
                : null;
    }

    /// <summary>Counts the "is there already such a row" questions the store is asked.</summary>
    private sealed class StoreQuestions : DbCommandInterceptor
    {
        private int _count;
        private int _commands;

        public int Count => Volatile.Read(ref _count);

        /// <summary>Every command sent through the app's context, whatever it asks.</summary>
        public int Commands => Volatile.Read(ref _commands);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            if (command.CommandText.Contains("SELECT EXISTS", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [GeneratedRegex("data-rask-on-input=\"([^\"]+)\"")]
    private static partial Regex InputHandler();

    [GeneratedRegex("data-rask-on-change=\"([^\"]+)\"")]
    private static partial Regex ChangeHandler();
}
