// The rask.sh hero's second file, compiled here (see Product.cs). An app's global usings bring these.
using Rask;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Testing.Database.Tests.Hero;

// A form over the operation: its field is Reprice's price,
// checked by UnitPrice and the Refuse as it is typed, then
// saved through Product.Reprice(id, version, price).
public sealed partial class PriceForm : Component
{
    public ProductId Id { get; set; }

    protected override Component? Render() =>
        Form.For(Product.Reprice, Id)[f => [
            Ui.Field.Bind(() => f.Price).Label("Price"),
            Ui.Button.Primary.Submit["Save"],
        ]];
}
