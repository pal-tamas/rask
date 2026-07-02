namespace Rask.Bootstrap;

// A Bootstrap table: <table class="table …">. Wraps the core Table() with the typed style toggles;
// set Responsive to wrap it in a .table-responsive scroll container. Children are the usual thead/
// tbody/tr/td markup (core Thead/Tbody/Tr/Td or plain elements).
public sealed class BsTable : BsBlock
{
    public BsColor? Color { get; set; }
    public bool? Striped { get; set; }
    public bool? StripedColumns { get; set; }
    public bool? Bordered { get; set; }
    public bool? Borderless { get; set; }
    public bool? Hover { get; set; }
    public bool? Small { get; set; }
    public bool? Responsive { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "table",
            Color is { } c ? c.Table() : null,
            Striped is true ? "table-striped" : null,
            StripedColumns is true ? "table-striped-columns" : null,
            Bordered is true ? "table-bordered" : null,
            Borderless is true ? "table-borderless" : null,
            Hover is true ? "table-hover" : null,
            Small is true ? "table-sm" : null,
            Class);

        var table = Table(Id: Id, Class: cls)[Items];
        return Responsive is true ? Div(Class: "table-responsive")[table] : table;
    }
}
