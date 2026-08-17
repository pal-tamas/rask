// Framework projects opt out of the generator's global usings (Directory.Build.props sets
// RaskGlobalUsings=false for non-Rask.Example projects), so import the primitives explicitly —
// exactly as Rask.Bootstrap does. This makes Component/Element and Div()/Span()/Text available
// unqualified, plus the Bs* factories the dashboard's pages are built from.
global using Rask.Bootstrap;
global using Rask.Core;
global using Rask.Core.Components;
global using Rask.Html.Components;
global using static Rask.Bootstrap.Generated;
global using static Rask.Core.Components.Generated;
// Router()/Outlet() — the dashboard ships a route chain of its own, so it needs the routing factories.
global using static Rask.Core.Routing.Generated;
