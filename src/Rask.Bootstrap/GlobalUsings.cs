// The component TYPES, so a signature can name one. The markup itself needs none of these: a chain
// entry is a member of the markup host, inherited or injected, and is in scope without any using.
global using Rask.Core;
global using Rask.Core.Components;
// The HTML/SVG element family, split out of Rask.Core.Components into Rask.Html. Both halves
// are imported: Core still declares the components it builds itself, Rask.Html the rest.
global using Rask.Html.Components;
