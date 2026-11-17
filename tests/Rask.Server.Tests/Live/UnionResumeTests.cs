using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Server.Tests.Live;

/// <summary>
/// A C# union a page persisted rides the signed resume record to the process that rebuilds the page, and reads back
/// there as the case it held.
/// </summary>
public sealed class UnionResumeTests : IDisposable
{
    public sealed record Search(string Term);

    public sealed record Pick(int Id);

    public union Selection(Search, Pick);

    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "rask-union-resume-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_keyRing, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    // One key ring under two providers: the process that sealed the record and the one a deploy replaced it with.
    private SessionHandoffProtector NewProtector() =>
        new(new ServiceCollection()
                .AddDataProtection()
                .PersistKeysToFileSystem(Directory.CreateDirectory(_keyRing))
                .SetApplicationName("union-resume-tests")
                .Services
                .BuildServiceProvider()
                .GetRequiredService<IDataProtectionProvider>(),
            TimeSpan.FromHours(1));

    private static ClaimsPrincipal User() => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u1")], "test"));

    [Fact]
    public void A_persisted_union_reads_back_as_its_case_after_the_record_crosses_a_deploy()
    {
        var before = new PersistentState();
        before.Persist<Selection>("search", new Search("boots"));
        before.Persist<Selection>("pick", new Pick(7));
        var token = NewProtector().Protect("/catalog", User(), before.Entries);
        var after = new PersistentState();

        Assert.True(NewProtector().TryUnprotect(token, User(), out var record, out _));
        after.Restore(record!.Entries);

        Assert.True(after.TryGet<Selection>("search", out var search));
        Assert.True(after.TryGet<Selection>("pick", out var pick));
        Assert.Equal(new Search("boots"), search.Value);
        Assert.Equal(new Pick(7), pick.Value);
    }
}
