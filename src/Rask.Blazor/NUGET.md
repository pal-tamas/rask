# Rask.Blazor

Hosts a **real Blazor component** inside a [Rask](https://rask.sh/) page — one from your Razor Class
Library, MudBlazor, Radzen, or any `ComponentBase` you already have. Rask is a full-stack .NET web framework
for teams of any size; this package lets it reuse the Blazor components a team already owns.

- The Razor SDK compiles `.razor` exactly as it always has; Rask renders the result **server-side into the
  first HTTP response**.
- Parameters cross as **live C# objects**, not serialized — and the chain steps are read from the
  component's own `[Parameter]`s, so nothing is redeclared.
- The hosted component's own `@onclick` and `@bind` fire over Rask's existing channel, with **no Blazor
  circuit**.
- Works on the ASP.NET host and on WebAssembly, **trimmed publish included**.

## Install

```bash
dotnet add package Rask.Blazor
```

## Use

```csharp
// Program.cs
builder.Services.AddRask();
builder.Services.AddRaskBlazor();
```

```csharp
// The whole declaration: PriceTag is an ordinary .razor component from a class library.
public sealed partial class Quote : BlazorComponent<PriceTag>;
```

```csharp
Div.Class("grid")[
    H1["Watchlist"],
    Quote.Symbol("RASK").Price(12.5m).Tone("up")    // PriceTag's [Parameter]s, as chain steps
]
```

Read the "what works, and what does not" table in the guide first — it is the whole shape of the feature.

Guide: [Blazor components](https://rask.sh/docs/guides/blazor-components)
