using System.Xml.Linq;

namespace Rask.Workbooks.Tests;

public sealed class XlsxWriteTests
{
    private static readonly XNamespace M = OpenedWorkbook.Main;

    private static IReadOnlyList<object?> Row(params object?[] cells) => cells;

    private static byte[] Invoices(params IReadOnlyList<object?>[] rows) =>
        Xlsx.Write("Invoices", ["Number", "Day", "Amount"], rows);

    [Fact]
    public void A_workbook_holds_the_parts_Excel_expects_all_well_formed()
    {
        var file = Invoices(Row("A-1", new Xlsx.Day(new DateTime(2026, 10, 14)), 1250.5m));

        var book = new OpenedWorkbook(file);

        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/workbook.xml", "xl/styles.xml", "xl/worksheets/sheet1.xml"],
            book.PartNames);
        Assert.Equal("Invoices", (string?)book.Part("xl/workbook.xml").Descendants(M + "sheet").Single().Attribute("name"));
        Assert.Equal("1", (string?)book.Part("xl/workbook.xml").Descendants(M + "calcPr").Single().Attribute("fullCalcOnLoad"));
    }

    [Fact]
    public void The_sheet_xml_is_byte_for_byte_what_the_first_version_wrote()
    {
        var file = Invoices(Row("A-1", new Xlsx.Day(new DateTime(2026, 10, 14)), 1250.50m));

        var sheet = new OpenedWorkbook(file).Sheet(1).ToString(SaveOptions.DisableFormatting);

        Assert.Equal(
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetPr><outlinePr summaryBelow=\"0\" /></sheetPr>"
            + "<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\" /></sheetView></sheetViews>"
            + "<sheetFormatPr defaultRowHeight=\"15\" outlineLevelRow=\"1\" />"
            + "<cols><col min=\"1\" max=\"1\" width=\"9\" customWidth=\"1\" /><col min=\"2\" max=\"2\" width=\"12\" customWidth=\"1\" /><col min=\"3\" max=\"3\" width=\"10\" customWidth=\"1\" /></cols>"
            + "<sheetData><row r=\"1\"><c r=\"A1\" s=\"3\" t=\"inlineStr\"><is><t xml:space=\"preserve\">Number</t></is></c>"
            + "<c r=\"B1\" s=\"3\" t=\"inlineStr\"><is><t xml:space=\"preserve\">Day</t></is></c>"
            + "<c r=\"C1\" s=\"3\" t=\"inlineStr\"><is><t xml:space=\"preserve\">Amount</t></is></c></row>"
            + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t xml:space=\"preserve\">A-1</t></is></c><c r=\"B2\" s=\"2\"><v>46309</v></c><c r=\"C2\" s=\"4\"><v>1250.5</v></c></row>"
            + "</sheetData><autoFilter ref=\"A1:C2\" /></worksheet>",
            sheet);
    }

    [Fact]
    public void Headings_are_bold_and_frozen_with_a_filter_over_every_column()
    {
        var book = new OpenedWorkbook(Invoices(Row("A-1", null, 1m), Row("A-2", null, 2m)));

        var pane = book.Sheet(1).Descendants(M + "pane").Single();

        Assert.Equal("frozen", (string?)pane.Attribute("state"));
        Assert.Equal("3", (string?)book.Cell(1, "A1").Attribute("s"));
        Assert.Equal("A1:C3", (string?)book.Sheet(1).Descendants(M + "autoFilter").Single().Attribute("ref"));
        Assert.Contains("<b/>", book.Part("xl/styles.xml").ToString(SaveOptions.DisableFormatting).Replace("<b />", "<b/>", StringComparison.Ordinal));
    }

    [Fact]
    public void Columns_are_as_wide_as_the_longest_cell_and_never_past_eighty()
    {
        var book = new OpenedWorkbook(Xlsx.Write("Wide", ["A", "B"], [Row("short", new string('x', 500))]));

        var widths = book.Sheet(1).Descendants(M + "col").Select(c => (int)c.Attribute("width")!).ToArray();

        Assert.Equal([7, 80], widths);
    }

    [Fact]
    public void A_folded_row_is_hidden_at_outline_level_one_under_the_row_that_folds_it()
    {
        var book = new OpenedWorkbook(Invoices(
            Row("Total", null, 30m),
            new Xlsx.Folded(Row("A-1", null, 10m)),
            new Xlsx.Folded(Row("A-2", null, 20m))));

        var rows = book.Rows(1).ToList();

        Assert.Equal("1", (string?)rows[1].Attribute("collapsed"));
        Assert.Null(rows[1].Attribute("outlineLevel"));
        Assert.Equal(["1", "1"], rows.Skip(2).Select(r => (string?)r.Attribute("outlineLevel")));
        Assert.Equal(["1", "1"], rows.Skip(2).Select(r => (string?)r.Attribute("hidden")));
    }

    [Fact]
    public void The_total_row_sums_with_SUBTOTAL_over_the_amounts_above_it()
    {
        var file = Xlsx.Write("Invoices", ["Number", "Amount"], [Row("A-1", 10.25m), Row("A-2", 20m)], total: [new Xlsx.Strong("Total"), new Xlsx.Sum()]);

        var cell = new OpenedWorkbook(file).Cell(1, "B4");

        Assert.Equal("SUBTOTAL(9,B2:B3)", cell.Element(M + "f")!.Value);
        Assert.Equal("30.25", cell.Element(M + "v")!.Value);
    }

    [Fact]
    public void Text_is_escaped_and_control_characters_are_written_as_Excels_own_escapes()
    {
        var book = new OpenedWorkbook(Xlsx.Write("T", ["H&<>"], [Row("a & b < c > d \"q\" 'r' \u0001 \u001f ok"), Row("_x0041_ literal")]));

        var heading = book.Text(1, "A1");
        var body = book.Text(1, "A2");
        var literal = book.Text(1, "A3");

        Assert.Equal("H&<>", heading);
        Assert.Equal("a & b < c > d \"q\" 'r' _x0001_ _x001F_ ok", body);
        Assert.Equal("_x005F_x0041_ literal", literal);
    }

    [Fact]
    public void Text_that_looks_like_a_formula_is_written_as_a_string_never_a_formula()
    {
        var book = new OpenedWorkbook(Xlsx.Write("T", ["H"], [Row("=1+1"), Row("+SUM(A1)"), Row("-2+3"), Row("@cmd"), Row("\tTab"), Row("\rCR")]));

        var cells = book.Sheet(1).Descendants(M + "c").Skip(1).ToList();

        Assert.All(cells, c => Assert.Equal("inlineStr", (string?)c.Attribute("t")));
        Assert.Empty(book.Sheet(1).Descendants(M + "f"));
    }

    [Fact]
    public void Empty_cells_are_left_out_and_a_sheet_with_no_rows_is_just_its_headings()
    {
        var book = new OpenedWorkbook(Xlsx.Write("E", ["A", "B"], [Row(null, "")]));
        var bare = new OpenedWorkbook(Xlsx.Write("E", ["A", "B"], []));

        Assert.Empty(book.Rows(1).Last().Elements());
        Assert.Single(bare.Rows(1));
        Assert.Equal("A1:B1", (string?)bare.Sheet(1).Descendants(M + "autoFilter").Single().Attribute("ref"));
    }

    [Fact]
    public void A_raw_DateTime_DateOnly_and_DateTimeOffset_are_written_as_days_and_moments()
    {
        var file = Xlsx.Write("D", ["A", "B", "C", "D"], [Row(new DateTime(2026, 10, 14), new DateTime(2026, 10, 14, 12, 0, 0), new DateOnly(2026, 10, 14), new DateTimeOffset(2026, 10, 14, 6, 0, 0, TimeSpan.FromHours(2)))]);

        var book = new OpenedWorkbook(file);

        Assert.Equal(("2", "46309"), (S(book, "A2"), V(book, "A2")));
        Assert.Equal(("1", "46309.5"), (S(book, "B2"), V(book, "B2")));
        Assert.Equal(("2", "46309"), (S(book, "C2"), V(book, "C2")));
        Assert.Equal(("1", "46309.25"), (S(book, "D2"), V(book, "D2")));

        static string S(OpenedWorkbook b, string at) => (string)b.Cell(1, at).Attribute("s")!;
        static string V(OpenedWorkbook b, string at) => b.Cell(1, at).Element(M + "v")!.Value;
    }

    [Fact]
    public void A_cell_of_a_kind_Excel_cannot_hold_is_refused_by_name()
    {
        var guid = Assert.Throws<ArgumentException>(() => Xlsx.Write("G", ["A"], [Row(Guid.NewGuid())]));
        var nan = Assert.Throws<ArgumentException>(() => Xlsx.Write("G", ["A"], [Row(double.NaN)]));

        Assert.Contains("Guid", guid.Message, StringComparison.Ordinal);
        Assert.Contains("NaN", nan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Numbers_and_booleans_use_the_invariant_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("hu-HU");
        try
        {
            var book = new OpenedWorkbook(Xlsx.Write("N", ["A", "B", "C", "D"], [Row(1.5, 2.25m, 7, true)]));

            Assert.Equal(["1.5", "2.25", "7", "1"], new[] { "A2", "B2", "C2", "D2" }.Select(at => book.Cell(1, at).Element(M + "v")!.Value));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void More_than_twenty_six_columns_get_Excels_letters()
    {
        var headings = Enumerable.Range(0, 28).Select(i => $"H{i}").ToList();

        var book = new OpenedWorkbook(Xlsx.Write("Many", headings, [Row(Enumerable.Repeat<object?>("x", 28).ToArray())]));

        Assert.Equal("AB2", (string?)book.Sheet(1).Descendants(M + "c").Last().Attribute("r"));
        Assert.Equal("A1:AB2", (string?)book.Sheet(1).Descendants(M + "autoFilter").Single().Attribute("ref"));
    }

    [Fact]
    public void Many_sheets_keep_their_order_and_their_own_rows()
    {
        var sheets = Enumerable.Range(1, 12).Select(i => new Xlsx.Sheet($"S{i}", ["H"], [Row($"row{i}")])).ToArray();

        var book = new OpenedWorkbook(Xlsx.Write(sheets));

        Assert.Equal(sheets.Select(s => s.Name), book.Part("xl/workbook.xml").Descendants(M + "sheet").Select(s => (string)s.Attribute("name")!));
        Assert.Equal("row12", book.Text(12, "A2"));
        Assert.Equal(12, book.Part("[Content_Types].xml").Descendants().Count(e => ((string?)e.Attribute("ContentType"))?.EndsWith("worksheet+xml", StringComparison.Ordinal) == true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Thirty-two characters long name!!")]
    [InlineData("a[b")]
    [InlineData("a]b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("'quoted")]
    [InlineData("quoted'")]
    public void A_sheet_name_Excel_would_reject_is_refused_before_anything_is_written(string name)
    {
        Assert.Throws<ArgumentException>(() => Xlsx.Write(name, ["H"], []));
    }

    [Fact]
    public void A_sheet_name_of_thirty_one_characters_and_two_names_differing_by_case()
    {
        var ok = Xlsx.Write(new string('n', 31), ["H"], []);

        Assert.NotEmpty(ok);
        Assert.Throws<ArgumentException>(() => Xlsx.Write(new Xlsx.Sheet("Same", ["H"], []), new Xlsx.Sheet("SAME", ["H"], [])));
    }

    [Fact]
    public void A_hundred_thousand_rows_write_a_workbook_that_still_reads_back()
    {
        var rows = Enumerable.Range(0, 100_000).Select(i => Row($"Invoice {i}", new DateTime(2026, 1, 1).AddMinutes(i), i * 1.25m));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var file = Xlsx.Write("Big", ["Number", "At", "Amount"], rows, [new Xlsx.Strong("Total"), null, new Xlsx.Sum()]);

        clock.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var book = new OpenedWorkbook(file);
        Assert.Equal(100_002, book.Rows(1).Count());
        Assert.Equal("SUBTOTAL(9,C2:C100001)", book.Cell(1, "C100002").Element(M + "f")!.Value);
        TestContext.Current.SendDiagnosticMessage($"100000 rows: {clock.ElapsedMilliseconds} ms, {allocated:N0} bytes allocated, {file.Length:N0} bytes file");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
        Assert.True(allocated < 1_500_000_000, $"allocated {allocated:N0} bytes");
    }
}
