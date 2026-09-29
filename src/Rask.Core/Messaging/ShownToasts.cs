namespace Rask.Core.Messaging;

/// <summary>What <c>toasts.Shown("Saved")</c> found — asserted with <c>.Once()</c>, <c>.Never()</c> or <c>.As(…)</c>.</summary>
public readonly struct ShownToasts
{
    private readonly string _message;
    private readonly IReadOnlyList<ToastMessage> _all;
    private readonly IReadOnlyList<ToastMessage> _matching;

    internal ShownToasts(string message, IReadOnlyList<ToastMessage> all, IReadOnlyList<ToastMessage> matching)
    {
        _message = message;
        _all = all;
        _matching = matching;
    }

    /// <summary>How many toasts said it.</summary>
    public int Count => _matching.Count;

    /// <summary>Asserts exactly one toast said it.</summary>
    /// <exception cref="InvalidOperationException">None did, or more than one.</exception>
    public void Once()
    {
        if (_matching.Count != 1)
        {
            throw Failure($"Expected one toast saying \"{_message}\", but {Said()}.");
        }
    }

    /// <summary>Asserts no toast said it.</summary>
    /// <exception cref="InvalidOperationException">One did.</exception>
    public void Never()
    {
        if (_matching.Count != 0)
        {
            throw Failure($"Expected no toast saying \"{_message}\", but {_matching.Count} did.");
        }
    }

    /// <summary>Asserts every toast that said it was a <paramref name="level" /> — and that one did.</summary>
    /// <param name="level">Success, Info, Warning or Error.</param>
    /// <exception cref="InvalidOperationException">None said it, or one said it at another level.</exception>
    public void As(ToastLevel level)
    {
        if (_matching.Count == 0)
        {
            throw Failure($"Expected a {level} toast saying \"{_message}\", but {Said()}.");
        }

        if (_matching.FirstOrDefault(t => t.Level != level) is { } other)
        {
            throw Failure($"Expected \"{_message}\" as a {level} toast, but it was shown as {other.Level}.");
        }
    }

    private string Said()
    {
        if (_all.Count == 0)
        {
            return "no toast was shown";
        }

        return _matching.Count == 0
            ? "the toasts said: " + string.Join(", ", _all.Select(t => $"\"{t.Message}\""))
            : $"{_matching.Count} did";
    }

    private static InvalidOperationException Failure(string message) => new(message);
}
