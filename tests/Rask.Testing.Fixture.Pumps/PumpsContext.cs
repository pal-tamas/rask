using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Testing.Fixture.Pumps;

/// <summary>The app's own context over this assembly's model.</summary>
public sealed class PumpsContext(DbContextOptions<PumpsContext> options) : RaskDbContext(options);
