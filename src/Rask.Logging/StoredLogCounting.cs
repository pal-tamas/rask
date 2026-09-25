using Microsoft.Extensions.Logging;
using Rask.Batteries;

namespace Rask.Logging;

/// <summary>The steps that narrow what a test asks about its log.</summary>
public static class StoredLogCounting
{
    extension(Counting<LogRecord> stored)
    {
        /// <summary>Only entries at or above <paramref name="level" />.</summary>
        public Counting<LogRecord> AtLeast(LogLevel level) =>
            stored.Where(e => e.Level >= level, $"at {level} or above");

        /// <summary>Only entries whose message or exception contains <paramref name="text" />.</summary>
        public Counting<LogRecord> Saying(string text) =>
            stored.Where(
                e => e.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || (e.Exception?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false),
                $"saying \"{text}\"");

        /// <summary>Only entries logged under <paramref name="category" />.</summary>
        public Counting<LogRecord> From(string category) =>
            stored.Where(e => e.Category.Contains(category, StringComparison.OrdinalIgnoreCase), $"from {category}");
    }
}
