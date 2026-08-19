namespace Rask.Cqrs.Codecs.Tests;

public enum Priority
{
    Low = 0,
    High = 2,
}

public sealed record Tag(string Name, int Weight);

// A class rather than a record, built by object initializer: the other construction path the emitter
// has to cover, and the shape a form model usually takes.
public sealed class Filter
{
    public string? Text { get; set; }

    public Priority? MinPriority { get; set; }
}

public sealed record ListTodos(
    bool Done,
    int Skip,
    string Owner,
    string? Note,
    Priority Priority,
    Priority? Escalation,
    Guid Batch,
    DateOnly Since,
    TimeSpan Window,
    decimal Budget,
    Uri? Link,
    byte[]? Thumbnail,
    Tag[] Tags,
    IReadOnlyList<string> Labels,
    Dictionary<string, int> Counts,
    Filter Filter) : IQuery<TodoDto[]>;

public sealed record TodoDto(int Id, string Title, Priority Priority, Tag[] Tags);

public sealed record ArchiveTodo(int Id) : ICommand;

public sealed record AddTodo(string Title, Priority Priority) : ICommand<int>;

public sealed record TodoArchived(int Id, DateTimeOffset At) : INotification;

public sealed record UploadAttachment(int TodoId, RaskFile File, RaskFile? Extra) : ICommand<string>;

public sealed record ExportTodos(bool Done) : IQuery<FileDownload>;

// Never sent anywhere, and carrying a shape that has no wire encoding on purpose: the pair of things
// [LocalOnly] exists for. If the generator ever stops honouring it, this file stops compiling.
[LocalOnly]
public sealed record RebuildIndex(IComparer<string> Order) : ICommand;
