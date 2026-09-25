using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.WebPush.Tests;

public sealed class PushBatteryTests
{
    [Fact]
    public async Task A_browser_that_subscribes_twice_is_kept_once()
    {
        await using var harness = new PushHarness();

        await harness.Push.Subscribe(PushHarness.Browser("phone"));
        await harness.Push.Subscribe(PushHarness.Browser("phone"));

        Assert.Equal(1, harness.Rows());
    }

    [Fact]
    public async Task A_subscription_belongs_to_the_user_who_was_signed_in()
    {
        await using var harness = new PushHarness();
        var ada = Guid.NewGuid();

        PushSubscriber row;
        using (Current.UseUser(ada))
        {
            row = await harness.Push.Subscribe(PushHarness.Browser("laptop"));
        }

        Assert.Equal(ada, row.UserId);
    }

    [Fact]
    public async Task A_send_reaches_every_subscriber_and_counts_the_deliveries()
    {
        await using var harness = new PushHarness();
        await harness.Push.Subscribe(PushHarness.Browser("phone"));
        await harness.Push.Subscribe(PushHarness.Browser("laptop"));

        var delivered = await harness.Push.Send(WebPushMessage.Text("Order shipped"));

        Assert.Equal(2, delivered);
        Assert.Equal(2, harness.Sender.Reached.Count);
    }

    [Fact]
    public async Task A_send_to_a_user_reaches_only_that_user_s_browsers()
    {
        await using var harness = new PushHarness();
        var ada = Guid.NewGuid();
        using (Current.UseUser(ada))
        {
            await harness.Push.Subscribe(PushHarness.Browser("ada-phone"));
        }

        using (Current.UseUser(Guid.NewGuid()))
        {
            await harness.Push.Subscribe(PushHarness.Browser("someone-else"));
        }

        var delivered = await harness.Push.Send(WebPushMessage.Text("Your order shipped")).To(ada);

        Assert.Equal(1, delivered);
        Assert.Equal(["https://push.example/ada-phone"], harness.Sender.Reached);
    }

    [Fact]
    public async Task A_subscription_the_push_service_says_is_gone_is_dropped()
    {
        await using var harness = new PushHarness();
        await harness.Push.Subscribe(PushHarness.Browser("phone"));
        await harness.Push.Subscribe(PushHarness.Browser("uninstalled"));
        harness.Sender.Gone.Add("https://push.example/uninstalled");

        var delivered = await harness.Push.Send(WebPushMessage.Text("Hello"));

        Assert.Equal(1, delivered);
        Assert.Equal(1, harness.Rows());
    }

    [Fact]
    public async Task The_facade_reaches_the_battery_of_the_work_in_progress()
    {
        await using var harness = new PushHarness();
        await harness.Push.Subscribe(PushHarness.Browser("phone"));

        int delivered;
        using (Ambient.Enter(harness.Services))
        {
            delivered = await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way", "/orders/1042"));
        }

        Assert.Equal(1, delivered);
    }

    [Fact]
    public async Task Sending_without_a_key_pair_names_the_settings_to_set()
    {
        await using var harness = new PushHarness(stubSender: false);
        await harness.Push.Subscribe(PushHarness.Browser("phone"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await harness.Push.Send(WebPushMessage.Text("Hello")));

        Assert.Contains("Rask:WebPush:VapidKeys:PublicKey", error.Message, StringComparison.Ordinal);
        Assert.Contains("Rask:WebPush:Subject", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fake_records_a_send_and_answers_in_sentences()
    {
        var ada = Guid.NewGuid();
        using var push = Push.Fake();

        await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way")).To(ada);

        push.Sent().To(ada).WithTitle("Order shipped").Saying("#1042").Once();
        push.Sent().ToEveryone().None();
    }

    [Fact]
    public async Task A_context_without_the_subscriber_table_fails_the_boot_naming_the_line_to_add()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskWebPush<UnmappedContext>();
        services.AddDbContextFactory<UnmappedContext>(o => o.UseSqlite("Data Source=:memory:"));
        await using var provider = services.BuildServiceProvider();
        var check = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<PushModelCheck<UnmappedContext>>().Single();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));

        Assert.Contains("modelBuilder.AddRaskWebPush();", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_browser_subscribes_through_the_endpoint_and_is_kept()
    {
        var database = Path.Combine(Path.GetTempPath(), $"rask-push-endpoint-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRaskWebPush<PushDbContext>();
        builder.Services.AddDbContextFactory<PushDbContext>(o => o.UseSqlite($"Data Source={database}"));
        await using var app = builder.Build();
        app.MapRaskPush();
        await app.StartAsync();
        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<PushDbContext>>().CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        using var http = app.GetTestClient();
        var subscribed = await http.PostAsJsonAsync("/_rask/push/subscribe", new
        {
            endpoint = "https://push.example/browser",
            p256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM",
            auth = "tBHItJI5svbpez7KI4CCXg",
        });
        var key = await http.GetFromJsonAsync<Dictionary<string, string>>("/_rask/push/key");

        Assert.Equal(HttpStatusCode.NoContent, subscribed.StatusCode);
        Assert.Equal("", key!["publicKey"]);
        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<PushDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal("https://push.example/browser", (await db.Set<PushSubscriber>().SingleAsync()).Endpoint);
        }

        await app.StopAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(database);
    }

    public sealed class UnmappedContext(DbContextOptions<UnmappedContext> options) : DbContext(options);
}
