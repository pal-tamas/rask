using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NpgsqlTypes;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>The generated <c>tsvector</c> column and its GIN index, for every entity that declares a search index.</summary>
internal sealed class PostgresFullTextSearchConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (!FullTextSearchSpec.TryParse(entityType.FindAnnotation(FullTextSearchSpec.AnnotationName)?.Value, out var spec)
                || entityType.GetTableName() is null)
            {
                continue;
            }

            var vector = entityType.Builder.Property(typeof(NpgsqlTsVector), PostgresFullTextSearch.VectorProperty)
                ?? throw new InvalidOperationException(
                    $"'{entityType.DisplayName()}' declares HasFullTextSearch, but a property called "
                    + $"'{PostgresFullTextSearch.VectorProperty}' is already configured another way, so the search "
                    + "vector has nowhere to live.");

            // A real string[]: Npgsql stores the list as given and casts it back to string[] when it reads it, so the
            // read-only wrapper a collection expression would build fails the first migration with InvalidCastException.
            vector.IsGeneratedTsVectorColumn(PostgresFullTextSearch.ConfigurationFor(spec), spec.Properties.ToArray());

            entityType.Builder.HasIndex([vector.Metadata])?.HasMethod("GIN");
        }
    }
}
