using System.Globalization;

namespace Rask.Site.Features.UiDocs;

public sealed partial class UiFiltersPage
{
    private static readonly string[] Fleet =
        [.. Enumerable.Range(1, 40).Select(n => "Van " + n.ToString("00", CultureInfo.InvariantCulture))];

    private const string LongRoute =
        "Budapest, Tatabánya, Győr, Mosonmagyaróvár, Hegyeshalom, Bruck an der Leitha, Schwechat, Wien, Sankt Pölten, Linz, "
        + "Salzburg, Rosenheim, München, Augsburg, Ulm, Stuttgart, Karlsruhe, Strasbourg, Nancy, Metz, Reims, Paris";

    private List<string> _vans = [.. Fleet];
    private string? _route = LongRoute;

    private Component LongFilterExample()
    {
        var filters = Ui.Filters[
            Ui.Filter.Label("Vehicles").Shown(_vans).OnRemove(() => { _vans = []; }),
            Ui.Filter.Label("Route").Shown(_route).OnRemove(() => { _route = null; })
        ];

        return Div.Class("space-y-4")[
            Div.Class("flex items-center justify-end gap-2")[
                filters,
                Ui.Dropdown[
                    Ui.Button.IconTrailing(Ui.IconName.ChevronDown)["Export"],
                    Ui.Menu[
                        Ui.MenuItem.Icon(Ui.IconName.ArrowDownTray)["Excel"],
                        Ui.MenuItem.Icon(Ui.IconName.ArrowDownTray)["CSV"]
                    ]
                ],
                Ui.Toggle.Icon(Ui.IconName.ArchiveBox).Tooltip("Archived")
            ],
            filters.Badges
        ];
    }
}
