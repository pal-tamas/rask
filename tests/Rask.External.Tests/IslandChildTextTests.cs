using System.Globalization;
using Rask.Core;
using Rask.Core.Components;

namespace Rask.External.Tests;

/// <summary>
///     An island child renders as exactly the text a Rask child renders as, for every value either one takes.
/// </summary>
/// <remarks>
///     The island child types spell values through <c>ChildText</c>, and <see cref="Component" />'s own conversions keep
///     their spelling on the render hot path. Nothing else ties the two together, so this does — under a culture that
///     spells negatives, decimals and dates differently from the invariant one, where a drift would show.
/// </remarks>
public sealed class IslandChildTextTests
{
    [Fact]
    public void Every_value_an_island_child_takes_renders_as_the_text_a_rask_child_does()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
        try
        {
            var guid = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
            var date = new DateOnly(2026, 9, 11);
            var time = new TimeOnly(13, 5, 7);
            var dateTime = new DateTime(2026, 9, 11, 13, 5, 7, DateTimeKind.Utc);
            var offset = new DateTimeOffset(2026, 9, 11, 13, 5, 7, TimeSpan.FromHours(2));
            var span = TimeSpan.FromMinutes(-90);

            (ReactChild Island, Component Rask)[] cases =
            [
                ("text", "text"),
                (-42, -42),
                (-42L, -42L),
                (-3.5, -3.5),
                (2.5f, 2.5f),
                (-1.25m, -1.25m),
                (true, true),
                ('x', 'x'),
                (guid, guid),
                (date, date),
                (time, time),
                (dateTime, dateTime),
                (offset, offset),
                (span, span),
            ];

            foreach (var (island, rask) in cases)
            {
                Assert.Equal(Assert.IsType<Text>(rask).Value, Assert.IsType<string>(island.Value));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
