namespace Rask.Example.Playground;

// The code the editor opens with. Kept verbatim (raw string) so what the visitor sees is exactly what
// compiles. Components live in a namespace, as in any real Rask project — that's what lets the generator's
// `global using static Demo.Generated;` bring a user component's own factory into scope.
internal static class PlaygroundSamples
{
    public const string Starter =
        """
        using Rask.Core;

        namespace Demo;

        // Welcome to the Rask playground! This C# is compiled in your browser — Roslyn and the Rask
        // source generator run in WebAssembly, no server involved. Edit the code, then press Run
        // (or Ctrl/Cmd + Enter). Define a component named `Playground` as the entry point.
        public sealed class Playground : Component
        {
            private int _count;

            protected override Component? Render() =>
                Div(Class: "card")[
                    H1()["Hello, Rask 👋"],
                    P()[$"You clicked {_count} times."],
                    Button(Class: "btn", OnClick: () => _count++)["Click me"]
                ];
        }
        """;
}
