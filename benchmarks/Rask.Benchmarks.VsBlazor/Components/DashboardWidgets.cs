using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rask.Core;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks.VsBlazor.Components;

/// <summary>
///     Representative dashboard: header bar + nav sidebar + 6 widgets (counter,
///     chart placeholder, table summary, alert list, status grid, footer). One widget's
///     counter mutates per iteration; every other widget renders identical HTML. Signal:
///     the diff codec must keep five widget subtrees out of the wire payload entirely.
///     If a regression accidentally pulls an unchanged widget into the diff, the
///     payload-bytes number will balloon.
/// </summary>
internal static partial class DashboardWidgets
{
    public const int AlertCount = 8;
    public const int StatusGridSize = 12;

#pragma warning disable RASK014
    public sealed partial class StatefulDashboard : Component
#pragma warning restore RASK014
    {
        private List<Component>? _staticAlerts;
        private List<Component>? _staticSidebar;
        private List<Component>? _staticStatusGrid;

        public int Counter { get; private set; }

        public void Tick()
        {
            Counter++;
            StateHasChanged();
        }

        protected override Component? Render()
        {
            if (_staticSidebar is null)
            {
                _staticSidebar = new List<Component>
                {
                    Li.Class("nav-item")[A.Href("/dashboard")["Dashboard"]],
                    Li.Class("nav-item")[A.Href("/reports")["Reports"]],
                    Li.Class("nav-item")[A.Href("/users")["Users"]],
                    Li.Class("nav-item")[A.Href("/settings")["Settings"]],
                    Li.Class("nav-item")[A.Href("/help")["Help"]]
                };

                _staticAlerts = new List<Component>(AlertCount);
                for (var i = 0; i < AlertCount; i++)
                {
                    _staticAlerts.Add(Li.Class("alert").Id($"a{i}")[
                        Span.Class("alert-sev")[$"Sev{(i % 3) + 1}"],
                        Span.Class("alert-msg")[$"Alert message {i}"]
                    ]);
                }

                _staticStatusGrid = new List<Component>(StatusGridSize);
                for (var i = 0; i < StatusGridSize; i++)
                {
                    _staticStatusGrid.Add(Div.Class("status-cell").Id($"s{i}")[
                        Span.Class("status-label")[$"Service {i}"],
                        Span.Class("status-value")["healthy"]
                    ]);
                }
            }

            return Div.Class("dashboard")[
                Header.Class("topbar")[
                    Span.Class("brand")["Rask Dashboard"],
                    Span.Class("user")["alice"]
                ],
                Aside.Class("sidebar")[Ul[_staticSidebar]],
                Main.Class("content")[
                    // Widget 1: counter — the only mutating widget
                    Div.Class("widget counter-widget").Id("w-counter")[
                        Span.Class("widget-title")["Live count"],
                        Span.Class("widget-value")[Counter.ToString()]
                    ],
                    // Widget 2: chart placeholder
                    Div.Class("widget chart-widget").Id("w-chart")[
                        Span.Class("widget-title")["Throughput"],
                        Div.Class("chart-canvas")
                    ],
                    // Widget 3: table summary
                    Div.Class("widget table-widget").Id("w-table")[
                        Span.Class("widget-title")["Recent orders"],
                        Div.Class("summary-row")[Span["Total"], Span["1,234"]],
                        Div.Class("summary-row")[Span["Pending"], Span["56"]],
                        Div.Class("summary-row")[Span["Failed"], Span["3"]]
                    ],
                    // Widget 4: alert list
                    Div.Class("widget alerts-widget").Id("w-alerts")[
                        Span.Class("widget-title")["Alerts"],
                        Ul.Class("alerts")[_staticAlerts!]
                    ],
                    // Widget 5: status grid
                    Div.Class("widget status-widget").Id("w-status")[
                        Span.Class("widget-title")["Service status"],
                        Div.Class("status-grid")[_staticStatusGrid!]
                    ],
                    // Widget 6: footer
                    Div.Class("widget footer-widget").Id("w-footer")[
                        Span["© 2026 Rask Inc."]
                    ]
                ]
            ];
        }
    }

    public sealed class BlazorDashboard : ComponentBase
    {
        [Parameter] public int Counter { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder b)
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "dashboard");

            // Header
            b.OpenElement(2, "header");
            b.AddAttribute(3, "class", "topbar");
            b.OpenElement(4, "span");
            b.AddAttribute(5, "class", "brand");
            b.AddContent(6, "Rask Dashboard");
            b.CloseElement();
            b.OpenElement(7, "span");
            b.AddAttribute(8, "class", "user");
            b.AddContent(9, "alice");
            b.CloseElement();
            b.CloseElement();

            // Sidebar
            b.OpenElement(10, "aside");
            b.AddAttribute(11, "class", "sidebar");
            b.OpenElement(12, "ul");
            string[] navItems =
                ["/dashboard:Dashboard", "/reports:Reports", "/users:Users", "/settings:Settings", "/help:Help"];
            foreach (var item in navItems)
            {
                var parts = item.Split(':');
                b.OpenElement(13, "li");
                b.AddAttribute(14, "class", "nav-item");
                b.OpenElement(15, "a");
                b.AddAttribute(16, "href", parts[0]);
                b.AddContent(17, parts[1]);
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();
            b.CloseElement();

            // Main
            b.OpenElement(18, "main");
            b.AddAttribute(19, "class", "content");

            // Widget 1: counter
            b.OpenElement(20, "div");
            b.AddAttribute(21, "class", "widget counter-widget");
            b.AddAttribute(22, "id", "w-counter");
            b.OpenElement(23, "span");
            b.AddAttribute(24, "class", "widget-title");
            b.AddContent(25, "Live count");
            b.CloseElement();
            b.OpenElement(26, "span");
            b.AddAttribute(27, "class", "widget-value");
            b.AddContent(28, Counter.ToString());
            b.CloseElement();
            b.CloseElement();

            // Widget 2: chart
            b.OpenElement(29, "div");
            b.AddAttribute(30, "class", "widget chart-widget");
            b.AddAttribute(31, "id", "w-chart");
            b.OpenElement(32, "span");
            b.AddAttribute(33, "class", "widget-title");
            b.AddContent(34, "Throughput");
            b.CloseElement();
            b.OpenElement(35, "div");
            b.AddAttribute(36, "class", "chart-canvas");
            b.CloseElement();
            b.CloseElement();

            // Widget 3: table summary
            b.OpenElement(37, "div");
            b.AddAttribute(38, "class", "widget table-widget");
            b.AddAttribute(39, "id", "w-table");
            b.OpenElement(40, "span");
            b.AddAttribute(41, "class", "widget-title");
            b.AddContent(42, "Recent orders");
            b.CloseElement();
            string[][] summaryRows = [["Total", "1,234"], ["Pending", "56"], ["Failed", "3"]];
            foreach (var row in summaryRows)
            {
                b.OpenElement(43, "div");
                b.AddAttribute(44, "class", "summary-row");
                b.OpenElement(45, "span");
                b.AddContent(46, row[0]);
                b.CloseElement();
                b.OpenElement(47, "span");
                b.AddContent(48, row[1]);
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();

            // Widget 4: alerts
            b.OpenElement(49, "div");
            b.AddAttribute(50, "class", "widget alerts-widget");
            b.AddAttribute(51, "id", "w-alerts");
            b.OpenElement(52, "span");
            b.AddAttribute(53, "class", "widget-title");
            b.AddContent(54, "Alerts");
            b.CloseElement();
            b.OpenElement(55, "ul");
            b.AddAttribute(56, "class", "alerts");
            for (var i = 0; i < AlertCount; i++)
            {
                b.OpenElement(57, "li");
                b.AddAttribute(58, "class", "alert");
                b.AddAttribute(59, "id", $"a{i}");
                b.OpenElement(60, "span");
                b.AddAttribute(61, "class", "alert-sev");
                b.AddContent(62, $"Sev{(i % 3) + 1}");
                b.CloseElement();
                b.OpenElement(63, "span");
                b.AddAttribute(64, "class", "alert-msg");
                b.AddContent(65, $"Alert message {i}");
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();
            b.CloseElement();

            // Widget 5: status grid
            b.OpenElement(66, "div");
            b.AddAttribute(67, "class", "widget status-widget");
            b.AddAttribute(68, "id", "w-status");
            b.OpenElement(69, "span");
            b.AddAttribute(70, "class", "widget-title");
            b.AddContent(71, "Service status");
            b.CloseElement();
            b.OpenElement(72, "div");
            b.AddAttribute(73, "class", "status-grid");
            for (var i = 0; i < StatusGridSize; i++)
            {
                b.OpenElement(74, "div");
                b.AddAttribute(75, "class", "status-cell");
                b.AddAttribute(76, "id", $"s{i}");
                b.OpenElement(77, "span");
                b.AddAttribute(78, "class", "status-label");
                b.AddContent(79, $"Service {i}");
                b.CloseElement();
                b.OpenElement(80, "span");
                b.AddAttribute(81, "class", "status-value");
                b.AddContent(82, "healthy");
                b.CloseElement();
                b.CloseElement();
            }

            b.CloseElement();
            b.CloseElement();

            // Widget 6: footer
            b.OpenElement(83, "div");
            b.AddAttribute(84, "class", "widget footer-widget");
            b.AddAttribute(85, "id", "w-footer");
            b.OpenElement(86, "span");
            b.AddContent(87, "© 2026 Rask Inc.");
            b.CloseElement();
            b.CloseElement();

            b.CloseElement(); // main
            b.CloseElement(); // dashboard
        }
    }
}
