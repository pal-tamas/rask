using Microsoft.EntityFrameworkCore;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Features.Products;

public sealed record RestoreProductCommand(Guid Id) : ICommand;

public sealed class RestoreProductCommandHandler(IDbContextFactory<AppDbContext> dbContextFactory)
    : ICommandHandler<RestoreProductCommand>
{
    public async Task HandleAsync(RestoreProductCommand command, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Restore();
        await db.SaveChangesAsync(cancellationToken);
    }
}

// A reusable restore button: dispatches the restore command, then invokes OnRestored so the caller
// (the list page) can refresh.
public sealed class RestoreProduct(IDispatcher dispatcher) : Component
{
    public Guid Id { get; set; }

    public Func<Task>? OnRestored { get; set; }

    private async Task RestoreAsync()
    {
        await dispatcher.DispatchAsync(new RestoreProductCommand(Id), CancellationToken);
        if (OnRestored is not null)
        {
            await OnRestored();
        }
    }

    protected override Component? Render() =>
        Button("button", OnClickAsync: RestoreAsync)["Restore"];
}
