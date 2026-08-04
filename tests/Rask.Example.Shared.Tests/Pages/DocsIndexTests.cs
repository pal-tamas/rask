using System.Text.RegularExpressions;

namespace Rask.Example.Shared.Tests.Pages;

/// <summary>
/// <c>docs/README.md</c> is described as "the full map", and the doctrine page sends readers to it. This
/// guards that claim: every guide must be reachable from it, directly or through a hub page.
///
/// <para>Reachability, not a direct link — the docs are deliberately hub-and-subpage
/// (<c>authentication.md</c> → <c>authentication-cookie.md</c> …), so demanding a top-level row for each
/// would fight the structure. What must never happen is a doc reachable from <em>nothing</em>: written,
/// committed, and findable only by someone who already knows the filename.</para>
/// </summary>
public sealed class DocsIndexTests
{
    [Fact]
    public void Every_doc_is_reachable_from_the_docs_index()
    {
        var docs = DocsDirectory();
        var all = Directory.GetFiles(docs, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(docs, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var reachable = new HashSet<string>(StringComparer.Ordinal) { "README.md" };
        var queue = new Queue<string>([.. reachable]);
        while (queue.Count > 0)
        {
            foreach (var link in LinksIn(Path.Combine(docs, queue.Dequeue()), all))
            {
                if (reachable.Add(link))
                {
                    queue.Enqueue(link);
                }
            }
        }

        var orphaned = all.Except(reachable).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            orphaned.Length == 0,
            "These docs can't be reached from docs/README.md by any path, so a reader has no way to find "
            + $"them — link each from the index or from the hub page it belongs under:{Environment.NewLine}  "
            + string.Join($"{Environment.NewLine}  ", orphaned));
    }

    /// <summary>Markdown links to a sibling doc, resolved relative to the linking file's own folder.</summary>
    private static IEnumerable<string> LinksIn(string path, IReadOnlySet<string> known)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        var folder = Path.GetDirectoryName(Path.GetRelativePath(DocsDirectory(), path).Replace('\\', '/')) ?? string.Empty;
        foreach (Match match in Regex.Matches(File.ReadAllText(path), @"\]\(([^)#\s]+\.md)"))
        {
            var target = match.Groups[1].Value;
            var combined = folder.Length == 0 ? target : $"{folder}/{target}";

            // Normalize "tutorial/../cli.md" and the like without touching the filesystem.
            var resolved = new Uri(new Uri("doc:///"), combined).AbsolutePath.TrimStart('/');
            if (known.Contains(resolved))
            {
                yield return resolved;
            }
        }
    }

    private static string DocsDirectory()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Rask.slnx")))
            {
                return Path.Combine(dir, "docs");
            }
        }

        throw new InvalidOperationException("Could not locate the repo root (Rask.slnx) from the test base directory.");
    }
}
