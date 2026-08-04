namespace Rask.Example.Shop.Features.Orders;

public sealed class Order : Entity<Guid>
{
    private Order() { } // EF Core materialization

    private Order(OrderCustomer customer, decimal total)
    {
        Id = Guid.NewGuid();
        this.Customer = customer;
        this.Total = total;
    }

    public OrderCustomer Customer { get; private set; }

    public decimal Total { get; private set; }

    public static Order Create(string customer, decimal total)
    {
        var entity = new Order(OrderCustomer.Create(customer), total);
        entity.Raise(new OrderCreated(entity.Id));
        return entity;
    }

    public void Update(string customer, decimal total)
    {
        this.Customer = OrderCustomer.Create(customer);
        this.Total = total;
        Raise(new OrderUpdated(Id));
    }

    public void RaiseDeleted() => Raise(new OrderDeleted(Id));
}
