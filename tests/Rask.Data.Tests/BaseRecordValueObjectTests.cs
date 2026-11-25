using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary>
/// A value object declared from a base record, stored: one column for one value and complex columns for several,
/// the length and precision it declares, a stored value loaded as it is, and the rules a generated model asks.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class BaseRecordValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-vo-tanker-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Each_one_value_object_round_trips_as_one_column()
    {
        await using var database = await StartDatabaseAsync();
        var inspected = new DateOnly(2024, 5, 1);
        var fueled = new DateTime(2024, 5, 1, 14, 35, 12);
        var lorry = Rig.Of("  Ada  ", "ABC-123", 120.5m, 3, chip: " C1 ", inspected: inspected, fueled: fueled);
        await lorry.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Rig>(lorry.Id, TestContext.Current.CancellationToken);

        Assert.Equal("Ada", stored!.Driver.Value);
        Assert.Equal(" C1 ", stored.Chip!.Value);
        Assert.Equal("ABC-123", stored.Plate.Value);
        Assert.Equal(120.5m, stored.Tank.Value);
        Assert.Equal(3, stored.Axles.Value);
        Assert.Equal(inspected, stored.Inspected!.Value);
        Assert.Equal((fueled, DateTimeKind.Unspecified), (stored.Fueled!.Value, stored.Fueled.Value.Kind));
        var type = database.Context.Model.FindEntityType(typeof(Rig))!;
        Assert.Equal(typeof(string), type.FindProperty(nameof(Rig.Driver))!.GetValueConverter()!.ProviderClrType);
        Assert.DoesNotContain(type.GetComplexProperties(), p => p.Name == nameof(Rig.Driver));
    }

    [Fact]
    public async Task A_value_object_of_several_members_round_trips_as_complex_columns()
    {
        await using var database = await StartDatabaseAsync();
        var haul = Haul.Of("Ada", 10, 20);
        haul.Reopen(new DateOnly(2026, 3, 1), 7.5m);
        await haul.Save(cancellationToken: TestContext.Current.CancellationToken);

        var stored = await database.Load<Haul>(haul.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RigKm.Create(10, 20), stored!.Km);
        Assert.Equal(RigOpening.Create(new DateOnly(2026, 3, 1), 7.5m), stored.Opening);
        Assert.Contains(database.Context.Model.FindEntityType(typeof(Haul))!.GetComplexProperties(), p => p.Name == nameof(Haul.Km));
    }

    [Fact]
    public async Task The_column_takes_the_length_and_precision_the_value_object_declares()
    {
        await using var database = await StartDatabaseAsync();

        var type = database.Context.Model.FindEntityType(typeof(Rig))!;

        Assert.Equal(20, type.FindProperty(nameof(Rig.Driver))!.GetMaxLength());
        Assert.Equal(RigLimits.Chip, type.FindProperty(nameof(Rig.Chip))!.GetMaxLength());
        Assert.Null(type.FindProperty(nameof(Rig.Plate))!.GetMaxLength());
        Assert.Equal(9, type.FindProperty(nameof(Rig.Tank))!.GetPrecision());
        Assert.Equal(2, type.FindProperty(nameof(Rig.Tank))!.GetScale());
        Assert.Equal(new ValueFacets(MaxLength: 12), RigChip.__Facets);
    }

    [Fact]
    public async Task A_stored_value_its_rules_now_refuse_is_loaded_as_it_was_stored()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var lorry = Rig.Of("Ada", "ABC-123", 10, 3);
        await lorry.Save(cancellationToken: token);
        var haul = Haul.Of("Bea", 10, 20);
        await haul.Save(cancellationToken: token);
        var tooLong = new string('x', 30);
        await WritePastTheRulesAsync(database, typeof(Rig), "\"Driver\" = {0}, \"Plate\" = {1}", tooLong, "nonsense");
        await WritePastTheRulesAsync(database, typeof(Haul), "\"Driver\" = {0}, \"Km_End\" = {1}", " padded ", 5);

        var rig = await database.Load<Rig>(lorry.Id, token);
        var loaded = await database.Load<Haul>(haul.Id, token);
        var queried = await Rig.Where(r => r.Id == lorry.Id).First(token);

        Assert.Equal(tooLong, rig!.Driver.Value);
        Assert.Equal("nonsense", rig.Plate.Value);
        Assert.Equal((10, 5), (loaded!.Km.Start, loaded.Km.End));
        Assert.Equal(" padded ", loaded.Driver.Value);
        Assert.Equal(tooLong, queried.Driver.ToString());
        Assert.False(RowLoading.IsOn);
    }

    [Fact]
    public async Task A_tracked_edit_saves_while_another_stored_value_breaks_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var haul = Haul.Of("Ada", 10, 20);
        await haul.Save(cancellationToken: token);
        await WritePastTheRulesAsync(database, typeof(Haul), "\"Driver\" = {0}, \"Km_End\" = {1}", new string('x', 30), 5);
        await using var context = Db.CreateOwnContext();
        var tracked = await context.Set<Haul>().SingleAsync(h => h.Id == haul.Id, token);

        tracked.Reopen(new DateOnly(2026, 3, 1), 2m);
        await context.SaveChangesAsync(token);

        var stored = await database.Load<Haul>(haul.Id, token);
        Assert.Equal(2m, stored!.Opening.Quantity);
        Assert.Equal(5, stored.Km.End);
        Assert.Equal(30, stored.Driver.Value.Length);
    }

    [Fact]
    public async Task A_failed_materialization_leaves_the_next_Create_checking_its_rules()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var row = Sealed.Of(1);
        await row.Save(cancellationToken: token);
        await WritePastTheRulesAsync(database, typeof(Sealed), "\"Seal_Count\" = {0}", -1);
        var raw = Record.Exception(() => database.Context.Set<Sealed>().AsNoTracking().ToList());
        var afterTheRawQuery = RowLoading.IsOn;

        var read = await Record.ExceptionAsync(() => database.Load<Sealed>(row.Id, token));

        Assert.IsType<InvalidOperationException>(raw);
        Assert.IsType<InvalidOperationException>(read);
        Assert.False(afterTheRawQuery);
        Assert.False(RowLoading.IsOn);
        Assert.Throws<RaskValidationException>(() => RigKm.Create(5, 1));
    }

    [Fact]
    public async Task A_range_the_generated_write_refuses_is_refused_under_the_member_that_holds_it()
    {
        await using var database = await StartDatabaseAsync();
        var backwards = new LegModel { Km = new() { Start = 20, End = 10 } };

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Leg.Create(backwards, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["Km.End"], refused.Failures.Select(f => f.Fields.Single()));
    }

    [Fact]
    public async Task The_generated_writes_refuse_a_value_its_rules_refuse_under_its_field()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var valid = new RigModel { Driver = "Ada", Plate = "ABC-123", Tank = 50, Axles = 3, Chip = "" };

        var created = await Rig.Create(valid, cancellationToken: token);
        var create = await Assert.ThrowsAsync<RaskValidationException>(
            () => Rig.Create(new RigModel { Driver = new string('x', 21), Plate = "ABC-123", Tank = 50, Axles = 3 }, cancellationToken: token));
        var update = await Assert.ThrowsAsync<RaskValidationException>(
            () => Rig.Update(created.Id, new RigModel { Driver = "Ada", Plate = "nope", Tank = 50, Axles = 3 }, cancellationToken: token));

        Assert.Null(created.Chip);
        Assert.Equal([("Driver", "Enter at most 20 characters.")], create.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal([("Plate", RigPlate.Form)], update.Failures.Select(f => (f.Fields.Single(), f.Message)));
    }

    [Fact]
    public void A_form_bound_to_the_generated_model_asks_the_value_objects_rule_under_its_field()
    {
        var ruleOf = RuleOfRigModel();

        var driver = (Rask.Core.Forms.Validate<string?>)ruleOf("Driver")!;
        var chip = (Rask.Core.Forms.Validate<string?>)ruleOf("Chip")!;
        var axles = (Rask.Core.Forms.Validate<long?>)ruleOf("Axles")!;

        Assert.Equal(["Enter at most 20 characters."], driver(new string('x', 21)));
        Assert.Equal([GeneratedModelRules.RequiredMessage], driver(null));
        Assert.Empty(driver("Ada"));
        Assert.Empty(chip(""));
        Assert.Equal(["Enter at most 12 characters."], chip(new string('x', 13)));
        Assert.Equal(["Enter 2 or more."], axles(1));
    }

    [Fact]
    public void A_value_of_several_members_is_asked_live_under_the_member_its_problem_names_beside_the_others()
    {
        var model = new BunkerModel();
        var ruleOf = Generated<Func<string, Delegate?>>("RigOpeningModel_RuleOf", typeof(BunkerModel));
        var quantity = (Func<object, decimal?, IEnumerable<string>>)ruleOf("Quantity")!;
        var date = (Func<object, DateOnly?, IEnumerable<string>>)ruleOf("Date")!;

        var alone = quantity(model.Opening!, 5m);
        var dateAlone = date(model.Opening!, null);
        model.Opening!.Date = new DateOnly(2026, 3, 1);
        var both = quantity(model.Opening, 5m);

        Assert.Equal([RigOpening.BothOrNeither], alone);
        Assert.Empty(dateAlone);
        Assert.Empty(both);
    }

    [Fact]
    public void Check_answers_a_hand_written_form_what_Create_would_refuse_with()
    {
        Rask.Core.Forms.Validate<string> driver = RigDriver.Check;
        Rask.Core.Forms.Validate<long> axles = RigAxles.Check;

        var empty = driver("");
        var tooLong = driver(new string('x', 21));
        var tooFew = axles(1);

        Assert.Equal([GeneratedModelRules.RequiredMessage], empty);
        Assert.Equal(["Enter at most 20 characters."], tooLong);
        Assert.Equal(["Enter 2 or more."], tooFew);
        Assert.Empty(driver("Ada"));
    }

    [Fact]
    public void A_record_of_one_member_declared_from_Value_is_asked_on_its_field_like_a_base_record()
    {
        var ruleOf = Generated<Func<string, Delegate?>>("Root_RuleOf", typeof(LadderModel));
        var rank = (Rask.Core.Forms.Validate<int?>)ruleOf("Rank")!;
        var seats = (Rask.Core.Forms.Validate<int?>)ruleOf("Seats")!;

        var zero = rank(0);
        var nothing = rank(null);
        var tooMany = seats(10);

        Assert.Equal([RigRank.AtLeastOne], zero);
        Assert.Equal([GeneratedModelRules.RequiredMessage], nothing);
        Assert.Empty(rank(3));
        Assert.Equal(["Enter 9 or less."], tooMany);
    }

    [Fact]
    public async Task A_one_member_Value_record_and_an_int_Whole_are_one_column_each_and_a_refusal_names_the_field()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;
        var entity = database.Context.Model.FindEntityType(typeof(Ladder))!;

        var created = await Ladder.Create(new LadderModel { Rank = 2, Seats = 4 }, cancellationToken: token);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Ladder.Create(new LadderModel { Rank = 0, Seats = 4 }, cancellationToken: token));
        var made = Assert.Throws<RaskValidationException>(() => RigRank.Create(0, "Rank"));

        Assert.Equal(typeof(int), entity.FindProperty(nameof(Ladder.Rank))!.GetValueConverter()!.ProviderClrType);
        Assert.Equal(typeof(int), entity.FindProperty(nameof(Ladder.Seats))!.GetValueConverter()!.ProviderClrType);
        Assert.Equal((2, 4), (created.Rank.Value, created.Seats.Value));
        Assert.Equal([("Rank", RigRank.AtLeastOne)], refused.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal([("Rank", RigRank.AtLeastOne)], made.Failures.Select(f => (f.Fields.Single(), f.Message)));
    }

    [Fact]
    public void A_text_that_is_not_required_keeps_an_empty_value_and_still_checks_a_filled_one()
    {
        var blank = RigNote.Create("   ");
        var tooLong = RigNote.TryCreate("123456", out _, out var problems);
        var optional = RigNote.CreateOptional("");

        Assert.Equal("", blank.Value);
        Assert.False(tooLong);
        Assert.Equal(["Enter at most 5 characters."], problems.Select(p => p.Message));
        Assert.Null(optional);
    }

    [Fact]
    public async Task A_field_holding_a_text_that_is_not_required_is_neither_refused_nor_announced_and_saves_empty()
    {
        await using var database = await StartDatabaseAsync();
        var rule = (Rask.Core.Forms.Validate<string?>)Generated<Func<string, Delegate?>>("Root_RuleOf", typeof(NotebookModel))("Note")!;

        var empty = rule(null);
        var tooLong = rule("123456");
        var created = await Notebook.Create(new NotebookModel(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(empty);
        Assert.Equal(["Enter at most 5 characters."], tooLong);
        Assert.Null(typeof(RigModel).Assembly.GetTypes().Single(t => t.Name.EndsWith("__NotebookModelRules", StringComparison.Ordinal))
            .GetMethod("Root_IsRequired", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        Assert.Equal("", created.Note.Value);
    }

    [Fact]
    public void Every_base_prints_its_value_culture_invariant_whatever_the_culture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("hu-HU");
        try
        {
            var litres = RigLitres.Create(1234.5m).ToString();
            var axles = RigAxles.Create(9).ToString();
            var day = RigInspected.Create(new DateOnly(2026, 1, 31)).ToString();

            Assert.Equal("1234.5", litres);
            Assert.Equal("9", axles);
            Assert.Equal("2026-01-31", day);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_form_bound_to_the_generated_model_is_told_each_fields_shape()
    {
        var shapeOf = Generated<Func<string, Rask.Core.Forms.FieldShape?>>("Root_ShapeOf");

        var driver = shapeOf("Driver");
        var chip = shapeOf("Chip");
        var plate = shapeOf("Plate");
        var tank = shapeOf("Tank");
        var axles = shapeOf("Axles");
        var inspected = shapeOf("Inspected");

        Assert.Equal(new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Text, MaxLength: 20), driver);
        Assert.Equal(12, chip!.MaxLength);
        Assert.Equal(new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Text) { Pattern = @"\s*[A-Z]{3}-\d{3}\s*" }, plate);
        Assert.Equal(new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Number, Min: 1, Scale: 2), tank);
        Assert.Equal(new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Integer, Min: 2, Max: 9), axles);
        Assert.True(inspected!.NotAfterToday);
        Assert.Equal(new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Moment, NotAfterNow: true), shapeOf("Fueled"));
    }

    [Fact]
    public async Task A_moment_is_stored_as_one_date_and_time_column()
    {
        await using var database = await StartDatabaseAsync();

        var fueled = database.Context.Model.FindEntityType(typeof(Rig))!.FindProperty(nameof(Rig.Fueled))!;

        Assert.Equal(typeof(DateTime), fueled.GetValueConverter()!.ProviderClrType);
        Assert.Equal("TEXT", fueled.GetColumnType());
    }

    [Fact]
    public void A_range_on_a_generated_model_is_there_from_the_start_and_its_end_is_checked_beside_its_start()
    {
        var model = new LegModel();
        var ruleOf = Generated<Func<string, Delegate?>>("RigKmModel_RuleOf", typeof(LegModel));
        var end = (Func<object, int?, IEnumerable<string>>)ruleOf("End")!;
        model.Km!.Start = 10;

        var notAfter = end(model.Km, 10);
        var after = end(model.Km, 11);
        var empty = end(model.Km, null);

        Assert.Equal(["It must be after the start, 10."], notAfter);
        Assert.Empty(after);
        Assert.Equal([GeneratedModelRules.RequiredMessage], empty);
    }

    [Fact]
    public void A_ranges_ends_are_shaped_as_whole_numbers_and_the_range_names_them()
    {
        var model = new LegModel();
        var shapeOf = Generated<Func<string, Rask.Core.Forms.FieldShape?>>("RigKmModel_ShapeOf", typeof(LegModel));

        var start = shapeOf("Start");
        var km = Generated<Func<string, Rask.Core.Forms.FieldShape?>>("Root_ShapeOf", typeof(LegModel))("Km");

        Assert.NotNull(model.Km);
        Assert.Equal(new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Integer), start);
        Assert.Equal(
            new Rask.Core.Forms.FieldShape(Rask.Core.Forms.FieldKind.Range, Strict: true, Start: "Start", End: "End", Ends: Rask.Core.Forms.FieldKind.Integer),
            km);
    }

    [Fact]
    public async Task An_optional_complex_value_left_empty_reads_back_as_none_on_both_faces()
    {
        await using var database = await StartDatabaseAsync();
        var empty = Tankyard.Of("Empty");
        var opened = Tankyard.Of("Opened");
        opened.Open(new DateTime(2026, 3, 1), 1234.5678901234567891m);
        await empty.Save(cancellationToken: TestContext.Current.CancellationToken);
        await opened.Save(cancellationToken: TestContext.Current.CancellationToken);

        var storedEmpty = await database.Load<Tankyard>(empty.Id, TestContext.Current.CancellationToken);
        var storedOpened = await database.Load<Tankyard>(opened.Id, TestContext.Current.CancellationToken);
        var rows = await Tankyard.OrderBy(d => d.Name);
        var withStart = await Tankyard.Count(d => d.StartQuantity > 1000m, TestContext.Current.CancellationToken);

        Assert.Null(storedEmpty!.Start);
        Assert.Equal(RigStart.Create(new DateTime(2026, 3, 1), RigQuantity.Create(1234.5678901234567891m)), storedOpened!.Start);
        Assert.Equal(["Empty", "Opened"], rows.Select(r => r.Name));
        Assert.Equal((null, null), (rows[0].StartDate, rows[0].StartQuantity));
        Assert.Equal((new DateTime(2026, 3, 1), 1234.5678901234567891m), (rows[1].StartDate, rows[1].StartQuantity));
        Assert.Equal(1, withStart);
    }

    [Fact]
    public async Task The_generated_writes_store_an_optional_complex_value_and_take_it_away_again()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;

        var created = await Tankyard.Create(new TankyardModel { Name = "Yard" }, cancellationToken: token);
        var opened = await Tankyard.Update(
            created.Id, new TankyardModel { Name = "Yard", Start = new() { Date = new DateTime(2026, 3, 1), Quantity = 50m } }, cancellationToken: token);
        var closed = await Tankyard.Update(created.Id, new TankyardModel { Name = "Yard" }, cancellationToken: token);
        var row = await Tankyard.Where(t => t.Id == created.Id).First(token);

        Assert.Null(created.Start);
        Assert.Equal(RigStart.Create(new DateTime(2026, 3, 1), RigQuantity.Create(50m)), opened.Start);
        Assert.Null(closed.Start);
        Assert.Equal((null, null), (row.StartDate, row.StartQuantity));
    }

    [Fact]
    public async Task A_value_object_held_inside_a_complex_value_shapes_its_column_as_it_does_on_its_own()
    {
        await using var database = await StartDatabaseAsync();

        var start = database.Context.Model.FindEntityType(typeof(Tankyard))!.FindComplexProperty(nameof(Tankyard.Start))!.ComplexType;
        var quantity = start.FindProperty(nameof(RigStart.Quantity))!;

        Assert.Equal((32, 16), (quantity.GetPrecision(), quantity.GetScale()));
    }

    // Written past the rules on purpose: a row stored before a rule was tightened. The table name comes from the
    // model, the values are parameters.
    private static async Task WritePastTheRulesAsync(TestDatabase database, Type entity, string set, params object[] values)
    {
        var table = database.Context.Model.FindEntityType(entity)!.GetTableName();
#pragma warning disable EF1002
        await database.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{table}\" SET {set}", values, TestContext.Current.CancellationToken);
#pragma warning restore EF1002
    }

    // The generated switch a form asks, reached the only way a file-local class can be: by its name.
    private static Func<string, Delegate?> RuleOfRigModel() => Generated<Func<string, Delegate?>>("Root_RuleOf");

    private static T Generated<T>(string name, Type? model = null)
        where T : Delegate
    {
        var holder = "__" + (model ?? typeof(RigModel)).Name + "Rules";
        var rules = typeof(RigModel).Assembly.GetTypes().Single(t => t.Name.EndsWith(holder, StringComparison.Ordinal));
        var method = rules.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return method.CreateDelegate<T>();
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
