# Rask.Storage

Keep the files your users upload — on a directory on the deploy volume by default, or in S3-compatible
storage or Azure Blob Storage by configuration — with a row per file on your application's own database.

```csharp
builder.Services.AddRaskStorage<AppDbContext>();
// OnModelCreating: modelBuilder.AddRaskStorage();
// after app.UseRask<App>(): app.MapRaskStorage();
```

```csharp
public sealed class SetCover(IFiles files)
{
    public async Task HandleAsync(Post post, RaskFile upload, CancellationToken ct)
    {
        var file = await files.SaveAsync(upload, o => o.Public = true, ct);
        post.CoverId = file.Id;
    }
}

Img.Src(files.Url(post.CoverId));                                    // a public file
await files.TemporaryUrlAsync(invoice.FileId, TimeSpan.FromMinutes(5)); // a private one, expiring
app.MapGet("/invoices/{id}", (Guid id, IFiles files) => files.Download(id)); // behind your own check
```

- **Content types are sniffed from the bytes**, never taken from the browser. Only images, audio and video
  are served inline; everything else — SVG and HTML included — downloads as an attachment.
- **Uploads are capped** at 50 MB by default (`MaxFileSize`), and `AllowedTypes` narrows what is accepted.
- **Orphaned bytes are swept**: a file whose row was never written is removed after a grace period.

Included in the [`Rask`](https://www.nuget.org/packages/Rask) package and on by default. See the
[storage guide](https://rask.sh/docs/guides/storage).
