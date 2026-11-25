# Domain modelling — aggregates, entities and value objects

> **In practice:** [Tutorial Ch 2](tutorial/02-first-feature.md) · [forms over an operation](forms.md#a-form-over-an-operation) · [testing the domain](testing.md#testing-the-domain) · [cheat sheet](cheatsheet.md).
> **The rest of Rask.Data:** [storage — queries, conventions and the database](data.md) · [writing many rows](data-writes.md).

A Rask app's business code is its **domain**: aggregates, the entities they hold and the value objects their
properties are. Every write is one of the aggregate's own methods, and everything else — the form that calls it,
the save, the transaction, the check that a field is valid — is Rask's. There is one way to write it:

- **A write is an aggregate method.** `supply.Change(…)`, `order.Ship(…)`. There is no service layer, no
  application service and no domain service: a rule that decides something is on the aggregate it is about, and
  another aggregate is read through its [read face](data.md#reading-the-read-face), never changed from here.
- **The aggregate is the transaction boundary.** One aggregate is loaded whole, changed and saved in one
  transaction — the operation's generated entry does exactly that. A change to a second aggregate follows from a
  [domain event](#keeping-state-inside-the-aggregate); a save of two at once is
  [the last resort](#the-transaction-boundary).
- **A value object carries its rule.** Required, length, format and range are said once, in the type —
  `public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 200);` — and every form, every
  save and every message reads it from there ([value objects](#value-objects)).
- **Properties are value objects or typed ids.** `DriverName`, `Litres`, `VehicleId` — only a `bool` and an `enum`
  stay primitive ([strongly-typed ids](#strongly-typed-ids)).
- **A domain refusal is `Refuse`.** It names the field it is about, and a form shows it there
  ([refusing an operation](#refusing-an-operation-refuse)).
- **The form is the operation's.** `Form.For(Supply.Change, id)` is a form whose fields are the method's
  parameters, checked by the method's own rules as they are typed ([a form over an operation](forms.md#a-form-over-an-operation)).
- **No SQL and no provider API.** The app names what it means — `InTextOrder()`, `Db.Lock`, `.Covering(…)` — and
  Rask writes it for SQLite, SQL Server and PostgreSQL alike ([provider neutrality](data.md#code-written-for-one-database)).
- **Tests run on SQLite in memory**, a database per test, in parallel — `TestDatabase.Start<T>()`,
  `Test.Refuses`, `Test.Concurrently` ([testing a model](#testing-a-model)).

> Included in [`Rask.Server`](../README.md), so there is nothing to install. The value-object bases
> (`Rask.Values`) are the `Rask` package's, so a library on `Rask` alone declares its values the same way.

## The whole of it

```csharp
public readonly partial record struct ProductId(Guid Value);                              // a typed id: one line

public sealed partial record ProductName(string Value) : Text(Value, MaxLength: 200);    // a value object: one line
public sealed partial record UnitPrice(decimal Value) : Number(Value, Min: 0, Scale: 2);

public sealed partial class Product : Aggregate<ProductId, Timestamps, Versioned, Deletable>
{
    public ProductName Name { get; private set; } = null!;
    public UnitPrice Price { get; private set; } = null!;
    public bool Discontinued { get; private set; }

    private static Product Add(ProductName name, UnitPrice price) =>        // a factory: its entry is Product.Add(…)
        new() { Id = ProductId.New(), Name = name, Price = price };

    public void Reprice(UnitPrice price)                                     // an operation: Product.Reprice(id, version, …)
    {
        Refuse.When(Discontinued, () => Price, "A discontinued product keeps its last price.");
        Price = price;
    }

    public void Discontinue() => Discontinued = true;
}
```

That is the whole declaration. The build turns it into a mapped table, a read face to query, and — because the class
is `partial` — a static entry per operation that runs it as one write:

```csharp
ProductId id = await Product.Add(ProductName.Create("Anvil"), UnitPrice.Create(9.90m));   // the factory, saved
await Product.Reprice(id, version, UnitPrice.Create(12.50m));                             // Find(id, version) → Reprice → Save()

var cheap = await Product.Where(p => p.Price < 10).OrderBy(p => p.Name);                  // the read face, in SQL
```

A page binds a form to the operation itself — its fields are the parameters, checked by `UnitPrice`'s rule and by
the `Refuse` in `Reprice` as they are typed, and saved through the entry:

```csharp
Form.For(Product.Reprice, id)[f => [
    Ui.Field.Bind(() => f.Price).Label("Price"),
    Ui.Button.Primary.Submit["Save"],
]]
```

And a test proves the rule on SQLite in memory, a database of its own:

```csharp
[Fact]
public async Task A_discontinued_product_keeps_its_price()
{
    await using var database = await TestDatabase.Start();
    var id = await Product.Add(Test.Make<ProductName>(), UnitPrice.Create(9.90m));
    var product = (await Product.Find(id))!;
    product.Discontinue();
    await product.Save();

    var refusal = await Test.Refuses(() => Product.Reprice(id, product.Version, UnitPrice.Create(5m)));

    refusal.Only("Price");
}
```

`TestDatabase.Start()` is a SQLite database in memory for this test alone (`TestDatabase.Start<AppDbContext>()` for an
app with a context of its own), so tests run side by side; [testing a model](#testing-a-model) has the rest.

Note `p.Price < 10`: the read face is **primitives**, so a one-value value object is the value it holds, and a value
object of several values arrives as the columns it is stored in ([the read face](data.md#what-the-read-face-is)).

**Generated, never reflected.** A source generator finds every aggregate at build time and hands it to
`RaskAppDbContext`; no assembly is scanned and no method is found by name, so a trimmed publish cannot quietly drop
an aggregate and leave you a missing table with a green build.

Here is a whole model running — a `Note` aggregate, queried with `Note.OrderByDescending(…)`, renamed through
`Form.For(Note.Rename, id)` and searched with `Note.Search`, in a SQLite database that lives in this browser tab and
survives a reload:

<!-- demo:data-notes -->

## Aggregates, entities and value objects

Rask.Data speaks domain-driven design, and the base class you derive from says which of the three a type is. Two
entities are equal when they are of the same type with the same `Id` — two loads of one row are the same entity —
and one with no id yet (an integer key before its insert) is equal only to itself:

| You declare | What it is | What it gets |
| --- | --- | --- |
| `Aggregate<TId, …>` | A consistency boundary: the thing you load, change and save as one | Its table, the static writes, its read face, domain events — and the [traits](#choosing-what-a-table-carries) it names: timestamps, `Version`, a form model, a `Delete` |
| `Entity<TId>` | Something with identity inside an aggregate, such as an order's line | Its table, and a form model its parent's carries ([below](#children)); it is loaded, saved and deleted with its aggregate |
| a `record`, a `record struct`, or a class on `ValueObject` it holds | A value object: `Money`, `Address`, `Email` | Columns on the owner's row ([below](#value-objects)) |

`Aggregate<TId>` derives from `Entity<TId>`, and the framework's columns are the ones its base names. They are
real properties with private setters, so a screen can show them and a query can sort by them, while only the
framework writes them:

| Property | Declared on | Effect |
|-----------|--------|--------|
| `Id` | `Entity<TId>` | The key. A `Guid` (or a typed id over one) is assigned in the factory; an integer (or a typed id over one) is the store's identity. A `[Key]` on another member is [RASK143](diagnostics.md#rask143). |
| `CreatedAt`, `UpdatedAt` | `Entity<TId>` | **With [`Timestamps`](#choosing-what-a-table-carries)** (or `Created` / `Updated` for one). Stamped (UTC) on insert and on every update. |
| `Version` | `Aggregate<TId>` | **With [`Versioned`](#choosing-what-a-table-carries).** The optimistic-concurrency token, bumped on every update. |
| `DeletedAt` | `Aggregate<TId>` | **With [`SoftDelete`](#choosing-what-a-table-carries).** The generated `Delete` stamps it instead of removing the row. |

```csharp
var recent = await Product.OrderByDescending(p => p.CreatedAt).Take(10);
```

`Aggregate<TId>` also carries the domain-events buffer: `Raise(…)` inside a method. After the save commits, each
`IEventHandler<T>` runs in memory, and each `IDurableHandler<T>` runs from the [outbox](outbox.md), which stored it in
the same transaction ([below](data.md#what-the-interceptors-do)).

**Aggregates declare no constructor.** The implicit parameterless one is what EF Core materialises rows
through, what `Product.Create(model)` starts from, and what gives `new ProductModel()` the aggregate's
defaults. Domain creation goes in a static factory, as `Product.Create` does above; a constructor that takes
arguments turns off the generated creates ([RASK086](diagnostics.md#rask086)).

**State changes through the type's own methods.** A public `set`, a hand-written public `init` or a public
mutable field on an aggregate, an entity or a value object one of them holds is a build error
([RASK084](diagnostics.md#rask084)). EF Core and the generated writes both work through private setters, so
nothing needs a public one.

## Value objects

A value object is **its values**: two with the same values are the same. Any composite an entity holds that is
not itself an entity is one, and its values become columns on the owning row: one that holds a single value is that
value's column, and one that holds several is an EF Core **complex type**.

### Declared from a base record

A value object is declared from the base record of what it holds, with its rules as the base's arguments, and is
checked as it is made — no instance exists that its rules refuse. Rask writes the rest. The bases — `Value`, `Text`,
`Number`, `Whole`, `Day`, `Range` and their `Problem` — are the `Rask` package's (namespace `Rask.Values`), and so is the
generator that writes `Create`, `TryCreate`, `CreateOptional` and `Check`: a browser app or a component library on
`Rask` alone, with no data layer, declares its values the same way with a `using Rask.Values;`. Rask.Data maps them to
columns and imports the namespace for you, with `Range` aliased over `System.Range`:

```csharp
public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);
public sealed partial record ChipCode(string Value) : Text(Value, MaxLength: 1000, Trim: false);
public sealed partial record Vin(string Value) : Text(Value, MinLength: 17, MaxLength: 17, Upper: true);
public sealed partial record TaxNumber(string Value) : Text(Value, Pattern: @"^\d{8}-\d-\d{2}$", Message: Texts.TaxNumberForm);
public sealed partial record Litres(decimal Value) : Number(Value, Min: 1, Scale: 2);
public sealed partial record UnitPrice(decimal Value) : Number(Value, Min: 0, Scale: 9, Round: true);   // computed: rounded, not refused
public sealed partial record Pieces(long Value) : Whole(Value, Min: 0, Max: 1000);
public sealed partial record InvoiceDay(DateOnly Value) : Day(Value, NotAfterToday: true);
public sealed partial record FuelingMoment(DateTime Value) : Moment(Value, NotAfterNow: true);
public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);   // End > Start
public sealed partial record ChipPeriod(DateTime From, DateTime? To) : Range(From, To);       // no end: open
public sealed partial record RefundMonths(int From, int To) : Range(From, To, Min: 1, Max: 12);
public sealed partial record KmReading(int StartKm, int EndKm) : Range(StartKm, EndKm, Strict: true,
    Message: "A záró km-nek nagyobbnak kell lennie a kezdőnél.");                              // its own words

public sealed partial record Opening(DateOnly? Date, decimal? Quantity) : Value
{
    protected override IEnumerable<Problem> Rules()
    {
        if (Date is null != Quantity is null) yield return Problem.At(Quantity, "Both or neither.");
    }
}

public sealed class Delivery : Aggregate<Guid, Model>
{
    private Delivery() { }

    public DriverName Driver { get; private set; } = null!;   // no made-up value: a new form starts it empty (RASK150)
    public Litres Fuel { get; private set; } = null!;
    public KmRange Km { get; private set; } = null!;

    public static Delivery Start(string driver) => new() { Driver = DriverName.Create(driver, nameof(Driver)), Fuel = Litres.Create(1), Km = KmRange.Create(0, 1) };

    public void Fill(decimal litres) => Fuel = Litres.Create(litres, nameof(Fuel));
    public void Drive(int start, int end) => Km = KmRange.Create(start, end, nameof(Km));
}
```

| Base | Holds | Rules |
|---|---|---|
| `Text` | `string Value` | filled (`Required: false`: an empty text is the value `""`), `MaxLength`, `MinLength`, `Pattern`, `Trim` (on by default), `Upper` (kept in capitals) |
| `Number` | `decimal Value` | `Min`, `Max`, `Scale` (most decimal places), `Precision` (most digits), `Round` (to the scale instead of refusing) |
| `Whole` | its own `int` or `long Value` | `Min`, `Max` |
| `Day` | `DateOnly Value` | `NotBefore`, `NotAfter`, `NotAfterToday` (today is `Clock.Today`) |
| `Moment` | `DateTime Value`, on the wall clock | `NotBefore`, `NotAfter`, `NotAfterNow` (now is `Clock.Wall`); `.Day` is its `DateOnly` |
| `Range` | its own two members | the end not before the start (`Strict`: after it), `Min` and `Max` for both ends; a null end is open |
| `Value` | its own members | whatever `Rules()` yields — `Problem.At(member, message)` or `Problem.Whole(message)`; with ONE member it is a one-value value object: one column, asked on its field, refused under it |

**Every base but `Value` takes a `Message`**: what a value that breaks any rule declared there is told, in the app's
own words, said once however many rules it breaks (a `Range` says it under each end it is about) — instead of the
framework's sentence for each, from Rask's strings catalog. A text left blank is still told it is required.

#### Kinds of text: Email, Phone, Url, Password, Multiline

Five bases are a `Text` that also knows what kind of text it holds — its format rule and the input it is typed into.
Each takes `Text`'s arguments (`MaxLength`, `MinLength`, `Message`, `Trim`, `Upper`, `Required`; `Password` and
`Multiline` a `Pattern` too), is made, stored and asked exactly as a `Text`, and gives the control bound to it on the
generated model what the matching DataAnnotations attribute would give a plain `string`:

```csharp
public sealed partial record ContactEmail(string Value) : Email(Value, MaxLength: 200);
public sealed partial record ContactPhone(string Value) : Phone(Value);
public sealed partial record Homepage(string Value) : Url(Value, Required: false);
public sealed partial record SignInPassword(string Value) : Password(Value, MinLength: 8);
public sealed partial record ChosenPassword(string Value) : Password(Value, MinLength: 12, New: true);
public sealed partial record Remark(string Value) : Multiline(Value, MaxLength: 4000);

Ui.Field.Bind(() => m.Email)    // <input type="email" maxlength="200" autocomplete="email">, the email rule on change, on submit, on save
```

| Base | Its rule | Column | A bound control | Attribute path |
|---|---|---|---|---|
| `Email` | `[EmailAddress]`'s own check: one `@`, neither first nor last | 254 unless declared | `type="email"`, `autocomplete="email"` | `[EmailAddress]` |
| `Phone` | `[Phone]`'s own check | 32 unless declared | `type="tel"`, `inputmode="tel"`, `autocomplete="tel"` | `[Phone]` |
| `Url` | an absolute `http` or `https` address (`[Url]` also lets `ftp` and anything that only starts like one through) | 2048 unless declared | `type="url"` | `[Url]` |
| `Password` | its lengths, its pattern and its `Rules()`; not trimmed unless `Trim: true` | — never stored | `type="password"`, `autocomplete="current-password"` (`New: true`: `"new-password"`), its value never written by the server | `[DataType(DataType.Password)]` |
| `Multiline` | as `Text` | as declared | `Ui.Field.Bind` draws a `Ui.Textarea` | `[DataType(DataType.MultilineText)]`, `[UIHint("MultilineText")]` |

The two paths give the identical `FieldShape` (`Input`, `AutoComplete`, `InputMode`, `Hint`, the lengths), so a
control cannot tell them apart; the value-object path also checks the rule wherever the value is made — a command,
a job, an import — not only where a form posts it. The format messages are the strings catalog's
(`ValidationEmail`, `ValidationPhone`, `ValidationWebAddress`), or the record's own `Message`.

**A password is input only.** Its `ToString()` and its debugger view say `••••`, so a log line or `$"{password}"`
never shows it; read `.Value` where it is checked or hashed. An aggregate or entity that stores one — directly, in a
value object or in a list — is [RASK175](diagnostics.md#rask175) (store the hash), and the generated read face leaves
it out; a query's or a command's result, a subscription's item or an event that carries one is
[RASK176](diagnostics.md#rask176). A command that takes one in — `record SignIn(ContactEmail Email, SignInPassword
Password) : ICommand<Token>` — is what it is for.

The record's positional parameter `Value` is the base's get-only `Value` (so `name with { Value = … }` does not
compile); a `Range` or `Value` record keeps its own typed members. The constructor asks the rules and throws a
`RaskValidationException` — one failure per problem, under the member it names — and the generated half adds,
over the primary constructor's parameters:

- `X.Create(…, field)` — the same, with `field` in front of each failure: `KmRange.Create(10, 5, "Km")` is refused
  under `Km.End`, `DriverName.Create(text, "Driver")` exactly under `Driver`, so a form whose submit lets it through
  shows each message there. The `field` is optional.
- `X.TryCreate(…, out var x, out var problems)` — the answer without an exception.
- `X.CreateOptional(…, field)` — nothing when nothing was given (text: only blanks), a required failure under each
  member left empty when only some were (`KmRange.CreateOptional(5, null, "Km")` → `Km.End`), otherwise `Create`.

`new KmRange(…)` would throw without naming the field, and a `with` copies a value past its rules:
[RASK127](diagnostics.md#rask127) refuses both outside the type, tests included, and its quick-fix writes the
`Create` (`km with { End = 5 }` → `KmRange.Create(km.Start, 5)`). It must be `sealed partial`
([RASK129](diagnostics.md#rask129)), since Rask writes its other half. Two values are equal when they are of the
same type and hold the same values; a one-value record's `ToString()` is its value — `$"{chip}"` is `C-17`, never
`FuelingChip { Value = C-17 }`. Every base writes it culture-invariant — `Number` and `Whole` as
`1234.5` on every machine, never `1234,5`, a `Day` as ISO 8601 `2026-01-31` and a `Moment` as `2026-01-31T14:05:00`: it is the value's text, and showing it to a reader is the UI's and
`Format`'s job. One declared straight from `Value` (`FuelingChip(string Value) : Value`) gets it generated the same
way; a `ToString` the record declares itself is kept.

**`Required: false` lets a text be empty**: `public sealed partial record FuelingChip(string Value) : Text(Value,
MaxLength: 1000, Required: false);` makes `FuelingChip.Create("")` (or only blanks) the value `""` with no problem, while
a filled one is still held to its lengths and its pattern. A field bound to it on the generated model is neither
refused nor announced as required when left empty, and saves `""`; `CreateOptional("")` is still nothing.

**`Upper: true` keeps the text in capitals**, culture-invariant (`"wvwzzz1jzxw000001"` → `"WVWZZZ1JZXW000001"`,
`"árvíztűrő"` → `"ÁRVÍZTŰRŐ"`): after the trim and before the lengths and the pattern, so a `Pattern` is written
for the capitals. A stored value is loaded as it was stored. The input is not drawn in capitals yet — the text is
put in capitals when it is made.

**`Round: true` rounds a number to its `Scale` instead of refusing its extra digits** — half away from zero, as SQL
Server rounds what it stores — before the bounds and the precision are checked. It is for a number nobody typed: a
unit price or a share of a total computed in the domain, which used to be rounded silently by the column and would
otherwise be refused for its fourteenth decimal. `UnitPrice.Create(1.0000000005m)` holds `1.000000001m`. A `Round`
with no `Scale` throws as the value is made.

**A one-value record of an ordered value orders like it.** Over a number, a whole number, a day, a moment or a
text (any value that is `IComparable` of itself) the generated half makes the record `IComparable` and gives it `<`,
`>`, `<=` and `>=` against its own type: `fuelings.OrderBy(f => f.Moment)` sorts a list as the database sorts the
column, and `f.Moment >= since` reads as it means — and translates on the write model, see
[value objects of any shape](#value-objects-of-any-shape). A record that writes its own `CompareTo` or operators keeps them.

**An enum is a one-value record over `Value`**: `public sealed partial record FuelKind(Fuel Value) : Value;`, with
any rule of its own in `Rules()`. It is one column of the enum's own type — EF Core's enum-to-number, or whatever
`HasConversion` the app gives the enum — the generated model carries `Fuel? Fuel`, and a control bound to it is
offered the enum's members, labelled by their `[Display(Name)]`, as for a bare enum.

**A test that needs a valid one and does not care which** asks `Test.Make<Vin>()` (Rask.Testing): it reads the rules
the record hands its base and makes one through `Create` — see [testing](testing.md#a-valid-value-object-testmake).

**A `Moment` is a wall-clock time, never a UTC one.** It holds the moment as the clock on the wall shows it in
`Clock.Zone` (what `Clock.Wall` reads), with `DateTimeKind.Unspecified` — how a `datetime2` (SQL Server) or `TEXT`
(SQLite) column keeps it and reads it back. A `DateTime` of `DateTimeKind.Utc` is converted to `Clock.Zone` as it is
made, and one of `DateTimeKind.Local` from the machine's zone, so `FuelingMoment.Create(Clock.Now.UtcDateTime)` and
`FuelingMoment.Create(Clock.Wall)` are the same value, and a column of moments never mixes the two. A bound field of
one draws a `Ui.DayAndTime`, ending now for `NotAfterNow`.

**A stored value is loaded, never re-checked.** EF Core builds what a row holds without asking the rules — a row
stored before a rule was tightened still loads, a tracked entity holding one still saves, and a query that fails on
a row leaves the next `Create` checking again. Only what the code makes goes through them.

**Over remote dispatch it travels as what it holds.** A command, query, event or result crossing between the
browser and the server (`Rask.Cqrs.Client`/`Rask.Cqrs.Server`) may carry one: a one-value record is its bare value
on the wire — `"ABC-123"`, `12.5`, never `{"value":"ABC-123"}` — and a `Range` or `Value` record an object of its
members. The server rebuilds what a browser sent through the generated `Create` **with its rules asked**, so no handler is
given a value they refuse: the request is answered `400` with each refusal under its field (`Driver.Name`). The
browser rebuilds what the server answers as a loaded row is, unchecked, so a value stored before a rule was
tightened still reaches the page. See [the wire codecs](cqrs.md#wire-codecs-and-rask053).

**The column takes what the value object declares**, read from its base arguments when the app is compiled —
constants included (`Text(Value, MaxLength: Limits.Name)`), and without building an instance: a `Text(Value,
MaxLength: 1000)` is `HasMaxLength(1000)`, a `Number(Value, Scale: 2, Precision: 9)` is `HasPrecision(9, 2)` (a
scale alone keeps EF Core's precision of 18). A one-value record is one column; a `Range` or `Value` record is a
complex type, one column per member. An attribute on the property — `[MaxLength]`, `[StringLength]`,
`[Precision]` — is the app's word for that one column and wins over the value object's, and the entity's own
`Configure` runs after both and has the last word. [RASK128](diagnostics.md#rask128) warns when either gives the
column a length or precision other than the value object's, since a value the rule accepts would then not fit.
The generated model carries the attribute like any other (`[Required]` included, which also makes the column
`NOT NULL`), so a form and `Create(model)`/`Update(id, model)` ask it beside the value object's rule: the tighter
of the two refuses.

**A value object held inside another keeps its own column shape.** `Opening(DateTime Date, Amount Quantity) : Value`
with `Amount : Number(Value, Scale: 16, Precision: 32)` stores `Start_Quantity` as `decimal(32,16)` — the `Amount`
says so, and no `HasPrecision` in `Configure` is needed; a plain `decimal Quantity` member has no rules to read, so
it keeps EF Core's default.

**An optional complex value is null when its row has none.** `public Opening? Start { get; private set; }` is an EF
Core optional complex property: every column under it is nullable, a row with no opening holds `NULL` in all of
them and loads `Start` as `null`, and the read face's `StartDate` and `StartQuantity` are `DateTime?` and
`decimal?` however `Opening`'s own members are declared. When a member is not nullable, that one column tells a
row with an opening from one without. **When every member is optional** — `Opening(DateTime? Date, decimal? Quantity)`
held as `Opening?`, the shape a legacy table with two nullable columns has — every column `NULL` is none: a row
holding only a date, or only a quantity, loads as that `Opening`, a row holding neither loads as `null`, and
`Where(p => p.Start == null)` asks that every column is `NULL`. No column is added for it, so a schema another tool
made is read as it is. An `Opening(null, null)` saved is therefore read back as `null`: a value whose members are all
empty is no value. EF Core alone refuses such a property (it asks for a discriminator column); every
`RaskDbContext` holds it on any relational provider, and `UseRaskSqlite`, `UseRaskSqlServer` and `UseRaskPostgres`
hold it on a plain `DbContext` too. Don't start it from a placeholder (`= Opening.None`, which
[RASK150](diagnostics.md#rask150) flags): leave it `null`.

**An empty text, or nothing?** A nullable text left empty is stored as nothing: `CreateOptional` turns blank text
into `null`, and the column holds `NULL`. A table whose rows tell `''` from `NULL` — an older client that refuses
`NULL`, a report that counts the two apart — keeps them apart with `KeepEmpty` in the aggregate's `Configure`:

```csharp
public VatNumber? VatNumber { get; private set; }
public string? Remark { get; private set; }

public static void Configure(EntityTypeBuilder<Company> b)
{
    b.KeepEmpty(c => c.VatNumber);   // a string, or a value object of one text
    b.KeepEmpty(c => c.Remark);
}
```

A field left empty, or emptied, then writes `''` — the value object holding it, whose rules are not asked of an
empty text — and a new row starts with `''`. A row that holds `NULL` there and is saved with the field still empty
keeps its `NULL`, so a save that changes nothing writes nothing; a filled field still answers to every rule. A
stored `''` loads as the value object holding it and fills the model with `""`. The writes read the call from the
aggregate's own static `Configure`, as they read `IsUnique` — written anywhere else it is
[RASK165](diagnostics.md#rask165); on a member not stored as text it throws when the model is built.

**Columns named before the value object existed.** EF Core names a complex type's columns `Km_Start` and `Km_End`.
A table whose columns were already `StartKm` and `EndKm` — read by reports and raw SQL — keeps them with one line in
the entity's `Configure`:

```csharp
public KmRange Km { get; private set; } = null!;

public static void Configure(EntityTypeBuilder<DeliveryNote> b) =>
    b.Columns(n => n.Km, "{Member}Km").Columns(n => n.Trip, "{Member}Date");   // StartKm, EndKm; StartDate, EndDate
```

`{Member}` is each member of the value object; a value object nested inside it keeps EF Core's naming below the
pattern (`ReadingKm_Value`). The read face follows the same names — `DeliveryNote.Where(n => n.StartKm >= 100)`, not
`KmStart` — because the generator reads the call from `Configure`, so write it there with the pattern as a constant
(a pattern that makes no C# name, `"Km {Member}"`, names the columns only and leaves the face's `KmStart`). A
pattern without `{Member}`, or a member that is not a value object of several members, throws as the model is built.
The form model is unchanged: `Km.Start` and `Km.End`.

A generated form model carries a one-value record as its value (`string? Driver`): a form bound to it asks the same
rules under the field and gives a text input the `MaxLength`/`MinLength` as its `maxlength`/`minlength` (its field
shape, `X.__Field` — see [forms validation](forms-validation.md)), and the generated
`Create(model)`/`Update(id, model)` ask them before anything is written — each message in the reader's language
from Rask's strings catalog. A property that is not nullable is required; a nullable one may be left empty.

An aggregate that stores a bare `string`, number, moment or `Guid` gets [RASK130](diagnostics.md#rask130) — a
suggestion, and a warning under `<RaskStrict>` — pointing at the value object that would carry its rules.

A value object of any other shape — below — holds no rule of its own: a `public static Validate` beside its
value is never asked ([RASK132](diagnostics.md#rask132)).

### Value objects of any shape

One an abstract base declares is mapped the same way, at any depth, on every aggregate deriving from it: each
concrete aggregate is its own table holding the base's columns, and its read face and form model carry them too.
A generic base (`Filing<TSelf> where TSelf : Filing<TSelf>`) is no different: `VatReturn : Filing<VatReturn>` holds
the base's children and collections of values behind its private fields as its own.
The base itself is never an entity of the model.

It must compare by value, or the build says so ([RASK105](diagnostics.md#rask105)). A `record` or a
`record struct` already does, and needs nothing else. A plain class derives from `ValueObject` and names the values
that make it the same:

```csharp
public sealed class Address : ValueObject
{
    private Address() { }

    public string Street { get; private set; } = "";
    public string City { get; private set; } = "";

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Street;
        yield return City;
    }
}
```

A record that compares some other way says so in its own `Equals` — the language uses it for `==` too:

```csharp
public sealed record Email(string Value)
{
    public bool Equals(Email? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}
```

**The database may disagree with C#.** A string record compares ordinally and case-sensitively, while SQL Server's
default `*_CI_*` collation — and a unique index under it — treats `"Abc"` and `"abc"` as one. Compare the value
object the way its column does, as `Email` above does.

```csharp
public sealed record Money(decimal Amount, string Currency);
public sealed record Email(string Value);

public sealed class Customer : Aggregate<Guid, Model>
{
    public Money CreditLimit { get; private set; } = new(0m, "EUR");   // CreditLimit_Amount, CreditLimit_Currency
    public Email Email { get; private set; } = new("");                 // one column: Email
}
```

**A one-value value object is one column named after the property**, so `Email Email` is stored as `Email`, and a
form model carries it as its value, `string? Email`. A value object with several values is prefixed by the
property, and a form model carries it as a nested model.

**Declare it nullable and it is optional** — a class or a struct alike:

```csharp
public sealed partial record Percent(decimal Value) : Number(Value, Min: 0, Max: 100);
public readonly record struct Grade(int Value);

public Email? Backup { get; private set; }        // nullable column Backup,   form field string?  Backup
public Percent? Discount { get; private set; }    // nullable column Discount, form field decimal? Discount
public Grade? Rank { get; private set; }          // nullable column Rank,     form field int?     Rank
```

An empty field is stored as `NULL` and is not put to the rule; a filled one is asked `Percent`'s rules by the form
and by `Create`/`Update`, and the read face shows `decimal? Discount`. A value object that checks a rule is a
`sealed record`: a struct has a `default` that no constructor checked, so `Grade` holds no rule. A nullable struct of **several**
values (`Money?` over a `record struct`) is not unwrapped: the form model carries the struct as it is, and its
mapping is yours to write in `Configure`.

That one column is an ordinary scalar column with a conversion — the value object goes in and out through its
one value — so everything a column can have, it can have, configured on the property itself:

```csharp
public static void Configure(EntityTypeBuilder<Customer> builder)
{
    builder.Property(c => c.Email).HasMaxLength(255);
    builder.HasIndex(c => new { c.Email, c.TenantId }).IsUnique("This address is already registered.");
}
```

The unique rule's failure names the field `Email` — the form's own name for it — not `Email.Value`. The value
object is rebuilt through the constructor that takes its value, or, for the private shape, a parameterless
constructor and its property; both may be private.

**A query on the aggregate reads the value as its column.** `.Value` is the column itself, typed as the value — on
SQL Server, SQLite and PostgreSQL alike, with the column's own store type and no `CAST` — so a write-side query
filters, sums and sorts by it; and a value object of one ordered value (a number, a whole number, a day, a moment,
a text) is generated `IComparable` with `<`, `>`, `<=` and `>=` against its own type, so the whole value compares and
sorts too, in the database and in a list:

```csharp
// on the read face the value IS the primitive
await Customer.Where(c => c.Email.StartsWith("ada@")).OrderBy(c => c.Email);

// on the write model (plain EF Core over the aggregate) the value inside translates…
await db.Set<Transaction>().Where(t => t.Quantity.Value > 0).SumAsync(t => t.Quantity.Value, ct);

// …and so does the whole value: compared, ordered, set by ExecuteUpdate
await db.Set<Fueling>().Where(f => f.Moment >= since).OrderBy(f => f.Moment).ThenBy(f => f.Id).ToListAsync(ct);
await db.Set<Fueling>().Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Moment, moment), ct);

// …and outside the database it orders the way the column does
fuelings.OrderBy(f => f.Moment);
```

Only Rask's own conversion is read through: a column the app maps with a converter of its own stores what that
converter writes, which need not be the value, and `.Value` there is still "could not be translated". Two value
objects of different types are never compared — the operators take the one type.

**The same holds inside another value object.** `Currency` in `record Money(decimal Amount, Currency Currency)`
is a converted scalar column of `Money` — `Price_Currency`, the column it always was — which is what lets EF Core
hand it to `Money`'s constructor. Its facets go on that property:

```csharp
builder.ComplexProperty(c => c.Price).Property(m => m.Currency).HasMaxLength(3);
```

One shape stays out of reach: a value object built through its **constructor** cannot hold another value object
of **several** values (`record Order(Money Total, …)`) — EF Core binds a constructor parameter to a column, never
to a complex type. The build refuses it ([RASK103](diagnostics.md#rask103)) rather than let the app fail when it
starts: give the outer type a private parameterless constructor and private setters, and it maps.

What is **not** a value object: a type EF Core maps as a column on its own (`string`, `DateTime`, `Guid`, an
enum, anything from `System` or `Microsoft`), a collection, an abstract or generic type, and an `Entity<TId>`.

**A value object with several values is a complex type, not an owned entity**, and the distinction is the point. An owned entity is a row
with hidden identity: tracked separately, nullable in ways a value has no business being, and quietly
producing a join. A complex type is part of the row, which is what a value object *is*, and it is why `Money` can
be held by two aggregates without either owning it.

Nesting works: a value object made of value objects is mapped all the way down. **One EF Core
constraint applies to the outer one**: a complex type is materialised through its constructor, and EF
cannot bind a nested complex type to a constructor parameter. So a value object that *contains another
value object* needs a parameterless constructor and private setters, which is all EF Core needs:

```csharp
// holds only scalars: a positional record is fine
public sealed record Money(decimal Amount, string Currency);

// contains a value object: a parameterless constructor and private setters
public sealed class Packaging
{
    private Packaging() { }

    public Packaging(Money cost, string material) => (Cost, Material) = (cost, material);

    public Money Cost { get; private set; } = new(0m, "EUR");

    public string Material { get; private set; } = "card";
}
```

A *collection* of value objects is not mapped automatically; configure it in the aggregate's own
`Configure`.

### Splitting a total: `Allocation.Split`

A delivery shared among the vehicles that took fuel, a cost spread over categories: each share is rounded to what the
column keeps, and the shares must still add up to the total — rounding each one on its own loses or invents a cent.
`Allocation.Split` (namespace `Rask.Values`, in the `Rask` package) does it in one call:

```csharp
IReadOnlyList<decimal> shares = Allocation.Split(100m, 3, step: 0.01m);              // [33.34, 33.33, 33.33]
var byWeight = Allocation.Split(total, litres, step: 0.01m, remainder: Remainder.ToLargest);
```

- **The sum is the total, exactly.** Each share is its exact part rounded toward zero to a whole number of `step`s;
  what that leaves over goes out a step at a time to the parts `Remainder` names — `ToFirst` (the default: the first
  parts, in the order given), `ToLast`, or `ToLargest` (the parts that lost most to the rounding, the earlier one first
  on a tie). No share is more than one step from its exact part.
- **Equal parts** take a count (`Split(total, receivers.Count, step: 0.01m)`); **weighted** parts take the weights. A
  weight of zero gets zero and none of the remainder; a negative weight, or none above zero, is refused.
- **A total off the step's grid** (`10.005m` by `0.01m`) keeps its last fraction on the part that takes the first step
  of the remainder (`[5.005, 5.00]`), so the sum still is the total. A negative total splits into negative shares.
- **Pure and deterministic** — no database, no clock, no culture — so it belongs inside an aggregate's method, and the
  same arguments give the same shares on every host. Give the order a meaning (by moment, then number, then id) and the
  remainder lands on the same rows every time.

## Strongly-typed ids

A typed id is one line:

```csharp
public readonly partial record struct VehicleId(int Value);
public readonly partial record struct DriverId(Guid Value);
public readonly partial record struct PlateId(string Value);
public readonly partial record struct ShiftId(Guid Value);

public sealed class Vehicle : Aggregate<VehicleId> { }
public sealed class Shift : Aggregate<ShiftId>
{
    public VehicleId Vehicle { get; private set; }   // cannot be handed a DriverId: a compile error
}
```

**What counts as one.** A top-level, non-generic `readonly partial record struct` whose name ends in `Id`, with
exactly one positional parameter named `Value` of `int`, `long`, `Guid` or `string`. Leave out `partial` or
`readonly`, name it otherwise, or wrap another type, and it is an ordinary record struct that Rask still converts
to its value in the database (below) but does not complete.

**What Rask completes it with** (the `Rask` package's generator, so a domain library that references only `Rask`
gets it too):

| Member | Does |
|---|---|
| `VehicleId.New()` | a new version-7 `Guid` — an id over a `Guid` only; the others come from the store or the caller |
| `VehicleId.Parse(s)`, `VehicleId.TryParse(s, out id)` | the id `s` spells; `IParsable<T>` and `ISpanParsable<T>` too. An id over a `string` refuses `""` (whitespace is kept: it may be a real key) |
| `id.ToString()` | the value alone — `42`, `3e18ad13-…` (lower case), `ABC-123` — not `VehicleId { Value = 42 }` |
| JSON | the value alone: `{"Vehicle":42}`, and a `Dictionary<VehicleId, …>` is keyed by it; a number sent as text is read too |
| routing | `[RouteParam] public VehicleId Id { get; set; }` binds `/vehicles/42`, and `Routes.VehiclePage(id)` writes `42` back |

A member you write yourself — your own `ToString`, `Parse` or `[JsonConverter]` — is kept, and only the rest is
generated.

The JSON converter is a `[JsonConverter]` attribute, which `JsonSerializer` finds by reflection. A
`JsonSerializerContext` of your own (System.Text.Json's source generation) cannot see what another generator added, so
register it there: `[JsonSourceGenerationOptions(Converters = [typeof(VehicleId.__JsonConverter)])]` — or, simpler, keep
the reflection serializer for types that hold typed ids. Rask's own wire (commands, events, jobs) carries a typed id as
its bare value without either.

**In the model.** Use one as the aggregate's key and it is converted to its underlying value automatically. The
converter is registered once for the type, in `ConfigureConventions`, so **every** property of that type is
converted — the key, a foreign key on another entity, a nullable one — without any of them being named. An id the
generator cannot build a converter for is reported as [RASK073](diagnostics.md#rask073) rather than left to fail
at model build. Ids that need no converter — `Guid`, `int`, `long`, `string` — are left alone.

Who produces the key follows the value it holds:

- **Over an `int` or a `long`** the key is the store's identity, exactly as the integer would be: `Vehicle.Create(model)`
  inserts it and reads the number back, and there is no `Create(id, model)`. An application-numbered table says
  `b.Property(x => x.Id).ValueGeneratedNever()` in its `Configure`, as for an integer key.
- **Over a `Guid`** the key is the application's: `VehicleId.New()` in the factory, and the generated
  `Create(model)` calls `VehicleId.New()` too.
- **Over a `string`** the caller names it: `Create(id, model)`.

**A `Guid` id in a text column is written lower case.** Where the column holds text — every `Guid` on SQLite, or an
existing schema's text key — the id is written as `ToString()` prints it,
`3e18ad13-d95e-4808-97bd-918b457c006b`, rather than the provider's own spelling (SQLite's is upper case). A row an
older tool wrote in upper case still loads. Whether a `Where` still finds it is the column's comparison: under
[`OrderTextBy`](sqlite.md#text-that-compares-as-sql-server-does) with a case-insensitive collation (`Hungarian_100_CI_AS` — on SQLite through `UseRaskSqlite`,
every text column is `RASK_TEXT`) and on SQL Server's usual `_CI_` collations it does; a column that compares as
written (SQLite's default `BINARY`, PostgreSQL's `text`) does not, so lower-case such a column once. A column that
stores a `Guid` natively (`uniqueidentifier`, `uuid`) is untouched.

## Children

An aggregate holds its parts. Declare them as `Entity<TId>`, keep them in a field, and hand out a read-only
view — nothing else is configured, and no `HasMany`, no foreign key and no key generation is written anywhere:

```csharp
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }                                  // EF materialization

    public string Reference { get; private set; } = "";

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public static Order Place(string reference) =>
        new() { Id = Guid.CreateVersion7(), Reference = reference };

    public OrderLine Add(string product, int quantity)   // the aggregate guards its own invariants
    {
        var line = OrderLine.For(product, quantity);
        _lines.Add(line);
        return line;
    }
}

public sealed class OrderLine : Entity<Guid>
{
    private OrderLine() { }

    public string Product { get; private set; } = "";

    public int Quantity { get; private set; }

    public void SetQuantity(int quantity) => Quantity = quantity;

    internal static OrderLine For(string product, int quantity) =>
        new() { Id = Guid.CreateVersion7(), Product = product, Quantity = quantity };
}
```

An auto-property works the same: `public IReadOnlyList<OrderLine> Lines { get; private set; } = [];`, refilled
with `Lines = [.. Lines, line]`. Rask hands EF Core a collection it can add to whenever it loads or saves the lines.

**Children made from a form's rows: `Children<TEntity, TSpec>`.** When the aggregate's children are whatever a list
of rows says — a delivery's parts, a driver's chips — declare them as one property, with no field and no view, and
let `Sync` do the add/change/remove loop:

```csharp
public sealed record SupplyPartSpec(SupplyPartId? Id, TankId Tank, decimal Quantity);   // a row of the form

public sealed class Supply : Aggregate<SupplyId>
{
    public Children<SupplyPart, SupplyPartSpec> Parts { get; } = new();

    public void Change(IReadOnlyList<SupplyPartSpec> parts)
    {
        var sync = Parts.Sync(parts, s => s.Id,
            add: SupplyPart.For,                    // a spec whose key is null, or names no part of this supply
            update: (p, s) => p.Hold(s),            // a spec whose key is a part's Id
            remove: p => p.Release());              // optional: a part no spec names, as it is let go
        if (sync.Removed.Count > 0) Raise(new PartsReleased(sync.Removed));
    }
}
```

`Parts` is an `IReadOnlyList<SupplyPart>` to everyone, in the specs' order — the order a form posted them, so a
part's rule fails under `Parts[1].Quantity` as for any child. It is also the collection EF Core maps (through the
property's own backing field): the part `Sync` adds is inserted, the one it changes is updated, the one it lets go
is deleted, all by the aggregate's save. Each part's place is stored too, in a `Position` column Rask adds to the
child's table (`Columns.Position`, a shadow property: a migration adds it), so the parts load back in the order the
last `Sync` gave them, whatever order the store reads them in. `Sync` returns what it did: `Added`, `Changed` (a part whose update moved
one of its public properties — one its row left as it was is not there), `Removed`, and `Touched` (added or
changed), which is what a rule over old rows asks so a row stored before the rule was tightened is let be. The key
is the child's own `Id` type — a `SupplyPartId?`, not its `Guid` — and two rows naming one part, or a key of another
type, are refused with an `ArgumentException` before any part is touched. The generated form model (`Model`) does not
carry a `Children<,>` collection yet: an aggregate that declares one writes through its own methods and `Save()`, and
the form over such a method is `Form.For(Supply.Change, id)` — a row per part, refused under its place
([a form over an operation](forms.md#a-form-over-an-operation)).

Children matched by something other than their `Id` — a delivery note's fuellings by their transaction — take a key
of their own on both sides, with the same refusal of a key named twice:

```csharp
Fuellings.Sync(fuellings, f => f.TransactionId, f => f.TransactionId,
    add: DeliveryNoteTransaction.For, update: (f, s) => f.Use(s.Quantity));
```

A child the aggregate makes itself, from no row, is `Add`ed — `Fuellings.Add(await Transaction.FreeFor(…))` — and
one it lets go is `Remove`d. Both are the aggregate's alone: called from any other type, or on children held in a
local, they are [RASK180](diagnostics.md#rask180), an error.

**Loading one root loads it whole.** Every write that starts from an id brings the children with it, and so
does `db.Orders.FindAsync(id)`. On the read face the children are an ordinary navigation, so you ask for them
when you want them — listing a thousand orders should not drag in every line each of them holds:

```csharp
await Order.Update(id, o => o.Add("anvil", 1));                      // loaded whole, changed, saved

var open = await Order.Where(o => o.Reference.StartsWith("2026"));   // Lines EMPTY
var withLines = await Order.Query((q, ct) => q.Include(o => o.Lines).ToListAsync(ct));

// …and a child is queryable on its own, because the read side has no borders:
var heavy = await OrderLine.Where(l => l.Quantity > 10 && l.Order.Reference.StartsWith("2026"));
```

**A change to any part is a change to the whole.** A line's quantity moving stamps the order's `UpdatedAt` and
bumps its `Version` (it names `Timestamps` and `Versioned`), so the version a caller read stops being current — which is what makes the aggregate, and
not the row, the unit of concurrency:

```csharp
await Order.Update(id, o => o.Lines.First().SetQuantity(5));
// UPDATE OrderLine SET Quantity = 5 …
// UPDATE Order     SET Version = Version + 1, UpdatedAt = @now WHERE Id = @id AND Version = @read
```

**The form model carries them, and a save syncs them.** `OrderModel` gets a `List<OrderLineModel>`, and each
child model carries an `Id` so a save knows which stored line each row is:

```csharp
var model = order.ToModel();

model.Lines.Single(l => l.Product == "anvil").Quantity = 9;    // edits that line
model.Lines.Add(new OrderLineModel { Product = "rope", Quantity = 2 });   // no Id: a new line
model.Lines.RemoveAll(l => l.Product == "spring");             // not posted: that line is DELETED

await Order.Update(id, model);
```

> **What the posted list holds is what the aggregate holds afterwards.** A row with no `Id` is added, a row
> whose `Id` matches a stored child updates it, and **a stored child whose id is in none of the posted rows is
> removed** — so a form that renders only some of the lines deletes the rest, and an empty list deletes them
> all. Bind the whole collection, or apply the change through a domain method (`Order.Update(id, o =>
> o.Add(…))`) instead of a partial model.

An `Id` that matches nothing in *this* aggregate is never followed: it lands as a new child with an id of its
own. A posted id therefore cannot reach — or delete — another aggregate's line.

**A child cannot outlive its parent.** Rask makes the relationship required and its delete a cascade, so taking a
line out of the collection deletes the row. Left to EF Core's own convention the foreign key would be nullable,
and severing a child would set that key to `NULL` and leave the row in the table — invisible through the
navigation, unreachable through the aggregate, and impossible to delete through it either.

Deleting the aggregate follows the same rule: `Order.Delete(id)` removes the order and its lines with it.
An order that names [`SoftDelete`](#choosing-what-a-table-carries) instead of `Deletable` is different, and deliberately
so: `DeletedAt` is stamped rather than the row removed, so nothing cascades and the lines are still there if the
order comes back.

The child gets a table, a model so its parent's form can carry it, and a read face of its own —
`OrderLine.Where(…)` — because the read side has no borders. It gets no **writes** of its own: there is no
`OrderLine.Create`, no version and no soft delete, because it is not a thing you save on its own. A collection of
another **aggregate** is not a child at all ([RASK087](diagnostics.md#rask087)), and a collection Rask cannot
write is [RASK088](diagnostics.md#rask088).

The field behind a read-only view can be named anything — `Notes => _items.AsReadOnly()` — and can live on an
abstract base, generic or not: the collection is loaded into and saved from that field, not the view.

**A child is written only through its aggregate.** A save that adds, changes or removes a child whose root the
same context does not hold — a line found through `db.OrderLines` and edited, a `new OrderLine` attached on its
own, a `BulkInsert` of lines — is refused with an `InvalidOperationException` naming the child and its root.
`Tenant.Across()` does not lift it: it is about the aggregate's shape, not whose rows are visible. Load the
order, change it, save the order:

```csharp
await Order.Update(id, o => o.Lines.First().SetQuantity(5));   // ✓ through the aggregate

var line = await db.OrderLines.FirstAsync(ct);
line.SetQuantity(5);
await db.SaveChangesAsync(ct);   // ✗ 'OrderLine' was changed on its own … load its 'Order'
```

**Timestamps are the aggregate's**, like its tenant. A child has no `CreatedAt` or `UpdatedAt` — not in its
table, not on `OrderLineRead`, never stamped — and the root's `UpdatedAt` moves whenever any of its children is
added, changed or removed, so it means "anything in this order changed". A child whose table does carry them
names the trait, and keeps them — a timestamp is the only trait an `Entity` takes:

```csharp
public sealed class OrderLine : Entity<Guid, Timestamps>   // this child table has CreatedAt and UpdatedAt
{
}
```

### A key of several columns

A child that is the meeting of two aggregates (which transactions fuelled which delivery note) is keyed by the pair,
not by an `Id` of its own. Declare the key as a tuple of the **types** it joins, and make the class `partial`:

```csharp
public sealed class DeliveryNote : Aggregate<int, PerTenant>   // no Model: a form does not carry it yet, see below
{
    private readonly List<DeliveryNoteTransaction> _fuellings = [];

    public IReadOnlyList<DeliveryNoteTransaction> Fuellings => _fuellings;

    public void Fill(IEnumerable<(int TransactionId, decimal Quantity)> rows)
    {
        _fuellings.Clear();
        _fuellings.AddRange(rows.Select(r => DeliveryNoteTransaction.For(r.TransactionId, r.Quantity)));
    }
}

public sealed partial class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)>
{
    public decimal Quantity { get; private set; }

    internal static DeliveryNoteTransaction For(int transactionId, decimal quantity) =>
        new() { TransactionId = transactionId, Quantity = quantity };
}
```

Rask generates the rest. Each aggregate in the tuple becomes a `{Aggregate}Id` of **that aggregate's own key
type**, so it is never written twice. `Id` becomes the key's values together:

```csharp
// generated
public int DeliveryNoteId { get; private set; }
public int TransactionId { get; private set; }
public new (int DeliveryNoteId, int TransactionId) Id => (DeliveryNoteId, TransactionId);
```

The table's primary key is `(DeliveryNoteId, TransactionId)`, in the order the tuple names them, and nothing
generates either value. A part that is a plain value is named, and the entity declares it:
`Entity<(Order, int LineNo)>` with `public int LineNo { get; private set; }`. The same key reaches a context you
wired yourself, through `ApplyRaskConventions`.

It reads like any child. The read face lists the key's parts where `Id` would be. Each part infers its navigation
by the usual `{X}Id` rule. The tenant comes through the aggregate that holds it, so the row needs no `TenantId`
of its own:

```csharp
var fuellings = await DeliveryNoteTransaction.Where(t => noteIds.Contains(t.DeliveryNoteId))
    .Select(t => new { t.DeliveryNoteId, t.TransactionId, t.Quantity, t.Transaction.ProductId });

var note = await DeliveryNote.Include(n => n.Fuellings).First(n => n.Id == id);
```

**It is written through its aggregate**, like any child. The entity makes itself in a factory of its own, setting
the parts it knows — the generated ones have private setters, so its own type can set them — and Rask fills the
part that names the holding aggregate when the save attaches it. Replacing the whole collection, as `Fill` does,
is the usual shape, and the save matches the new rows to the stored ones **by key**:

```csharp
await DeliveryNote.Update(id, n => n.Fill(rows));
// stored (10, 12.5) (11, 3.25) (13, 2) ← rows (10, 12.5) (11, 4) (12, 1)
// UPDATE DeliveryNoteTransactions SET Quantity = @p0 WHERE DeliveryNoteId = @p1 AND TransactionId = @p2   (11)
// INSERT INTO DeliveryNoteTransactions (DeliveryNoteId, TransactionId, Quantity) VALUES (…)               (12)
// DELETE FROM DeliveryNoteTransactions WHERE DeliveryNoteId = @p0 AND TransactionId = @p1                 (13)
//                                                                                     nothing for (10)

var note = DeliveryNote.Issue("A-1", rows);   // a factory of the note's own, which calls Fill
await note.Save();                           // the note, then its rows under the note's new key
```

The same holds for `Find` + `Save()` and for a shared context (`db:`): every row the aggregate holds afterwards is in
the table, and nothing else is. A row changed or added without its aggregate is refused, as for any child.

**A grandchild names the child that holds it.** A column of a report, keyed by the report and its own number, holds
totals keyed by the column and theirs. The column's whole key becomes the grandchild's, one generated property per
part, named as EF Core names a foreign key to it:

```csharp
public sealed partial class ReportColumn : Entity<(Report, int ColumnId)>
{
    private readonly List<ReportGroupTotal> _totals = [];

    public int ColumnId { get; private set; }

    public IReadOnlyList<ReportGroupTotal> Totals => _totals;
}

public sealed partial class ReportGroupTotal : Entity<(ReportColumn, int TotalColumnId)>
{
    public int TotalColumnId { get; private set; }
}

// generated: ReportColumnReportId, ReportColumnColumnId, and
// public new (int ReportColumnReportId, int ReportColumnColumnId, int TotalColumnId) Id
```

A column that is replaced takes the new instance's totals with it, and they are matched by key the same way.

A class that is not `partial`, a part with no name or a named aggregate or entity part, a name that is already
taken, and a key of several columns on an aggregate **root** are [RASK106](diagnostics.md#rask106).

#### Link tables and child rows on the form

An aggregate whose base names `Model` carries these rows on its form too, in one of two shapes, and saves them in
the aggregate's one save. A table an existing app made — a company, the activities it does, and the columns of its
report:

```csharp
public sealed class Company : Aggregate<int, Model, PerTenant<int>>
{
    private readonly List<CompanyActivity> _activities = [];
    private readonly List<CompanyColumn> _columns = [];

    public string Name { get; private set; } = "";
    public IReadOnlyList<CompanyActivity> Activities => _activities;
    public IReadOnlyList<CompanyColumn> Columns => _columns;
}

public sealed partial class CompanyActivity : Entity<(Company, Teaor)> { }   // CompanyTeaors(CompanyId, TeaorId)

public sealed partial class CompanyColumn : Entity<(Company, int ColumnId)>
{
    public int ColumnId { get; private set; }
    public int Order { get; private set; }
    public bool Sum { get; private set; }
}
```

**A link — the holder's part and one other, and nothing else — is a list of ids.** It is named after the collection,
made singular, plus `Ids` (`Activities` → `ActivityIds`, `Statuses` → `StatusIds`, `Boxes` → `BoxIds`, `People` →
`PersonIds`), and holds the other part's key type. The singular follows Rails' rules — `ies` → `y`, `es` dropped where
it was added (after x/ch/sh/ss/zz, and `Statuses`, `Buses`, `Aliases`), a final `s`, and a short list of irregular
plurals — so `Houses` is `HouseIds`. A name no rule makes singular keeps the collection's own name: `StaffIds`. It is saved as a
**set**: a link is inserted for an id it does not hold yet, deleted for one no longer listed, never updated, and an
id given twice is one link. The same set in another order writes nothing.

```csharp
var model = await Company.Model(id);           // model.ActivityIds = [0111, 0112]
model.ActivityIds = [teaorA.Id, teaorB.Id];
await Company.Update(id, model);               // INSERT the new link, DELETE the dropped one, nothing else
```

Laravel's `$company->teaors()->sync($ids)` and Rails' `company.teaor_ids = ids`, on the form model.

**A row with fields of its own is a list of rows**, keeping the collection's name. Each row is a generated
`CompanyColumnModel` carrying the parts of the key the holder does not give — `ColumnId` here, `TransactionId` for
`Entity<(DeliveryNote, Transaction)>` — and the row's own settable fields. The holder's part is never on the form:
the save fills it. Rows are matched to the stored ones **by key**, as a refill is above:

```csharp
model.Columns = [new() { ColumnId = 3, Order = 1, Sum = true }];
await Company.Update(id, model);               // UPDATE a changed row, INSERT a new key, DELETE a dropped one
```

`Company.Create(model)`, `Company.Update(id, model)` and `Company.Model(id)` carry both; with `db:` or inside
`Db.Transaction` they stage with everything else, so several aggregates saved together are one transaction. A
per-tenant holder keeps its rows to its tenant through itself — the rows carry no `TenantId`.

**What a form is told.** A row whose key part is empty is required, under its own field (`Columns[0].ColumnId`).
Two rows of one key are refused under `Columns` before anything is loaded — the strings catalog's
`ValidationDuplicateRow`. An id the database refuses — no such activity, the foreign key the table declares — is a
field error under `ActivityIds` (or `Columns`) saying `ValidationNotFound`, and the whole save, every aggregate in it,
is rolled back. Both are in the reader's language — the kit carries Hungarian — and an app's own catalog overrides
them ([Localization](localization.md)).

**What a form still cannot carry is [RASK106](diagnostics.md#rask106):** rows that hold rows of their own (a
`ReportColumn` holding `Totals`), a row with no parameterless constructor to add one with, and a key with no part for
the aggregate that holds it. Leave `Model` off the holder's base and fill those through a method of the aggregate.

### Collections of values

A collection whose elements are **values** rather than entities is one column, not a table. Declare it the same
way as a child — a private field and a read-only view — and nothing else is configured:

```csharp
public sealed class Trip : Aggregate<Guid, Model>
{
    private readonly List<string> _tags = [];
    private readonly List<Stop> _stops = [];

    private Trip() { }

    public IReadOnlyList<string> Tags => _tags;        // one column of plain values
    public IReadOnlyList<Stop> Stops => _stops;        // one JSON column of value objects

    public void Tag(string tag) => _tags.Add(tag);
    public void StopAt(string city, int day) => _stops.Add(new Stop(city, day));
}

public sealed record Stop(string City, int Day);
```

`Tags` becomes a **primitive collection** and `Stops` a **JSON column**. Both appear on the form model and on
the read face, and both are **queryable in SQL**:

```csharp
await Trip.Where(t => t.Tags.Contains("urgent"));
await Trip.Where(t => t.Stops.Any(s => s.City == "Vienna" && s.Day >= 2));
```

**Values are replaced wholesale, not reconciled.** A child has an id, so a form post updates the rows it names
and deletes the rest. A value has no id, so there is nothing to match against: what the form posts is what the
aggregate holds afterwards, and an empty list clears the collection.

**The column is NULL only where the model says it may be.** `Tags` and `Stops` above are `NOT NULL`, and an empty
collection is stored as `[]`, never as NULL. A nullable one, `List<Stop>? Detours { get; private set; }`, is a NULL
column. This works the same way as Laravel's `$table->json('stops')` and Rails' `t.json :stops, null: false`. An
adopted `Stops nvarchar(max) NOT NULL` column is mapped as it is, with no migration.

The column is the database's own text of any length: `nvarchar(max)` on SQL Server at **every** compatibility level
(EF Core alone would make it SQL Server 2025's `json` at 170, and change the model — and so the migrations — with the
level the server reports), `TEXT` on SQLite and `jsonb` on PostgreSQL. A plain value collection (`List<string>`) is
kept the same way. No app names the type; a `SetContainerColumnType(…)` or `HasColumnType(…)` of its own still wins.

A JSON column is an EF Core *complex collection*, which EF Core writes through a list, so the property needs a
`List<T>` behind it. **A get-only `IReadOnlyList<T>` auto-property keeps a NULL column**, whatever its nullability:

```csharp
public IReadOnlyList<Stop> Stops { get; private set; } = new List<Stop>();   // NULL column: no list behind it

private readonly List<Stop> _stops = [];
public IReadOnlyList<Stop> Stops => _stops;                                  // NOT NULL: the field is the list

public List<Stop> Stops { get; private set; } = [];                          // NOT NULL: the property is the list
```

The first shape is mapped the old way, as an owned collection, and EF Core makes an owned collection's column NULL and
refuses `IsRequired()` on it.

**Moving an existing app onto `NOT NULL` is one migration, with no edit.** Every value-object collection used to be a
NULL column, so the next `rask db add` writes an `AlterColumn` to `NOT NULL` for each non-nullable one, with
`defaultValue: "[]"`. SQLite, SQL Server and PostgreSQL fill every NULL row with `[]` before the column turns
`NOT NULL`, and an `AddColumn` of a new collection fills the rows already there the same way. EF Core on its own would
write `"{}"`, an object that no collection can read back, so a context writes its migrations through Rask's differ
when it is on `UseRaskSqlite`, `UseRaskPostgres` or `UseRaskSqlServer`, or is a RaskApp's. A context on plain
`UseSqlite`, `UseNpgsql` or `UseSqlServer` gets EF Core's `"{}"`: switch it to the Rask call. A JSON column that holds
one value object keeps `"{}"`.

**Choose a child entity instead when the elements need identity** — to be referenced, updated one at a time, or
indexed. That is the real trade-off, and the deciding factor is usually the index:

| | SQLite | PostgreSQL | SQL Server |
|---|---|---|---|
| plain values | `json_each` | native `text[]` | `OPENJSON` |
| value objects | `json_each` | `jsonb` | `OPENJSON` |
| index over every element | **none** | GIN | — |

PostgreSQL can index straight into both. SQLite needs no extension for JSON (`json_extract` is core since
3.38), and it can index a **fixed** path through a generated column — but it has no equivalent of GIN, so a
filter over *any* element of the collection is a scan. If that filter is hot on SQLite, make the elements
`Entity<TId>` children and index the column.

`byte[]` is never a collection of bytes: it stays one BLOB column. Anything Rask has no column for — a
`Dictionary`, a collection of some framework type — is left alone for the entity's own `Configure` to map, and
so is any collection that `Configure` already mapped.

A converter on the **element** is read back on the read face too — an enum list stored by name, or values the app
stores its own way:

```csharp
builder.PrimitiveCollection(s => s.Handling).ElementType().HasConversion<string>();   // ["Fragile","Cold"]

await Skid.Where(s => s.Handling.Contains(SkidHandling.Cold));                         // the enum, not its name
```

### A list that is a value: `ValueList<T>`

A `List<T>` is compared by reference, so a value object that holds one is never equal to another holding the same
elements, and anything holding the list can change it under the value object's feet. **`ValueList<T>`** (namespace
`Rask.Values`) is the list that is a value: equal by its elements in order, and never changed once made — a
collection expression makes one, and a new list replaces it:

```csharp
public sealed class Tenant : Aggregate<TenantId, Versioned>
{
    public ValueList<CategoryCode> Codes { get; private set; } = [];     // a JSON column, as IReadOnlyList<T> is

    public void Classify(params CategoryCode[] codes) => Codes = [.. codes];
}
```

On an aggregate or an entity it takes the same column an `IReadOnlyList<T>` does — a primitive collection of plain
values, a JSON column of value objects, `NOT NULL` and `[]` when empty — and is queried the same way
(`Tenant.Where(t => t.Codes.Contains(code))`). It implements `IList<T>` only because that is what EF Core reads a
JSON collection into: once anything has read it, a change through that interface throws `NotSupportedException`.

### One value object kept whole as JSON: `b.Json(…)`

A value object of several members is a complex type, one column per member. A value object that **holds a list** —
a partner's settings, a set of pairings — has no columns to spread over, so it is kept whole in **one JSON column**,
named in the entity's `Configure`:

```csharp
public sealed record LocalWell
{
    private LocalWell() { }                                            // EF Core builds a holder of a list
    public LocalWell(LocalWellProvider provider, Secret<ApiKey>? key, ValueList<Pair> tanks) =>   // through its members
        (Provider, Key, Tanks) = (provider, key, tanks);

    public LocalWellProvider Provider { get; private init; }
    public Secret<ApiKey>? Key { get; private init; }
    public ValueList<Pair> Tanks { get; private init; } = [];
}

public sealed partial class Tenant
{
    public LocalWell? LocalWell { get; private set; }

    static void Configure(EntityTypeBuilder<Tenant> b) => b.Json(t => t.LocalWell, column: "LocalSettings");
}
```

`column:` names a column an adopted table already has; without it the column is named after the property. A
nullable member is a `NULL` column while it holds nothing. It is EF Core's own JSON mapping, so the column is `TEXT`
on SQLite, `nvarchar(max)` (or `json`) on SQL Server and `jsonb` on PostgreSQL, a filter on what is inside runs in
the database on all three (`Tenant.Where(t => t.LocalWell!.Provider == provider)`, `….Tanks.Any(p => p.DbId == id)`),
and the conformance suite proves it on each (`JsonValueParityTests`). `b.Json(…)` on a collection of value objects
renames its JSON column. The read face leaves a value object kept as JSON out; read it through the aggregate.

EF Core builds a value object that holds another, or a list, through its members rather than its constructor — so
such a holder has a private parameterless constructor and `private init` members, as above.

### Secrets: `Secret<T>`

An API key a partner issued, a token the app must send on: a value the app **stores and uses but never shows**.
Declare it `Secret<T>` — over a `string`, or over a value object that holds one text (its rules checked as it is
made):

```csharp
public sealed partial record ApiKey(string Value) : Text(Value, MaxLength: 200);

public sealed partial class Partner
{
    public Secret<ApiKey>? Key { get; private set; }

    public void Connect(Change<ApiKey> key) => Key = Secret.Applied(key, Key);   // empty field: keep; typed: replace
}
```

- **Stored as ciphertext.** The column — and a `Secret<T>` inside a JSON column, as `LocalWell.Key` above — holds the
  value encrypted through `ISecrets`, which Rask.Server backs with the host's ASP.NET Core Data Protection key ring
  (the ring a Rask deploy persists to its volume, under a purpose of its own). A dump, a backup or a logged SQL
  parameter shows nothing usable. A row stored before a rule was tightened still loads. **Keep the key ring as long
  as the data**: a ciphertext is only readable with the keys that wrote it.
- **Never shown.** `ToString()` and the debugger say `••••`, and it has no public member, so a serializer writes `{}`.
  The read face leaves it out, and the generated model — so every form over the aggregate — has no field for it: it
  is never prefilled into a form and never sent to a browser.
- **Set through an operation** that takes the plain value object. `Change<ApiKey>` gives the form an empty field that
  keeps the stored key unless something is typed, and `Secret.Applied` wraps what was typed. Read `Key.Reveal()` in
  the one place the key is used — the request to the partner.
- **A host wired by hand** registers its own `ISecrets`; a test sets `SecretKeeping.Default` to one. A
  `Secret<T>` of a `Password` value object is not [RASK175](diagnostics.md#rask175): it is kept as ciphertext, which is
  what a password the app must send on asks for. A sign-in password is still hashed, never kept.

Here is a whole model running — a `Note` aggregate, queried with `Note.OrderByDescending(…)`, written with `Note.Create`, and
searched with `Note.Search`, in a SQLite database that lives in this browser tab and survives a reload:

<!-- demo:data-notes -->

## Operations: what the aggregate does

An aggregate is not *updated*: it is placed, paid, shipped or cancelled, and each of those is a public method on it
that keeps its own rules. That method is the whole of a write — there is no handler to put around it and no service
to call it from. Rask gives each one a static entry that loads the aggregate, runs the method and saves it in one
transaction, a dry run a form asks while it is filled in, and a `May…` that says whether the signed-in user may run
it.

### An operation, run as one write

A `partial` aggregate's public methods are its **operations**, and each gets a static entry of the same name that
runs it as one write — the load, the method and the save in one `Db.Transaction`:

```csharp
public sealed partial class Supply : Aggregate<SupplyId, Versioned, PerTenant>
{
    public void Change(SupplyDetails details, IReadOnlyList<SupplyPartSpec> parts) { … }   // an operation
    public void SetOpening(Opening opening) { … }                                           // an operation
    public bool IsOpen() => …;                                                              // a question: no entry
    private static Supply Register(CompanyId company, SupplyDetails details, IReadOnlyList<SupplyPartSpec> parts) => …;   // a factory

    public static void Configure(EntityTypeBuilder<Supply> b)
    {
        b.Operation(s => s.SetOpening, roles: Roles.Admin);
        b.Operation(nameof(Register), roles: Roles.Buyer);
    }
}

await Supply.Change(id, version, details, parts);           // generated: Find(id, version) → Change → Save()
await Supply.SetOpening(id, version, opening);              // and first: is the signed-in user an Admin?
SupplyId id = await Supply.Register(company, details, parts);   // generated: Register → Save(), the id handed back
if (Supply.MaySetOpening) { … }                             // generated: may the signed-in user run it?
```

- **What is an operation.** A public instance method declared on the aggregate, not generic, with no `ref`/`out`/`in`
  parameter, that returns `void`, `Task`, `ValueTask` or the aggregate itself (a fluent `Driver Change(…)`). A method
  that returns anything else answers a question and gets no entry; neither do `Validate`, `ValidateDelete`, `On` or a
  method named like a write (`Create`, `Find`, `Update`, `Delete`, `Save`, `Model`). Only a `partial` aggregate gets
  entries, because they are static overloads in its own class.
- **A factory** is a static method declared on the aggregate that returns it (or a `Task` of it), of any accessibility
  — written `private` or `internal`, because its generated entry takes the public name: `await Supply.Register(…)`
  asks its roles, builds the aggregate, saves it (its rules asked) in one `Db.Transaction` and hands back its id. A
  factory left public would win every call over its entry, so it gets none ([RASK173](diagnostics.md#rask173)). Its
  roles are named with `nameof`, since its method group holds the entry too: `b.Operation(nameof(Register), roles: …)`.
- **A delete of its own.** A `partial` aggregate that can be deleted (`Deletable`, `SoftDelete`) gets
  `await Supply.Delete(id, version)`: the roles of `b.Operation(nameof(Delete), roles: …)` asked, then the aggregate
  loaded at that version and deleted in one `Db.Transaction`, its `ValidateDelete()` asked by the delete; a hidden
  `Supply.DryRunDelete(id, version)` tries it with nothing deleted, and `Supply.MayDelete` says who may. Called with
  no version, or with a `db:`, `Supply.Delete(id)` is still the generated write's. `Ui.EditorDialog`'s
  [`.Delete(Supply.Delete)`](ui-kit.md) is built on the three.
- **`Supply.May…`** answers, for every operation and factory, whether the signed-in principal holds one of its roles —
  always `true` for one that names none — so a page hides or disables what the entry would refuse.
- **What the entry does**, in order: the roles of `b.Operation(x => x.M, roles: …)` are asked of the signed-in
  principal — any one of them admits, comma-separated as `[Authorize(Roles = …)]` takes them — and anyone else is a
  `ForbiddenException` before anything is read; then `Find(id, version)` (a save since is refused as the form's
  whole-form failure), the method, and `Save()`, which asks the aggregate's [rules](#what-every-save-asks). A refusal
  anywhere — a stale version, a rule, a `Refuse.When(…)` in the method — rolls the whole operation back and reaches
  the caller as the `RaskValidationException` a form shows. An aggregate with no `Versioned` takes no `version`.
- **Tried, not done: `Supply.DryRunChange(id, version, …)`** (hidden; a factory's is `DryRunRegister(…)`) runs the
  operation on a fresh copy of the aggregate and asks the rules a save would — the changed properties' attributes,
  `Validate()`, a `Refuse` in the method, a stale version — and hands back every `FieldFailure` under the field it
  names, empty when it would go through. Nothing is written, no version bumped, no event published, and a
  `Db.Lock` inside the rules is not taken. It is what a form over the operation asks while it is filled in, so the
  rules the form shows are the domain's own.
- **The roles are read where the entry is generated**, as `[Authorize(Roles = …)]` is read by the compiler: written
  in the aggregate's own static `Configure`, the operation as a lambda and the roles as a constant. Anywhere else the
  entry would ask nothing, so the build stops ([RASK172](diagnostics.md#rask172)). Only the entry asks: the method
  called on an aggregate in hand is your own code and runs as written.
- **Two aggregates, or a row held while it runs:** `b.Operation(x => x.M, manyAggregates: "why")` runs the entry
  in `Db.Transaction(…, manyAggregates: "why")` ([one aggregate per transaction](#one-aggregate-per-transaction-manyaggregates));
  `forUpdate: true` reads the aggregate's row under an update lock before the load, and
  `forUpdate: s => s.Parts` the rows of that child collection — `UPDLOCK, HOLDLOCK` on SQL Server, `FOR UPDATE` on
  PostgreSQL, nothing more on SQLite, whose one writer serializes already — so a second operation on the same rows
  waits for the first to commit instead of being refused as stale. The reason is a constant, read like the roles. In
  a static method of your own, `await id.Load(version, forUpdate: true)` does the same: generated on a typed id that
  one aggregate alone is keyed by, it loads the aggregate whole (`KeyNotFoundException` when there is none, the
  stale-version refusal as `Find(id, version)`), and refuses a lock outside a `Db.Transaction`, which would hold
  nothing.
- **A job or an event handler, which read no version:** `await Supply.Change(id, details, parts)` — the same entry
  without the `version`, generated beside it for a `Versioned` aggregate — loads the aggregate as it now stands and
  runs the operation on that, its roles and rules asked as ever. It is the system actor's call (a rematch, an
  ingest), never a form's: a person changes what they read. An operation whose first parameter takes a number
  (`Book(int seats)`) gets none, since `Supply.Book(id, 3)` would read both ways — pass the version it loaded.
- **Inside the aggregate, call the method, not the entry.** A bare `Change(id, version, …)` written in `Supply` binds
  to the entry and saves ANOTHER copy in a transaction of its own ([RASK170](diagnostics.md#rask170)); an operation
  whose parameter is named `id`, `version` or `cancellationToken` gets no entry ([RASK171](diagnostics.md#rask171)).
- **Outside the aggregate, call the entry, not a context.** `db.Products.Add(Product.Place(…))` or `db.Update(product)`
  followed by `SaveChangesAsync()` writes the aggregate past its roles and its rules
  ([RASK133](diagnostics.md#rask133)); `await Product.Place(…)` and `await Product.Reprice(id, version, …)` are the
  same write through the aggregate, and a check that must go with it is a `Db.Transaction` around the operation.

### Refusing an operation: `Refuse`

An operation that cannot go ahead says so with `Refuse`, never with an exception of its own: it throws a
`RaskValidationException`, which a form shows like any failed rule — the message under the field it names, or above
the form when it names none — and an API caller gets as a 400 with `errors`. The same is true of a test, where
[`Test.Refuses`](testing.md#asserting-a-refusal-testrefuses) asserts the field.

```csharp
public void Ship(TrackingNumber tracking, ShipDay shippedOn)
{
    Refuse.Unless(Status == OrderStatus.Paid, $"A {Status} order cannot ship.");                    // the whole form
    Refuse.When(shippedOn < PaidOn, () => shippedOn, "An order ships after it is paid.");         // under "ShippedOn"
    (Status, Tracking, ShippedOn) = (OrderStatus.Shipped, tracking, shippedOn);
}

for (var i = 0; i < Parts.Count; i++)
{
    Refuse.When(locked.Contains(Parts[i].Id), () => Parts[i].Quantity, Texts.LockedChanged);  // "Parts[2].Quantity"
}
```

- `Refuse.When(condition, …)` refuses when the condition holds, `Refuse.Unless(condition, …)` when it does not,
  and `Refuse.At(() => field, message)` always — the branch that already knows. Each takes the field as a lambda,
  or no field for the form as a whole.
- **The field path is read from the lambda**, as the form names it: `() => Km.Start` is `Km.Start`; an index is
  evaluated, so `() => Parts[i].Quantity` is `Parts[2].Quantity`; a captured local or parameter takes its form
  field's name (`() => parts[i].Quantity` is `Parts[2].Quantity`); `this.` is dropped. A lambda that is not a chain
  of members and indexers (`() => Total()`) throws an `ArgumentException` naming the shape to write.
- **The lambda is read only when the operation is refused.** A call that goes through pays for the expression the
  compiler builds at the call site and nothing more, so a guard in a loop over thousands of rows stays cheap.
- A child row's own key, rather than its index, is `Problem.At(child)`'s to give, from a value object's rules.
- **A problem inside a list a value or an aggregate holds** names its path the same way, positions evaluated:
  `yield return Problem.At(() => LocalWells[i].Companies[j].DbId, Texts.NotThisPartners)` is under
  `LocalWells[2].Companies[0].DbId` — where the form shows that row's field. `Problem.At(member, …)` without a lambda
  keeps the member as written.

### Find, change, Save

When the change is the aggregate's own method, load it, call it, save it:

```csharp
var order = await Order.Find(id) ?? throw new KeyNotFoundException();
order.Ship("1Z999");
await order.Save();

await Order.Place("A-1001").Save();   // a new aggregate is inserted
```

It works the way Rails' and Laravel's models do: `Find` reads the row once and remembers what it read, and `Save()`
writes what changed since — it never reads the row again.

- **Read once.** `Find` loads the aggregate whole, through a context of its own, and hands it back detached with a
  snapshot of what it read. `Save()` compares with that snapshot and writes only the columns you changed — a column
  someone else changed meanwhile keeps their value — with no SELECT, however large the row. Every successful save
  takes a new snapshot, so the next `Save()` reads nothing either.
- **New is inserted with no read.** An aggregate your code made (`Order.Place(…)`, `new() { Id = Guid.CreateVersion7() }`)
  and never saved is an INSERT. So an aggregate built — or deserialised — with the key of an EXISTING row is an
  insert too, refused by the key: `Find` it and change it instead.
- **Children are synced by id.** A child you added is inserted, one you removed is deleted, one you changed is
  updated.
- **Changed underneath is refused, never written over.** The `Version` compared is the one `Find` read, so on a
  `Versioned` aggregate a save after someone else's — or after a soft delete, which bumps it — is refused with the
  whole-form `RaskValidationException` above. So is a save whose row, or a changed child's, has been deleted outright: nothing is
  silently re-inserted. An aggregate with no `Version` that names `SoftDelete` is asked first whether its row is
  still there (by its key alone — one small SELECT), and is `KeyNotFoundException` when it is not.
- **The domain is asked first** — the changed properties' attributes, `Validate()`, `ValidateDelete()` — and a save
  it refuses is a `RaskValidationException` with nothing written, stamped or versioned
  ([what every save asks](#what-every-save-asks)).
- **The interceptors run** as for any write: times stamped, `Version` bumped, tenant checked, domain events
  published after the commit.
- **Nothing is held open.** `Find` takes no `db:` and returns the aggregate untracked, so it is safe to keep in a
  page field between `Find` and `Save`. Pass `db:` to `Save` to stage it in your own unit of work; its snapshot is
  taken when that context's `SaveChangesAsync` succeeds.
- **Delete what you found: `await file.Delete();`** — `var file = await QueuedFile.Find(id); if (file.Expired) await
  file.Delete();`. Like Rails' `record.destroy` and Laravel's `$model->delete()` it works from what `Find` read, with no
  read first: an aggregate that names `SoftDelete` is stamped, any other has its row and its children removed. The
  `Version` read is compared, so a row changed or deleted since is the same whole-form refusal (an unversioned
  soft-deleting one is asked by its key first, `KeyNotFoundException` when gone); one never saved is
  `InvalidOperationException`. It takes `db:` like `Save`, and inside `Db.Transaction` it is written in the
  transaction — a rollback leaves the row and its children, and its events go out after the commit. The static
  `QueuedFile.Delete(id)` stays, and loads the row first.
- **Only to show it? Do not `Find` it.** A list, a detail page or a check reads the
  [read face](data.md#reading-the-read-face) (`Order.Where(…)`); an edit form reads `Order.Model(id)`. `Find` is for changing
  the aggregate through its own methods.

`Order.Update(id, o => o.Ship("1Z999"))` does the same in one call when there is nothing to show in between.

### What every save asks

Every save — `Save()`, `Update(id, apply)`, `Create(model)`, `Delete`, a `db:` context you save yourself — asks the
domain before anything is written. A rule is written once, on the aggregate, and holds however the row is changed:

```csharp
public sealed class Voyage : Aggregate<Guid, Versioned, Deletable>
{
    private readonly List<Leg> _legs = [];

    [Required, StringLength(80)] public string Title { get; private set; } = "";   // a DataAnnotations rule
    public bool Sealed { get; private set; }
    public IReadOnlyCollection<Leg> Legs => _legs;

    public void Seal() => Sealed = true;

    IEnumerable<Problem> Validate()                     // the aggregate's own rule, over its rows
    {
        if (Sealed && _legs.Count == 0) yield return Problem.At(Legs, "A sealed voyage needs a leg.");
    }

    async IAsyncEnumerable<Problem> ValidateDelete()    // asked by a delete, in its place
    {
        if (await Booking.Where(b => b.VoyageId == Id).Any()) yield return Problem.Whole("It has bookings.");
    }
}
```

For every row the save adds or changes, in this order:

1. **The validation attributes of its properties** — every one on a new row, but on a stored row only the properties
   this save changes. A row stored before a rule was tightened still opens, and still saves for another field; the
   rule is asked again once that field is touched. A one-value value object is checked by the value it holds, as its
   form field is.
   The rules it states in code — a FluentValidation `RuleFor` in its own `static void Rules(InlineValidator<T> rules)`
   or in an `AbstractValidator<T>` class ([forms validation](forms-validation.md#rules-inside-the-type)) — are asked the
   same way, `MustAsync` included: every rule of a new row, only the changed members' of a stored one.
2. **Its type's attributes, and `IValidatableObject` / `IAsyncValidatableObject`** when it implements one.
3. **Its `Validate()`** — the root's whenever anything in the aggregate changes, a removed row included, because a
   rule over the rows is the root's.

A deleted aggregate is asked its `ValidateDelete()` instead. Both are plain methods found by name: an instance method
with no parameters returning `IEnumerable<Problem>`, or `IAsyncEnumerable<Problem>` when it reads something. Private
is fine, and the aggregate needs no `partial`.

What they refuse is ONE `RaskValidationException`, with every failure under the field the generated model names —
`Title`, `Legs`, `Legs[2].Port` — and a built-in attribute worded exactly as `Create(model)` words it, so a form that
lets the exception through shows each message under its field. It is asked first of every save step, so a refused
save has been stamped and versioned by nothing: fix the value and `Save()` again. A rule resolves services from the
request, live session, handler or job in progress (`validationContext.GetService<T>()`). Bulk insert asks nothing — it
runs no interceptor ([bulk insert](data-writes.md#bulk-insert)).

### A row closed by its parent: `LockedBy`

A delivery note of a closed period, a transaction of a filed return: the row stays as it was while the row it
belongs to says so. Declare it once in `Configure`, and every save holds to it — a page's, a job's, an import's:

```csharp
public static void Configure(EntityTypeBuilder<DeliveryNote> b) =>
    b.LockedBy(n => n.Period, (Period p) => p.Closed, Texts.PeriodClosed);

public static void Configure(EntityTypeBuilder<GasClaim> b) =>     // the words made from the parent
    b.LockedBy(c => c.Period, (Period p) => p.Closed, p => $"{p.Name} is closed.", LockRead.ForUpdate);
```

`n => n.Period` is the property holding the parent's key — a typed id, an `int`, a nullable one — and
`(Period p) => p.Closed` is translated to SQL, so it may reach further (`r => r.Filings.Any(f => f.UnfiledAt == null)`).
The save asks it with the rest of [what every save asks](#what-every-save-asks), one query per lock for all the rows
of the save:

- **The old and the new parent.** For a row the save adds, changes or deletes, the parent it was read with and the
  one it holds now are both read, so a row is moved neither out of a locked parent nor into one. A null key names no
  parent and locks nothing; a null set to a locked parent is refused.
- **A child's change locks through its root.** A line added to, changed on or removed from a note of a closed period
  is refused as the note would be: the aggregate is the unit the lock holds.
- **Under the parent field, or the whole row.** A new row, or one whose parent the save changes, is refused under
  that field (`Period`); any other write is refused about the row itself — the whole form for a root.
- **`LockRead.ForUpdate`** reads the parents with an update lock held to the end of the save — `UPDLOCK, HOLDLOCK` on
  SQL Server, `FOR UPDATE` on PostgreSQL, nothing on SQLite, whose one writer serializes the save already — so a
  "Close period" cannot slip in between the check and the write. The save runs in a transaction of its own when its
  caller opened none (inside `Db.Transaction`, it is that one). A context whose execution strategy retries cannot
  take one: wrap such a save in `Db.Transaction` for the lock to hold.

The parents are read past every query filter, so a soft-deleted closed period still holds its notes. A parent the
same save closes is judged as the database has it, before this save.

**Columns a lock lets through, and words for each write.** A filed return holds its transactions, but a claim may still
take one over by its refund columns; and "deleted" and "changed" want different sentences, which may name something
the parent holds:

```csharp
public static void Configure(EntityTypeBuilder<Transaction> b) =>
    b.LockedBy(t => t.RefundId, (Refund r) => r.Filings.Any(f => f.UnfiledAt == null),
            (r, write) => write.Kind == LockedWriteKind.Delete
                ? $"{r.Name} is filed at {r.Filings[0].Office}: it keeps its transactions."
                : $"{r.Name} is filed at {r.Filings[0].Office}: {write.Row.Litres} litres stay as filed.")
        .Except(t => t.RefundId, t => t.ForeignRefundId)    // a change to only these passes, under a filed refund too
        .Including(r => r.Filings);                         // loaded with each locked parent, for the words
```

- **`Except(columns)`** lets a change through when every column it touches is one of these — even the parent key, so a
  claim moves the transaction. An add, a delete, a change to any other column, and a change through a child are
  still refused.
- **The message `(parent, write)`** is told the write's `Kind` — `Add`, `Change`, `Delete` or `Move` (a new parent) —
  and its `Row`, as the save would write it.
- **`Including(p => p.Navigation)`** loads that navigation of each locked parent before the message reads it; without
  it the parent comes back as its own columns only.

### Behaviour-rich aggregates

An aggregate whose state moves through steps is the usual shape, not a special one. An order is placed, paid, shipped
or cancelled; an invoice is never deleted, it is voided, and a mistake is corrected by a credit note. Each step is an
operation, each refuses what it does not allow, and nothing else can change the aggregate:

```csharp
public enum OrderStatus { Placed, Paid, Shipped, Cancelled }

public readonly partial record struct OrderId(Guid Value);
public sealed partial record OrderReference(string Value) : Text(Value, MaxLength: 40, Upper: true);
public sealed partial record TrackingNumber(string Value) : Text(Value, MaxLength: 60, Trim: true);

public sealed partial class Order : Aggregate<OrderId, Timestamps, Versioned>   // no Model, no Deletable: operations only
{
    public OrderReference Reference { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public TrackingNumber? Tracking { get; private set; }

    private static Order Place(OrderReference reference) =>                      // the factory: Order.Place(reference)
        new() { Id = OrderId.New(), Reference = reference, Status = OrderStatus.Placed };

    public void Pay()
    {
        Refuse.Unless(Status == OrderStatus.Placed, $"A {Status} order cannot be paid.");
        Status = OrderStatus.Paid;
    }

    public void Ship(TrackingNumber tracking)
    {
        Refuse.Unless(Status == OrderStatus.Paid, $"A {Status} order cannot ship.");
        (Status, Tracking) = (OrderStatus.Shipped, tracking);
    }

    public void Cancel()
    {
        Refuse.Unless(Status is OrderStatus.Placed or OrderStatus.Paid, $"A {Status} order cannot be cancelled.");
        Status = OrderStatus.Cancelled;
    }
}
```

```csharp
OrderId id = await Order.Place(OrderReference.Create("a-1001"));   // the factory decides what a new order is
await Order.Pay(id, version);                                       // load whole, run the rule, save
await Order.Ship(id, version, TrackingNumber.Create("1Z999"));      // a stale version is refused
```

A page binds each step to a form or a button — `Form.For(Order.Ship, id)` asks for the tracking number and shows
*A Placed order cannot ship.* at its top before anything is saved — and when a page shows the order between loading
and changing it, the same rules run through [`Find` and `Save`](#find-change-save).

- **Only the methods are callable.** The properties have private setters ([RASK084](diagnostics.md#rask084)), so
  nothing outside the aggregate sets `Status` without going through `Ship`. Each entry is one unit of work: the
  aggregate is loaded with its children, the method runs, the version is checked, and any events it
  [raises](#keeping-state-inside-the-aggregate) are saved in the same transaction.
- **No `Model` closes the form-model path.** A generated form model writes each field onto the private setters
  directly, which is right for a category and wrong here: a posted form could set `Status` without going through
  `Ship`. The forms of an aggregate like this are its operations' ([a form over an operation](forms.md#a-form-over-an-operation)).
- **No `Deletable` means no `Delete`.** `Order.Delete(id)` is then a compile error. Retiring an order is `Cancel`,
  which keeps the row, its history and everything that refers to it.

### Time in the domain

An aggregate, an entity and a value object read the time from Rask's `Clock`, never the machine's: `Clock.Now` is
now in UTC, `Clock.Wall` the wall clock in the app's zone (`AddRask(timeZone: "Europe/Budapest")`, `c.TimeZone` on a
`RaskApp`), `Clock.Today` its day and `Clock.Zone` the zone itself. A test's `Clock.Fake(at: …)` freezes all of
them, together with the audit stamps and `3.Days.Ago`; it freezes nothing that reads `DateTime.Now`.

```csharp
public void Stamp() => ModifiedAt = Clock.Wall;                                   // not DateTime.Now
public DateTime OnTheWall(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Clock.Zone);  // not utc.ToLocalTime()
```

`DateTime.Now`, `Today`, `UtcNow`, `DateTimeOffset.Now`/`UtcNow` and `.ToLocalTime()` in domain code are
[RASK140](diagnostics.md#rask140), whose quick-fix writes the `Clock` read of the same type.

### Services stay outside the domain

The domain decides from the state it holds and what its caller hands it. A service it would need — a price list, an
exchange rate, the mail sender — is the caller's to use: pass what it answered into the operation
(`order.Price(sku, prices.Of(sku))`), or raise a [domain event](#keeping-state-inside-the-aggregate) and let its
handler use the service. `Current.Service<T>()` inside an aggregate, an entity or a value object is
[RASK141](diagnostics.md#rask141).

### Keeping state inside the aggregate

State changes only through the aggregate's own methods, which keeps its invariants and its domain events in one
place. The build holds that line:

- **No public setters** ([RASK084](diagnostics.md#rask084), an error). A public `set`, a hand-written public
  `init` or a public mutable field on an aggregate, an entity (or an abstract base the app puts between them) or a
  value object one of them holds is refused, with a lightbulb that makes it `private`. EF Core and the generated
  writes both work through private setters. A positional record's parameters are exempt, so
  `record Money(decimal Amount, string Currency)` is never reported.
- **No mutable collections of entities** ([RASK085](diagnostics.md#rask085)). A navigation to many
  entities is exposed read-only, over a private field EF Core maps, and changed through a method. The
  lightbulb rewrites it:

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyCollection<OrderLine> Lines => _lines;

    public void AddLine(Guid productId, int quantity) => _lines.Add(OrderLine.For(productId, quantity));
}

public sealed class OrderLine : Entity<Guid>
{
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    public static OrderLine For(Guid productId, int quantity) =>
        new() { Id = Guid.CreateVersion7(), ProductId = productId, Quantity = quantity };
}
```

**The entity owns its key.** Rask.Data's key convention leaves an integer key (or a typed id over one) to the database's identity and
marks every other key never generated, so a `Guid` or a strongly-typed id is assigned where the entity is
built: `Id = Guid.CreateVersion7()` in its factory. That is what lets a child with an id of its own be added
to a loaded aggregate and saved as an insert. An entity added with its key still at the default is refused at
the save, by name, rather than inserted with an empty key. A key you configured yourself
(`ValueGeneratedOnAdd()`, a database default) is left as you set it, and an integer key you marked
`ValueGeneratedNever()` is yours to assign like the rest: `0` is refused, and the aggregate takes its key
[in `Create(id, …)`](#writing-create-update-delete).

**One aggregate refers to another by id.** `Order` holds a `Guid ProductId`, not a `Product`: each aggregate is
loaded and saved on its own, and a change that spans two follows from an event — or, as [the last resort](#changing-two-aggregates-together--the-last-resort), is saved with it in one transaction.

### Domain events

What happened is said once, by the aggregate, with `Raise(…)` inside the operation — `Raise(new OrderShipped(Id))`.
The events are saved with the aggregate and published after its transaction commits, never on a rollback: each
`IEventHandler<T>` runs in memory straight after the commit, and each `IDurableHandler<T>` runs from the
[outbox](outbox.md), which stored it in the save's own transaction, so it is retried until it succeeds. A handler
decides nothing — a change to another aggregate is that aggregate's own operation, called from the handler
([the transaction boundary](#the-transaction-boundary)). A test asserts what a save published with
[`Events.Fake()`](testing.md#asserting-what-was-published-eventsfake).

#### What a delete raises: `OnDelete()`

A delete is a change like any other, and others may need to hear of it — a driver removed frees the chips it held, so
its fuelings are matched again. The generated delete (`Driver.Delete(id, version)`, an editor dialog's
`.Delete(Driver.Delete)`) has no operation body to `Raise` in, so the aggregate says it in one private hook:

```csharp
public sealed partial class Driver : Aggregate<int, Versioned>
{
    private void OnDelete() => Raise(new DriverChipsChanged([.. Chips.Select(c => c.Code)]));
}
```

- **Every save that deletes the aggregate calls it** — the generated delete, a dialog's delete, `db.Remove(x)` and a
  save — after its `ValidateDelete()` has passed and before a `SoftDelete` aggregate's removal becomes an update. What it
  raises goes out exactly as an operation's events do: a durable handler's outbox row in the delete's own transaction,
  the in-memory handlers after the commit, nothing on a rollback.
- **Once per aggregate**: a save that fails keeps the events it raised, and its retry carries them without asking again.
- **Its shape is checked at build** ([RASK190](diagnostics.md#rask190)): `private void OnDelete()` on a `partial`
  aggregate. Private, since a public method of an aggregate is an operation of its own. A bulk `ExecuteDelete` is not a
  save and calls no hook.

## A form model for data-shaped aggregates

Some aggregates have no steps — a category, a setting, a destination whose only rule is "the fields are valid".
Such an aggregate may name the `Model` trait, and Rask generates a form model beside it and the writes that take it.
Its rules are still the aggregate's own — its value objects, its `Validate()`, its unique indexes — and the save asks
them exactly as it asks an operation's. Where the aggregate has steps, leave `Model` off and bind a form to each
operation instead ([a form over an operation](forms.md#a-form-over-an-operation)).

### Writing: create, update, delete

The writes live on the aggregate type, beside the reads, and a create reads like the update it pairs with. The
update just names the row first:

| Create | Update | From |
| --- | --- | --- |
| `Product.Create(model)` | `Product.Update(id, model)` | a form: the generated `ProductModel` |
| `Product.Create(model, p => …)` | `Product.Update(id, model, p => …)` | a form, plus values it does not carry |
| `Product.Create(name, price)` (its factory) + `Save()` | `Product.Update(id, p => …)` | code, with no form behind it |

- **A create** builds an empty `Product`, applies the model and then the lambda, and inserts it. The key is a new
  version-7 `Guid` (or the store's identity for an integer key). The lambda is for what a form must not carry —
  the signed-in user, a parent from the route, a domain event: `Ticket.Create(model, t => t.Open(Current.UserId))`.
- **From code, an aggregate is born by its own factory** — so its invariants hold from the first moment — and
  `Save()` inserts it. There is no `Create(p => …)`: an empty aggregate put together afterwards is the shape a
  factory exists to prevent, and `new Product()` outside `Product` is [RASK107](diagnostics.md#rask107).
- **`Product.Create(id, model)`** does the same under a key you give. They
  are the only creates for a key nothing can produce, such as a strongly-typed id over a `string`. They are not
  generated for an integer key the database produces: an explicit identity value fails on SQL Server and leaves
  PostgreSQL's sequence behind.
- **An integer key your application assigns** is a key you give, too. Say so in the aggregate's own `Configure`
  — `b.Property(x => x.Id).ValueGeneratedNever();` — and the aggregate gets `Create(id, model)` **instead of**
  the create that takes no key, which would insert the row under `0`:

  ```csharp
  public sealed class Destination : Aggregate<int, Model>
  {
      public string Name { get; private set; } = "";

      public static void Configure(EntityTypeBuilder<Destination> b) =>
          b.Property(d => d.Id).ValueGeneratedNever();          // the table is numbered by the application
  }

  await Destination.Create(1000, model);                        // inserted under 1000
  await Destination.Create(1001, d => d.Rename("Baja"));
  await Destination.Create(model);                              // does not compile: there is no such create
  ```

  A key given as `0` is refused at the save, by name, like any key left at its default; a key that is taken is
  the database's own duplicate-key error (`DbUpdateException`). The line is read from the source, so it has to
  be written in the static `Configure` on the aggregate, as one chain from `Property(x => x.Id)` — or
  `Property("Id")`, `Property(nameof(Id))`. Written in a helper method, an `IEntityTypeConfiguration` or
  `OnModelCreating`, the key is still inserted as given, but the aggregate keeps the creates of a stored
  identity.
- **`Product.Create(entity)`** inserts an aggregate you built with its own factory:
  `Product.Create(Product.Create("Anvil", new(30m, "EUR")))`.
- **An update** loads the row, applies the model and then the lambda, and saves **only the columns that changed**.
- **`Product.Delete(id)`** loads the row and **removes** it — generated for an aggregate that names `Deletable`.
  One that names `SoftDelete` keeps the row instead: `DeletedAt` is stamped and every read stops seeing it. One
  that names neither has no `Delete`. A row that is still referenced is refused by the database —
  [with the sentence you declared](data.md#what-deleting-a-referenced-row-says); a soft delete never asks it.

Each write is one unit of work for one aggregate, and each goes through EF Core's change tracker, so the
interceptors run exactly as for a hand-written save: `CreatedAt`/`UpdatedAt` are stamped and `Version` is bumped
where the aggregate names them, a delete becomes a stamp *for an aggregate that names `SoftDelete`*, and the aggregate's domain events are
published after the commit.

- **The id is always yours.** The model carries none; the row is addressed by the `id` you pass, never by anything
  a form posted.
- **Values that do not come from the form** go in an optional lambda, which runs after the model's values are
  written, so it has the last word: `Product.Create(model, p => p.AssignTo(user.Id))`. With private setters
  the lambda calls the aggregate's methods, and so the rules in those methods run.
- **A stale edit is refused** on a `Versioned` aggregate. `Update(id, model)` checks the `Version` the model carries and
  refuses the save when someone saved since — a `RaskValidationException` about the whole form, "Someone else changed
  this in the meantime. Reload it and try again." (`RaskString.ValidationChangedSince`, translated by the kit's
  catalog), which a form shows at its top with nothing to catch; EF's `DbUpdateConcurrencyException` is its
  `InnerException`, for the log. A model whose `Version` is `null` skips the check. `Update(id, p => …)` and
  `Delete(id)` take an optional `version:` for the same check, and `Update(id, async p => await p.Distribute(…),
  version: v)` awaits a change that reads first.
- **The version a form was filled from** goes to `Find` as well: `await Supply.Find(id, version)` loads the
  aggregate and refuses it with that same sentence when it has moved on, before anything is changed. A write
  STAGED on your own context (`db:`) is saved by your `SaveChangesAsync`, which throws EF's exception as it is.
- **A missing row**, never created or soft-deleted, is `KeyNotFoundException`, naming the aggregate and the key.

A write refreshes what is on screen: once it commits, every `Rask.Query` query about that aggregate —
`QueryKey.For<Product>(…)` — refetches in the session that made it, so a list beside a create form updates
with no invalidation to write. See [Writes refresh queries by themselves](query.md#writes-refresh-queries-by-themselves).

A create needs the parameterless constructor an aggregate has when it declares none. One that declares a
constructor with arguments still gets `Update` and `Delete`, and the build says why it has no
`Create` ([RASK086](diagnostics.md#rask086)).

### A create and an edit form

A form edits something mutable and an aggregate is not, so an aggregate that names `Model` gets a **form model
generated beside it**: `ProductModel` for `Product`. Forms always bind the model, never the aggregate. It is a plain class with a
settable copy of every mapped property, and:

- **every property is nullable**, because a form field can be empty whatever the column is — and a column that
  cannot be empty (`decimal Price`, `string Name`) is **required by its type**: leaving it unfilled is a
  validation error (*This field is required.*, in the reader's language) rather than a silent 0. No `[Required]`
  is generated for it — see [what a form over the model asks by itself](#what-a-form-over-the-model-asks-by-itself).
  A control bound to it says `aria-required="true"`: the type's registration answers
  [the one question a control asks](forms-validation.md#how-a-control-knows-a-field-is-required);
- **`new ProductModel()` holds the aggregate's defaults**: it is filled from an empty `Product`, so a create form
  starts from the same values a `Product` would;
- **`product.ToModel()`** copies a row into a model for an edit form, `Version` included;
- **the aggregate's validation attributes are copied onto it**, so `Form.Model(…)` checks input by the aggregate's
  own rules in the form, and EF Core reads the same attributes for the column;
- **`Product.Create(model)` and `Product.Update(id, model)` check them again on the server** — the value
  objects' rules first, then the attributes — before the database is asked anything, and throw
  `RaskValidationException` with nothing written. Its `Failures` name each field as the form names it (`Name`,
  `Price.Amount`, `Lines[2].Sku`), so a form shows them under the field and an API caller gets the 400 with
  `errors`. A posted form cannot skip the rules, and neither can a call with no form behind it.

A create page starts from a new one:

```csharp
[Route("/products/new")]
public sealed partial class NewProductPage : Component
{
    private readonly ProductModel _product = new();

    protected override Component Render() =>
        Form.Model(_product).OnSubmit(Create)[f => [
            Ui.Input.Bind(() => _product.Name).Label("Name"),
            Ui.Input.Bind(() => _product.Price!.Amount).Label("Price"),
            Ui.Button.Submit.Disabled(f.Submitting)["Create"],
        ]];

    private async Task Create(ProductModel product)
    {
        await Product.Create(product);
        Routes.ProductsPage().Go();
    }
}
```

An edit page fills one from the row it read, and hands it back with the route's id:

```csharp
[Route("/products/{id:guid}/edit")]
public sealed partial class EditProductPage : Component
{
    [RouteParam] public Guid Id { get; set; }

    private ProductModel? _product;

    protected override async Task OnMount() =>
        _product = await Product.Model(Id);

    protected override Component? Render() =>
        _product is null ? P["Loading…"] :
        Form.Model(_product).OnSubmit(Save)[
            Ui.Input.Bind(() => _product.Name).Label("Name"),
            Ui.Input.Bind(() => _product.Price!.Amount).Label("Price"),
            Ui.Textarea.Bind(() => _product.Notes).Label("Notes"),
            Ui.Button.Submit["Save"],
        ];

    private async Task Save(ProductModel edit)
    {
        await Product.Update(Id, edit);        // Version checked: a stale one is shown at the form's top
        Routes.ProductsPage().Go();
    }
}
```

The list those pages link from is a [table](ui-kit.md#tables) over one page of `Product` at a time
([above](data.md#handing-a-query-to-a-component-asqueryable)): the rows are read faces, read-only by construction, and
each links to its edit page.

### Choosing what a table carries

A table carries only what its base names. The type arguments after the key are **traits**, in any order, and an
aggregate that names none — `Aggregate<Guid>` — has no timestamps, no `Version`, no form model and no generated
`Delete`: its own columns, its read face, `X.Update(id, x => …)`, and [`Find` and `Save`](#find-change-save).

| trait | what the table gets |
|---|---|
| `Timestamps` — or `Created` / `Updated` for one | `CreatedAt` and `UpdatedAt`, stamped on save |
| `Versioned` | a `Version` concurrency token: a save from a stale copy is refused |
| `Model` — or `CreateModel` / `UpdateModel` for half | the form model, `X.Create(model)` and `X.Update(id, model)` — [below](#turning-the-form-surface-off) |
| `Deletable` | a generated `X.Delete(id)` that removes the row |
| `SoftDelete` | `DeletedAt` and a filter; the generated `Delete` stamps it instead (it implies `Deletable`) |
| `PerTenant` — or `PerTenant<int>` / `PerTenant<long>` | a `TenantId`, a filter and a stamp — see [Multi-tenancy](multi-tenancy.md) |
| `Broadcast` | a save announced to the whole process — see [Announcing a save](#announcing-a-save) |
| `Audited` | an `AuditEntry` written in every save — see [Who changed what](#who-changed-what-audited) |
| `PerTenantThrough<T>` | no column: the tenant of the `T` it points at — see [Multi-tenancy](multi-tenancy.md#an-aggregate-that-belongs-to-another-ones-tenant-pertenantthrought) |
| `OneInForce<TChild>` | at most one of its `TChild` children in force, held by a partial unique index — [below](#history-and-the-one-in-force) |

With neither `Deletable` nor `SoftDelete` there is no `Delete` at all — calling it is a compile error, which is
what a [behaviour-rich aggregate](#behaviour-rich-aggregates) wants.

```csharp
public sealed class Product : Aggregate<Guid, Timestamps, Versioned, Model, Deletable> { }    // what rask new scaffolds
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, SoftDelete> { }     // keep the row when deleted
public sealed class Invoice : Aggregate<Guid, Timestamps, Versioned> { }   // behaviour-rich: no model, no Delete
public sealed class Reading : Aggregate<Guid, Created> { }                 // append-only: no UpdatedAt, no Version
```

What the table does not carry, the generated types do not either. A `Reading` like the one above has no
`Version`, and its read face has `CreatedAt` and nothing else of the framework's:
`Reading.OrderBy(r => r.UpdatedAt)` is a compile error, not a query that fails. `DeletedAt` is on a read face
only for an aggregate that names `SoftDelete`.

A child `Entity` takes only a timestamp trait and `SoftRemoved` — `OrderLine : Entity<Guid, Timestamps>`,
`VirtualTank : Entity<VirtualTankId, Timestamps, SoftRemoved>`. Tenancy, the version, the delete and the form model are
the aggregate's, so naming one on an entity is a compile error (CS0311).

#### History and the one in force

**`SoftRemoved`** keeps a child its aggregate lets go of. Removed from the collection — by `Sync`, `Remove`, a
list's own — its row stays, stamped with `DeletedAt`, together with what it holds: whatever points at it (a fueling
at a virtual tank) still does, and a unique index over it still counts it, so the name it held stays taken. The
aggregate's load and the child's read face no longer see it; `IgnoreQueryFilters()` lists it again. An aggregate root
names `SoftDelete` instead, and a root that names `SoftRemoved` is refused when the model is built.

**`OneInForce<TChild>`** keeps the history of a child and at most one of it in force — a return's filings, a
period's closings. The child says when one is, in its own `Configure`; the aggregate puts the next one in force and
ends the last in one step:

```csharp
public sealed class Period : Aggregate<PeriodId, Versioned, OneInForce<PeriodFiling>>
{
    private readonly List<PeriodFiling> _filings = [];
    public IReadOnlyList<PeriodFiling> Filings => _filings;

    public void Close(Filer by) => _filings.Put(PeriodFiling.Of(by, Clock.Now), end: f => f.Unfile(by, Clock.Now));
    public void Reopen(Filer by) => _filings.End(f => f.Unfile(by, Clock.Now));
    public bool Closed => Filings.InForce() is not null;
}

public sealed class PeriodFiling : Entity<PeriodFilingId>
{
    public DateTime? UnfiledAt { get; private set; }
    static void Configure(EntityTypeBuilder<PeriodFiling> b) => b.InForceWhile(f => f.UnfiledAt == null, Texts.AlreadyFiled);
}
```

The rule is a unique index on the child's key to its aggregate over the rows in force only — filtered on SQL
Server, partial on PostgreSQL and SQLite — so of two saves that each file one, one commits and the other is refused
with a `RaskValidationException` under the new child's row (`Filings[1].PeriodId`) carrying the message; never a
check-then-insert. `InForce()`, `Put(…)` and `End(…)`
ask the same condition in memory, and `InForce()` throws if it ever finds two.

### Announcing a save

A save already refreshes the screen of the session that made it — that is `IDataChanges`, and it costs nothing and
needs no declaration. What it deliberately does not do is reach anyone else's screen, because another session's cache
is not this save's to touch.

`Broadcast` is that other half:

```csharp
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Broadcast> { }
```

The save is now announced to the whole process once it commits, and every query that reads orders refetches — a
read face keyed `QueryKey.For<Order>(…)`, or a message query carrying `[Live(typeof(Order))]`. Nothing is said in the
component; see [live queries](query.md#staying-fresh). Nothing is published unless a page is listening, and the
page's own query decides who may see the refresh.

Three things worth knowing:

- **After the commit, never on a rollback** — the same rule the interceptors already follow.
- **It needs no session.** A background job's write announces itself; `IDataChanges` could not, having no session's
  cache to invalidate. That is the gap this closes.
- **It is off unless named**, so nothing is published for a table no page is watching.

Like every subscription in Rask today, it reaches the process it was published in. Behind a load balancer, a page on
the other server catches up on its next refetch.

**Why a type argument and not an attribute.** The base is part of the type, so the generator reads it at
compile time and can never quietly miss it, and a trait in the wrong place is a compile error at the
declaration. Nothing is reflected over at run time, so a trimmed publish has the same shape as a debug build.

### Who changed what: `Audited`

An aggregate that names `Audited` leaves one `AuditEntry` in **every save that writes it — in that same save**, so
the entry and the change commit or roll back together:

```csharp
public sealed partial class DeliveryNote : Aggregate<int, Versioned, Deletable, Audited> { … }
```

| column | holds |
|---|---|
| `Aggregate` | the aggregate's type name, `DeliveryNote` |
| `Action` | `Insert`, `Update` or `Delete` (a soft delete is a `Delete`) |
| `Key` | `Id=42` — read once the rows went out when the database generates it, before the commit |
| `Before` / `After` | `Number=W-1 \|\| Km.Start=1200 \|\| Fuellings[0].Litres=12.5`: an insert every value it stores, a delete every value it held, an update the ones that changed — a value that became null among them, left out of the side where it is null |
| `User` | the signed-in user's id as their claim reads, `system` for a job, a hosted service or startup |
| `At` | when the save was made, in UTC (Rask's clock, so `Clock.Fake` moves it) |

- **Its children are part of it.** A line added, changed or removed is recorded in its aggregate's entry, under the
  place its form shows it at (`Fuellings[1].Quantity`), and only the aggregate's key names the entry.
- **The values are the stored ones**, a one-value value object as its value, written in the **invariant culture** —
  a decimal without trailing zeros (`1.5`, whatever scale the column keeps), a moment as `2026-11-24T13:05:00.0000000Z` —
  so the same save writes the same text on SQLite, SQL Server and PostgreSQL.
- **The table is `Audits`**, mapped in every model with an `Audited` aggregate and nowhere else, and its names stay
  as they are under the app's `TableNames`/`ColumnNames`. Map it onto a table you already have in your `IRaskModel`
  (`b.Entity<AuditEntry>().Property(a => a.Aggregate).HasColumnName("TableName")`).
- **The text is replaceable.** Register an `AuditFormat` of your own — `services.AddSingleton<AuditFormat, LegacyFormat>()`
  — and override `Values` for the whole text or `Value` for one value's, when the old rows of a table were written
  another way.
- **So is the row.** A table an older app already writes — its own columns, the whole row as JSON, the user as an `int`
  foreign key — keeps being written byte for byte by a format of the app's own, named once in its `IRaskModel`; Rask's
  `AuditEntry` is then left out of the model, and the `Audits` table is the app's to map:

```csharp
public sealed class AppModel : IRaskModel
{
    public static void Model(ModelBuilder m) => m.AuditAs(new LegacyAudits());
}

public sealed class LegacyAudits : AuditFormat<Audit, int>   // the app's row, its users' key type
{
    public const int SystemUser = 1;

    public override Audit Row(AuditedWrite<int> write) => Audit.Of(
        table: write.Aggregate.Name,
        user: write.User ?? SystemUser,                          // null: a job, a hosted service, startup
        action: write.Action.ToString(),
        oldData: write.Action == AuditAction.Insert ? null : Json(write.Before),
        newData: write.Action == AuditAction.Delete ? null : Json(write.After),
        key: Json(write.Key),
        at: write.At);
}
```

  `AuditedWrite<TUserId>` hands it everything: `Before` and `After` are **every** value the aggregate held and holds —
  its children's rows by path (`Lines[0].Quantity`), a value object as its stored value — and `Changed` the paths the
  save changed, so a format that writes only what changed filters by it (`write.Before.Where(v =>
  write.Changed.Contains(v.Name))`, then `Values(…)` for the `Name=value || …` text). `User` is a `Current.UseUser`
  scope's user, or the signed-in principal's id claim read as the key type (`Guid`, `int` or `long`), whether or not
  the app says `IUser`; null for nobody. The row is added to the same save; when the database generates the aggregate's
  key, `Row` is asked again once the rows went out, and the columns that moved are written before the commit. A
  format of the app's own rows registered as a service rather than named in the model is refused at the first save,
  with the `m.AuditAs(…)` line to write.
- A save that fails writes no entry, and its retry writes one. A raw `ExecuteUpdate`/`ExecuteDelete` writes no
  entry: it never passes through a save.

**The traits are the whole family.** Anything per-property or expression-shaped — a column name, an index, a value
converter, a length — stays in [`static Configure`](data.md#mapping-rules-the-conventions-dont-cover), which is where
it can actually be expressed.

#### Why the scaffold checks the version and soft-deletes nothing

Both are opt-in, and `rask new` names `Versioned` and `Deletable`. They look symmetrical and are not.

Soft delete used to be the default and is not any more, because it costs an ordinary application more than the
recovery is worth. A stamped row still occupies its UNIQUE constraints, so deleting the account `a@b.com` and
letting that person sign up again fails on a row nobody can see. "Delete my account" has to be able to mean
delete. And Rask already ships real recovery — [SQLite snapshots and Litestream](sqlite.md) — so keeping the
row was solving that problem a second time, worse, while charging every query a predicate.

The version check is scaffolded because **a lost delete is visible and a lost update is not**. A deleted row is
gone and somebody notices; two people editing one form both see "Saved" and the second silently erases the
first. The cost that would normally argue for turning it off — an unhandled concurrency exception — Rask already
absorbs: a stale save is a `RaskValidationException` about the whole form, which the form shows at its top.

#### An entity that knows its own time

`CreatedAt` and `UpdatedAt` — on a table that names `Timestamps` — are normally the framework's, stamped by
Rask's auditing interceptor on save. An
entity in a package that does **not** own the `DbContext` cannot rely on that, and a column nobody stamped
reads as `0001-01-01` — which is not obviously wrong anywhere it is used. Rask's own file store found this the
hard way: an unstamped `CreatedAt` became a `Last-Modified` header and the cutoff of an orphan sweep.

So `Entity<TId>` has a `protected void Stamp(DateTime at)`, and the interceptor fills only what is still
unset. An entity that knows its own creation time says so; one that does not gets the framework's; neither can
end up at `0001-01-01`.

### Turning the form surface off

Not every aggregate should be creatable from a form. A passkey is minted by a WebAuthn ceremony; a session
is started by signing in. The form surface is a trait, so an aggregate that leaves `Model` off has none:

```csharp
public sealed class Passkey : Aggregate<Guid, Timestamps, Versioned, Deletable>
{
    public string Name { get; private set; } = "";

    public void Rename(string name) => Name = name;
}
```

| The base names | What is generated |
|---|---|
| `Model` | `PasskeyModel`, `Create(model)`, `Update(id, model)`, `Model(id)`, `ToModel()` |
| `CreateModel` | `PasskeyModel`, `Create(model)`, `Model(id)`, `ToModel()` |
| `UpdateModel` | `PasskeyModel`, `Update(id, model)`, `Model(id)`, `ToModel()` |
| none of them | no `PasskeyModel` at all, and nothing that takes one |

**It reaches the form surface and nothing else.** These take no model, so they are there either way:

```csharp
var passkey = Passkey.For(me, "laptop"); await passkey.Save();   // its factory, then Save — takes no model
await Passkey.Update(id, p => p.Rename("desktop"));       // behaviour
await Passkey.Delete(id);                                 // never took a model (there under Deletable or SoftDelete)
await Passkey.Where(p => p.UserId == me);   // the read face is not negotiable
```

The read face stays because querying works through read models: an aggregate that could switch its own off
would be an aggregate nothing can read.

A **child** takes its root's answer: its model exists to be an element of the root's list, so an `Entity` cannot
name `Model` at all — the compiler refuses it.

### How a form save writes

**The model is the truth.** A save stores what the model holds, property by property, and reads nothing back
from the row to fill a gap:

| The model's value | The aggregate's property | What is saved |
| --- | --- | --- |
| a value | any | the value |
| `null` | declared nullable (`string? Notes`) | `null`: the user emptied the field |
| `null` | not nullable (`string Name`) | nothing: the write is refused, *This field is required.* under `Name` |
| a nested value-object model | a value object | the value object **built from the nested model alone**, by the same rules |
| `null` | a value object declared nullable (`Address? Billing`) | `null`: every column of it |
| a nested model **left wholly empty** | a value object declared nullable (`Address? Billing`) | the same `null`: it was not given, and nothing in it is asked for |
| `null` | a value object that is not nullable (`Money Price`) | nothing: the write is refused, under `Price` |
| `Version` is `null` | | no concurrency check |

The rules are the same one level down, and at every level below that. `Address` holds `string Street`,
`string? Floor` and `int? Door`; a reader who empties the floor has emptied it:

```csharp
var model = (await Customer.Model(id))!;      // Address: Street "1 Quay", Floor "2", Door 7
model.Address!.Floor = null;                  // what an emptied field binds
model.Address.Door = null;

await Customer.Update(id, model);             // Address_Floor NULL, Address_Door NULL, Address_Street untouched

model.Address.Street = null;
await Customer.Update(id, model);             // RaskValidationException: "Address.Street" — This field is required.
```

So a model from `new ProductModel()` or `product.ToModel()` saves exactly what the form shows, an update that
changes nothing writes nothing — text stored as `''` stays `''`, it is not turned into `NULL` — and a model is
never a patch: one built in code with only some members set clears the nullable rest and is refused for the
required rest. To change a few properties from code, `Product.Update(id, p => p.Reprice(price))` says so
directly. A member that is text and cannot be empty is refused when it holds `''` or white space as well, as a
root property is; declare it `string?` where the table really holds empty text.

The same holds for a create, which builds the value object from the model and nothing else: a member the model
leaves empty is not filled from the default the aggregate's initializer gave it.

**An optional value object left empty was not given.** A form over `Address? Billing` has to hold a nested model
to bind its fields to (`model.Billing ??= new()`), so a reader who fills none of them posts a model that is there
and empty. That is stored as no address — every column `NULL` — and none of its fields is asked for. Fill one
and it is an address: the rest of the rules apply, and a missing street is refused under `Billing.Street`.

```csharp
model.Billing = new();                                   // nothing typed
await Customer.Update(id, model);                        // Billing is null: every Billing_ column NULL

model.Billing = new() { City = "Szeged" };               // one field typed
await Customer.Update(id, model);                        // refused — "Billing.Street": This field is required.
```

*Empty* means exactly this, for every member of the nested model, at every level:

| The member's value | Counts as |
| --- | --- |
| `null` | empty |
| text that is `""` or only white space | empty |
| `false` | empty: a box nobody ticked says nothing |
| a nested model that is `null`, or itself wholly empty | empty |
| any other value — **including `0`** | filled |

Every member of a model is nullable (`bool?`, `int?`), so each has an empty state. A control the reader never
touched writes nothing and leaves `null`; a number field that is cleared goes back to `null`; a box that is
ticked and unticked again writes `false`, which is why `false` is empty too. `0` is a number somebody typed. A
value object that is **not** nullable is never read this way — its empty model is refused for each field it
lacks — and so is a row of a list, which is there because somebody added it.

**A save that changes nothing still writes nothing.** When the model is empty and what the row holds there is
empty by the same rule — every column `NULL`, blank text or `false` — the row is left exactly as it is: text
stored as `''` stays `''`. The columns are cleared only when something stored was a value.

**A form says "required" per place, not per type.** `Address Shipping` and `Address? Billing` are one nested
model type, and a control bound to `Shipping.Street` announces `aria-required` and refuses an empty street
before anything is saved, as ever. One bound to `Billing.Street` announces nothing and lets an empty street
through: a field's rule sees its own value and not the fields beside it, so it cannot tell an address nobody
gave from one that lacks a street. The write can, and says *This field is required.* under `Billing.Street`,
[where the form shows it](#a-create-and-an-edit-form). A rule the field's value object carries (`Zip`, declared
`: Text(Value, MinLength: 4)`) is still asked there about a value that was typed. Nothing is written for this: the generated model registers
which of its properties may be left empty
([`RaskValidation.RegisterOptionalFields`](forms-validation.md#how-a-control-knows-a-field-is-required)).

A form save writes properties through the aggregate's private setters, with generated `[UnsafeAccessor]`s and no
reflection. **It does not call the aggregate's methods**, so a rule that must hold for form input belongs in a
validation attribute on the aggregate (copied to the model), and a change that is a domain operation goes through
a method in the lambda.

**Or send it in a command.** A command, a query or an API endpoint can carry a `ProductModel`
(`public sealed record AddProduct(ProductModel Product) : ICommand<Guid>`), and the generators that build their
codecs recognise it although it is itself generated; the handler calls `Product.Create(command.Product)`. The
model carries every mapped property, so input from outside your own pages is best a command of its own that says
what may change.

**Your own write wins.** A static `Create(ProductModel, …)` or `Delete(Guid, …)` declared on `Product`
replaces the generated one at every call site (a member on the type beats an extension member), and the
generated one stays reachable as `ProductModelExtensions.Create(…)` for an override that only adds to it.

### What the model carries

- **Every mapped property** of the aggregate, nullable, and its `Version` when it names `Versioned`.
- **Not** the `Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, the domain events, navigations, collections or computed
  properties. Computed is read off the member: no setter, no backing field, and — for a collection — a getter that
  makes something of a list the aggregate already stores rather than handing it out. `OpenLines => _lines.Where(…)`
  beside `Lines => _lines`, or `SortedTags => _tags.Order().ToList()` beside `Tags => _tags`, is on neither the model
  nor the read face, and is not mapped; `Lines` and `Tags` stay, edited by the form as before.
- **A value object as a model of its own:** `Money Price` on `Product` is a nested `MoneyModel? Price` on `ProductModel`, filled by `new ProductModel()` and `ToModel()`, so a
  form binds `() => _product.Price!.Amount` like any [nested model](forms-advanced.md), however `Money` is declared.
  A one-value value object (`record Email(string Value)`) is carried as its value: `string? Email`.
- **Children keyed by several columns:** a link table as a list of ids (`List<int> ActivityIds`), a row with fields of
  its own as a list of rows (`List<CompanyColumnModel> Columns`) — see
  [Link tables and child rows on the form](#link-tables-and-child-rows-on-the-form).

### What a form over the model asks by itself

A form bound to an aggregate's generated model runs two kinds of rule with nothing written on the form:

```csharp
public sealed partial record DestinationName(string Value) : Text(Value, MinLength: 2, MaxLength: 255);

builder.HasIndex(d => new { d.Name, d.TenantId }).IsUnique("A destination with this name already exists.");

Form.Model(_destination).OnSubmit(d => Destination.Create(d))[   // _destination is a DestinationModel
    Ui.Input.Bind(() => _destination.Name),                      // both rules, no step
    Ui.Button.Submit["Save"]
]
```

**The value object's own rules.** A value object that holds ONE value and is
[declared from a base record](#declared-from-a-base-record) — `Text`, `Number`, `Whole`, `Day` or `Moment`, its base's
arguments and any `Rules()` of its own, or `: Value` with ONE positional member (`record Rank(int Value) : Value`) and
its `Rules()` — is asked by the field that holds it, before any `.Validate(…)` written on
the field, which then runs only for a value it accepted. The form asks exactly what its constructor checks, through
the generated `X.__Problems`, so the two cannot drift. A value object not declared from a base record has no rule
of its own here, and a `public static Validate` on one an aggregate holds is never asked — the build says so
([RASK132](diagnostics.md#rask132)) rather than let it look like a rule that passed.

**A value object of several values is asked as the form is filled, too.** Each member of its nested model —
`Km.Start` and `Km.End` of a `Range`, `Opening.Date` and `Opening.Quantity` of a `: Value` record with its own
`Rules()` — asks the value object's generated `TryCreate` with what the model holds beside it, and shows each
`Problem` under the member it names: `Problem.At(Quantity, "Both or neither.")` is under `Opening.Quantity` the moment
the quantity is typed without a date, not only on save. A member the value object needs and the model leaves empty
says only that it is required; a problem about the whole value (`Problem.Whole`) is shown on save, under the field.

**A non-nullable value-object property is required, and its base says so.** No attribute is needed:

```csharp
// before                                               // after
[Required(ErrorMessage = "A destination needs a name.")]
public DestinationName Name { get; private set; }       public DestinationName Name { get; private set; } = null!;
```

- a **text** value object is asked about `""` when the field holds nothing, and `Text` answers that it is
  required — in the reader's language, from the strings catalog
- a value object over a **number or a date** has no empty value to be asked about, so an unfilled field
  says the one sentence *This field is required.* and the rule is asked once there is a value
- a **nullable** property (`DestinationName? Alias`) is optional: an empty field (text: only blanks) is not put
  to the rule — and so is a one-value **struct** held as `T?` (`NormPercent? Discount`), whose field is the
  `decimal?` it holds
- a **plain** non-nullable property — `int Weight`, `decimal Price`, `DateOnly Built`, `Guid OwnerId`, `bool`,
  `string Plate` — and a value object with no rule are required the same way: an unfilled field says the one
  sentence. For text, unfilled means `null`, `""` or only white space; for everything else, no value
- a nullable plain property (`int? Axles`, `string? Notes`) is optional
- a `[Required(ErrorMessage = "…")]` you wrote on the aggregate yourself is kept, with its message, and is then
  the only thing that says it

A control bound to a field that is required this way says `aria-required="true"`
([how a control knows](forms-validation.md#how-a-control-knows-a-field-is-required)): each generated model type
— the root, a nested value-object model, a row — announces exactly the fields its write refuses unfilled. A
member of a value object of several values is such a field: `string Street` on `Address` is required under
`Address.Street`, `string? Floor` is optional, and the validation attributes the value object's own properties
carry are checked by the write under the same path. The exception is a field under a value object that
[may be left wholly empty](#how-a-form-save-writes) — `Address? Billing` — which a form announces nothing
about, while the same type held as `Address Shipping` is announced as ever.

The one sentence is the strings catalog's `ValidationRequired` — *This field is required.*, with the kit's
built-in translations — so it follows the reader's language, and
[a catalog of your own](localization.md) can reword it.

These rules are not attribute validation, so `RaskValidation.AutoValidate = false` does not switch them off, and
they are the same ones `Create(model)` and `Update(id, model)` ask with no form at all.

**The aggregate's unique rules.** **Write the `IsUnique("…")` in the aggregate's own `Configure`**: an index
declared in a helper method or an `IEntityTypeConfiguration` is enforced at save but not asked beforehand, and
so is one without a message. Every index `Configure` declares with a message is
asked of the database when a bound field is [committed](forms-validation.md#the-database-said-no) and again on
submit. With the default binding a field is committed with the reader's next action — a click, a submit — and a
`.Blur()` field on leaving it; in a [form over an operation](forms.md#bind-timing) a field typed into is committed on
leaving it unless it says `.Defer()`, so a taken name is told as the field is left there and at Save elsewhere.
The message shows under the field, or under each field of an index over several:

- over **exactly the index's columns**: one that names `TenantId` about the current tenant's rows — the tenant is
  the one in flight, never the form's to say, and with no tenant, or inside `Tenant.Across()`, the rule is left to
  the save — and one that does not about **every tenant's rows**, with a tenant or without one
  ([an index is what you write](multi-tenancy.md#an-index-is-what-you-write))
- an edit form does not collide with **the row it was filled from**: the model remembers its key from
  `Destination.Model(id)` / `ToModel()`. It is not a field — nothing binds or posts it — and it only decides
  which row this courtesy check leaves out; the save takes its id beside the model and is checked on its own
- **an unfilled field is not asked** — `null`, or a text field still `""` — so an index over two fields is
  asked once both hold a value, and once for the pair, not once per field. A genuine empty-string duplicate is
  caught by the save
- the same indexes are asked as [by the save on an adopted table](data.md#when-the-index-may-not-be-there): an index
  filter other than its own columns being `NOT NULL` is skipped
- on every host that has Rask.Data, whoever created the schema

It is a courtesy, never the rule: two people can pass it at the same moment, and the save — the index itself,
or the declared rule asked again in the save's transaction — still refuses the second, shown the same way.

Both belong to the **generated** model. A model you write by hand gets neither — write the rule on the field
(`.Validate(…)`) and let the save report the index. An index declared somewhere other
than the aggregate's own `Configure`, and one on a row inside an aggregate, are enforced by the save and not
asked beforehand.

A `partial class ProductModel` of your own merges into the generated one, which is the way to add members,
`IValidatableObject` or display helpers. `Create(model)` and `Update(id, model)` run the fields' attributes, then the
model's own `IValidatableObject` and the validation attributes on your half of it, with the services of the work in
progress in every `ValidationContext`. An `AsyncValidationAttribute` is awaited by the form, and on the server by the
save, which asks it of the aggregate's property ([what every save asks](#what-every-save-asks)); an
`IAsyncValidatableObject` on the model is the form's alone — put a rule that must hold on the server in the aggregate's
`Validate()`. The build says when it cannot generate: a **hand-written, non-`partial`
`ProductModel`** beside a `Product` is an error ([RASK082](diagnostics.md#rask082)), and an aggregate **declared
inside another class** is a warning and gets no model ([RASK083](diagnostics.md#rask083)).

On the wire a model's properties are **camelCase**. Only DataAnnotations attributes are copied from the aggregate — the
validation ones, those that describe a field (`[Display]`, `[DataType]`, `[Editable]`, `[DisplayFormat]`, `[UIHint]`…),
and any `ValidationAttribute` of your own — never `[Key]`, `[Timestamp]` or `[ConcurrencyCheck]`, a Schema attribute
like `[Column]`, or a `[JsonPropertyName]`. So a `[JsonPropertyName]` on the aggregate does not rename the model's
property; a property you declare yourself on a `partial ProductModel` keeps its own pin.

What each one does, where it is read — on the aggregate's property, a generated model's, or a hand-written form model's:

| Attribute | The form and its control | `Create(model)` / `Update(id, model)` | Every save | The column |
| --- | --- | --- | --- | --- |
| `[Required]` | checked; `aria-required` and the *Required* badge | checked | checked | `NOT NULL` (EF Core's) |
| `[StringLength]`, `[MaxLength]`, `[MinLength]`, `[Length]` | checked; a text input's `maxlength`/`minlength` — on a value object's property, the tighter of the attribute and the value object's own | checked | checked | `[MaxLength]`/`[StringLength]` the length (EF Core's; RASK128 when it and the value object disagree) |
| `[RegularExpression]` | checked; a text input's `pattern` when the browser reads the expression the same way (none otherwise) | checked | checked | – |
| `[Range]`, `[CreditCard]`, `[FileExtensions]`, `[EnumDataType]`, `[Base64String]`, `[AllowedValues]`, `[DeniedValues]`, `[Compare]`, your own `ValidationAttribute` | checked | checked | checked | – |
| `[EmailAddress]`, `[Phone]`, `[Url]` | checked; the input's `type` (`email`, `tel`, `url`) | checked | checked | – |
| `[DataType]` | the input's `type`: `Password`, `EmailAddress`, `PhoneNumber`, `Url`/`ImageUrl`; `Date` on a `string` or a `DateTime` (the day of it), `Time`/`DateTime` on a `string`; `MultilineText` a textarea | – | – | – |
| `[Editable(false)]` | a read-only input (any other kit control disabled), which wires no write | – | – | – |
| `[DisplayFormat]` | `ConvertEmptyStringToNull = false`: an emptied `string?` is written `""`, not `null`; `DataFormatString` and `NullDisplayText`: how a `Ui.TableCell.For` writes it | – | – | – |
| `[ScaffoldColumn(false)]` | no `Ui.TableColumn.For` heading and no `Ui.TableCell.For` cell | – | – | – |
| `[UIHint]` | `Ui.Field.Bind` draws the kit control it names where one fits — `FieldHints.Switch` on a `bool`, `FieldHints.RadioGroup` on an enum, `FieldHints.MultilineText` on a text; any other name is kept as `FieldShape.Hint` | – | – | – |
| `[Display]` | the label, help text, placeholder, and the `{0}` of every message | the `{0}` of its messages | the `{0}` of its messages | – |

The same rules written with FluentValidation — in the aggregate's own `static void Rules(InlineValidator<T> rules)`, or
in an `AbstractValidator<T>` class — are read in exactly these places: the generated model's members carry the
aggregate's rules on its form (live, `MustAsync` included, a row's under `Lines[2].Quantity`), every save asks them, and
`NotEmpty()`, `MaximumLength`/`Length`, `EmailAddress()`, `InclusiveBetween`/`GreaterThanOrEqualTo`… and
`PrecisionScale(p, s)` give the hint and the column their attribute twin gives — `NOT NULL`, the length, `decimal(p, s)`
— which a migration shows. An attribute on the same property wins for its column. The value object stays the
recommended way: see [three ways to say a rule](forms-validation.md#three-ways-to-say-a-rule).

`[Editable(false)]` keeps the reader from changing the field; it does not stop the aggregate's own method from changing
the property. The read face (`ProductRead`) carries `[Display]`, `[DisplayFormat]` and `[ScaffoldColumn]` from the
aggregate's property, so a list's columns are named, written and shown as the property says
([ui-kit — columns from the row's members](ui-kit.md#columns-from-the-rows-members)).

## The transaction boundary

**One aggregate, one transaction.** An operation's entry, `Save()` and every generated write load one aggregate
whole, change it and save it in one transaction — its children, its version check and its events with it. That is
the boundary the aggregate was drawn for — so app code does not write an aggregate on a `DbContext` of its own
(`db.Products.Add(p)`, `db.Update(order)`, `db.Remove(supply)`, then `SaveChangesAsync()`), which goes around the
factory, the operations, their roles and the save's rules; [RASK133](diagnostics.md#rask133) points at it, outside seeds,
migrations and tests. A change that seems to need two aggregates is decided in this order:

1. **Draw the boundary again.** Two things that must always change together are usually one aggregate: a part that
   never stands on its own is a [child](#children), not an aggregate of its own.
2. **Follow from an event.** The first aggregate raises what happened, and a handler runs the second one's operation
   after the commit — in memory (`IEventHandler<T>`), or through the [outbox](outbox.md) when it must not be lost
   (`IDurableHandler<T>`), at least once and in its own transaction:

   ```csharp
   public sealed record OrderPlaced(OrderId Order, StockItemId Stock, Quantity Quantity) : IEvent;

   public sealed partial class Order : Aggregate<OrderId, Versioned>
   {
       public StockItemId Stock { get; private set; }
       public Quantity Quantity { get; private set; } = null!;

       public void Confirm() => Raise(new OrderPlaced(Id, Stock, Quantity));   // what happened, said once
   }

   public sealed partial class StockItem : Aggregate<StockItemId>
   {
       public void Reserve(Quantity quantity) { /* its own rules */ }
   }

   public sealed class ReserveStock : IDurableHandler<OrderPlaced>
   {
       public Task Handle(OrderPlaced e) => StockItem.Reserve(e.Stock, e.Quantity);   // the other aggregate's own entry
   }
   ```

   The handler is one line: it decides nothing, it calls the operation that does. A refusal there is the second
   aggregate's to give, and the outbox retries the handler, so the operation it calls should be safe to run twice.
3. **The last resort: both in one save.** A check that must hold across two aggregates at the very moment of the
   write — and that no boundary and no event can carry — saves them together, below. It is the exception, so each
   use says why: `manyAggregates: "…"`.

### Changing two aggregates together — the last resort

When neither a new boundary nor an event will do, the two entries run inside one `Db.Transaction`, and each joins it:

```csharp
OrderId order = await Db.Transaction(async () =>
{
    await StockItem.Reserve(stock, quantity);              // its rules, its version check — refused rolls both back
    return await Order.Place(reference, stock, quantity);  // joins the same transaction
}, manyAggregates: "A reservation that followed from an event could sell the last unit twice");
```

Each entry still loads its own aggregate whole and asks its own rules; what changes is that both commit together or
neither does, and the events of both are published after that one commit. The reason is written where the decision
is made, so the next reader does not have to guess which invariant needed it — and a transaction that writes a
second aggregate without one is refused in a test ([below](#one-aggregate-per-transaction-manyaggregates)). An
operation that always needs it says so once, in its `Configure`: `b.Operation(x => x.M, manyAggregates: "why")`.

#### One aggregate per transaction: `manyAggregates:`

An aggregate is the transaction boundary. A `Db.Transaction` that writes a second one is a decision, so it says
why, where it is made:

```csharp
await Db.Transaction(async () =>
{
    await filling.Save();
    foreach (var withdrawal in withdrawals) await withdrawal.Save();
}, manyAggregates: "The IBC filling and its withdrawals are only valid together");

builder.Services.AddRaskData(o => o.OneAggregatePerTransaction = AggregatePolicy.Throw);   // Off | Warn | Throw
```

- **What a second aggregate without a reason meets** is `RaskDataOptions.OneAggregatePerTransaction`: `Warn` by
  default (a warning in the log naming the aggregates, once per transaction), `Throw` — a `ManyAggregatesException`
  naming them (`Supply 42, Tank 7`), the transaction rolled back — or `Off`. **A `TestDatabase` runs at `Throw`**, so
  a test finds the transaction that owes a reason; a test's own `services:` can set it back.
- **What counts** is what the transaction's saves wrote, each aggregate once: a child counts as its aggregate (a
  changed `SupplyPart` is its `Supply`), the same aggregate saved twice is one, and two rows of one type are two.
  Rask's own tables — outbox, jobs, mail, sessions, passkeys, stored files, cache, push subscribers — are no aggregate
  of the app's and never count; mark an app table of the same kind `b.Infrastructure()` in its `Configure`. A
  [set write](data-writes.md#writes-over-many-rows) (`X.UpdateWhere`, `X.DeleteWhere`) counts once, as `Pairing (set)`,
  whatever rows it touched; a batch of `X.UpdateEach` or `X.Import` states its own reason — each row is an aggregate of
  its own — and is let through. A raw `ExecuteUpdate` and a `BulkInsert` are not saves and are not counted
  ([RASK187](diagnostics.md#rask187) points at the first). A save refused for another reason counts nothing.
- **Nested blocks are one transaction.** The outermost counts, and a nested `Db.Transaction(…, manyAggregates: …)`
  states the reason for all of it.
- **The build says it first.** [RASK178](diagnostics.md#rask178) points at a `Db.Transaction` whose work writes two
  aggregate types with no `manyAggregates:`, and its quick-fix writes `manyAggregates: "TODO: why"` for you to fill in.
- **A test asks it outright:** `Db.Record()` records every transaction that commits in the flow until disposed —
  `saves.Transactions` holds each one's aggregates and reason — and `saves.EachWroteOneAggregate()` fails on one that
  wrote more, reason or not:

  ```csharp
  using var saves = Db.Record();
  await DeliveryNote.Write(note, 0);
  saves.EachWroteOneAggregate();
  ```

#### After the commit: `AfterCommitFailures`

What a `Db.Transaction` announces runs once it has committed: its in-memory `IEventHandler`s, the data-changed notice
to the live pages, the outbox's wake-up. The work has happened by then, so a step that throws there does not make the
call fail:

```csharp
builder.Services.AddRaskData(o => o.AfterCommitFailures = AfterCommitFailure.Throw);   // Log (default) | Throw
```

- **`Log`** (the default): the error is logged, every step after it still runs, and the caller sees the committed
  work succeed — which it did.
- **`Throw`**: every step still runs, then the caller gets the failure — the exception itself, or an
  `AggregateException` when several threw. The work is committed all the same.
- Before, the first step that threw skipped the ones behind it and failed a call whose work was already committed.
- A durable handler is not such a step: it runs from the outbox, retried until it succeeds.

A generated write also takes `db:`, to **stage** its change on a context the caller already holds and saves itself —
a seed, a context of the app's own — rather than open one: `await Order.Create(model, db: db)`, then
`db.SaveChangesAsync()`. One `SaveChangesAsync` is one transaction, so writes staged on one context commit together.
A staged `Create` returns an aggregate not yet saved (a store-generated integer key is `0` until then), and the
concurrency check is made at *that* save.

### Declared projections: `Projects`

Some state is made *from* rows rather than kept on them: a fueling's sequence number per pistol, the company a vehicle
last fuelled for. Written as a save hook — an interceptor that looks at what changed and writes elsewhere after the
save — it runs outside the save's transaction, so a crash between the two leaves them disagreeing, and it hangs on
whichever aggregate happened to be saved. Declare it on the rows instead, and let the rows raise an event:

```csharp
public static void Configure(EntityTypeBuilder<Fueling> b) =>
    b.Projects(f => new { f.Moment, f.PistolId, f.Number },
               changed => new FuelingsToNumber(changed.Values(f => f.PistolId)));
```

- **What it watches:** a save that adds a `Fueling`, removes one, or changes a member `watched` names to another value
  — `f => f.Moment`, or several as `f => new { … }`. A change to anything else, or a member set to the value it held,
  raises nothing. Anything but the row's own members (`f => f.Number + 1`) is refused where it is declared.
- **Once per save:** `raise` gets every row the save changed as `ProjectedRows<Fueling>` — each a `ProjectedRow` with
  `Before` (a detached copy of what is stored, `null` for an added row) and `After` (the row as written, `null` for a
  removed one). `changed.Values(f => f.PistolId)` is every value a member holds on either side, each once: the pistol a
  fueling left as well as the one it went to. Return `null` to raise nothing for this save.
- **Raised as the aggregate's own event**, onto the first row the save changed: a [durable handler](outbox.md) of it —
  `IDurableHandler<FuelingsToNumber>` — has its outbox row written **in that same save**, so the projection's work is
  stored with the change or not at all; an in-memory `IEventHandler` hears it after the commit, and a rollback raises
  nothing. A retried save raises it once, and a `raise` that throws fails the save.
- **On an aggregate**, since its events ride on its rows: a `Projects` on a child or a plain entity is refused where it
  is declared. Declaring the same event again on the same type replaces the first declaration.
- Nothing is written to the database for it — no column, no SQL, no migration.

### A check and the writes it guards: `Db.Transaction`

A read face (`Pistol.Count`, `Tank.Any`) opens a context of its own, so a check made with one outside the write runs
outside the save it guards, and two requests can both see "free" and both take it. A check like that belongs in the
operation itself — its `Validate()` or a `Refuse` reading the read face runs in the entry's transaction. Where the check
and the write must still be put together by hand, `Db.Transaction` holds them — Laravel's `DB::transaction`, Rails'
`transaction(isolation:)`:

```csharp
await Db.Transaction(async () =>
{
    if (await Pistol.Count(p => ids.Contains(p.Id)) != ids.Count) return;   // runs in the same transaction
    await Tank.Update(id, t => t.Change(...));                              // joins it, with no db:
});                                                                         // provider default isolation

await Db.Transaction(async () => { ... }, isolation: IsolationLevel.Serializable);

var order = await Db.Transaction(async () => await Order.Create(model));    // hands back a value
```

- **Everything without a `db:` joins.** `Find`, `Where`, `Any`, `Count`, the read faces, `Create`, `Update`,
  `Delete`, `Save`, child writes and the unique/store-rule checks each still open a context — on the
  transaction's connection, enlisted in it.
- **A `db:` you pass joins too**, or is refused — it never commits on its own beside the work. On the same
  connection string, and not opened before it was handed in, it is enlisted (so `db.SaveChangesAsync()` writes
  inside the transaction); on another database, in a transaction of its own, or with its connection already
  open, the call throws `InvalidOperationException`. The same goes for `db.BulkInsert(...)`. It stays yours:
  when the transaction ends — committed, rolled back or retried — it is out of it and back on its own connection,
  so the same `db` can go into the next `Db.Transaction` or save on its own.
- **Your own context joins at its first command.** A context of the app's own — from the factory `AddRaskData<T>(o => …)`
  registers or any whose options carry Rask's interceptors (`AddInterceptors(sp.GetServices<ISaveChangesInterceptor>())`,
  which a `RaskApp` and every documented hand-wired host already add) — is enlisted before the first thing it runs
  inside the block, even when it was created before it: a LINQ read, `SaveChangesAsync`, any command it sends. So its save does not wait on the locks the transaction's own reads took, a rollback
  takes its writes back, and a later `db: db` call finds it joined. It is handed back on its own connection like
  any `db:`. No `db.Database.BeginTransactionAsync()` inside the block — the context is already in one, and Rask
  refuses a second with an `InvalidOperationException` saying so; `Database.Migrate()` and `EnsureCreated()` are
  refused the same way, since schema work belongs outside the block (at startup).

  A context on another database, already in a transaction of its own, or whose connection you opened before the
  block runs as it always did, outside — and a `db:` call with it is refused as above.
  A context that joins as it opens still has the open reported to its connection interceptors, on the transaction's
  connection. Rask's SQL Server and SQLite interceptors send nothing then — that connection got its `SET`s and
  pragmas when it opened. A `ConnectionOpened` interceptor of your own that sets a connection up should skip it the
  same way: `if (eventData.Context?.Database.CurrentTransaction is not null) return;`.
- **A job or a mail queued inside is a row in the transaction.** `Jobs.Enqueue(...)` (with or without
  `.UnlessWaiting()`) and `Mail.Send(...)` write on its connection: no processor sees them before the commit,
  and a rollback leaves nothing to run or deliver. Their table must be in the same database; otherwise the
  enqueue throws rather than write outside.
- **Committed when the work completes; rolled back when it throws**, and the exception reaches you unchanged.
- **After the commit, never before:** domain events are published, the outbox is woken (its rows were written
  inside, so they commit or roll back with the work), and the session's queries are told what changed.
  A rollback announces nothing.
- **What Rask starts while the block is open runs outside it.** A render that a `StateHasChanged` asks for, a
  handler's repaint between its awaits, a query's fetch and a subscription's stream each read on a connection of
  their own, at the database's default isolation, and see only what is committed. They never take the
  transaction's locks, so a page's read cannot deadlock the work it repaints for. Reads that the block's own code
  makes are inside the transaction.
- **`isolation` is optional.** Omitted, none is sent and the database's default applies: read committed on SQL
  Server and PostgreSQL. SQLite is serializable and has one writer — a second `Db.Transaction` waits at its
  `BEGIN` for the first to commit, then sees what it wrote.
- **Serializable is what makes a check-then-write safe** on SQL Server and PostgreSQL. Two such transactions
  that both read the same rows and then write cannot both commit: SQL Server picks a deadlock victim (1205),
  PostgreSQL refuses one with a serialization failure (40001). Both are transient, so with retrying on (the
  default for both providers) the loser runs again from the top and sees the winner's row; with retrying off
  it throws.
- **On a retrying context the whole work may run again**, so keep side effects outside the database (an HTTP
  call, a cache write) out of it — or put them in a [durable handler](outbox.md). `Mail.Send` and `Jobs.Enqueue`
  are rows, so they are written once. What an attempt that rolled back did to the aggregates it saved is taken
  back — their remembered read, `Version`, domain events, and a key the database generated for a row that is gone —
  so an aggregate loaded or made before the block is saved by the retry as if the failed attempt had never run.
- **Nested `Db.Transaction` joins the outer one.** Same connection, same transaction, committed by the
  outermost. It is not a savepoint, and asking for a different `isolation` inside throws.
- **One connection, one thing at a time:** no `Task.WhenAll` over reads inside it — [RASK142](diagnostics.md#rask142)
  says so where it sees one.
- **What does not join:** the cache does not take part — `Cache.Remember` and `Cache.Forget` act at once and
  are not undone by a rollback. Nor does a context built without Rask's interceptors and never handed to a Rask call.

It works the same from a live event handler, an HTTP endpoint, a job and a test, on a `RaskApp` host and on
one wired by hand with `AddRaskData<TContext>()`.

## Testing a model

An operation's rule is asked of the aggregate itself — found, and its method called, with nothing saved — and
`Test.Refuses` says which field the refusal names:

```csharp
[Fact]
public async Task An_unpaid_order_does_not_ship()
{
    await using var database = await TestDatabase.Start<AppDbContext>();
    var order = (await Order.Find(await Order.Place(Test.Make<OrderReference>())))!;

    Test.Refuses(() => order.Ship(Test.Make<TrackingNumber>())).Whole();
}
```

Every test gets a real database in one line — SQLite in memory, a database of the test's own, made
from the app's own context so the save runs exactly what it runs in production: the domain's rules, the interceptors,
the version check, the domain events:

```csharp
[Fact]
public async Task A_second_supply_of_one_delivery_note_is_refused()
{
    await using var database = await TestDatabase.Start<AppDbContext>();
    await Supply.Register(company, details, parts);

    (await Test.Refuses(() => Supply.Register(company, details, parts))).At("Details.Number");
}

[Fact]
public async Task Two_edits_from_one_version_keep_one()
{
    await using var database = await TestDatabase.Start<AppDbContext>();
    var id = await Supply.Register(company, details, parts);
    var version = (await Supply.Find(id))!.Version;   // both forms were filled from the same version

    (await Test.Concurrently(
        () => Supply.Change(id, version, details, partsA),
        () => Supply.Change(id, version, details, partsB))).Whole();   // one kept, the other refused as stale
}
```

- **`TestDatabase.Start<AppDbContext>()`** — or `TestDatabase.Start()` for an app on Rask's own context — is a named
  shared-cache SQLite database in memory, kept alive for the test and let go when it is disposed. A database started
  inside a test binds the read faces and the entries for that test's own flow, so **tests run in parallel**, each on
  its own rows. The schema is made once per model and copied into each later database, so a start costs well under a
  millisecond.
- **The same answers as production.** It is configured through `UseRaskSqlite`, so text compares as the app's
  `OrderTextBy` collation says, a decimal orders as a number, `end - start` is a duration and `Db.Lock` holds — what
  the app writes is neutral ([code written for one database](data.md#code-written-for-one-database)), and Rask's own
  conformance suite proves each of those behaves the same on SQLite, SQL Server and PostgreSQL
  ([the conformance suite](data.md#the-conformance-suite)). An app's tests need no SQL Server of their own.
- **`Test.Make<T>()`** hands back a value object its rules accept, so a test that does not
  care about a value never writes a literal a tightened rule would one day refuse.
- **`Events.Fake()`** records what a save published; **`Test.Concurrently`** proves exactly one of two writers wins —
  a version check, a unique index, a `LockRead.ForUpdate` lock or a `Db.Lock`.

A test that needs an aggregate to work on and does not care which asks `await Test.Make<Order>()`
(`Rask.Testing.Data`): it is made through the aggregate's own factory, what it refers to is made and stored first, and
it is saved with its rules asked ([testing](testing.md#a-stored-aggregate-its-rules-accept-await-testmakesupply)).

[Testing the domain](testing.md#testing-the-domain) has all of it — fixtures shared by a class, what a save runs, the
fake clock, a signed-in user.
