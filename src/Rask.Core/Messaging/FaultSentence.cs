using Rask.Core.Live;

namespace Rask.Core.Messaging;

// What a person is told of an exception that was never theirs to read: the caller's own sentence, and in
// Development the exception's type and message after it. Outside Development none of the exception is in
// it, since that text is written for the log and may name a server or a password. Development is the session's
// own host's answer, never another host's in the same process, and unanswered it is not Development.
internal static class FaultSentence
{
    /// <summary><paramref name="sentence" /> as the person reads it, for <paramref name="thrown" />.</summary>
    internal static string Of(string sentence, Exception thrown) =>
        HostDevelopment.Answer == true
            ? $"{sentence} {thrown.GetType().Name}: {thrown.Message}"
            : sentence;
}
