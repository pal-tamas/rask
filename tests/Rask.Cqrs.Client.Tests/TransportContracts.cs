namespace Rask.Cqrs.Client.Tests;

public sealed record GetThing(int Id) : IQuery<ThingDto>;

public sealed record ThingDto(int Id, string Name);

public sealed record RenameThing(int Id, string Name) : ICommand;

public sealed record CountThings(string Filter) : IQuery<int>;

public sealed record AttachToThing(int Id, RemoteFile File) : ICommand<string>;

public sealed record ExportThings(int Year) : IQuery<FileDownload>;

public sealed record ThingRenamed(int Id) : INotification;
