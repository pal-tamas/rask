using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Example.Crdt.Data;
using Rask.ObjectStore;
using Rask.SQLite.Crdt;
using Rask.SQLite.Crdt.Sync;

namespace Rask.Example.Crdt.Devices;

/// <summary>
///     Three devices of one family, each with its own SQLite database, sharing a bucket and nothing
///     else. Normally these would be three phones; here they are three files in one process, which is
///     the only difference.
/// </summary>
public sealed class FamilyDevices : IAsyncDisposable
{
    private readonly string _root;

    private FamilyDevices(string root, string bucket, IReadOnlyList<FamilyDevice> devices)
    {
        _root = root;
        BucketPath = bucket;
        All = devices;
    }

    private FamilyDevices(string root, string setupHint)
    {
        _root = root;
        BucketPath = string.Empty;
        All = [];
        SetupHint = setupHint;
    }

    /// <summary>The devices, or empty when cr-sqlite is not available.</summary>
    public IReadOnlyList<FamilyDevice> All { get; }

    /// <summary>Where the shared "bucket" lives on disk.</summary>
    public string BucketPath { get; }

    /// <summary>Why the demo cannot run, when it cannot.</summary>
    public string? SetupHint { get; }

    public bool Available => All.Count > 0;

    /// <summary>
    ///     Builds the devices, or reports why it could not.
    /// </summary>
    /// <remarks>
    ///     cr-sqlite ships a separate native binary per platform and is not redistributed here, so the
    ///     sample explains what to download rather than failing at the first query — a missing extension
    ///     otherwise surfaces as "no such function", which says nothing about what to do.
    /// </remarks>
    public static async Task<FamilyDevices> CreateAsync(IConfiguration configuration)
    {
        var root = Path.Combine(Path.GetTempPath(), $"rask-crdt-demo-{Guid.NewGuid():N}");
        var extension = configuration["RASK_CRSQLITE_PATH"];

        if (string.IsNullOrWhiteSpace(extension) || !File.Exists(extension))
        {
            return new FamilyDevices(root,
                "Set RASK_CRSQLITE_PATH to cr-sqlite's loadable extension for this platform "
                + "(crsqlite.dylib / .so / .dll) — download it from the cr-sqlite releases page.");
        }

        Directory.CreateDirectory(root);
        var bucket = Path.Combine(root, "bucket");

        // A folder, so the sample runs with no cloud credentials. Swapping in S3ObjectStore is the only
        // change needed to make these three devices sync over the internet instead.
        var shared = new FolderObjectStore(bucket);

        var devices = new List<FamilyDevice>();
        foreach (var name in (string[])["Phone", "Laptop", "Tablet"])
        {
            devices.Add(await FamilyDevice.CreateAsync(name, Path.Combine(root, $"{name}.db"), extension, shared));
        }

        return new FamilyDevices(root, bucket, devices);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var device in All)
        {
            await device.DisposeAsync();
        }

        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing shutdown over.
        }
    }
}

/// <summary>One device: its own database, its own replica identity, its own view of the bucket.</summary>
public sealed class FamilyDevice : IAsyncDisposable
{
    private FamilyDevice(string name, TodoDbContext context, CrdtSyncEngine engine, SwitchableObjectStore link)
    {
        Name = name;
        Context = context;
        Engine = engine;
        Link = link;
    }

    public string Name { get; }

    public TodoDbContext Context { get; }

    public CrdtSyncEngine Engine { get; }

    /// <summary>This device's connection to the bucket — switch it off to go offline.</summary>
    public SwitchableObjectStore Link { get; }

    public CrdtSyncStatus Status { get; private set; } = new(CrdtSyncPhase.Idle, 0, 0, 0);

    internal static async Task<FamilyDevice> CreateAsync(
        string name, string file, string extension, IObjectStore shared)
    {
        // Pooling off: cr-sqlite keeps per-connection state, and a handle returned to the pool
        // mid-state and reused elsewhere corrupts quietly rather than failing.
        var connectionString = $"Data Source={file};Pooling=False";

        // Order matters, and getting it wrong is silent: loading cr-sqlite seeds its own bookkeeping
        // tables, and EnsureCreated treats a database that already has tables as provisioned. So the
        // schema is created on a context WITHOUT the extension, and only then promoted.
        await using (var plain = new TodoDbContext(
                         new DbContextOptionsBuilder<TodoDbContext>().UseSqlite(connectionString).Options))
        {
            await plain.Database.EnsureCreatedAsync();
        }

        var context = new TodoDbContext(new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlite(connectionString)
            .UseRaskCrdt(o => o.ExtensionPath = extension)
            .Options);

        await context.PromoteToCrrsAsync();

        var link = new SwitchableObjectStore(shared);
        return new FamilyDevice(name, context, new CrdtSyncEngine(link, new CrdtChangeFeed(context)), link);
    }

    /// <summary>Adds a todo. A local write and nothing else — no network, online or not.</summary>
    public async Task AddAsync(string title)
    {
        Context.Todos.Add(new Todo { Id = Guid.NewGuid(), Title = title, Priority = 1 });
        await Context.SaveChangesAsync();
    }

    public async Task ToggleAsync(Guid id)
    {
        var todo = await Context.Todos.FindAsync(id);
        if (todo is not null)
        {
            todo.Done = !todo.Done;
            await Context.SaveChangesAsync();
        }
    }

    public async Task BumpPriorityAsync(Guid id)
    {
        var todo = await Context.Todos.FindAsync(id);
        if (todo is not null)
        {
            todo.Priority = todo.Priority % 3 + 1;
            await Context.SaveChangesAsync();
        }
    }

    public async Task<IReadOnlyList<Todo>> ReadAsync()
    {
        Context.ChangeTracker.Clear();
        return await Context.Todos.OrderBy(t => t.Title).AsNoTracking().ToListAsync();
    }

    public async Task SyncAsync() => Status = await Engine.SyncAsync();

    public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}
