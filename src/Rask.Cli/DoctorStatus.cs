using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rask.Cli;

/// <summary>How a <c>rask doctor</c> check came out.</summary>
/// <remarks>
///     Serialized as its name, not its ordinal. <c>"status": 2</c> would make a consumer depend on the
///     declaration order of an enum it cannot see, and reorder silently the day someone inserts a member.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DoctorStatus>))]
internal enum DoctorStatus
{
    /// <summary>Fine.</summary>
    Ok,

    /// <summary>Worth knowing, but nothing is broken — a tool only some commands need, say.</summary>
    Warn,

    /// <summary>
    ///     Not checked, because there was nothing here to check — <c>rask doctor</c> run outside a
    ///     project, say.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="Warn" /> on purpose. A warning says something about this machine could
    ///     bite you later; "you are not standing in a project" says nothing about the machine at all, and
    ///     printing it in warning yellow on a freshly installed box made a clean install read as a
    ///     qualified one. It is reported so the row is not silently missing, and styled as the non-event
    ///     it is.
    /// </remarks>
    Skip,

    /// <summary>Would stop a command from starting. Only these decide the exit code.</summary>
    Fail,
}
