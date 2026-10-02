using System.Text.Json.Serialization;
using Rask.Web.Types;

namespace Rask.Site.Features;

[JsonSerializable(typeof(PushSubscriptionJSON))]
internal sealed partial class PushJsonContext : JsonSerializerContext;
