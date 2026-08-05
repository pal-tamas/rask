using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Server.Tests.Live;

/// <summary>
/// The seal on the record a browser carries between one live session and the next. Every input here is
/// attacker-controlled, so the interesting assertions are all refusals.
/// </summary>
public sealed class SessionHandoffProtectorTests : IDisposable
{
    private readonly string _keyRing = Path.Combine(Path.GetTempPath(), "rask-handoff-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_keyRing, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Two providers over one key ring stand in for the two processes a deploy actually involves: the
    /// container that sealed the record, and the replacement that has to open it.
    /// </summary>
    private IDataProtectionProvider Provider() =>
        new ServiceCollection()
            .AddDataProtection()
            .PersistKeysToFileSystem(Directory.CreateDirectory(_keyRing))
            .SetApplicationName("handoff-tests")
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();

    private SessionHandoffProtector NewProtector(TimeSpan? lifetime = null) =>
        new(Provider(), lifetime ?? TimeSpan.FromHours(1));

    private static ClaimsPrincipal User(string id) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static Dictionary<string, byte[]> Bag(params (string Key, string Json)[] entries) =>
        entries.ToDictionary(e => e.Key, e => Encoding.UTF8.GetBytes(e.Json), StringComparer.Ordinal);

    [Fact]
    public void Round_trips_the_url_and_the_bag()
    {
        var protector = NewProtector();
        var token = protector.Protect("/orders?page=2", User("u1"), Bag(("tab", "\"reviews\""), ("n", "7")));

        Assert.True(protector.TryUnprotect(token, User("u1"), out var record, out var rejection));

        Assert.Equal(ResumeRejection.None, rejection);
        Assert.Equal("/orders?page=2", record!.Url);
        Assert.Equal(2, record.Entries.Count);
        Assert.Equal("\"reviews\"", Encoding.UTF8.GetString(record.Entries.Single(e => e.Key == "tab").Value));
        Assert.Equal("7", Encoding.UTF8.GetString(record.Entries.Single(e => e.Key == "n").Value));
    }

    /// <summary>
    /// The whole point: the process that opens the record is not the one that sealed it. This is the
    /// deploy, and it only works because the key ring outlives the container — which is why the scaffold
    /// persists it.
    /// </summary>
    [Fact]
    public void A_record_sealed_by_one_process_opens_in_another_sharing_the_key_ring()
    {
        var before = NewProtector();
        var token = before.Protect("/cart", User("u1"), Bag(("items", "3")));

        var after = NewProtector();

        Assert.True(after.TryUnprotect(token, User("u1"), out var record, out _));
        Assert.Equal("/cart", record!.Url);
        Assert.Equal("3", Encoding.UTF8.GetString(record.Entries.Single().Value));
    }

    /// <summary>A record must not be replayable onto another account.</summary>
    [Fact]
    public void A_record_issued_to_one_user_is_refused_for_another()
    {
        var protector = NewProtector();
        var token = protector.Protect("/orders", User("alice"), Bag(("secret", "\"alice-only\"")));

        Assert.False(protector.TryUnprotect(token, User("bob"), out var record, out var rejection));

        Assert.Equal(ResumeRejection.Principal, rejection);
        Assert.Null(record);
    }

    /// <summary>Signing in must not inherit the page an anonymous visitor was on, nor the reverse.</summary>
    [Fact]
    public void An_anonymous_record_and_an_authenticated_one_do_not_cross()
    {
        var protector = NewProtector();

        var anonymousToken = protector.Protect("/", Anonymous(), Bag());
        Assert.False(protector.TryUnprotect(anonymousToken, User("alice"), out _, out var signedIn));
        Assert.Equal(ResumeRejection.Principal, signedIn);

        var authenticatedToken = protector.Protect("/", User("alice"), Bag());
        Assert.False(protector.TryUnprotect(authenticatedToken, Anonymous(), out _, out var signedOut));
        Assert.Equal(ResumeRejection.Principal, signedOut);
    }

    [Fact]
    public void An_anonymous_record_opens_for_an_anonymous_reconnect()
    {
        var protector = NewProtector();
        var token = protector.Protect("/browse", Anonymous(), Bag(("filter", "\"boots\"")));

        Assert.True(protector.TryUnprotect(token, Anonymous(), out var record, out _));
        Assert.Equal("/browse", record!.Url);
    }

    [Fact]
    public void A_tampered_record_is_refused()
    {
        var protector = NewProtector();
        var token = protector.Protect("/orders", User("u1"), Bag(("n", "1")));

        // Flip a byte in the middle of the base64 payload.
        var chars = token.ToCharArray();
        var mid = chars.Length / 2;
        chars[mid] = chars[mid] == 'A' ? 'B' : 'A';

        Assert.False(protector.TryUnprotect(new string(chars), User("u1"), out _, out var rejection));
        Assert.Equal(ResumeRejection.Unprotect, rejection);
    }

    [Fact]
    public void An_expired_record_is_refused()
    {
        // Already dead on arrival — expiry is enforced by the time-limited protector, not by a field we
        // remember to compare, so a record past its lifetime cannot be opened at all.
        var protector = NewProtector(TimeSpan.FromMilliseconds(-1));
        var token = protector.Protect("/orders", User("u1"), Bag());

        Assert.False(protector.TryUnprotect(token, User("u1"), out _, out var rejection));
        Assert.Equal(ResumeRejection.Unprotect, rejection);
    }

    /// <summary>A record sealed under a key ring this host doesn't have is exactly what an unpersisted ring looks like after a deploy.</summary>
    [Fact]
    public void A_record_from_a_foreign_key_ring_is_refused()
    {
        var token = NewProtector().Protect("/orders", User("u1"), Bag());

        var stranger = new SessionHandoffProtector(new EphemeralDataProtectionProvider(), TimeSpan.FromHours(1));

        Assert.False(stranger.TryUnprotect(token, User("u1"), out _, out var rejection));
        Assert.Equal(ResumeRejection.Unprotect, rejection);
    }

    [Fact]
    public void Garbage_is_refused_without_throwing()
    {
        var protector = NewProtector();

        Assert.False(protector.TryUnprotect("not base64 at all !!!", User("u1"), out _, out var malformed));
        Assert.Equal(ResumeRejection.Malformed, malformed);

        // An empty string is valid base64, so it gets as far as the unprotect and fails there.
        Assert.False(protector.TryUnprotect("", User("u1"), out _, out var empty));
        Assert.Equal(ResumeRejection.Unprotect, empty);

        Assert.False(protector.TryUnprotect(Convert.ToBase64String("hello"u8.ToArray()), User("u1"), out _, out var notARecord));
        Assert.Equal(ResumeRejection.Unprotect, notARecord);
    }

    /// <summary>Refuse an oversized token before spending anything on decoding it.</summary>
    [Fact]
    public void An_oversized_token_is_refused_before_it_is_decoded()
    {
        var protector = NewProtector();
        var huge = new string('A', SessionHandoffProtector.MaxTokenChars + 1);

        Assert.False(protector.TryUnprotect(huge, User("u1"), out _, out var rejection));
        Assert.Equal(ResumeRejection.TooLarge, rejection);
    }

    [Fact]
    public void An_empty_bag_round_trips()
    {
        var protector = NewProtector();
        var token = protector.Protect("/", Anonymous(), Bag());

        Assert.True(protector.TryUnprotect(token, Anonymous(), out var record, out _));
        Assert.Empty(record!.Entries);
        Assert.Equal("/", record.Url);
    }

    /// <summary>
    /// Even with nothing declared, the URL alone is worth carrying: it turns a deploy's full-page reload
    /// into a re-render of the page the user was already on.
    /// </summary>
    [Fact]
    public void A_url_with_no_declared_state_still_survives()
    {
        var protector = NewProtector();
        var token = protector.Protect("/reports/2026?tab=summary&sort=desc", User("u1"), Bag());

        Assert.True(protector.TryUnprotect(token, User("u1"), out var record, out _));
        Assert.Equal("/reports/2026?tab=summary&sort=desc", record!.Url);
    }

    [Fact]
    public void Values_with_arbitrary_bytes_survive_the_round_trip()
    {
        var protector = NewProtector();
        var awkward = Bag(("emoji", "\"\\ud83d\\ude80 éè\""), ("empty", ""));

        var token = protector.Protect("/", Anonymous(), awkward);

        Assert.True(protector.TryUnprotect(token, Anonymous(), out var record, out _));
        Assert.Equal(
            awkward["emoji"],
            record!.Entries.Single(e => e.Key == "emoji").Value);
        Assert.Empty(record.Entries.Single(e => e.Key == "empty").Value);
    }
}
