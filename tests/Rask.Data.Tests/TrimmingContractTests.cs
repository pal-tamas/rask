using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     Pins what makes Rask.Data safe in a trimmed publish (#1132): the assembly is built <c>IsTrimmable</c>, and the
///     annotations that carry EF Core's own requirements through Rask's generic paths are still in place.
/// </summary>
/// <remarks>
///     <para>
///         The trim analyzer enforces most of this at build — but only on the paths it can see. An annotation dropped
///         from a PUBLIC entry that nothing inside this assembly calls generically (<c>GeneratedReadQuery.Of</c>,
///         the generated writes, the read-face mapping records) is not an error here; it becomes an IL2091 in every
///         trimmed app that reaches it, or — through the records, which the generator fills with <c>typeof</c> — a
///         read face whose properties the trimmer removed, failing at the first query with nothing at build.
///     </para>
///     <para>
///         The browser demo's E2E over a trimmed publish (<c>scripts/run-data-demo-e2e-local.sh</c>) proves the whole
///         mechanism end to end. These are the fast half: they fail in the unit gate, in seconds, and say what went.
///     </para>
/// </remarks>
public sealed class TrimmingContractTests
{
    [Fact]
    public void Rask_Data_is_built_trimmable()
    {
        var metadata = typeof(Db).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(a => a.Key == "IsTrimmable");

        Assert.NotNull(metadata);
        Assert.Equal("True", metadata.Value);
    }

    /// <summary>The Rask sets cover what EF Core itself asks for, so they can be handed straight on to it.</summary>
    [Fact]
    public void The_entity_and_context_sets_cover_what_EF_Core_requires()
    {
        var set = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.GetGenericArguments()[0];
        var factory = typeof(IDbContextFactory<>).GetGenericArguments()[0];

        Assert.Equal(Required(set), Required(set) & DataTrimming.Entity);
        Assert.Equal(Required(factory), Required(factory) & DataTrimming.Context);

        // A zero requirement would make both asserts vacuous — EF's annotation moved, not vanished.
        Assert.NotEqual(DynamicallyAccessedMemberTypes.None, Required(set));
        Assert.NotEqual(DynamicallyAccessedMemberTypes.None, Required(factory));
    }

    [Fact]
    public void The_read_face_query_keeps_its_element_type()
    {
        var of = typeof(GeneratedReadQuery).GetMethod(nameof(GeneratedReadQuery.Of))!;

        AssertEntity(of.GetGenericArguments()[0], "GeneratedReadQuery.Of<TRead>");
        AssertEntity(typeof(ModelQuery<>).GetGenericArguments()[0], "ModelQuery<TEntity>");
    }

    [Fact]
    public void Every_generated_write_keeps_its_entity_type()
    {
        var methods = typeof(GeneratedModelWrites)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.IsGenericMethodDefinition)
            .ToList();

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            AssertEntity(method.GetGenericArguments()[0], $"GeneratedModelWrites.{method.Name}<TEntity>");
        }
    }

    /// <summary>
    ///     The generator hands these records <c>typeof(OrderRead)</c>; the annotation on each is what keeps the read
    ///     face's properties, which EF maps by reflection and the app never reads (a <c>Version</c>, a <c>DeletedAt</c>).
    /// </summary>
    [Fact]
    public void The_read_face_mapping_records_keep_the_types_they_are_given()
    {
        var mapping = typeof(ReadEntityMapping).GetConstructors().Single();
        AssertEntity(mapping.GetParameters().Single(p => p.Name == "readType"), "ReadEntityMapping(readType)");
        AssertEntity(mapping.GetParameters().Single(p => p.Name == "writeType"), "ReadEntityMapping(writeType)");
        AssertEntity(typeof(ReadEntityMapping).GetProperty(nameof(ReadEntityMapping.ReadType))!, "ReadEntityMapping.ReadType");

        var child = typeof(ReadChildMapping).GetConstructors().Single();
        AssertEntity(child.GetParameters().Single(p => p.Name == "childReadType"), "ReadChildMapping(childReadType)");

        var reference = typeof(ReadReferenceMapping).GetConstructors().Single();
        AssertEntity(reference.GetParameters().Single(p => p.Name == "targetReadType"), "ReadReferenceMapping(targetReadType)");
    }

    [Fact]
    public void A_declared_value_object_collection_keeps_its_element_type()
    {
        var declare = typeof(ConventionRegistry).GetMethod(nameof(ConventionRegistry.DeclareCollection))!;

        AssertEntity(declare.GetParameters().Single(p => p.Name == "element"), "ConventionRegistry.DeclareCollection(element)");
    }

    /// <summary>
    ///     An app's context gets exactly the IL2026 a plain <see cref="DbContext" /> subclass gets — and no more, since
    ///     that is the one warning a trimmed EF Core app suppresses.
    /// </summary>
    [Fact]
    public void The_Rask_contexts_repeat_EF_Cores_own_trimming_warning()
    {
        var ef = typeof(DbContext).GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(DbContextOptions)])!
            .GetCustomAttribute<RequiresUnreferencedCodeAttribute>();

        Assert.NotNull(ef);

        foreach (var context in new[] { typeof(RaskDbContext), typeof(RaskReadDbContext) })
        {
            foreach (var constructor in context.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.Equal(ef.Message, constructor.GetCustomAttribute<RequiresUnreferencedCodeAttribute>()?.Message);
            }
        }
    }

    private static DynamicallyAccessedMemberTypes Required(Type genericParameter) =>
        genericParameter.GetCustomAttribute<DynamicallyAccessedMembersAttribute>()?.MemberTypes
        ?? DynamicallyAccessedMemberTypes.None;

    private static void AssertEntity(ICustomAttributeProvider target, string what)
    {
        var annotation = target.GetCustomAttributes(typeof(DynamicallyAccessedMembersAttribute), inherit: false)
            .Cast<DynamicallyAccessedMembersAttribute>()
            .SingleOrDefault();

        Assert.True(
            annotation?.MemberTypes == DataTrimming.Entity,
            $"{what} must carry [DynamicallyAccessedMembers(DataTrimming.Entity)], or a trimmed app loses the members "
            + "EF Core maps by reflection.");
    }
}
