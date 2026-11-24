using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     F-42 + F-16: every public method of a partial aggregate gets a static entry of the same name that runs it as one
///     write, and the roles its Configure names are asked by that entry.
/// </summary>
public class OperationGeneratorTests
{
    private const string Supply = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;

        namespace Shop
        {
            public static class Roles { public const string Admin = "Admin"; }

            public sealed partial class Supply : Aggregate<System.Guid, Versioned>
            {
                public string Note { get; private set; } = "";
                public int Opening { get; private set; }

                public void Change(string note, IReadOnlyList<int> parts) => Note = note + parts.Count;
                public Supply Rename(string note) { Note = note; return this; }
                public async Task Distribute(int count = 3) { await Task.Yield(); Opening = count; }
                public void SetOpening(int opening) => Opening = opening;
                public bool IsOpen() => Opening > 0;
                public static void Configure(EntityTypeBuilder<Supply> b) => b.Operation(s => s.SetOpening, roles: Roles.Admin);
            }
        }
        """;

    [Fact]
    public void Each_public_method_that_changes_the_aggregate_gets_an_entry_of_its_name_that_compiles()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static global::System.Threading.Tasks.Task Change(global::System.Guid id, int version, string note, global::System.Collections.Generic.IReadOnlyList<int> parts, global::System.Threading.CancellationToken cancellationToken = default) =>", generated, StringComparison.Ordinal);
        Assert.Contains("Run<global::Shop.Supply>(\"Change\", null, id!, version, aggregate => { aggregate.Change(note, parts); return global::System.Threading.Tasks.Task.CompletedTask; }, cancellationToken);", generated, StringComparison.Ordinal);
        Assert.Contains("aggregate => { aggregate.Rename(note); return", generated, StringComparison.Ordinal);
        Assert.Contains("Distribute(global::System.Guid id, int version, int count = (int)3, global::System.Threading.CancellationToken", generated, StringComparison.Ordinal);
        Assert.Contains("aggregate => aggregate.Distribute(count), cancellationToken);", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_that_answers_a_question_and_the_conventions_get_no_entry()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.DoesNotContain(" IsOpen(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(" Configure(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void The_roles_its_Configure_names_are_asked_by_the_entry()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.Contains("Run<global::Shop.Supply>(\"SetOpening\", \"Admin\", id!, version,", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_with_no_version_is_found_by_its_id_alone()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop
            {
                public sealed partial class Tag : Aggregate<int>
                {
                    public string Name { get; private set; } = "";
                    public void Rename(string name) => Name = name;
                }
            }
            """);

        var generated = run.GeneratedSource("Shop.Tag.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("Rename(int id, string name, global::System.Threading.CancellationToken cancellationToken = default)", generated, StringComparison.Ordinal);
        Assert.Contains("(\"Rename\", null, id!, null, aggregate =>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_is_not_partial_gets_no_entries()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop
            {
                public sealed class Tag : Aggregate<int>
                {
                    public string Name { get; private set; } = "";
                    public void Rename(string name) => Name = name;
                }
            }
            """);

        var generated = run.RunResult.Results.SelectMany(static r => r.GeneratedSources);

        Assert.DoesNotContain(generated, static s => s.HintName.Contains(".Operations", StringComparison.Ordinal));
    }

    [Fact]
    public void An_operation_whose_parameter_takes_the_entrys_name_is_RASK171_and_gets_no_entry()
    {
        var run = Run("""
            using Rask.Data;
            namespace Shop
            {
                public sealed partial class Tag : Aggregate<int, Versioned>
                {
                    public int Level { get; private set; }
                    public void Bump(int version) => Level = version;
                }
            }
            """);

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, static d => d.Id == "RASK171");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Tag.Bump' has a parameter named 'version'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.RunResult.Results.SelectMany(static r => r.GeneratedSources), static s => s.HintName.Contains(".Operations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Roles_that_are_not_a_constant_are_RASK172_and_no_entry_is_left_open()
    {
        var source = """
            private static readonly string Admins = "Admin";
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(t => t.Bump, roles: Admins);
            """;

        var run = Run(Tagged(source));
        var diagnostics = await Analyze(Tagged(source), new UnreadConfigurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "RASK172");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("its roles are not a constant", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource(".Operations"));
    }

    [Fact]
    public async Task Roles_named_outside_the_aggregates_Configure_are_RASK172()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public static void Configure(EntityTypeBuilder<Tag> b) => Lock(b);
            private static void Lock(EntityTypeBuilder<Tag> b) => b.Operation(t => t.Bump, roles: "Admin");
            """), new UnreadConfigurationAnalyzer());

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "RASK172");
        Assert.Contains("it is made outside that method", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Constant_roles_in_the_aggregates_Configure_are_not_RASK172()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(t => t.Bump, roles: "Admin");
            """), new UnreadConfigurationAnalyzer());

        Assert.DoesNotContain(diagnostics, static d => d.Id == "RASK172");
    }

    [Fact]
    public void A_non_public_factory_gets_a_public_entry_that_saves_it_and_hands_back_its_id()
    {
        var run = Run(Tagged("""
            public int Level { get; private set; }
            private static Tag Open(int level) => new() { Id = Guid.CreateVersion7(), Level = level };
            internal static async Task<Tag> Later(int level) { await Task.Yield(); return Open(level); }
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(nameof(Open), roles: "Admin");
            """));

        var generated = run.GeneratedSource("Shop.Tag.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static global::System.Threading.Tasks.Task<global::System.Guid> Open(int level, global::System.Threading.CancellationToken cancellationToken = default) =>", generated, StringComparison.Ordinal);
        Assert.Contains("Build<global::Shop.Tag, global::System.Guid>(\"Open\", \"Admin\", () => global::System.Threading.Tasks.Task.FromResult(Open(level)), cancellationToken);", generated, StringComparison.Ordinal);
        Assert.Contains("Build<global::Shop.Tag, global::System.Guid>(\"Later\", null, () => Later(level), cancellationToken);", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_operation_and_factory_gets_a_hidden_dry_run_that_compiles()
    {
        var run = Run(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            private static Tag Open(int level) => new() { Id = Guid.CreateVersion7(), Level = level };
            """));

        var generated = run.GeneratedSource("Shop.Tag.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("DryRunBump(global::System.Guid id, int version, int level, global::System.Threading.CancellationToken cancellationToken = default) =>", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedOperations.Try<global::Shop.Tag>(id!, version, aggregate => { aggregate.Bump(level); return", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedOperations.TryBuild<global::Shop.Tag>(() => global::System.Threading.Tasks.Task.FromResult(Open(level)), cancellationToken);", generated, StringComparison.Ordinal);
        Assert.Contains("EditorBrowsableState.Never", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_public_factory_is_RASK173_and_gets_no_entry()
    {
        var run = Run(Tagged("""
            public int Level { get; private set; }
            public static Tag Open(int level) => new() { Id = Guid.CreateVersion7(), Level = level };
            """));

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, static d => d.Id == "RASK173");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Tag.Open' is a public factory", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource(".Operations"));
    }

    [Fact]
    public void Each_operation_says_whether_the_signed_in_principal_may_run_it()
    {
        var run = Run(Supply);

        var generated = run.GeneratedSource("Shop.Supply.Operations");

        Assert.Contains("public static bool MaySetOpening => global::Rask.Data.GeneratedOperations.Admits(\"Admin\");", generated, StringComparison.Ordinal);
        Assert.Contains("public static bool MayChange => true;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_factory_named_by_nameof_in_its_Configure_is_not_RASK172()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            private static Tag Open(int level) => new() { Id = Guid.CreateVersion7(), Level = level };
            public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(nameof(Open), roles: "Admin");
            """), new UnreadConfigurationAnalyzer());

        Assert.DoesNotContain(diagnostics, static d => d.Id == "RASK172");
    }

    private const string Driver = """
        using System;
        using System.Threading.Tasks;
        using Rask.Data;
        using Rask.Values;

        namespace Shop
        {
            public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 40);
            public sealed partial record Nickname(string Value) : Text(Value, MaxLength: 10);

            public sealed partial class Driver : Aggregate<Guid, Versioned>
            {
                public DriverName Name { get; private set; } = null!;
                public Nickname? Nick { get; private set; }
                public int Seats { get; private set; }

                public void Change(DriverName name, Nickname? nick, int seats) => (Name, Nick, Seats) = (name, nick, seats);
                private static Driver Hire(DriverName name) => new() { Id = Guid.CreateVersion7(), Name = name };
            }
        }
        """;

    [Fact]
    public void An_operation_gets_a_form_over_its_parameters_that_compiles()
    {
        var run = RunWithForms(Driver);

        var forms = run.GeneratedSource("Shop.Driver.OperationForms");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public sealed class DriverChangeInput", forms, StringComparison.Ordinal);
        Assert.Contains("public string? Name { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("public int Seats { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("For(this global::Rask.Core.RaskSeed_Form form, global::System.Func<global::System.Guid, int, global::Shop.DriverName, global::Shop.Nickname?, int, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task> operation, global::System.Guid id)", forms, StringComparison.Ordinal);
        Assert.Contains("Name = aggregate.Name.Value, Nick = aggregate.Nick?.Value, Seats = aggregate.Seats", forms, StringComparison.Ordinal);
        Assert.Contains("\"Name\" => global::Shop.DriverName.__Field,", forms, StringComparison.Ordinal);
        Assert.Contains("property is \"Name\";", forms, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Nickname.CreateOptional(input.Nick, \"Nick\")", forms, StringComparison.Ordinal);
        Assert.Contains("DryRunChange(input.Id, input.Version, name, nick, seats, cancellationToken)", forms, StringComparison.Ordinal);
        Assert.Contains("static () => global::Shop.Driver.MayChange", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void A_factory_gets_a_form_that_starts_empty_and_saves_through_its_entry()
    {
        var run = RunWithForms(Driver);

        var forms = run.GeneratedSource("Shop.Driver.OperationForms");

        Assert.Contains("For(this global::Rask.Core.RaskSeed_Form form, global::System.Func<global::Shop.DriverName, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<global::System.Guid>> operation)", forms, StringComparison.Ordinal);
        Assert.Contains("Task.FromResult<DriverHireInput?>(new DriverHireInput())", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_operations_that_take_the_same_parameters_get_no_form_and_RASK174_says_why()
    {
        var run = RunWithForms(Tagged("""
            public int Level { get; private set; }
            public int Floor { get; private set; }
            public void Bump(int level) => Level = level;
            public void Lower(int floor) => Floor = floor;
            """));

        Assert.Equal(2, run.RunResult.Diagnostics.Count(static d => d.Id == "RASK174"));
        Assert.False(run.HasGeneratedSource(".OperationForms"));
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_partial_aggregate_that_can_be_deleted_gets_a_delete_entry_its_dry_run_and_who_may()
    {
        var run = Run("""
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop
            {
                public sealed partial class Tag : Aggregate<System.Guid, Versioned, Deletable>
                {
                    public string Name { get; private set; } = "";
                    public static void Configure(EntityTypeBuilder<Tag> b) => b.Operation(nameof(Delete), roles: "Admin");
                }
            }
            """);

        var generated = run.GeneratedSource("Shop.Tag.Operations");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static global::System.Threading.Tasks.Task Delete(global::System.Guid id, int version, global::System.Threading.CancellationToken cancellationToken = default) =>", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedOperations.Remove<global::Shop.Tag>(\"Delete\", \"Admin\", id!, version, cancellationToken);", generated, StringComparison.Ordinal);
        Assert.Contains("GeneratedOperations.TryRemove<global::Shop.Tag>(id!, version, cancellationToken);", generated, StringComparison.Ordinal);
        Assert.Contains("public static bool MayDelete => global::Rask.Data.GeneratedOperations.Admits(\"Admin\");", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregate_that_is_never_deleted_gets_no_delete_entry()
    {
        var run = Run(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            """));

        var generated = run.GeneratedSource("Shop.Tag.Operations");

        Assert.DoesNotContain(" Delete(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("MayDelete", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_change_parameter_starts_empty_keeps_when_empty_and_a_nullable_one_can_be_cleared()
    {
        var run = RunWithForms("""
            using System;
            using Rask.Data;
            using Rask.Values;

            namespace Shop
            {
                public sealed partial record Nickname(string Value) : Text(Value, MaxLength: 10);
                public sealed partial record Secret(string Value) : Password(Value, MinLength: 8);

                public sealed partial class Driver : Aggregate<Guid, Versioned>
                {
                    public Nickname? Nick { get; private set; }
                    public Secret Code { get; private set; } = null!;

                    public void Change(Change<Nickname?> nick, Change<Secret> code) => (Nick, Code) = (nick.Applied(Nick), code.Applied(Code)!);
                }
            }
            """);

        var forms = run.GeneratedSource("Shop.Driver.OperationForms");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public string? Nick { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("public bool NickCleared { get; set; }", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("CodeCleared", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("Nick = aggregate", forms, StringComparison.Ordinal);
        Assert.DoesNotContain("Code = aggregate", forms, StringComparison.Ordinal);
        Assert.Contains("RegisterClearing(typeof(DriverChangeInput), static property => property switch { \"Nick\" => \"NickCleared\", _ => null });", forms, StringComparison.Ordinal);
        Assert.Contains("!string.IsNullOrWhiteSpace(input.Nick) ? global::Rask.Values.Change<global::Shop.Nickname?>.Set(", forms, StringComparison.Ordinal);
        Assert.Contains(": input.NickCleared ? global::Rask.Values.Change<global::Shop.Nickname?>.Clear : global::Rask.Values.Change<global::Shop.Nickname?>.Keep", forms, StringComparison.Ordinal);
        Assert.Contains(": global::Rask.Values.Change<global::Shop.Secret>.Keep", forms, StringComparison.Ordinal);
    }

    private const string Cargo = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        using Rask.Values;

        namespace Shop
        {
            public sealed partial record Tonnage(decimal Value) : Number(Value, Min: 0.1m, Scale: 2);
            public sealed partial record Hold(string Value) : Text(Value, MaxLength: 8);
            public sealed record CargoPartSpec(Guid? Id, Hold Hold, Tonnage Tonnes, int Crates, bool Fragile);

            public sealed partial class Cargo : Aggregate<Guid, Versioned>
            {
                public Children<CargoPart, CargoPartSpec> Parts { get; } = new();
                public void Load(IReadOnlyList<CargoPartSpec> parts) => Parts.Sync(parts, s => s.Id, CargoPart.For, (p, s) => p.Take(s));
            }

            public sealed class CargoPart : Entity<Guid>
            {
                public Hold Hold { get; private set; } = null!;
                public decimal Tonnes { get; private set; }
                public int Crates { get; private set; }
                internal static CargoPart For(CargoPartSpec spec) { var part = new CargoPart { Id = Guid.CreateVersion7() }; part.Take(spec); return part; }
                internal void Take(CargoPartSpec spec) => (Hold, Tonnes, Crates) = (spec.Hold, spec.Tonnes.Value, spec.Crates);
            }
        }
        """;

    [Fact]
    public void A_list_of_rows_is_an_editable_list_filled_from_the_children_each_row_made_under_its_place()
    {
        var run = RunWithForms(Cargo);

        var forms = run.GeneratedSource("Shop.Cargo.OperationForms");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public global::System.Collections.Generic.List<PartsRow> Parts { get; set; } = [];", forms, StringComparison.Ordinal);
        Assert.Contains("public sealed class PartsRow", forms, StringComparison.Ordinal);
        Assert.Contains("public global::System.Guid? Id { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("public int? Crates { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("public bool Fragile { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("Parts = [.. global::System.Linq.Enumerable.Select(aggregate.Parts, static __PartsChild => new CargoLoadInput.PartsRow { Id = __PartsChild.Id, Hold = __PartsChild.Hold.Value, Tonnes = __PartsChild.Tonnes, Crates = __PartsChild.Crates })]", forms, StringComparison.Ordinal);
        Assert.Contains("parts = [.. LoadPartsMade(input.Parts, \"Parts\", failures)];", forms, StringComparison.Ordinal);
        Assert.Contains("global::Shop.Tonnage.Create(__given, __at + \".Tonnes\")", forms, StringComparison.Ordinal);
        Assert.Contains("__made.Add(new global::Shop.CargoPartSpec(Id: __m_Id, Hold: __m_Hold, Tonnes: __m_Tonnes, Crates: __m_Crates, Fragile: __m_Fragile));", forms, StringComparison.Ordinal);
        Assert.Contains("RegisterFieldShapes(typeof(CargoLoadInput.PartsRow), LoadPartsShapeOf);", forms, StringComparison.Ordinal);
        Assert.Contains("property is \"Hold\" or \"Tonnes\" or \"Crates\";", forms, StringComparison.Ordinal);
        Assert.Contains("\"Crates\" => global::Rask.Core.Forms.StatedRules.Shape(typeof(global::Shop.CargoPart), \"Crates\", new global::Rask.Core.Forms.FieldShape(global::Rask.Core.Forms.FieldKind.Whole)),", forms, StringComparison.Ordinal);
        Assert.Contains("DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(CargoLoadInput.PartsRow))", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_whose_rows_hold_a_member_no_field_holds_gets_no_form()
    {
        var run = RunWithForms(Tagged("""
            public sealed record Pair(int A, int B);
            public sealed record TagSpec(Guid? Id, Pair Pair);
            public void Retag(System.Collections.Generic.IReadOnlyList<TagSpec> tags) { }
            """));

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.False(run.HasGeneratedSource(".OperationForms"));
    }

    private const string Depot = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        using Rask.Values;

        namespace Shop
        {
            public sealed partial record KmRange(int Start, int End) : Rask.Values.Range(Start, End, Strict: true, Min: 0);

            public sealed partial record Opening(DateOnly? Date, decimal? Quantity) : Value
            {
                protected override IEnumerable<Problem> Rules()
                {
                    if (Quantity < 0)
                    {
                        yield return Problem.At(Quantity, "An opening is never below zero.");
                    }
                }
            }

            public sealed partial class Depot : Aggregate<Guid, Versioned>
            {
                public KmRange Km { get; private set; } = null!;
                public Opening? Opening { get; private set; }
                public void Reach(KmRange km) => Km = km;
                public void Open(Opening? opening) => Opening = opening;
            }
        }
        """;

    [Fact]
    public void A_value_object_of_several_values_is_a_group_of_fields_made_through_its_create()
    {
        var run = RunWithForms(Depot);

        var forms = run.GeneratedSource("Shop.Depot.OperationForms");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public KmFields Km { get; set; } = new();", forms, StringComparison.Ordinal);
        Assert.Contains("public int? Start { get; set; }", forms, StringComparison.Ordinal);
        Assert.Contains("Km = aggregate.Km is { } __Km ? new DepotReachInput.KmFields { Start = __Km.Start, End = __Km.End } : new()", forms, StringComparison.Ordinal);
        Assert.Contains("km = ReachKmMade(input.Km, \"Km\", failures)!;", forms, StringComparison.Ordinal);
        Assert.Contains("global::Shop.KmRange.Create(__m_Start, __m_End, at)", forms, StringComparison.Ordinal);
        Assert.Contains("\"Km\" => global::Shop.KmRange.__Field,", forms, StringComparison.Ordinal);
        Assert.Contains("\"Start\" => __EndOf(global::Shop.KmRange.__Field),", forms, StringComparison.Ordinal);
        Assert.Contains("property is \"Start\" or \"End\";", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_that_may_be_absent_is_nothing_when_its_group_is_left_empty()
    {
        var run = RunWithForms(Depot);

        var forms = run.GeneratedSource("Shop.Depot.OperationForms");

        Assert.Contains("opening = OpenOpeningMade(input.Opening, \"Opening\", failures);", forms, StringComparison.Ordinal);
        Assert.Contains("if (given.Date is null && given.Quantity is null)", forms, StringComparison.Ordinal);
        Assert.Contains("RegisterOptionalFields(typeof(DepotOpenInput), static property => property is \"Opening\");", forms, StringComparison.Ordinal);
        Assert.Contains("\"Quantity\" => global::Rask.Core.Forms.StatedRules.Shape(typeof(global::Shop.Opening), \"Quantity\", new global::Rask.Core.Forms.FieldShape(global::Rask.Core.Forms.FieldKind.Number)),", forms, StringComparison.Ordinal);
    }

    [Fact]
    public void A_password_is_never_read_from_the_aggregate_into_its_form()
    {
        var run = RunWithForms("""
            using System;
            using Rask.Data;
            using Rask.Values;

            namespace Shop
            {
                public sealed partial record Secret(string Value) : Password(Value, MinLength: 8);

                public sealed partial class Driver : Aggregate<Guid, Versioned>
                {
                    public Secret Code { get; private set; } = null!;
                    public void Recode(Secret code) => Code = code;
                }
            }
            """);

        var forms = run.GeneratedSource("Shop.Driver.OperationForms");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("Code = aggregate", forms, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bare_call_inside_the_aggregate_that_lands_on_the_entry_is_RASK170()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public Task Again(Guid other) => Bump(other, 0, 4);
            """), new OperationEntryCallAnalyzer());

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "RASK170");

        Assert.Equal("Bump(other, 0, 4)", diagnostic.Flagged());
        Assert.Contains("'Bump(…)' inside 'Tag' calls the generated entry 'Tag.Bump(id, …)'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_entry_named_by_its_type_and_the_method_on_an_instance_are_not_RASK170()
    {
        var diagnostics = await Analyze(Tagged("""
            public int Level { get; private set; }
            public void Bump(int level) => Level = level;
            public Task Other(Guid other) => Tag.Bump(other, 0, 4);
            public void Twice(int level) { Bump(level); this.Bump(level); }
            """), new OperationEntryCallAnalyzer());

        Assert.DoesNotContain(diagnostics, static d => d.Id == "RASK170");
    }

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new OperationGenerator()],
            "Rask.Data", "Rask.Wire", "Rask.Cqrs", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational");

    private static GeneratorRun RunWithForms(string source) =>
        GeneratorHarness.Run(
            source,
            [new OperationGenerator(), new ValueObjectFactoryGenerator()],
            "Rask.Data", "Rask.Wire", "Rask.Cqrs", "Rask.Core", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore.Relational");

    // A partial, versioned Tag around the members given.
    private static string Tagged(string members) => $$"""
        using System;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;
        using Rask.Data;
        namespace Shop
        {
            public sealed partial class Tag : Aggregate<Guid, Versioned>
            {
        {{members}}
            }
        }
        """;

    // The analyzer runs over the snippet AND the entries the generator writes for it, as a build does.
    private static async Task<IReadOnlyList<Diagnostic>> Analyze(string source, DiagnosticAnalyzer analyzer)
    {
        var run = Run(source);

        Assert.Empty(run.GeneratedCompileErrors());
        var generated = run.RunResult.Results
            .SelectMany(static r => r.GeneratedSources)
            .Select(static g => CSharpSyntaxTree.ParseText(g.SourceText, new CSharpParseOptions(LanguageVersion.Latest), path: g.HintName));
        return await run.Compilation.AddSyntaxTrees(generated)
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync();
    }
}
