using Rask.Batteries;

namespace Rask.WebPush;

/// <summary>The steps that narrow <c>push.Sent()</c>.</summary>
public static class SentPushCounting
{
    extension(Counting<SentPush> sent)
    {
        /// <summary>Only the pushes addressed to this user.</summary>
        public Counting<SentPush> To(Guid userId) => sent.Where(p => p.To == userId, $"to {userId}");

        /// <summary>Only the pushes sent to everyone.</summary>
        public Counting<SentPush> ToEveryone() => sent.Where(p => p.To is null, "to everyone");

        /// <summary>Only the pushes with this title.</summary>
        public Counting<SentPush> WithTitle(string title) =>
            sent.Where(p => string.Equals(p.Title, title, StringComparison.Ordinal), $"titled \"{title}\"");

        /// <summary>Only the pushes whose body contains this text.</summary>
        public Counting<SentPush> Saying(string text) =>
            sent.Where(p => p.Body?.Contains(text, StringComparison.Ordinal) == true, $"saying \"{text}\"");
    }
}
