using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth;

internal interface IAuthUserBinding
{
    Type UserType { get; }

    ModelBuilder Map(ModelBuilder modelBuilder);

    IServiceCollection Add<TContext>(IServiceCollection services, Action<AuthOptions>? configure)
        where TContext : DbContext;
}
