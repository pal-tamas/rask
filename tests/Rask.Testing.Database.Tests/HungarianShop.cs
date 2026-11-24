using Microsoft.EntityFrameworkCore;

namespace Rask.Testing.Database.Tests;

/// <summary>
///     An app context the test project declares itself, as TA's and Eszaf's test projects do. Declaring one must change
///     nothing about the app Rask.Testing boots on demand (NothingBootsUnaskedTests).
/// </summary>
public sealed class HungarianShop(DbContextOptions<HungarianShop> options) : RaskDbContext(options);
