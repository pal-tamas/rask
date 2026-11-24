using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Rask.Core.Forms;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.TableColumn.For&lt;TRow&gt;(r =&gt; r.X)</c> and <c>Ui.TableCell.For(() =&gt; row.X)</c>: a heading named by
///     the member's <c>[Display]</c>, a cell written for the reader by its <c>[DisplayFormat]</c> or its shape, numbers at
///     the end, and a <c>[ScaffoldColumn(false)]</c> member drawn nowhere.
/// </summary>
public partial class UiTableForTests : global::Rask.Core.RaskMarkup
{
    static UiTableForTests() =>
        RaskValidation.RegisterFieldShapes(typeof(TankRow), static name => name switch
        {
            nameof(TankRow.Litres) => new FieldShape(FieldKind.Number, Scale: 2),
            nameof(TankRow.Kind) => new FieldShape(FieldKind.Choice, Options: [new FieldOption(Fuel.Diesel, "Gázolaj"), new FieldOption(Fuel.Petrol, "Benzin")]),
            _ => null,
        });

    [Fact]
    public void A_heading_is_named_by_the_members_display_and_a_label_in_its_brackets_wins()
    {
        var page = Page.Render(() => Ui.TableColumns[
            Ui.TableColumn.For<TankRow>(t => t.Name),
            Ui.TableColumn.For<TankRow>(t => t.Code),
            Ui.TableColumn.For<TankRow>(t => t.Name)["Saját"]
        ]);

        Assert.Equal(["Megnevezés", "Code", "Saját"], page.FindAll("th").Select(th => th.TextContent.Trim()));
    }

    [Fact]
    public void A_number_keeps_to_the_end_in_its_heading_and_its_cell_unless_told_otherwise()
    {
        var row = new TankRow { Size = 12000 };

        var page = Page.Render(() => Div[
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn.For<TankRow>(t => t.Size), Ui.TableColumn.For<TankRow>(t => t.Name)],
                Ui.TableRows[Ui.TableRow[Ui.TableCell.For(() => row.Size).Id("size"), Ui.TableCell.For(() => row.Name).Id("name")]]
            ],
            Ui.TableCell.For(() => row.Size).Align(Ui.Align.Start).Id("start")
        ]);

        Assert.Contains("group/end-align", page.FindAll("th")[0].Attribute("class"), StringComparison.Ordinal);
        Assert.DoesNotContain("group/end-align", page.FindAll("th")[1].Attribute("class") ?? "", StringComparison.Ordinal);
        Assert.Contains("text-end", page.Find("#size").Attribute("class"), StringComparison.Ordinal);
        Assert.DoesNotContain("text-end", page.Find("#name").Attribute("class") ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("text-end", page.Find("#start").Attribute("class") ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void A_cell_writes_its_value_for_the_reader_by_its_format_its_shape_or_what_it_holds()
    {
        var row = new TankRow
        {
            Name = "Kút 1", Size = 12000, Litres = 1234.5m, Ratio = 0.25m, Kind = Fuel.Petrol, Opened = new DateOnly(2026, 3, 14),
            Active = true, Shut = false, Spare = Fuel.Diesel,
        };

        var cells = InHungary(() => Page.Render(() => Ui.TableRow[
            Ui.TableCell.For(() => row.Name), Ui.TableCell.For(() => row.Size), Ui.TableCell.For(() => row.Litres),
            Ui.TableCell.For(() => row.Ratio), Ui.TableCell.For(() => row.Kind), Ui.TableCell.For(() => row.Opened),
            Ui.TableCell.For(() => row.Active), Ui.TableCell.For(() => row.Shut), Ui.TableCell.For(() => row.Spare)
        ]).FindAll("td").Select(td => td.TextContent).ToList());

        Assert.Equal(["Kút 1", "12 000", "1 234,50", "25,0%", "Benzin", "2026. 03. 14.", "Igen", "Nem", "Gázolaj (dízel)"], cells);
    }

    [Fact]
    public void No_value_is_its_null_display_text_else_a_dash()
    {
        var row = new TankRow();

        var page = Page.Render(() => Ui.TableRow[Ui.TableCell.For(() => row.Note), Ui.TableCell.For(() => row.Code)]);

        Assert.Equal(["nincs", "–"], page.FindAll("td").Select(td => td.TextContent));
    }

    [Fact]
    public void A_member_not_scaffolded_draws_neither_its_heading_nor_its_cells()
    {
        var row = new TankRow { Secret = "x" };

        var html = Page.Render(() => Ui.Table[
            Ui.TableColumns[Ui.TableColumn.For<TankRow>(t => t.Secret), Ui.TableColumn.For<TankRow>(t => t.Name)],
            Ui.TableRows[Ui.TableRow[Ui.TableCell.For(() => row.Secret), Ui.TableCell.For(() => row.Name)]]
        ]).Render();

        Assert.Single(Page.Render(() => Ui.TableColumns[Ui.TableColumn.For<TankRow>(t => t.Secret), Ui.TableColumn.For<TankRow>(t => t.Name)]).FindAll("th"));
        Assert.Equal(1, html.Split("<td", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(">x<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_in_a_bound_cells_brackets_wins_over_its_value()
    {
        var row = new TankRow { Name = "Kút 1" };

        var page = Page.Render(() => Ui.TableRow[Ui.TableCell.For(() => row.Name)[Ui.Badge["Kút 1"]]]);

        Assert.True(page.Exists("td [data-ui-badge]"));
    }

    private static T InHungary<T>(Func<T> read)
    {
        var before = CultureInfo.CurrentCulture;
        var beforeUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");
        try
        {
            var told = default(T)!;
            KitCulture.In("hu-HU", () =>
            {
                told = read();
                return string.Empty;
            });
            return told;
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
            CultureInfo.CurrentUICulture = beforeUi;
        }
    }

    private enum Fuel
    {
        [Display(Name = "Gázolaj (dízel)")]
        Diesel,

        Petrol,
    }

    private sealed class TankRow
    {
        [Display(Name = "Megnevezés")]
        public string Name { get; init; } = "";

        public string? Code { get; init; }

        public int Size { get; init; }

        public decimal Litres { get; init; }

        [DisplayFormat(DataFormatString = "{0:P1}")]
        public decimal Ratio { get; init; }

        public Fuel Kind { get; init; }

        public DateOnly Opened { get; init; }

        public bool Active { get; init; }

        public bool Shut { get; init; }

        public Fuel Spare { get; init; }

        [DisplayFormat(NullDisplayText = "nincs")]
        public string? Note { get; init; }

        [ScaffoldColumn(false)]
        public string? Secret { get; init; }
    }
}
