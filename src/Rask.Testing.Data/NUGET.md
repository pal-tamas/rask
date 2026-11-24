# Rask.Testing.Data

The half of [Rask.Testing](https://www.nuget.org/packages/Rask.Testing) that needs `Rask.Data`, so a test project
that only renders components never pulls in EF Core. Reference it beside `Rask.Testing` from the test project of an
app that stores data.

```bash
dotnet add package Rask.Testing
dotnet add package Rask.Testing.Data
```

## A valid value object: `Test.Make`

A test that needs a value object its rules accept, and does not care which, asks for one instead of writing a literal
that a tightened rule would one day refuse:

```csharp
var vin = Test.Make<Vin>();        // Text(Value, MinLength: 17, MaxLength: 17, Upper: true) → "ABCDEFGHIJKLMNOPQ"
var tax = Test.Make<TaxNumber>();  // Text(Value, Pattern: @"^\d{8}-\d-\d{2}$")            → "11111111-1-11"
var km  = Test.Make<KmRange>();    // Range(Start, End, Strict: true)                       → 1 to 2
```

It reads the rules the record hands its base — `Text` lengths and a simple `Pattern`, `Number`/`Whole` bounds and
scale, `Day` and `Moment` bounds, a `Range`'s `Min` one step on, a `: Value` record's members each sampled — and makes
the value through the generated `Create`, so the record's own `Rules()` accept it too. One it cannot build is an
`InvalidOperationException` that names it and says to make it by hand with `X.Create(…)`.

See the [testing guide](https://github.com/pal-tamas/rask/blob/main/docs/testing.md).
