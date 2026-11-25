# Chapter 3 — A second feature, and locking it down

> **Goal:** add an **Orders** feature on the same database — an order with lines, a shipping address and an optional
> gift note — then say who may change the catalog.
> **You'll write:** a second slice under `Features/Orders/`, the roles on `Product`'s operations, and their tests.

## 1. An order, with its lines

The One Person Framework idea is *one* database for the whole product, so a second feature does **not** get a
second `DbContext`: `RaskAppDbContext` maps `Order` exactly as it maps `Product`.

An order is bigger than a product, and it shows the rest of the shapes a domain takes. `Features/Orders/Order.cs`:

```csharp
namespace Shop.Features.Orders;

public readonly partial record struct OrderId(Guid Value);

public sealed partial record CustomerEmail(string Value) : Email(Value);

public sealed partial record TrackingNumber(string Value) : Text(Value, MaxLength: 40, Upper: true);

public sealed partial record GiftNote(string Value) : Text(Value, MaxLength: 300);

public sealed partial record ShippingAddress(string Street, string City, string PostCode) : Value
{
    protected override IEnumerable<Problem> Rules()
    {
        if (PostCode.Trim().Length is < 3 or > 10)
        {
            yield return Problem.At(PostCode, "A post code is 3 to 10 characters.");
        }
    }
}

public enum OrderStatus
{
    Placed,
    Shipped,
}

public sealed partial class Order : Aggregate<OrderId, Timestamps, Versioned, Deletable>
{
    public CustomerEmail Customer { get; private set; } = null!;

    public ShippingAddress ShipTo { get; private set; } = null!;

    public GiftNote? Gift { get; private set; }

    public OrderStatus Status { get; private set; }

    public TrackingNumber? Tracking { get; private set; }

    public Children<OrderLine, OrderLineSpec> Lines { get; } = new();

    private static Order Place(CustomerEmail customer, ShippingAddress shipTo, GiftNote? gift, IReadOnlyList<OrderLineSpec> lines)
    {
        var order = new Order { Id = OrderId.New(), Customer = customer, ShipTo = shipTo, Gift = gift };
        order.Take(lines);
        return order;
    }

    public void Change(ShippingAddress shipTo, Change<GiftNote?> gift, IReadOnlyList<OrderLineSpec> lines)
    {
        Refuse.Unless(Status == OrderStatus.Placed, "A shipped order cannot change.");
        (ShipTo, Gift) = (shipTo, gift.Applied(Gift));
        Take(lines);
    }

    public void Ship(TrackingNumber tracking)
    {
        Refuse.Unless(Status == OrderStatus.Placed, "This order has already shipped.");
        (Status, Tracking) = (OrderStatus.Shipped, tracking);
    }

    private void Take(IReadOnlyList<OrderLineSpec> lines)
    {
        Refuse.When(lines.Count == 0, () => lines, "An order has at least one line.");
        _ = Lines.Sync(lines, l => l.Id, OrderLine.For, static (line, spec) => line.Take(spec));
    }

#pragma warning disable S1144 // found by convention: every delete asks it
    private IEnumerable<Problem> ValidateDelete()
    {
        if (Status == OrderStatus.Shipped)
        {
            yield return Problem.Whole("A shipped order stays on the books.");
        }
    }
#pragma warning restore S1144
}
```

And its lines, `Features/Orders/OrderLine.cs`:

```csharp
using Shop.Features.Products;

namespace Shop.Features.Orders;

public readonly partial record struct OrderLineId(Guid Value);

public sealed partial record Quantity(long Value) : Integer(Value, Min: 1, Max: 100);

public sealed record OrderLineSpec(OrderLineId? Id, ProductId Product, Quantity Quantity);

public sealed class OrderLine : Entity<OrderLineId>
{
    public ProductId Product { get; private set; }

    public Quantity Quantity { get; private set; } = null!;

    internal static OrderLine For(OrderLineSpec spec)
    {
        var line = new OrderLine { Id = OrderLineId.New() };
        line.Take(spec);
        return line;
    }

    internal void Take(OrderLineSpec spec) => (Product, Quantity) = (spec.Product, spec.Quantity);
}
```

What is new since chapter 2:

- **A value object of several values.** `ShippingAddress` is a street, a city and a post code that only mean
  something together. It derives from `Value`, its members are columns on the order's row (`ShipTo_Street`,
  `ShipTo_City`, `ShipTo_PostCode`), and its own `Rules()` say what a valid one is. A member it may not lack is
  required under its own name, and `Problem.At(PostCode, …)` puts a message under the post code's field.
- **Kinds of text.** `CustomerEmail` derives from `Email`, a `Text` that also checks the address's form and tells a
  bound input it is an email. `Phone`, `Url`, `Password` and `Multiline` are its siblings
  ([kinds of text](../domain.md#kinds-of-text-email-phone-url-password-multiline)).
- **Children: `Children<OrderLine, OrderLineSpec>`.** The lines are entities inside the order — each has its own
  `OrderLineId`, and none stands on its own. `Lines.Sync(lines, …)` makes the children whatever the list of specs
  says: a spec with no id is added, one naming a line updates it, and a line no spec names is removed. They load and
  save with the order, so a change to a line bumps the order's `Version` ([children](../domain.md#children)).
- **Another aggregate, by id.** A line holds a `ProductId`, never a `Product`. Each aggregate is loaded and saved on
  its own, so an order never drags a product along with it.
- **Keep, clear or set: `Change<GiftNote?>`.** `Change` takes the gift note as a `Change<T>`: an empty field keeps
  the note the order already has, a typed one sets it, and the form draws a *Clear* beside it that takes it away.
  `gift.Applied(Gift)` is the one line that does all three.
- **A rule about deleting: `ValidateDelete()`.** A private method found by name, asked by every delete. A shipped
  order stays; the editor's *Delete* tries it first and shows the reason instead of asking
  ([what every save asks](../domain.md#what-every-save-asks)).
- **Refusals for the whole form, or one field.** `Refuse.Unless(…, "…")` names no field, so its message stands at the
  top of the form. `Refuse.When(lines.Count == 0, () => lines, …)` names the list, so it is under the lines.
- **An enum stays an enum.** `OrderStatus` is a column of its own, read and compared as it is.

`Ship` takes a `TrackingNumber`, so it is an operation of its own shape. That is also how `Form.For` tells two forms
apart: it picks the operation by the parameters of its entry ([RASK174](../diagnostics.md#rask174)).

## 2. The orders page

`Features/Orders/OrdersPage.cs` is chapter 2's shape — a list, and the editor over it — this time as a flyout from
the right, as wide as the screen allows, because an order has rows of its own:

```csharp
using Microsoft.AspNetCore.Authorization;
using Shop.Features.Products;

namespace Shop.Features.Orders;

[Authorize]
[Route("/orders")]
public sealed partial class OrdersPage(RouteState route) : Component
{
    [QueryParam("edit")] public string? Edit { get; set; }

    protected override string? PageTitle => "Orders";

    protected override Component? Render()
    {
        var orders = QueryClient.Query(QueryKey.For<Order>("recent"),
            async () => await Order.OrderByDescending(o => o.CreatedAt).Take(50));
        var products = QueryClient.Query(QueryKey.For<Product>("choices"),
            async () => (await Product.OrderBy(p => p.Name)).Select(p => new Ui.Choice<ProductId>(p.Id, p.Name)).ToList());

        return
        [
            Div.Class("flex flex-wrap items-center gap-3")[
                Ui.Heading.Level(1).Xl["Orders"],
                Ui.Spacer,
                Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New order"]
            ],
            Ui.Table.Class("w-full")[
                Ui.TableColumns[
                    Ui.TableColumn["Customer"],
                    Ui.TableColumn.Class("w-40")["City"],
                    Ui.TableColumn.Class("w-28")["Status"],
                    Ui.TableColumn.Class("w-52")["Placed"],
                    Ui.TableColumn.Class("w-16")[""]
                ],
                Ui.TableRows[
                    (orders.Data ?? []).Select(o => Ui.TableRow.Key(o.Id)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Class("truncate")[o.Customer],
                        Ui.TableCell[o.ShipToCity],
                        Ui.TableCell[$"{o.Status}"],
                        Ui.TableCell[$"{o.CreatedAt:g}"],
                        Ui.RowActions.Edit(route.With("edit", o.Id.ToString()))
                    ])
                ]
            ],
            Editor(products.Data ?? [])
        ];
    }

    private UiEditorDialog Editor(List<Ui.Choice<ProductId>> products) =>
        Ui.EditorDialog.In("edit").Heading(Edit == UiEditorDialog.New ? "New order" : "Order").Flyout()[
            Edit == UiEditorDialog.New ? NewOrder(products)
            : OrderId.TryParse(Edit, out var id) ? EditOrder(id, products)
            : null
        ].Delete(Order.Delete);

    private static OperationForm<OrderPlaceInput> NewOrder(List<Ui.Choice<ProductId>> products) =>
        Form.For(Order.Place)[f => [
            Ui.Field.Bind(() => f.Customer).Label("Customer's email"),
            Ui.Field.Bind(() => f.ShipTo.Street).Label("Street"),
            Ui.Field.Bind(() => f.ShipTo.City).Label("City"),
            Ui.Field.Bind(() => f.ShipTo.PostCode).Label("Post code"),
            Ui.Field.Bind(() => f.Gift).Label("Gift note"),
            Lines(f.Lines,
                row => Ui.Select.Bind(() => row.Product).Choices(products),
                row => Ui.Field.Bind(() => row.Quantity)),
            Ui.Button.Primary.Submit["Place the order"]
        ]];

    // Two operations, a form each: what the order holds, and shipping it.
    private static Component EditOrder(OrderId id, List<Ui.Choice<ProductId>> products) =>
    [
        Form.For(Order.Change, id)[f => [
            Ui.Field.Bind(() => f.ShipTo.Street).Label("Street"),
            Ui.Field.Bind(() => f.ShipTo.City).Label("City"),
            Ui.Field.Bind(() => f.ShipTo.PostCode).Label("Post code"),
            Ui.Field.Bind(() => f.Gift).Label("Gift note"),
            Lines(f.Lines,
                row => Ui.Select.Bind(() => row.Product).Choices(products),
                row => Ui.Field.Bind(() => row.Quantity)),
            Ui.Button.Primary.Submit["Save"]
        ]],
        Form.For(Order.Ship, id)[f => [
            Ui.Field.Bind(() => f.Tracking).Label("Tracking number"),
            Ui.Button.Submit["Ship it"]
        ]],
    ];

    // The lines of an order, a row each: the same table for the new order's form and the edit's, whose rows are
    // their own types (OrderPlaceInput.LinesRow, OrderChangeInput.LinesRow), so each form says how a row's two
    // controls bind.
    private static Component Lines<TRow>(List<TRow> rows, Func<TRow, Component> product, Func<TRow, Component> quantity)
        where TRow : new() =>
        Div[
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn["Product"], Ui.TableColumn.Class("w-28")["Quantity"], Ui.TableColumn.End[""]],
                Ui.TableRows[rows.Select((row, i) => Ui.TableRow.Key(i)[
                    Ui.TableCell[product(row)],
                    Ui.TableCell[quantity(row)],
                    Ui.TableCell.End[Ui.Button.Ghost.Sm.OnClick(() => rows.Remove(row))["Remove"]]
                ])]
            ],
            Ui.Button.Sm.OnClick(() => rows.Add(new()))["Add a line"]
        ];
}
```

- **A group of fields.** `f.ShipTo.Street`, `f.ShipTo.City` and `f.ShipTo.PostCode` are one value object's fields. It is
  made through `ShippingAddress.Create` as they are filled, so its `Rules()` refuse under `ShipTo.PostCode` as the
  field is left.
- **Rows.** `f.Lines` is an editable list, a row per line the order holds. `rows.Add(new())` adds one and
  `rows.Remove(row)` takes it away; every refusal is shown under its row — `Lines[1].Quantity`. The two forms' rows are
  types of their own (`OrderPlaceInput.LinesRow`, `OrderChangeInput.LinesRow`), so the shared `Lines` table is handed
  how each row's controls bind.
- **A picker.** `Ui.Select.Bind(() => row.Product).Choices(products)` is a searchable list of every product, in the
  reader's alphabet. A `Ui.Choice<ProductId>` pairs an id with the words shown for it.
- **Two operations, two forms.** The edit holds `Form.For(Order.Change, id)` and `Form.For(Order.Ship, id)`. Either
  one saves, says so and closes the dialog.
- **The read face flattens.** On `OrderRead` a value object of several values is its columns — `o.ShipToCity` — and a
  one-value one is its value: `o.Customer` is the address as a `string`.

`[Authorize]` on the page sends an anonymous visitor to `/login`: staff take orders here.

### Migrate

A new table means a new migration:

```bash
rask db add AddOrder
```

Restart `rask dev` and browse to `/orders`.

## 3. Who may change the catalog

Anyone signed in can add a product right now. The rule belongs where the write is — on the operation — so it holds
for this page, a future API and a job alike. Give `Product` a `Configure`; here is
`Features/Products/Product.cs` with it:

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shop.Features.Products;

public readonly partial record struct ProductId(Guid Value);

public sealed partial record ProductName(string Value) : Text(Value, MaxLength: 200);

public sealed partial record UnitPrice(decimal Value) : Number(Value, Min: 0, Max: 1_000_000, Scale: 2);

public sealed partial record StockLevel(long Value) : Integer(Value, Min: 0);

public sealed partial class Product : Aggregate<ProductId, Timestamps, Versioned, Deletable>
{
    public ProductName Name { get; private set; } = null!;

    public UnitPrice Price { get; private set; } = null!;

    public StockLevel Stock { get; private set; } = null!;

    public bool Discontinued { get; private set; }

    public Attachment? Photo { get; private set; }

    private static Product Add(ProductName name, UnitPrice price, StockLevel stock) =>
        new() { Id = ProductId.New(), Name = name, Price = price, Stock = stock };

    public void Change(ProductName name, UnitPrice price, StockLevel stock)
    {
        Refuse.When(Discontinued && price != Price, () => price, "A discontinued product keeps its last price.");
        (Name, Price, Stock) = (name, price, stock);
    }

    public void Discontinue() => Discontinued = true;

    public void Picture(Attachment photo) => Photo = photo;

    public static void Configure(EntityTypeBuilder<Product> b)
    {
        b.Operation(nameof(Add), roles: "admin");
        b.Operation(p => p.Change, roles: "admin");
        b.Operation(p => p.Discontinue, roles: "admin");
        b.Operation(p => p.Picture, roles: "admin");
        b.Operation(nameof(Delete), roles: "admin");
    }
}
```

- **`b.Operation(…, roles: "admin")`** makes the generated entry ask the signed-in user for the role before anything
  is read; anyone else gets a `ForbiddenException`. A factory is named with `nameof(Add)`, since its method group
  holds the entry too, and so is the generated delete, `nameof(Delete)`. The roles are read where the entry is
  generated, so they are written here, as constants.
- **`Product.MayAdd`, `Product.MayChange`, `Product.MayDelete`** are generated beside them, so a page shows only what
  the reader may do. A form over an operation the reader may not run draws every control disabled, and the editor's
  *Delete* is not drawn at all.

In `ProductsPage`, the *New product* button and each row's *Edit* follow the roles. The button becomes:

```csharp
Product.MayAdd ? Ui.Button.Primary.Href(route.With("edit", UiEditorDialog.New))["New product"] : null
```

and the row's last cell:

```csharp
Product.MayChange ? Ui.RowActions.Edit(route.With("edit", p.Id.ToString())) : Ui.TableCell
```

The first account to register is the administrator, so sign in as that one to see them. For markup that is no
operation's, the `Authorize` component does the same: `Authorize.Role("admin")[…]` renders its content for that
role only ([authentication](../authentication.md)).

## 4. Prove it

The product tests now need someone signed in. `Shop.Tests/Signed.cs` is a small helper for the whole test project:

```csharp
using System.Security.Claims;

namespace Shop.Tests;

// Who the test is signed in as, until the returned scope is disposed: the roles an operation's entry asks for.
internal static class Signed
{
    public static IDisposable As(string role) =>
        Current.UsePrincipal(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], "Test")));
}
```

`Current.UsePrincipal` is who the entries see as signed in, until the scope is disposed. Here are the product tests
again, with the admin signed in wherever a test calls an entry that asks — and one more, that a shopper may not:

```csharp
using Shop.Features.Products;

namespace Shop.Tests.Features.Products;

public sealed class ProductTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_product_is_stored_with_what_it_was_given()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        using var admin = Signed.As("admin");

        var id = await Product.Add(ProductName.Create("Anvil"), UnitPrice.Create(9.90m), StockLevel.Create(3), Ct);

        var stored = await Product.Where(p => p.Id == id).First(Ct);
        Assert.Equal("Anvil", stored.Name);
        Assert.Equal(9.90m, stored.Price);
    }

    [Fact]
    public async Task A_discontinued_product_keeps_its_price()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        using var admin = Signed.As("admin");
        var product = await Test.Make<Product>();
        await Product.Discontinue(product.Id, product.Version, Ct);

        var refusal = await Test.Refuses(
            () => Product.Change(product.Id, product.Version + 1, product.Name, UnitPrice.Create(5m), product.Stock));

        refusal.Only("Price");
    }

    [Fact]
    public void A_price_below_zero_is_refused()
    {
        var refusal = Test.Refuses(() => UnitPrice.Create(-1m, "Price"));

        refusal.Only("Price");
    }

    [Fact]
    public async Task The_edit_form_says_a_discontinued_product_keeps_its_price()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        using var admin = Signed.As("admin");
        var product = await Test.Make<Product>();
        await Product.Discontinue(product.Id, product.Version, Ct);

        await Test.FormShows(
            new ProductChangeInput(product.Id, product.Version + 1) { Name = "Anvil", Stock = 3 }, m => m.Price, 5m,
            "A discontinued product keeps its last price.");
    }

    [Fact]
    public Task The_new_product_form_says_a_name_is_too_long() =>
        Test.FormShows<ProductAddInput>(m => m.Name, new string('x', 201), "Enter at most 200 characters.");

    [Fact]
    public async Task Two_edits_of_one_version_keep_one_and_refuse_the_other()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        using var admin = Signed.As("admin");
        var product = await Test.Make<Product>();

        var refusal = await Test.Concurrently(
            () => Product.Change(product.Id, product.Version, ProductName.Create("Anvil"), product.Price, product.Stock),
            () => Product.Change(product.Id, product.Version, ProductName.Create("Hammer"), product.Price, product.Stock));

        refusal.Whole();
    }

    [Fact]
    public async Task A_shopper_may_not_change_a_product()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        var product = await Test.Make<Product>();
        using var shopper = Signed.As("shopper");

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Product.Change(product.Id, product.Version, product.Name, product.Price, product.Stock, Ct));
    }
}
```

`Test.Make<Product>()` builds through the private factory, not its entry, so it asks no role: test data is never held
up by the rule under test.

And the orders, `Shop.Tests/Features/Orders/OrderTests.cs`:

```csharp
using Shop.Features.Orders;

namespace Shop.Tests.Features.Orders;

public sealed class OrderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_without_lines_is_refused_under_its_lines()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);

        var refusal = await Test.Refuses(
            () => Order.Place(Test.Make<CustomerEmail>(), Test.Make<ShippingAddress>(), null, []));

        refusal.Only("Lines");
    }

    [Fact]
    public async Task A_shipped_order_is_not_deleted()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        var order = await Test.Make<Order>();
        await Order.Ship(order.Id, order.Version, TrackingNumber.Create("1Z999"), Ct);

        var refusal = await Test.Refuses(() => Order.Delete(order.Id, order.Version + 1));

        refusal.Whole();
    }

    [Fact]
    public Task A_line_of_no_items_is_refused_on_the_form() =>
        Test.FormShows<OrderPlaceInput>(m => m.Lines[0].Quantity, 0, "Enter 1 or more.");

    [Fact]
    public Task A_short_post_code_is_refused_on_the_form() =>
        Test.FormShows<OrderPlaceInput>(m => m.ShipTo.PostCode, "1", "A post code is 3 to 10 characters.");
}
```

`Test.Make<Order>()` made an order whose one line names a product — and made and stored that product first, because
the spec asks for a `ProductId`. `Test.Make<CustomerEmail>()` and `Test.Make<ShippingAddress>()` are values their rules
accept, and `m.Lines[0].Quantity` adds the row the form does not have yet
([a stored aggregate its rules accept](../testing.md#a-stored-aggregate-its-rules-accept-await-testmakesupply)).

## Verify

- `/orders` renders, and a new order with two lines persists in the same `app.db` as the products.
- A post code of one character is refused under *Post code* as you leave it; an order with no lines is refused under
  its lines.
- Ship an order, then press *Delete* in its editor: it says *A shipped order stays on the books.* instead of asking.
- Signed in as a second, non-admin account, `/products` shows no *New product* and no *Edit*.
- `dotnet test` passes.

**Learn more:** [domain modelling](../domain.md) · [children](../domain.md#children) ·
[authentication](../authentication.md) · [testing the domain](../testing.md#testing-the-domain)

Next → **[Chapter 4: Background jobs](04-background-jobs.md)**
