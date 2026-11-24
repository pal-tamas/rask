using Microsoft.EntityFrameworkCore;

namespace Rask.Testing.Database.Tests;

/// <summary>An invoice: a decimal to order by, and a customer's name to order and find as Hungarian does.</summary>
public sealed class Invoice : Aggregate<Guid>
{
    private Invoice() { } // EF materialization

    public string Customer { get; private set; } = "";

    public decimal Total { get; private set; }

    public static Invoice For(string customer, decimal total) => new() { Id = Guid.NewGuid(), Customer = customer, Total = total };
}

/// <summary>
///     The app's rules for its whole model, as TA's and Eszaf's state them: the database compares text as SQL Server's
///     Hungarian_100_CI_AS does. Only a context <c>UseRaskSqlite</c> configures acts on it.
/// </summary>
public sealed class ShopModel : IRaskModel
{
    public static void Conventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
}
