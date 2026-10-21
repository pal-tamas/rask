# Rask.Storage

Keep the files your users upload — on a directory on the deploy volume by default, or in S3-compatible
storage, Azure Blob Storage or your application's own database by configuration — with a row per file on that
database.

```csharp
builder.Services.AddRaskStorage<AppDbContext>();
// OnModelCreating: modelBuilder.AddRaskStorage();
// after app.MapRask<App>(): app.MapRaskStorage();
```

```csharp
public sealed class SetCover
{
    public async Task Handle(Post post, IRaskFile upload)
    {
        var file = await Files.Save(upload.OpenReadStream, upload.Name, upload.Size).Public();
        post.CoverId = file.Id;
    }
}

Img.Src(Files.Url(post.CoverId));                                    // a public file
await Files.Share(invoice.FileId).For(5.Minutes);                    // a private one, expiring
app.MapGet("/invoices/{id}", (Guid id) => Files.Download(id));       // behind your own check
```

- **Content types are sniffed from the bytes**, never taken from the browser. Only images, audio and video
  are served inline; everything else — SVG and HTML included — downloads as an attachment.
- **Uploads are capped** at 50 MB by default (`MaxFileSize`), and `AllowedTypes` narrows what is accepted.
- **`Rask__Storage__Provider=Database`** keeps the bytes in a `StoredFileBytes` table beside the file rows, written in
  one transaction with the row and streamed in and out on SQL Server and PostgreSQL (SQLite works too): the
  database backup is the file backup.
- **A save inside `Db.Transaction` takes part in it**: the row (and on the Database provider the bytes) commits
  with the work; bytes already on a disk or in a bucket are deleted again if the work rolls back, and a delete
  removes the bytes only after the commit.
- **Orphaned bytes are swept**: a file whose row was never written is removed after a grace period.
- **A file belongs to the tenant that saved it** in a multi-tenant app: finding, opening, linking and deleting
  it by id are scoped to the tenant in flight, so another tenant holding the id cannot reach it.
  `StoredFile.Where(…)` lists uploads with no context of your own.

Included in [`Rask.Server`](https://www.nuget.org/packages/Rask.Server) and on by default. See the
[file storage guide](https://rask.sh/docs/guides/file-storage).
