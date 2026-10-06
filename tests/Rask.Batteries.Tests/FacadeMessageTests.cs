using Microsoft.Extensions.DependencyInjection;
using Rask.Background;
using Rask.Caching;
using Rask.Cqrs;
using Rask.Logging;
using Rask.Mailing;
using Rask.Storage;
using Rask.WebPush;

namespace Rask.Batteries.Tests;

/// <summary>Every battery's static facade explains the same two problems in the same words.</summary>
public sealed class FacadeMessageTests
{
    public static TheoryData<string, Func<Task>> Facades => new()
    {
        { "Cache", () => Cache.Get<string>("k") },
        { "Mail", async () => await Mail.Send(Email.To("ann@example.com").Subject("Hi").Html("<p>hi</p>")) },
        { "Jobs", async () => await Jobs.Enqueue(new SendReceipt("ann@example.com")) },
        { "Files", () => Files.Get(Guid.NewGuid()) },
        { "Push", () => Push.Unsubscribe("https://push.example/1") },
        { "Logs", () => Logs.Count() },
    };

    [Theory]
    [MemberData(nameof(Facades))]
    public async Task Every_facade_names_the_same_fix_when_its_battery_is_not_running(string facade, Func<Task> call)
    {
        await using var empty = new ServiceCollection().BuildServiceProvider();
        using var work = Ambient.Enter(empty);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(call);

        Assert.StartsWith($"{facade} is not running in this app. A RaskApp has it on unless Program.cs says c.", error.Message, StringComparison.Ordinal);
        Assert.Contains("a hand-wired host calls builder.Services.AddRask", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Facades))]
    public async Task Every_facade_says_what_to_inject_when_no_work_is_in_progress(string facade, Func<Task> call)
    {
        using var nothing = Ambient.Enter((IServiceProvider?)null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(call);

        Assert.StartsWith($"{facade} was called outside any work in progress", error.Message, StringComparison.Ordinal);
        Assert.Contains($"Inject I{facade} in the constructor", error.Message, StringComparison.Ordinal);
    }
}
