namespace Rask.Cli;

/// <summary>Whether the local docker CLI can run a scaffolded Dockerfile's <c>RUN --mount</c> steps.</summary>
internal enum BuildxStatus
{
    /// <summary>The buildx plugin answers, so <c>docker build</c> goes through BuildKit.</summary>
    Present,

    /// <summary>
    ///     The <c>docker</c> on the PATH is Podman's shim, which builds with Buildah — cache mounts work
    ///     there without any plugin.
    /// </summary>
    Podman,

    /// <summary>
    ///     A real docker CLI with no buildx: it falls back to the legacy builder, which fails on
    ///     <c>RUN --mount</c>.
    /// </summary>
    Missing,
}
