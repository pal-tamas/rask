using System.IO.Compression;
using System.Text;

namespace Rask.Workbooks.Tests;

public sealed class XlsxReadTests
{
    private static IReadOnlyList<object?> Row(params object?[] cells) => cells;

    // the ELEKÁFA SZ layout: a title on rows 1-2, headings merged over rows 3-4 with a group over three columns, data from row 5
    private static readonly string[] ElekafaStrings =
    [
        "ELEKÁFA SZ – tankolások", "Ügyfél: Példa Kft.", "Rendszám", "Dátum", "Tankolás", "Liter", "Összeg",
        "<r><rPr><b/></rPr><t>Km</t></r><r><t xml:space=\"preserve\"> óra</t></r><rPh sb=\"0\" eb=\"1\"><t>ケイ</t></rPh>",
        "Megjegyzés", "ABC-123", "XYZ-987",
    ];

    private const string ElekafaData =
        "<row r=\"1\" spans=\"1:6\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"
        + "<row r=\"2\" spans=\"1:6\"><c r=\"A2\" t=\"s\"><v>1</v></c></row>"
        + "<row r=\"3\" spans=\"1:6\"><c r=\"A3\" t=\"s\"><v>2</v></c><c r=\"B3\" t=\"s\"><v>3</v></c><c r=\"C3\" t=\"s\"><v>4</v></c><c r=\"D3\" s=\"0\"/><c r=\"E3\" s=\"0\"/><c r=\"F3\" t=\"s\"><v>8</v></c></row>"
        + "<row r=\"4\" spans=\"1:6\"><c r=\"A4\" s=\"0\"/><c r=\"B4\" s=\"0\"/><c r=\"C4\" t=\"s\"><v>5</v></c><c r=\"D4\" t=\"s\"><v>6</v></c><c r=\"E4\" t=\"s\"><v>7</v></c></row>"
        + "<row r=\"5\" spans=\"1:6\" x14ac:dyDescent=\"0.2\"><c r=\"A5\" t=\"s\"><v>9</v></c><c r=\"B5\" s=\"1\"><v>46309</v></c><c r=\"C5\"><v>45.5</v></c><c r=\"D5\" s=\"4\"><v>27300</v></c><c r=\"E5\"><f>C5*2+29</f><v>120</v></c><c r=\"F5\" t=\"inlineStr\"><is><t xml:space=\"preserve\"> első </t></is></c></row>"
        + "<row r=\"6\" spans=\"1:6\"><c r=\"A6\" t=\"s\"><v>10</v></c><c r=\"B6\" s=\"2\"><v>46310.75</v></c><c r=\"C6\"><v>1.2345678901234567E-2</v></c><c r=\"D6\" s=\"5\"><v>-1500</v></c><c r=\"E6\" t=\"e\"><f>NA()</f><v>#N/A</v></c><c r=\"F6\" t=\"str\"><f>\"x\"&amp;\"y\"</f><v>xy</v></c></row>"
        + "<row r=\"7\" spans=\"1:6\"><c r=\"B7\" s=\"3\"><v>46311.5</v></c><c r=\"C7\" t=\"b\"><v>1</v></c></row>"
        + "<row r=\"9\" spans=\"1:6\"><c r=\"A9\" s=\"1\"/></row>";

    private const string ElekafaMerges = "A1:F1 A3:A4 B3:B4 C3:E3 F3:F4";

    private static byte[] Elekafa() => ExcelSaved.Workbook("Tankolás", ElekafaData, ElekafaStrings, ElekafaMerges);

    [Fact]
    public void Every_value_the_writer_takes_reads_back_as_the_writers_own_type()
    {
        var day = new DateTime(2026, 10, 14);
        var moment = new DateTime(2026, 10, 14, 13, 45, 30);
        var file = Xlsx.Write("Round", ["Text", "Amount", "Whole", "Float", "Flag", "Day", "Moment", "Empty", "Bold", "Formula"],
            [Row("A-1", 1250.50m, 42, 2.5d, true, new Xlsx.Day(day), new Xlsx.Moment(moment), null, new Xlsx.Strong("loud"), new Xlsx.Formula("B2*2", 2501m)),
             Row("=1+1", -0.0001m, long.MaxValue, 1e-7, false, new DateOnly(1900, 1, 1), new DateTime(1900, 2, 28, 6, 0, 0), "", new Xlsx.Strong(3m), null)]);

        var rows = Xlsx.Read(file).Rows.ToList();

        Assert.Equal(["A-1", 1250.5m, 42m, 2.5m, true, new Xlsx.Day(day), new Xlsx.Moment(moment), null, "loud", 2501m], rows[0]);
        Assert.Equal(["=1+1", -0.0001m, 9223372036854775807m, 0.0000001m, false, new Xlsx.Day(new DateTime(1900, 1, 1)), new Xlsx.Moment(new DateTime(1900, 2, 28, 6, 0, 0)), null, 3m, null], rows[1]);
    }

    [Fact]
    public void A_written_sheet_reads_back_with_its_name_headings_and_rows_by_heading()
    {
        var file = Xlsx.Write(
            new Xlsx.Sheet("Paid", ["Number", "Amount"], [Row("A-1", 10m)]),
            new Xlsx.Sheet("Open", ["Number", "Amount"], [Row("B-1", 20m), new Xlsx.Folded(Row("B-2", 5m))], [new Xlsx.Strong("Total"), new Xlsx.Sum()]));

        var paid = Xlsx.Read(file);
        var open = Xlsx.Read(file, "open");

        Assert.Equal("Paid", paid.Name);
        Assert.Equal(["Number", "Amount"], paid.Headings);
        Assert.Equal("A-1", paid.Rows.Single()["Number"]);
        Assert.Equal("Open", open.Name);
        Assert.Equal([20m, 5m, 25m], open.Rows.Select(row => row[" amount "]));
        Assert.Null(open.Total);
    }

    [Fact]
    public void The_ELEKAFA_sheet_reads_its_merged_two_row_headings_and_the_data_below_them()
    {
        var file = Elekafa();

        var sheet = Xlsx.Read(file, "Tankolás").HeadingsOn(3, 4);
        var rows = sheet.Rows.ToList();

        Assert.Equal(["Rendszám", "Dátum", "Tankolás Liter", "Tankolás Összeg", "Tankolás Km óra", "Megjegyzés"], sheet.Headings);
        Assert.Equal(3, rows.Count);
        Assert.Equal("ABC-123", rows[0]["Rendszám"]);
        Assert.Equal(new Xlsx.Day(new DateTime(2026, 10, 14)), rows[0]["Dátum"]);
        Assert.Equal(45.5m, rows[0]["Tankolás Liter"]);
        Assert.Equal(27300m, rows[0]["tankolás összeg"]);
        Assert.Equal(120m, rows[0]["Tankolás Km óra"]);
        Assert.Equal(" első ", rows[0]["Megjegyzés"]);
        Assert.Equal(new Xlsx.Day(new DateTime(2026, 10, 15, 18, 0, 0)), rows[1]["Dátum"]);
        Assert.Equal(0.012345678901234567m, rows[1]["Tankolás Liter"]);
        Assert.Equal(-1500m, rows[1]["Tankolás Összeg"]);
        Assert.Equal("#N/A", rows[1]["Tankolás Km óra"]);
        Assert.Equal("xy", rows[1]["Megjegyzés"]);
        Assert.Equal([null, new Xlsx.Moment(new DateTime(2026, 10, 16, 12, 0, 0)), true, null, null, null], rows[2]);
    }

    [Fact]
    public void Read_alone_takes_the_first_sheet_with_its_headings_on_row_one()
    {
        var file = Elekafa();

        var sheet = Xlsx.Read(file);

        Assert.Equal("Tankolás", sheet.Name);
        Assert.Equal(["ELEKÁFA SZ – tankolások", "ELEKÁFA SZ – tankolások", "ELEKÁFA SZ – tankolások", "ELEKÁFA SZ – tankolások", "ELEKÁFA SZ – tankolások", "ELEKÁFA SZ – tankolások"], sheet.Headings);
        Assert.Equal(6, sheet.Rows.Count());
    }

    [Fact]
    public void A_heading_spread_from_a_title_above_the_heading_rows_is_taken_once()
    {
        var file = ExcelSaved.Workbook("S", "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row><row r=\"2\"><c r=\"B2\" t=\"s\"><v>2</v></c></row><row r=\"3\"><c r=\"A3\"><v>1</v></c><c r=\"B3\"><v>2</v></c></row>", ["Group", "Plate", "Sub"], "A1:A2");

        var sheet = Xlsx.Read(file).HeadingsOn(2);
        var both = Xlsx.Read(file).HeadingsOn(1, 2);

        Assert.Equal(["Group", "Sub"], sheet.Headings);
        Assert.Equal(["Group", "Plate Sub"], both.Headings);
        Assert.Equal(2m, both.Rows.Single()["Plate Sub"]);
    }

    [Fact]
    public void A_1904_workbook_counts_its_serials_from_1904()
    {
        var file = ExcelSaved.Workbook("Mac", "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Day</t></is></c></row><row r=\"2\"><c r=\"A2\" s=\"1\"><v>0</v></c></row><row r=\"3\"><c r=\"A3\" s=\"1\"><v>44843</v></c></row>", [], date1904: true);

        var days = Xlsx.Read(file).Rows.Select(row => row["Day"]).ToList();

        Assert.Equal([new Xlsx.Day(new DateTime(1904, 1, 1)), new Xlsx.Day(new DateTime(2026, 10, 10))], days);
    }

    [Theory]
    [InlineData(1, 1900, 1, 1)]
    [InlineData(59, 1900, 2, 28)]
    [InlineData(61, 1900, 3, 1)]
    [InlineData(46309, 2026, 10, 14)]
    public void A_1900_serial_keeps_Excels_leap_year_bug(int serial, int year, int month, int day)
    {
        var expected = new DateTime(year, month, day);

        var wallClock = ExcelSerial.ToWallClock(serial, date1904: false);
        var written = ExcelSerial.From(expected);

        Assert.Equal(expected, wallClock);
        Assert.Equal((double)serial, written);
    }

    [Theory]
    [InlineData("yyyy\\.mm\\.dd\\.;@", "Day")]
    [InlineData("[$-40E]yyyy\\.\\ mmmm\\ d\\.;@", "Day")]
    [InlineData("h:mm:ss AM/PM", "Moment")]
    [InlineData("[h]:mm", "Moment")]
    [InlineData("yyyy-mm-dd hh:mm", "Moment")]
    [InlineData("#,##0\\ \"Ft\";[Red]\\-#,##0\\ \"d\"", "None")]
    [InlineData("0.00E+00", "None")]
    [InlineData("\"day\"0", "None")]
    [InlineData("[Red]0.00", "None")]
    [InlineData("General", "None")]
    [InlineData("@", "None")]
    public void A_number_format_shows_a_date_only_with_date_parts_outside_quotes_and_brackets(string code, string kind)
    {
        var custom = DateFormat.Of(164, code);

        Assert.Equal(kind, custom.ToString());
    }

    [Theory]
    [InlineData(14, "Day")]
    [InlineData(17, "Day")]
    [InlineData(20, "Moment")]
    [InlineData(22, "Moment")]
    [InlineData(46, "Moment")]
    [InlineData(4, "None")]
    [InlineData(49, "None")]
    public void Excels_built_in_date_formats_are_dates(int id, string kind)
    {
        var builtIn = DateFormat.Of(id, null);

        Assert.Equal(kind, builtIn.ToString());
    }

    [Fact]
    public void An_unknown_heading_throws_naming_the_headings_the_sheet_has()
    {
        var row = Xlsx.Read(Xlsx.Write("S", ["Plate", "Litres"], [Row("A", 1m)])).Rows.Single();

        var error = Assert.Throws<KeyNotFoundException>(() => row["Liter"]);

        Assert.Contains("No column is headed 'Liter'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Plate', 'Litres'", error.Message, StringComparison.Ordinal);
        Assert.Contains("HeadingsOn(3, 4)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_two_columns_share_throws_rather_than_guessing()
    {
        var row = Xlsx.Read(Xlsx.Write("S", ["Plate", "plate "], [Row("A", "B")])).Rows.Single();

        var error = Assert.Throws<InvalidOperationException>(() => row["Plate"]);

        Assert.Contains("row[0]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_the_workbook_lacks_throws_naming_the_ones_it_has()
    {
        var file = Elekafa();

        var error = Assert.Throws<ArgumentException>(() => Xlsx.Read(file, "Fuel"));

        Assert.Equal("sheet", error.ParamName);
        Assert.Contains("'Tankolás', 'Notes'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sheet_made_in_code_has_no_other_rows_to_read_headings_from()
    {
        var sheet = new Xlsx.Sheet("S", ["A"], [Row(1m)]);

        var error = Assert.Throws<InvalidOperationException>(() => sheet.HeadingsOn(2));

        Assert.Contains("new Xlsx.Sheet(name, headings, rows)", error.Message, StringComparison.Ordinal);
        Assert.Equal(1m, sheet.Rows.Single()["a"]);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 4, 3 })]
    [InlineData(new[] { 3, 3 })]
    public void Heading_rows_are_named_from_top_to_bottom_from_row_one(int[] rows)
    {
        var sheet = Xlsx.Read(Elekafa());

        var error = Record.Exception(() => sheet.HeadingsOn(rows));

        Assert.IsAssignableFrom<ArgumentException>(error);
    }

    [Fact]
    public void A_part_that_unpacks_past_its_cap_is_refused()
    {
        var file = Xlsx.Write("S", ["A"], Enumerable.Range(0, 2000).Select(n => Row($"row {n}")));

        var error = Assert.Throws<InvalidDataException>(() => Xlsx.Read(file, null, new ReadLimits(16 * 1024, 1000)));

        Assert.Contains("xl/worksheets/sheet1.xml", error.Message, StringComparison.Ordinal);
        Assert.Contains("unpacks to more than", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stream_that_unpacks_past_what_its_archive_claimed_is_cut_off()
    {
        using var capped = new CappedStream(new MemoryStream(new byte[4096]), 1024, "xl/worksheets/sheet1.xml");

        var error = Record.Exception(() => capped.CopyTo(Stream.Null));

        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public void A_sheet_of_more_cells_than_the_cap_is_refused()
    {
        var file = Xlsx.Write("S", ["A", "B"], Enumerable.Range(0, 600).Select(n => Row(n, n)));

        var error = Assert.Throws<InvalidDataException>(() => Xlsx.Read(file, null, new ReadLimits(1024 * 1024, 1000)));

        Assert.Contains("more than 1,000 cells", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_far_away_cell_cannot_make_the_rows_between_fill_the_memory()
    {
        var file = ExcelSaved.Workbook("S", "<row r=\"1\"><c r=\"XFD1\"><v>1</v></c></row><row r=\"1048576\"><c r=\"XFD1048576\"><v>1</v></c></row>", []);

        var error = Record.Exception(() => Xlsx.Read(file, null, new ReadLimits(1024 * 1024, 100_000)));

        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public void A_DTD_in_a_part_is_refused_rather_than_expanded()
    {
        var file = ExcelSaved.Workbook("S", "", []);
        var parts = Parts(file);
        parts["xl/worksheets/sheet1.xml"] = "<?xml version=\"1.0\"?><!DOCTYPE lol [<!ENTITY lol \"lol\"><!ENTITY lol2 \"&lol;&lol;&lol;&lol;\">]>"
            + $"<worksheet xmlns=\"{ExcelSaved.Main}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>&lol2;</t></is></c></row></sheetData></worksheet>";

        var error = Assert.Throws<InvalidDataException>(() => Xlsx.Read(ExcelSaved.Zip(parts)));

        Assert.Contains("is not well-formed XML", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_that_is_no_workbook_says_so()
    {
        var notZip = Encoding.UTF8.GetBytes("Plate,Litres\nABC,1");
        var noWorkbook = ExcelSaved.Zip(new Dictionary<string, string>(StringComparer.Ordinal) { ["hello.txt"] = "hi" });

        var zipError = Assert.Throws<InvalidDataException>(() => Xlsx.Read(notZip));
        var partError = Assert.Throws<InvalidDataException>(() => Xlsx.Read(noWorkbook));

        Assert.Contains("not a zip archive", zipError.Message, StringComparison.Ordinal);
        Assert.Contains("no part 'xl/workbook.xml'", partError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_string_the_workbook_does_not_hold_is_a_broken_file()
    {
        var file = ExcelSaved.Workbook("S", "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>7</v></c></row>", ["only"]);

        var error = Assert.Throws<InvalidDataException>(() => Xlsx.Read(file));

        Assert.Contains("cell A1 names shared string '7'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cells_and_rows_without_a_place_follow_the_one_before()
    {
        var file = ExcelSaved.Workbook("S", "<row><c t=\"inlineStr\"><is><t>A</t></is></c><c t=\"inlineStr\"><is><t>B</t></is></c></row><row><c><v>1</v></c><c r=\"D2\"><v>4</v></c></row>", []);

        var sheet = Xlsx.Read(file);

        Assert.Equal(["A", "B", "", ""], sheet.Headings);
        Assert.Equal([1m, null, null, 4m], sheet.Rows.Single());
    }

    [Fact]
    public void Excels_escapes_read_back_as_the_characters_they_stand_for()
    {
        var file = Xlsx.Write("S", ["T"], [Row("a \u0001 b"), Row("_x0041_ literal")]);

        var texts = Xlsx.Read(file).Rows.Select(row => row["T"]);

        Assert.Equal(["a \u0001 b", "_x0041_ literal"], texts);
    }

    private static Dictionary<string, string> Parts(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }, StringComparer.Ordinal);
    }
}
