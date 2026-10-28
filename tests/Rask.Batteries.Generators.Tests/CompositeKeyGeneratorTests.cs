using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A key of several columns, declared as a tuple of TYPES — <c>Entity&lt;(DeliveryNote, Transaction)&gt;</c> — read
/// the generated half, the read face, the form model's lists of ids and of rows, and the refusals (RASK106).
/// </summary>
public class CompositeKeyGeneratorTests
{
    private const string Aggregates = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Fuel;

        public sealed class DeliveryNote : Aggregate<int, PerTenant>
        {
            public string Number { get; private set; } = "";
            public IReadOnlyList<DeliveryNoteTransaction> Fuellings { get; private set; } = [];
        }

        public sealed class Transaction : Aggregate<int, PerTenant>
        {
            public int ProductId { get; private set; }
        }

        """;

    private const string Fuelling = Aggregates + """
        public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
        {
            public decimal Quantity { get; private set; }
        }
        """;

    // A report holding columns keyed by the report and a column number, each holding totals keyed under it.
    private const string Report = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Reports;

        public sealed class Report : Aggregate<int, PerTenant<int>>
        {
            public IReadOnlyList<ReportColumn> Layout { get; private set; } = [];
        }

        public sealed partial class ReportColumn : Entity<(Report, int ColumnId)>
        {
            public int ColumnId { get; private set; }
            public IReadOnlyList<ReportGroupTotal> Totals { get; private set; } = [];
        }

        public sealed partial class ReportGroupTotal : Entity<(ReportColumn, int TotalColumnId)>
        {
            public int TotalColumnId { get; private set; }
        }
        """;

    // The same note with a form: its fuellings, which carry a quantity of their own, are a list of rows on it.
    private static readonly string FuellingOnAForm = Fuelling.Replace(
        "class DeliveryNote : Aggregate<int, PerTenant>", "class DeliveryNote : Aggregate<int, Model, PerTenant>", StringComparison.Ordinal);

    // A company of an existing app linked to the activities it does: a pure link table, carried as their ids.
    private const string Activities = """
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Companies;

        public sealed class Company : Aggregate<int, Model, PerTenant<int>>
        {
            private readonly List<CompanyActivity> _activities = [];
            public string Name { get; private set; } = "";
            public IReadOnlyList<CompanyActivity> Activities => _activities;
        }

        public sealed class Teaor : Aggregate<int>
        {
            public string Code { get; private set; } = "";
        }

        public sealed partial class CompanyActivity : Entity<(Company, Teaor)>
        {
        }
        """;

    // The same aggregates, holding lines keyed by the note and a number instead.
    private static readonly string Lines = Aggregates.Replace("DeliveryNoteTransaction", "DeliveryNoteLine", StringComparison.Ordinal);

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new CompositeKeyGenerator(), new ModelInputGenerator(), new ModelRegistryGenerator(), new ReadModelGenerator()],
            "Rask.Data",
            "Rask.Cqrs",
            "Microsoft.EntityFrameworkCore");

    private static IEnumerable<string> Rask106(GeneratorRun run) =>
        run.Diagnostics.Where(static d => d.Id == "RASK106").Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Each_aggregate_the_key_names_is_a_generated_id_of_that_aggregates_key_type()
    {
        var run = Run(Fuelling);

        var key = run.GeneratedSource("Fuel.DeliveryNoteTransaction.Key");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("partial class DeliveryNoteTransaction", key, StringComparison.Ordinal);
        Assert.Contains("public int DeliveryNoteId { get; private set; }", key, StringComparison.Ordinal);
        Assert.Contains("public int TransactionId { get; private set; }", key, StringComparison.Ordinal);
    }

    [Fact]
    public void The_id_is_the_pair_of_key_values_in_the_order_the_tuple_names_them()
    {
        var key = Run(Fuelling).GeneratedSource("Fuel.DeliveryNoteTransaction.Key");

        Assert.Contains(
            "public new (int DeliveryNoteId, int TransactionId) Id => (DeliveryNoteId, TransactionId);",
            key,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_read_face_lists_the_key_parts_instead_of_an_id_and_navigates_to_both_aggregates()
    {
        var run = Run(Fuelling);

        var face = run.GeneratedSource("Fuel_DeliveryNoteTransactionRead");

        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASK089");
        Assert.Contains("public int DeliveryNoteId { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public int TransactionId { get; init; }", face, StringComparison.Ordinal);
        Assert.DoesNotContain(" Id { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public global::Fuel.TransactionRead Transaction { get; init; } = null!;", face, StringComparison.Ordinal);
        Assert.Contains("public global::Fuel.DeliveryNoteRead DeliveryNote { get; init; } = null!;", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_value_is_a_part_the_entity_declares_and_the_face_lists_once()
    {
        var run = Run(Lines + """
            public sealed partial class DeliveryNoteLine : Entity<(DeliveryNote, int LineNo)>
            {
                public int LineNo { get; private set; }
                public decimal Quantity { get; private set; }
            }
            """);

        var key = run.GeneratedSource("Fuel.DeliveryNoteLine.Key");
        var face = run.GeneratedSource("Fuel_DeliveryNoteLineRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public new (int DeliveryNoteId, int LineNo) Id => (DeliveryNoteId, LineNo);", key, StringComparison.Ordinal);
        Assert.DoesNotContain("public int LineNo", key, StringComparison.Ordinal);
        Assert.Equal(2, face.Split("public int LineNo { get; init; }").Length);
    }

    [Fact]
    public void A_grandchild_keyed_under_its_column_gets_a_column_for_each_part_of_the_columns_key()
    {
        var run = Run(Report);

        var key = run.GeneratedSource("Reports.ReportGroupTotal.Key");
        var face = run.GeneratedSource("Reports_ReportGroupTotalRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(Rask106(run));
        Assert.Contains("public int ReportColumnReportId { get; private set; }", key, StringComparison.Ordinal);
        Assert.Contains("public int ReportColumnColumnId { get; private set; }", key, StringComparison.Ordinal);
        Assert.Contains(
            "public new (int ReportColumnReportId, int ReportColumnColumnId, int TotalColumnId) Id => "
            + "(ReportColumnReportId, ReportColumnColumnId, TotalColumnId);",
            key,
            StringComparison.Ordinal);
        Assert.Contains("public int ReportColumnReportId { get; init; }", face, StringComparison.Ordinal);
        Assert.Contains("public int ReportColumnColumnId { get; init; }", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_grandchild_under_a_child_keyed_by_one_column_gets_that_childs_id()
    {
        var run = Run(Fuelling + """
            public sealed class Load : Aggregate<int>
            {
                public IReadOnlyList<Leg> Legs { get; private set; } = [];
            }

            public sealed class Leg : Entity<Guid>
            {
                public IReadOnlyList<LegStop> Stops { get; private set; } = [];
            }

            public sealed partial class LegStop : Entity<(Leg, int StopNo)>
            {
                public int StopNo { get; private set; }
            }
            """);

        var key = run.GeneratedSource("Fuel.LegStop.Key");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public global::System.Guid LegId { get; private set; }", key, StringComparison.Ordinal);
        Assert.Contains("public new (global::System.Guid LegId, int StopNo) Id", key, StringComparison.Ordinal);
    }

    [Fact]
    public void An_entity_part_given_a_name_is_refused()
    {
        var run = Run(Report.Replace("Entity<(ReportColumn, int TotalColumnId)>", "Entity<(ReportColumn Column, int TotalColumnId)>", StringComparison.Ordinal));

        Assert.Contains(Rask106(run), static m => m.Contains("names an entity", StringComparison.Ordinal));
    }

    [Fact]
    public void A_row_keyed_by_several_columns_gets_no_form_model()
    {
        var run = Run(Fuelling);

        Assert.False(run.HasGeneratedSource("DeliveryNoteTransactionModel"));
        Assert.Empty(Rask106(run));
    }

    [Fact]
    public void A_class_that_is_not_partial_is_refused()
    {
        var run = Run(Fuelling.Replace("sealed partial class DeliveryNoteTransaction", "sealed class DeliveryNoteTransaction", StringComparison.Ordinal));

        var message = Assert.Single(Rask106(run));

        Assert.Contains("declare it 'partial'", message, StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource("DeliveryNoteTransaction.Key"));
    }

    [Fact]
    public void A_value_part_without_a_name_is_refused()
    {
        var run = Run(Lines + """
            public sealed partial class DeliveryNoteLine : Entity<(DeliveryNote, int)>
            {
            }
            """);

        Assert.Contains(Rask106(run), static m => m.Contains("has no name", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_part_the_entity_does_not_declare_is_refused()
    {
        var run = Run(Lines + """
            public sealed partial class DeliveryNoteLine : Entity<(DeliveryNote, int LineNo)>
            {
            }
            """);

        Assert.Contains(Rask106(run), static m => m.Contains("declares no property 'LineNo'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_part_declared_with_another_type_is_refused()
    {
        var run = Run(Lines + """
            public sealed partial class DeliveryNoteLine : Entity<(DeliveryNote, int LineNo)>
            {
                public long LineNo { get; private set; }
            }
            """);

        Assert.Contains(Rask106(run), static m => m.Contains("'LineNo' is 'long'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_property_already_holding_a_generated_name_is_refused()
    {
        var run = Run(Aggregates + """
            public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
            {
                public int TransactionId { get; private set; }
            }
            """);

        Assert.Contains(Rask106(run), static m => m.Contains("declares 'TransactionId'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_of_several_columns_on_an_aggregate_root_is_refused()
    {
        var run = Run(Aggregates + """
            public sealed partial class Pairing : Aggregate<(DeliveryNote, Transaction)>
            {
            }
            """);

        Assert.Contains(Rask106(run), static m => m.Contains("is an aggregate root", StringComparison.Ordinal));
    }

    [Fact]
    public void A_link_on_an_aggregates_form_is_a_list_of_the_other_parts_ids()
    {
        var run = Run(Activities);

        var model = run.GeneratedSource("Companies.CompanyModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(Rask106(run));
        Assert.Contains("public global::System.Collections.Generic.List<int> ActivityIds { get; set; } = [];", model, StringComparison.Ordinal);
        Assert.DoesNotContain(" Activities {", model, StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource("CompanyActivityModel"));
    }

    [Fact]
    public void A_link_is_written_as_a_set_and_read_back_as_its_ids()
    {
        var run = Run(Activities);

        var model = run.GeneratedSource("Companies.CompanyModel");

        Assert.Contains("if (!__new.Remove(__stored.TeaorId))", model, StringComparison.Ordinal);
        Assert.Contains("__Link_ActivityIds(__link, __id);", model, StringComparison.Ordinal);
        Assert.Contains("model.ActivityIds.Add(__link.TeaorId);", model, StringComparison.Ordinal);
        Assert.Contains("Name = \"set_TeaorId\"", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_with_fields_of_its_own_on_an_aggregates_form_is_a_list_of_rows_keyed_by_its_other_parts()
    {
        var run = Run(FuellingOnAForm);

        var note = run.GeneratedSource("Fuel.DeliveryNoteModel");
        var row = run.GeneratedSource("Fuel.DeliveryNoteTransactionModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(Rask106(run));
        Assert.Contains(
            "public global::System.Collections.Generic.List<global::Fuel.DeliveryNoteTransactionModel> Fuellings { get; set; } = [];",
            note,
            StringComparison.Ordinal);
        Assert.Contains("public int? TransactionId { get; set; }", row, StringComparison.Ordinal);
        Assert.Contains("public decimal? Quantity { get; set; }", row, StringComparison.Ordinal);
        Assert.DoesNotContain(" Id { get; set; }", row, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryNoteId", row, StringComparison.Ordinal);
    }

    [Fact]
    public void A_posted_row_is_matched_to_the_stored_one_by_its_key_and_a_key_given_twice_is_refused()
    {
        var run = Run(FuellingOnAForm);

        var note = run.GeneratedSource("Fuel.DeliveryNoteModel");

        Assert.Contains(
            "__row.TransactionId is { } __part0 && global::System.Collections.Generic.EqualityComparer<int>.Default.Equals(__part0, __stored.TransactionId)",
            note,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Fuellings\", global::Rask.Core.Globalization.RaskStrings.Get(global::Rask.Core.Globalization.RaskString.ValidationDuplicateRow, \"The same row is in this list twice.\")",
            note,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_library_with_no_strings_catalog_says_the_duplicate_row_in_english()
    {
        var run = GeneratorHarness.RunWithout(
            FuellingOnAForm, new ModelInputGenerator(), ["Rask.Core"], "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

        var note = run.GeneratedSource("Fuel.DeliveryNoteModel");

        Assert.Contains("\"Fuellings\", \"The same row is in this list twice.\");", note, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidationDuplicateRow", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_value_part_of_a_row_leads_its_model_and_is_required()
    {
        var run = Run(Lines.Replace("class DeliveryNote : Aggregate<int, PerTenant>", "class DeliveryNote : Aggregate<int, Model, PerTenant>", StringComparison.Ordinal)
                      .Replace("IReadOnlyList<DeliveryNoteLine> Fuellings", "IReadOnlyList<DeliveryNoteLine> Lines", StringComparison.Ordinal) + """
            public sealed partial class DeliveryNoteLine : Entity<(DeliveryNote, int LineNo)>
            {
                public int LineNo { get; private set; }
                public decimal Quantity { get; private set; }
            }
            """);

        var row = run.GeneratedSource("Fuel.DeliveryNoteLineModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Empty(Rask106(run));
        Assert.True(
            row.IndexOf("public int? LineNo { get; set; }", StringComparison.Ordinal)
            < row.IndexOf("public decimal? Quantity { get; set; }", StringComparison.Ordinal));
        Assert.Contains("\"LineNo\", __Rule_Root_LineNo(model.LineNo)", row, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_holding_rows_of_its_own_on_an_aggregates_form_is_still_refused()
    {
        var run = Run(Report.Replace("class Report : Aggregate<int, PerTenant<int>>", "class Report : Aggregate<int, Model, PerTenant<int>>", StringComparison.Ordinal));

        var message = Assert.Single(Rask106(run));

        Assert.Contains("'Report.Layout' holds it on a form", message, StringComparison.Ordinal);
        Assert.Contains("hold rows of their own ('Totals')", message, StringComparison.Ordinal);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(run.Diagnostics, static d => d.Id == "RASK106").Severity);
    }

    [Fact]
    public void A_row_with_no_constructor_a_form_can_add_with_is_still_refused()
    {
        var run = Run(FuellingOnAForm.Replace(
            "public decimal Quantity { get; private set; }",
            "public DeliveryNoteTransaction(decimal quantity) => Quantity = quantity;\n    public decimal Quantity { get; private set; }",
            StringComparison.Ordinal));

        var message = Assert.Single(Rask106(run));

        Assert.Contains("no constructor without parameters", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_on_an_aggregate_without_a_form_gets_no_list()
    {
        var run = Run(Activities.Replace("Aggregate<int, Model, PerTenant<int>>", "Aggregate<int, PerTenant<int>>", StringComparison.Ordinal));

        var company = run.GeneratedSource("Companies.CompanyModel");

        Assert.Empty(Rask106(run));
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("ActivityIds", company, StringComparison.Ordinal);
        Assert.False(run.HasGeneratedSource("CompanyActivityModel"));
    }

    [Theory]
    [InlineData("Activities", "ActivityIds")]
    [InlineData("Vehicles", "VehicleIds")]
    [InlineData("Teaors", "TeaorIds")]
    [InlineData("Addresses", "AddressIds")]
    [InlineData("Boxes", "BoxIds")]
    [InlineData("Branches", "BranchIds")]
    [InlineData("Statuses", "StatusIds")]
    [InlineData("Matches", "MatchIds")]
    [InlineData("Houses", "HouseIds")]
    [InlineData("Courses", "CourseIds")]
    [InlineData("Classes", "ClassIds")]
    [InlineData("Buses", "BusIds")]
    [InlineData("People", "PersonIds")]
    [InlineData("ContactPeople", "ContactPersonIds")]
    [InlineData("Children", "ChildIds")]
    [InlineData("Movies", "MovieIds")]
    [InlineData("Staff", "StaffIds")]
    [InlineData("Glass", "GlassIds")]
    public void The_list_of_ids_is_named_after_the_collection_made_singular(string collection, string ids)
    {
        var name = Rask.Generators.Shared.FormListName.Ids(collection);

        Assert.Equal(ids, name);
    }
}
