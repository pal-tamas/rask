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
    public const string TooShort = "A berth's name has at least two characters.";

    public static IEnumerable<string> Validate(string value)
    {
        if (value.Trim().Length < 2)
        {
            yield return TooShort;
        }
    }
}

/// <summary>An existing table whose unique rule is over a value object's column.</summary>
public sealed class Berth : Aggregate<int>
{
    public const string NameTaken = "Ilyen néven már létezik kikötőhely.";

    private Berth() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public BerthName Name { get; private set; } = new("");

    public static void Configure(EntityTypeBuilder<Berth> builder)
    {
        builder.ToTable("Berths");
        builder.Property(b => b.Name).HasMaxLength(255);
        builder.HasIndex(b => new { b.Name, b.TenantId }).IsUnique(NameTaken);
    }
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

    // The message as the page carries it: text is encoded on its way into markup, accents included.
    private static string Shown(string message) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message);

    private static async Task<string> OpenAsync(RaskTestHost host, int tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/start");
        request.Headers.Add(CurrentCustomer.Header, tenant.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var response = await host.Http.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    // The socket carries no tenant: the session keeps the one it was opened with.
    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string page)
    {
        var sessionId = MarkupAssert.SessionId(page);
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
    private async Task<RaskTestHost> StartAsync<TApp>()
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
            diffMode: LiveDiffMode.DisabledFull);

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

    /// <summary>Counts the "is there already such a row" questions the store is asked.</summary>
    private sealed class StoreQuestions : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT EXISTS", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _count);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [GeneratedRegex("data-rask-on-input=\"([^\"]+)\"")]
    private static partial Regex InputHandler();

    [GeneratedRegex("data-rask-on-change=\"([^\"]+)\"")]
    private static partial Regex ChangeHandler();
}
