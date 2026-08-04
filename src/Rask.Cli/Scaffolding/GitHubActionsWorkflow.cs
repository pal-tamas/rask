namespace Rask.Cli.Scaffolding;

/// <summary>
/// The <c>.github/workflows/deploy.yml</c> that <c>rask deploy --github-actions</c> writes: push to
/// main, and the same <c>rask deploy</c> runs from a GitHub-hosted runner.
///
/// <para>The YAML carries no host, domain or port — those already live in <c>.rask/deploy.json</c>
/// alongside the code, so the workflow is byte-identical for every project and stays reviewable. Only
/// the two things that must not be committed (the SSH key and the host's fingerprint) come from
/// repository secrets.</para>
/// </summary>
internal static class GitHubActionsWorkflow
{
    /// <summary>Repo-relative path of the generated workflow.</summary>
    public const string RelativePath = ".github/workflows/deploy.yml";

    /// <summary>The private key the runner authenticates to the host with.</summary>
    public const string KeySecret = "RASK_SSH_PRIVATE_KEY";

    /// <summary>The host's public key, so the runner isn't trusting an unverified box.</summary>
    public const string KnownHostsSecret = "RASK_SSH_KNOWN_HOSTS";

    /// <summary>
    /// The workflow. GitHub's own runners ship the Docker CLI, which is all <c>rask deploy</c> needs
    /// locally — the image is still built on the host over SSH.
    /// </summary>
    public const string Content = """
        # Written by `rask deploy --github-actions`.
        #
        # Deploys on every push to main, exactly as `rask deploy` does from your machine: the image is
        # built on your host over SSH, health-checked, then swapped in with zero downtime.
        #
        # The host, domain and port come from .rask/deploy.json, which is committed alongside this file.
        # Two repository secrets are required — see `rask deploy --github-actions` for how to set them:
        #
        #   RASK_SSH_PRIVATE_KEY  the private key that can log in to the host
        #   RASK_SSH_KNOWN_HOSTS  the host's public key, so the runner won't trust an impostor
        name: Deploy

        on:
          push:
            branches: [main]
          workflow_dispatch:

        concurrency:
          # Never let two deploys race. cancel-in-progress stays false on purpose: interrupting a deploy
          # half-way through a blue-green swap is worse than waiting for it to finish.
          group: rask-deploy-${{ github.ref }}
          cancel-in-progress: false

        jobs:
          deploy:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4

              - uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: '10.0.x'

              - name: Configure SSH
                run: |
                  mkdir -p ~/.ssh
                  chmod 700 ~/.ssh
                  printf '%s\n' "${{ secrets.RASK_SSH_PRIVATE_KEY }}" > ~/.ssh/id_ed25519
                  chmod 600 ~/.ssh/id_ed25519
                  printf '%s\n' "${{ secrets.RASK_SSH_KNOWN_HOSTS }}" > ~/.ssh/known_hosts
                  chmod 600 ~/.ssh/known_hosts

              - name: Install the Rask CLI
                run: dotnet tool install --global Rask.Cli

              # --no-setup-host is deliberate: a host that isn't ready should fail this job loudly rather
              # than be reconfigured from CI. Prepare the box once from your own machine, where you can
              # see and confirm what's about to change:
              #
              #   rask deploy --setup-host
              #
              # Pass app secrets through here as environment variables, e.g.
              #   run: rask deploy --no-setup-host --env "ConnectionStrings__Db=${{ secrets.DB_CONNECTION }}"
              #
              # Every variable the app was last deployed with is recorded by name in .rask/deploy.json, and
              # a deploy that doesn't supply one of them FAILS rather than quietly starting the app without
              # it. So if this job starts failing after you deploy with a new --env from your machine, add
              # the same --env here — that's the check doing its job.
              - name: Deploy
                run: rask deploy --no-setup-host

        """;
}
