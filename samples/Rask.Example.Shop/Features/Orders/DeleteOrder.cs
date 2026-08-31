using Microsoft.EntityFrameworkCore;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Orders;

public sealed record DeleteOrderCommand(Guid Id) : ICommand;

public sealed class DeleteOrderCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<DeleteOrderCommand>
{
    public async Task HandleAsync(DeleteOrderCommand command, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Orders.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.RaiseDeleted();
        db.Orders.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// A reusable delete button: dispatches the delete command, then invokes OnDeleted so the caller
// (the list page) can refresh.
public sealed partial class DeleteOrder(IDispatcher dispatcher) : Component
{
    public Guid Id { get; set; }

    public Func<Task>? OnDeleted { get; set; }

    private async Task DeleteAsync()
    {
        await dispatcher.SendAsync(new DeleteOrderCommand(Id), CancellationToken);
        if (OnDeleted is not null)
        {
            await OnDeleted();
        }
    }

    protected override Component? Render() =>
        Button.Type("button").OnClickAsync(DeleteAsync)["Delete"];
}
