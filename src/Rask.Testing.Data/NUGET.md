# Rask.Testing.Data

The half of [Rask.Testing](https://www.nuget.org/packages/Rask.Testing) that needs `Rask.Data`, so a test project
that only renders components never pulls in EF Core. Reference it beside `Rask.Testing` from the test project of an
app that stores data.

```bash
dotnet add package Rask.Testing
dotnet add package Rask.Testing.Data
```

## A stored aggregate its rules accept: `await Test.Make<Supply>()`

A test that needs an aggregate to work on, and does not care which, asks for one instead of hand-building it and
everything it refers to:

```csharp
await using var database = await TestDatabase.Start<AppDbContext>();

var supply = await Test.Make<Supply>();                                   // its company and tank made and stored too
var acme   = await Test.Make<Company>();
var other  = await Test.Make<Supply>(o => o.Use(acme));                   // Acme's, and so is its tank
var big    = await Test.Make<Supply>(o => o.With("parts", parts));        // the factory's `parts`, as the test says
var ticket = await Test.Make<Ticket>(o => o.Factory = nameof(Ticket.Season));
```

- It is made through the aggregate's **own factory** — a static method declared on it that returns it (or a task of
  it), private or not — and saved as `supply.Save()` saves it, its rules asked.
- Each parameter gets a value its rules take: a value object made by `Test.Make<Vin>()`, a text, a number, today, an
  enum's first member, a row spec through its constructor, a list of one row. A nullable parameter is left null, one
  with a default keeps it.
- What it refers to is made first, the same way, and stored: an aggregate parameter (`Company company`), a typed id
  (`TankId tank`), a plain id named after its aggregate (`Guid companyId`) — once each per call, so a supply and its
  tank share one company.
- What it cannot make, or its rules refuse, is an `InvalidOperationException` naming the parameter and the option
  that helps.

See the [testing guide](https://github.com/pal-tamas/rask/blob/main/docs/testing.md).
