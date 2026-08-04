namespace Rask.Jobs.Tests.@event;

// Declared in a namespace whose segment is a C# keyword, and in its own file because a file-scoped
// namespace can't be reopened elsewhere.
//
// This is the shape that used to dead-letter. Roslyn's default display string escapes the keyword
// ("Rask.Jobs.Tests.@event.KeywordJob") but Type.FullName does not ("Rask.Jobs.Tests.event.KeywordJob"),
// so a generator that keyed on the display string registered a name the runtime never produces:
// Deserialize returned null, the processor recorded "No registered job type", and the job burned an
// attempt on every poll until it hit MaxAttempts.
//
// The generator registers this assembly's IJob types at module load, so these tests exercise the real
// generated registry, not a stand-in.
public sealed record KeywordJob(string Value) : IJob;

public sealed class KeywordJobHandler(Recorder recorder) : ICommandHandler<KeywordJob>
{
    public Task HandleAsync(KeywordJob command, CancellationToken cancellationToken)
    {
        recorder.Add(command.Value);
        return Task.CompletedTask;
    }
}
