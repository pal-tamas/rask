namespace Rask.Site.Features.UiDocs;

/// <summary>A table of what each key, or each thing a reader does, sets off: a Behaviour topic that is a list.</summary>
internal sealed partial class UiDocsKeys : Component
{
    /// <summary>The heading of the first column: "Key", or "When".</summary>
    public required string Column { get; set; }

    /// <summary>Each row: the key or the act, and what follows. A span between backticks is code.</summary>
    public required IReadOnlyList<(string Key, string Does)> Rows { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Table[
            Ui.TableColumns[
                Ui.TableColumn[Column],
                Ui.TableColumn["What happens"]
            ],
            Ui.TableRows[
                Rows.Select(row => Ui.TableRow.Key(row.Key)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("align-top whitespace-normal")[UiDocsProse.Of(row.Key)],
                    Ui.TableCell.Class("min-w-64 align-top whitespace-normal")[UiDocsProse.Of(row.Does)]
                ])
            ]
        ];
}
