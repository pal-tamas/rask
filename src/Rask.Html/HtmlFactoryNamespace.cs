using Rask.Core;

// Tell a consuming app's Rask factory generator to emit `global using static Rask.Html.Components.Generated;`
// whenever it references Rask.Html — so `Div.Class("card")[…]` and `Generated.Img(...)` resolve with no
// per-file using, exactly as they did while the element family lived in Rask.Core.Components.
//
// This is what makes the assembly split invisible at the call site. It has to be a SEPARATE namespace from
// Rask.Core.Components rather than a continuation of it: the generator emits one `public static partial class
// Generated` per compilation, and Rask.Core still declares components of its own, so sharing the namespace
// would put two public Rask.Core.Components.Generated types in the reference graph (CS0433).
[assembly: RaskFactoryNamespace("Rask.Html.Components")]
