namespace Rask.UiTests.Components;

/// <summary>
///     Flux's kanban: the element each part is, what each prop writes, and where the slots go.
/// </summary>
public partial class UiKanbanTests : global::Rask.Core.RaskMarkup
{
    private const string Card = "p-3 rounded-lg bg-white shadow-xs ring-1 ring-black/7 dark:bg-zinc-700 dark:ring-zinc-700";

    private const string Pressed =
        "block w-full text-start cursor-default select-none hover:bg-zinc-50 dark:hover:bg-zinc-700 dark:hover:ring-zinc-600";

    private const string Heading = "<div class=\"font-medium text-zinc-800 dark:text-white text-sm\" data-ui-heading>";

    private const string Muted = "text-sm text-zinc-500 dark:text-white/70";

    [Fact]
    public void A_board_is_the_row_its_columns_stand_in()
    {
        var board = Ui.Kanban[Span["columns"]];

        var html = board.ToHtml();

        Assert.Equal("<div class=\"flex gap-4\" data-ui-kanban><span>columns</span></div>", html);
    }

    [Fact]
    public void A_column_wraps_what_it_holds_in_a_fixed_width_panel()
    {
        var column = Ui.KanbanColumn[Span["parts"]];

        var html = column.ToHtml();

        Assert.Equal(
            "<div data-ui-kanban-column><div class=\"w-80 max-w-80 rounded-lg bg-zinc-100 dark:bg-zinc-800\"><span>parts</span></div></div>",
            html);
    }

    [Fact]
    public void Element_steps_reach_the_column_itself_and_not_its_panel()
    {
        var column = Ui.KanbanColumn.Id("planned").Class("snap-start").Data("testid", "col")["x"];

        var html = column.ToHtml();

        Assert.StartsWith("<div id=\"planned\" class=\"snap-start\" data-ui-kanban-column data-testid=\"col\"><div class=\"w-80 ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_header_draws_its_heading_and_count_and_an_empty_place_for_actions()
    {
        var header = Ui.KanbanColumnHeader.Heading("Planned").Count(6);

        var html = header.ToHtml();

        Assert.Equal(
            "<div class=\"flex flex-col p-2\" data-ui-kanban-column-header>"
            + "<div class=\"flex items-center justify-between min-h-8\">"
            + $"<div class=\"flex items-center gap-1.5 px-3\">{Heading}Planned</div><div class=\"{Muted}\">6</div></div>"
            + "<div class=\"flex items-center gap-1\"></div>"
            + "</div></div>",
            html);
    }

    [Fact]
    public void A_header_without_a_count_draws_none_and_a_count_of_zero_is_drawn()
    {
        var bare = Ui.KanbanColumnHeader.Heading("Planned").ToHtml();
        var empty = Ui.KanbanColumnHeader.Heading("Done").Count(0).ToHtml();

        Assert.DoesNotContain(Muted, bare, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"{Muted}\">0</div>", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subheading_is_a_second_row_under_the_heading()
    {
        var header = Ui.KanbanColumnHeader.Heading("Backlog").Subheading("Ideas and suggestions");

        var html = header.ToHtml();

        Assert.EndsWith(
            $"<div class=\"flex items-center gap-1.5 px-3 mb-1\"><div class=\"{Muted}\" data-ui-subheading>Ideas and suggestions</div></div></div>",
            html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Actions_sit_at_the_end_of_the_heading_row()
    {
        var header = Ui.KanbanColumnHeader.Heading("Planned").Actions(Span["add"]);

        var html = header.ToHtml();

        Assert.Contains("<div class=\"flex items-center gap-1\"><span>add</span></div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Children_of_a_header_take_the_place_of_its_heading_and_count()
    {
        var header = Ui.KanbanColumnHeader.Heading("Planned").Count(6)[Span["Custom"]];

        var html = header.ToHtml();

        Assert.Contains("<div class=\"flex items-center gap-1.5 px-3\"><span>Custom</span></div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Planned", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">6<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Cards_and_footer_stack_what_they_hold_with_the_same_gutters()
    {
        var cards = Ui.KanbanColumnCards["a"].ToHtml();
        var footer = Ui.KanbanColumnFooter["b"].ToHtml();

        Assert.Equal("<div class=\"flex flex-col gap-2 px-2 pb-2\" data-ui-kanban-column-cards>a</div>", cards);
        Assert.Equal("<div class=\"flex flex-col gap-2 px-2 pb-2\" data-ui-kanban-column-footer>b</div>", footer);
    }

    [Fact]
    public void A_card_is_a_div_showing_its_heading_and_marked_as_Flux_marks_it()
    {
        var card = Ui.KanbanCard.Heading("Update privacy policy in app");

        var html = card.ToHtml();

        Assert.Equal($"<div class=\"{Card}\" ui-kanban-card>{Heading}Update privacy policy in app</div></div>", html);
    }

    [Fact]
    public void A_card_as_a_button_is_a_button_that_does_not_submit_a_form()
    {
        var card = Ui.KanbanCard.As(Ui.KanbanCardAs.Button).Heading("Fix login");

        var html = card.ToHtml();

        Assert.Equal($"<button class=\"{Card} {Pressed}\" data-ui-kanban-card type=\"button\">{Heading}Fix login</div></button>", html);
    }

    [Fact]
    public void A_card_as_a_button_carries_its_click_handler()
    {
        var card = Ui.KanbanCard.As(Ui.KanbanCardAs.Button).OnClick(() => { }).Heading("Fix login");

        var html = card.ToHtml();

        Assert.True(card.OnClick.HasValue);
        Assert.StartsWith("<button ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_header_sits_above_the_heading_and_a_footer_under_it()
    {
        var card = Ui.KanbanCard.Heading("Fix login").Header(Span["tags"]).Footer(Span["people"]);

        var html = card.ToHtml();

        Assert.Equal(
            $"<div class=\"{Card}\" ui-kanban-card>"
            + "<div class=\"flex items-center gap-1.5 mb-2\"><span>tags</span></div>"
            + $"{Heading}Fix login</div>"
            + "<div class=\"flex items-center gap-1.5 mt-2\"><span>people</span></div>"
            + "</div>",
            html);
    }

    [Fact]
    public void Children_of_a_card_take_the_place_of_its_heading()
    {
        var card = Ui.KanbanCard.Heading("Fix login")[Span["Custom"]];

        var html = card.ToHtml();

        Assert.Equal($"<div class=\"{Card}\" ui-kanban-card><span>Custom</span></div>", html);
    }

    [Fact]
    public void A_whole_board_nests_its_parts_in_the_order_they_are_written()
    {
        var board = Ui.Kanban[
            Ui.KanbanColumn[
                Ui.KanbanColumnHeader.Heading("Planned").Count(1),
                Ui.KanbanColumnCards[Ui.KanbanCard.Heading("Fix login")],
                Ui.KanbanColumnFooter[Ui.Button.Subtle.Sm["New card"]]
            ]
        ];

        var html = board.ToHtml();

        var order = new[] { "data-ui-kanban>", "data-ui-kanban-column>", "data-ui-kanban-column-header>", "data-ui-kanban-column-cards>", "ui-kanban-card>", "data-ui-kanban-column-footer>" }
            .Select(marker => html.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
    }
}
