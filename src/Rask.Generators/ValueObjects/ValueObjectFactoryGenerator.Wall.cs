using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

// `FromWall(local, zone)` for a value object declared from Moment — `FuelingMoment(DateTime Value) : Moment(Value)`: a
// wall-clock time of a zone (a pump that reports its own local time, without an offset) made into the instant it names,
// by the one rule a Moment follows (GeneratedValueRules.MomentFromWall), then made through Create, so its rules are asked
// as they are for any other value. One the record declares itself is left alone, and so is one over a nullable member.
public sealed partial class ValueObjectFactoryGenerator
{
    private static void AppendFromWall(StringBuilder s, ValueObject value)
    {
        if (value.OwnFromWall || !value.OneValue || value.Parameters[0].Nullable ||
            !string.Equals(value.ValueBase, "Moment", System.StringComparison.Ordinal) ||
            !string.Equals(value.Parameters[0].Iso, "yyyy-MM-ddTHH:mm:ss", System.StringComparison.Ordinal))
        {
            return;
        }

        var name = value.Name;
        s.AppendLine();
        s.Append("    /// <summary>The moment the clocks of <paramref name=\"zone\" /> showed as <paramref name=\"local\" />, as a <see cref=\"").Append(name)
            .AppendLine("\" />, once its rules accept it: a wall-clock time from a device that keeps its own local time, made an instant.</summary>");
        s.AppendLine("    /// <remarks>A time the clock shows twice when summer time ends is its first occurrence; one it skips when summer time begins is moved forward by the jump.</remarks>");
        s.AppendLine("    /// <param name=\"local\">The wall-clock time; one of <c>DateTimeKind.Utc</c> or <c>DateTimeKind.Local</c> is the instant it names.</param>");
        s.AppendLine("    /// <param name=\"zone\">The zone whose clocks showed it; null for the app's own, <c>Clock.Zone</c>.</param>");
        AppendFieldParameter(s);
        s.AppendLine("    /// <returns>The value, its rules met.</returns>");
        s.AppendLine("    /// <exception cref=\"global::Rask.Cqrs.RaskValidationException\">A rule refuses it.</exception>");
        s.Append("    public static ").Append(name)
            .AppendLine(" FromWall(global::System.DateTime local, global::System.TimeZoneInfo? zone = null, string? field = null) =>");
        s.Append("        Create(").Append(Rules).AppendLine(".MomentFromWall(local, zone), field);");
    }

    // A FromWall of its own, declared in any part of the record.
    private static bool DeclaresFromWall(INamedTypeSymbol type) =>
        type.GetMembers("FromWall").OfType<IMethodSymbol>().Any(static m => !m.IsImplicitlyDeclared);
}
