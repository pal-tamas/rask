using System.Globalization;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary><c>rask deploy --github-actions</c>: the workflow that deploys main once CI passes, and the key it deploys with.</summary>
internal sealed partial class DeployCommand
{
    /// <summary>
    /// Write <c>.github/workflows/deploy.yml</c> and print how to make its deploy-only key and set the two secrets.
    /// Everything the workflow varies on lives in <c>.rask/deploy.json</c> and the repository's own CI workflow, so
    /// this needs no network — it works before the host exists.
    /// </summary>
    private int WriteGitHubActionsWorkflow(SshTarget target, string slug, bool dryRun)
    {
        var path = Path.Combine(_workingDirectory, GitHubActionsWorkflow.RelativePath);
        var ci = CiWorkflow.Find(_fileSystem, _workingDirectory);
        var content = GitHubActionsWorkflow.Render(ci);

        if (dryRun)
        {
            Console.Out.WriteLine($"Dry run — would write {GitHubActionsWorkflow.RelativePath}:");
            Console.Out.WriteLine();
            Console.Out.WriteLine(content);
            return 0;
        }

        // A workflow is a thing people edit. Overwriting one silently would throw away their changes.
        if (_fileSystem.FileExists(path))
        {
            Console.WriteErrorLine($"{GitHubActionsWorkflow.RelativePath} already exists — leaving it alone.", ConsoleStyle.Error);
            Console.Error.WriteLine("Delete it first if you want a fresh one, or edit it in place.");
            return 1;
        }

        _fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        _fileSystem.WriteAllText(path, content);
        WriteCreated(GitHubActionsWorkflow.RelativePath);
        Console.WriteLine(
            ci is null
                ? "  It deploys every push to main: no CI workflow was found to wait for (the file says how to add one)."
                : $"  It deploys main once the '{ci}' workflow has passed on it.",
            ConsoleStyle.Dim);

        WriteDeployKeySteps(target, slug);
        return 0;
    }

    /// <summary>
    /// A key of its own for this repository, which can do what a deploy does and nothing more: <c>restrict</c> takes
    /// away a terminal, agent and X11 forwarding and the login's rc file, <c>port-forwarding</c> gives back the one
    /// forward a deploy holds (the host's Docker socket). Revoked by deleting its one line on the host.
    /// </summary>
    private void WriteDeployKeySteps(SshTarget target, string slug)
    {
        var key = $"~/.ssh/rask-deploy-{slug}";
        var ssh = target.Port is { } port
            ? $"ssh -p {port.ToString(CultureInfo.InvariantCulture)} {target.Destination}"
            : $"ssh {target.Destination}";
        var keyscan = target.Port is { } p
            ? $"ssh-keyscan -p {p.ToString(CultureInfo.InvariantCulture)} {target.Host}"
            : $"ssh-keyscan {target.Host}";

        Console.Out.WriteLine();
        WriteHeading("Make a deploy-only key for this repository, then add the two secrets:");
        Console.Out.WriteLine();
        Console.WriteLine($"  ssh-keygen -t ed25519 -N '' -C rask-deploy-{slug} -f {key}", ConsoleStyle.Code);
        Console.WriteLine($"  {ssh} \"echo 'restrict,port-forwarding $(cat {key}.pub)' >> ~/.ssh/authorized_keys\"", ConsoleStyle.Code);
        Console.WriteLine($"  gh secret set {GitHubActionsWorkflow.KeySecret} < {key}", ConsoleStyle.Code);
        Console.WriteLine($"  gh secret set {GitHubActionsWorkflow.KnownHostsSecret} --body \"$({keyscan} 2>/dev/null)\"", ConsoleStyle.Code);
        Console.Out.WriteLine();
        Console.WriteLine($"  (the key logs in as {target.Destination}, the user deploys run as; it gets no terminal, no agent or X11 forwarding — only the Docker socket forward a deploy holds)", ConsoleStyle.Dim);
        Console.WriteLine($"  (revoke it by deleting its line from ~/.ssh/authorized_keys on {target.Host}; delete {key} here once the secret is set)", ConsoleStyle.Dim);
        Console.WriteLine("  (the workflow deploys with --no-setup-host: prepare the box once with `rask deploy --setup-host`)", ConsoleStyle.Dim);
    }
}
