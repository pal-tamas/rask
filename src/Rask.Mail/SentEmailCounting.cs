using Rask.Batteries;

namespace Rask.Mailing;

/// <summary>The steps that narrow what a test asks about its mail.</summary>
public static class SentEmailCounting
{
    extension(Counting<SentEmail> sent)
    {
        /// <summary>Only the mail addressed to <paramref name="address" />.</summary>
        public Counting<SentEmail> To(string address) =>
            sent.Where(e => e.To.Contains(address, StringComparer.OrdinalIgnoreCase), $"to \"{address}\"");

        /// <summary>Only the mail whose subject is <paramref name="subject" />.</summary>
        public Counting<SentEmail> WithSubject(string subject) =>
            sent.Where(e => string.Equals(e.Subject, subject, StringComparison.Ordinal), $"subject \"{subject}\"");

        /// <summary>Only the mail whose body contains <paramref name="text" />.</summary>
        public Counting<SentEmail> Saying(string text) =>
            sent.Where(e => e.Html.Contains(text, StringComparison.Ordinal), $"saying \"{text}\"");

        /// <summary>Only the mail held back by <c>.In(<paramref name="delay" />)</c>.</summary>
        public Counting<SentEmail> In(TimeSpan delay) =>
            sent.Where(e => e.Delay == delay, $"in {delay}");

        /// <summary>Only the mail held back until <c>.At(<paramref name="moment" />)</c>.</summary>
        public Counting<SentEmail> At(DateTimeOffset moment) =>
            sent.Where(e => e.Moment == moment, $"at {moment:u}");
    }
}
