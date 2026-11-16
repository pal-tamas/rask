using System.Text;

namespace Rask.Cli.Commands;

/// <summary>
/// The two Dockerfiles that carry a build's <c>RUN --mount=type=cache</c> contents across runners
/// (<see cref="RunnerBuildCache"/>). A cache mount belongs to the builder and is keyed by its id alone, so a build that
/// mounts the same id sees the same folder: <see cref="Inbound"/> copies the saved folders into a fresh builder's mounts
/// before the app's build, <see cref="Outbound"/> copies the mounts out as files after it.
/// </summary>
/// <remarks>
/// Both copy with <c>cp -a</c> in a <c>busybox</c> container. The step's layer key does not cover what a mount holds, so
/// a cached step would carry nothing; each build is handed a new <see cref="Nonce"/> instead, which the step reads.
/// Never <c>--no-cache</c>: BuildKit then gives the build cache mounts of its own, empty, and the copy finds nothing.
/// </remarks>
internal static class CacheMountTransfer
{
    /// <summary>The image the copies run in: a shell and <c>cp</c>, a few megabytes.</summary>
    internal const string Image = "busybox:1";

    /// <summary>The build argument that keeps a copy step from ever being a layer-cache hit.</summary>
    internal const string Nonce = "RASK_CACHE_TRANSFER";

    /// <summary>The Dockerfile that fills the builder's mounts from its build context, the saved mount folders.</summary>
    internal static string Inbound(IReadOnlyList<string> ids)
    {
        var text = new StringBuilder();
        text.Append("FROM ").Append(Image).Append('\n');
        text.Append("ARG ").Append(Nonce).Append('\n');
        text.Append("RUN --mount=type=bind,target=/from \\\n");
        foreach (var id in ids)
        {
            text.Append("    --mount=type=cache,id=").Append(id).Append(",target=/into/").Append(id).Append(" \\\n");
        }

        text.Append("    echo \"$").Append(Nonce).Append("\" && for id in ").AppendJoin(' ', ids).Append("; do if [ -d \"/from/$id\" ]; then cp -a \"/from/$id/.\" \"/into/$id/\"; fi; done\n");
        return text.ToString();
    }

    /// <summary>The Dockerfile whose output, exported as local files, is one folder per mount with what it now holds.</summary>
    internal static string Outbound(IReadOnlyList<string> ids)
    {
        var text = new StringBuilder();
        text.Append("FROM ").Append(Image).Append(" AS mounts\n");
        text.Append("ARG ").Append(Nonce).Append('\n');
        text.Append("RUN");
        foreach (var id in ids)
        {
            text.Append(" --mount=type=cache,id=").Append(id).Append(",target=/from/").Append(id).Append(" \\\n   ");
        }

        text.Append(" echo \"$").Append(Nonce).Append("\" && mkdir /into && for id in ").AppendJoin(' ', ids).Append("; do cp -a \"/from/$id\" \"/into/$id\"; done\n");
        text.Append("FROM scratch\n");
        text.Append("COPY --from=mounts /into /\n");
        return text.ToString();
    }
}
