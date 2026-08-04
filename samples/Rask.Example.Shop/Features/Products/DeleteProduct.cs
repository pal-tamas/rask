using Microsoft.EntityFrameworkCore;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Products;

public sealed record DeleteProductCommand(Guid Id) : ICommand;

public sealed class DeleteProductCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<DeleteProductCommand>
{
    public async Task HandleAsync(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Products.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        db.Products.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// A reusable delete button: dispatches the delete command, then invokes OnDeleted so the caller
// (the list page) can refresh.
public sealed class DeleteProduct(IDispatcher dispatcher) : Component
{
    public Guid Id { get; set; }

    public Func<Task>? OnDeleted { get; set; }

    private async Task DeleteAsync()
    {
        await dispatcher.DispatchAsync(new DeleteProductCommand(Id), CancellationToken);
        if (OnDeleted is not null)
        {
            await OnDeleted();
        }
    }

    protected override Component? Render() =>
        Button("button", OnClickAsync: DeleteAsync)["Delete"];
}
