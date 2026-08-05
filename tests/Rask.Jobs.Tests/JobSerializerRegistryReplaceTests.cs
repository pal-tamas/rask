using Rask.Cqrs;

namespace Rask.Jobs.Tests;

// The registry is process-global and the generated module initializer already owns a group in it, so
// every test here drives its own group key. That is the point of the group key: a contributor can only
// ever replace its own set.
//
// The stand-in types are plain ICommands rather than IJobs on purpose. The generator registers every IJob
// in the compilation, so an IJob here would sit in the *generated* group as well and could never be
// dropped by replacing a test-owned one. Deserialize only needs an ICommand to hand back.
public sealed class JobSerializerRegistryReplaceTests
{
    public sealed record Original(int N) : ICommand;

    public sealed record Renamed(int N) : ICommand;

    public sealed record Other(int N) : ICommand;

    private static string Name<T>() => typeof(T).FullName!.Replace('+', '.');

    [Fact]
    public void Replacing_a_group_drops_the_name_it_no_longer_registers()
    {
        // #537. RegisterJob upserted, so a rename under `rask dev` left BOTH names registered: the new one
        // worked, and the old one kept resolving to a type the generator no longer produced, until restart.
        var key = new object();
        JobSerializerRegistry.Replace(key, [(Name<Original>(), typeof(Original))]);
        Assert.NotNull(JobSerializerRegistry.Deserialize(Name<Original>(), """{"n":1}"""));

        JobSerializerRegistry.Replace(key, [(Name<Renamed>(), typeof(Renamed))]);

        Assert.Null(JobSerializerRegistry.Deserialize(Name<Original>(), """{"n":1}"""));
        Assert.NotNull(JobSerializerRegistry.Deserialize(Name<Renamed>(), """{"n":1}"""));
    }

    [Fact]
    public void Replacing_one_group_leaves_another_groups_entries_alone()
    {
        // Each assembly's generated initializer passes its own registry class as the key, and a hot reload
        // re-runs RefreshAll() for every loaded assembly — so refreshing one must not empty the others.
        var mine = new object();
        var theirs = new object();
        JobSerializerRegistry.Replace(theirs, [(Name<Other>(), typeof(Other))]);
        JobSerializerRegistry.Replace(mine, [(Name<Original>(), typeof(Original))]);

        JobSerializerRegistry.Replace(mine, [(Name<Renamed>(), typeof(Renamed))]);

        Assert.NotNull(JobSerializerRegistry.Deserialize(Name<Other>(), """{"n":1}"""));
    }

    [Fact]
    public void A_direct_registration_survives_a_group_replace()
    {
        // RegisterJob is public and belongs to no group, so a generated refresh must not drop it.
        var key = new object();
        JobSerializerRegistry.RegisterJob("Manual.Job", typeof(Other));
        JobSerializerRegistry.Replace(key, [(Name<Original>(), typeof(Original))]);

        JobSerializerRegistry.Replace(key, []);

        Assert.NotNull(JobSerializerRegistry.Deserialize("Manual.Job", """{"n":1}"""));
    }

    [Fact]
    public void The_generated_group_still_resolves_a_nested_job()
    {
        // The generator keys on Roslyn's dotted display name; Serialize normalizes Type.FullName's '+'.
        // Moving the generated call to Replace must not disturb that agreement — a mismatch dead-letters.
        var (typeName, payload) = JobSerializerRegistry.Serialize(new Outer.NestedJob(7));

        var rehydrated = JobSerializerRegistry.Deserialize(typeName, payload);

        Assert.Equal(new Outer.NestedJob(7), Assert.IsType<Outer.NestedJob>(rehydrated));
    }

    [Fact]
    public void Deserialize_returns_null_for_a_name_no_contributor_registers()
    {
        Assert.Null(JobSerializerRegistry.Deserialize("Nobody.Registers.This", "{}"));
    }

    [Fact]
    public void Replace_rejects_a_null_group_key_or_set()
    {
        Assert.Throws<ArgumentNullException>(() => JobSerializerRegistry.Replace(null!, []));
        Assert.Throws<ArgumentNullException>(() => JobSerializerRegistry.Replace(new object(), null!));
    }
}
