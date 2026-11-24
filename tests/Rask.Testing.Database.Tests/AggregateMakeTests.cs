using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>Test.Make builds a stored aggregate through its own factory, with what it refers to made and stored first.</summary>
public sealed class AggregateMakeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_aggregate_is_made_through_its_factory_and_stored()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var depot = await Test.Make<Depot>();

        var stored = await database.Load<Depot>(depot.Id, Ct);
        Assert.Equal("ABCDEFGH", stored!.Name.Value);
    }

    [Fact]
    public async Task An_aggregate_a_parameter_takes_is_made_and_stored_first()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var contact = await Test.Make<Contact>();

        Assert.NotNull(await database.Load<Company>(contact.CompanyId, Ct));
        Assert.NotNull(await database.Load<Contact>(contact.Id, Ct));
    }

    [Fact]
    public async Task A_typed_id_names_the_aggregate_to_make_for_it()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var tank = await Test.Make<Tank>();

        Assert.NotNull(await database.Load<Depot>(tank.Depot, Ct));
        Assert.Equal(0.01m, tank.Capacity.Value);
    }

    [Fact]
    public async Task A_private_factory_gets_one_row_and_every_reference_is_made_once()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var delivery = await Test.Make<Delivery>();

        var tank = await database.Load<Tank>(delivery.FirstTank, Ct);
        Assert.Equal(1, delivery.Lines);
        Assert.Equal(delivery.Depot, tank!.Depot);
        Assert.Equal(1, await Depot.Count(cancellationToken: Ct));
    }

    [Fact]
    public async Task A_nullable_parameter_is_left_empty_and_a_default_kept()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var delivery = await Test.Make<Delivery>();

        Assert.Null(delivery.Note);
        Assert.Equal(2, delivery.Copies);
    }

    [Fact]
    public async Task An_aggregate_the_test_made_is_used_wherever_it_is_referred_to()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var north = await Test.Make<Depot>();

        var delivery = await Test.Make<Delivery>(o => o.Use(north));

        var tank = await database.Load<Tank>(delivery.FirstTank, Ct);
        Assert.Equal(north.Id, delivery.Depot);
        Assert.Equal(north.Id, tank!.Depot);
        Assert.Equal(1, await Depot.Count(cancellationToken: Ct));
    }

    [Fact]
    public async Task A_value_the_test_gives_by_name_goes_to_that_parameter()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        var tank = await Test.Make<Tank>();
        DeliveryLineSpec[] lines = [new(null, tank.Id, Litres.Create(5m)), new(null, tank.Id, Litres.Create(7m))];

        var delivery = await Test.Make<Delivery>(o => o.Use(tank).With("lines", lines).With("note", "At the back"));

        Assert.Equal(12m, delivery.Litres);
        Assert.Equal("At the back", delivery.Note);
    }

    [Fact]
    public async Task What_the_rules_refuse_names_the_aggregate_and_the_way_out()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var make = () => Test.Make<Delivery>(o => o.With("lines", Array.Empty<DeliveryLineSpec>()));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(make);
        Assert.Contains(Delivery.NeedsALine, refused.Message, StringComparison.Ordinal);
        Assert.Contains("o.With(", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Of_several_factories_the_one_named_is_used()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var ticket = await Test.Make<Ticket>(o => o.Factory = nameof(Ticket.Season));

        Assert.Equal("season:ABCDEFGH", ticket.Kind);
    }

    [Fact]
    public async Task Several_factories_and_none_named_says_how_to_name_one()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var make = () => Test.Make<Ticket>();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(make);
        Assert.Contains("Single(…), Season(…)", refused.Message, StringComparison.Ordinal);
        Assert.Contains("o => o.Factory = \"Single\"", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_partial_aggregates_private_factory_is_the_one_made_through_not_its_entry()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);

        var memo = await Test.Make<Memo>();

        Assert.Equal("ABCDEFGH", (await database.Load<Memo>(memo.Id, Ct))!.Title);
        Assert.Equal(0.01m, memo.Litres);
    }
}
