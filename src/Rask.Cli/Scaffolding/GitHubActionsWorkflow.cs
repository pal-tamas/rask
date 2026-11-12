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
#pragma warning disable S6418 // the NAME of the repository secret the workflow reads, not a secret
    public const string KeySecret = "RASK_SSH_PRIVATE_KEY";
#pragma warning restore S6418

    /// <summary>The host's public key, so the runner isn't trusting an unverified box.</summary>
    public const string KnownHostsSecret = "RASK_SSH_KNOWN_HOSTS";

    /// <summary>
    /// The workflow. Built off the box: GitHub's runners ship Docker with buildx, so <c>rask deploy</c> builds the image
    /// on the runner for the host's platform and streams it to the host — the host only loads and runs it.
    /// </summary>
    /// <param name="ciWorkflow">
    /// The name of the repository's CI workflow (<see cref="CiWorkflow.Find"/>): the deploy then runs once that has
    /// passed on main. <c>null</c> deploys on every push to main, and the file says how to switch.
    /// </param>
    public static string Render(string? ciWorkflow) => Header + Trigger(ciWorkflow) + "\n" + Body;

    private const string Header = """
        # Written by `rask deploy --github-actions`.
        #
        # Deploys main, exactly as `rask deploy` does from your machine: the image is built on this runner for
        # your host's platform, sent to the host compressed over SSH, health-checked, then swapped in with zero
        # downtime. The host never builds, so a small box running several apps never runs out of memory on one.
        #
        # The host, domain and port come from .rask/deploy.json, which is committed alongside this file.
        # Two repository secrets are required — see `rask deploy --github-actions` for how to set them:
        #
        #   RASK_SSH_PRIVATE_KEY  a deploy-only private key that can log in to the host
        #   RASK_SSH_KNOWN_HOSTS  the host's public key, so the runner won't trust an impostor
        name: Deploy

        on:

        """;

    private static string Trigger(string? ciWorkflow) => ciWorkflow is null
        ? """
            # No CI workflow was found in .github/workflows when this was written, so this deploys on every push
            # to main. Once there is one, deploy only what it passed — replace `push:` and its `branches:` with
            #   workflow_run:
            #     workflows: ['<the name: of your CI workflow>']
            #     types: [completed]
            #     branches: [main]
            # The job's `if:` below already lets only a successful CI run of a push through.
            push:
              branches: [main]
            workflow_dispatch:

          """
        : $$"""
            # Once CI has passed on main, never before: a commit CI failed, or hasn't finished, is not deployed.
            workflow_run:
              workflows: ['{{ciWorkflow.Replace("'", "''", StringComparison.Ordinal)}}']
              types: [completed]
              branches: [main]
            workflow_dispatch:

          """;

    private const string Body = """
        concurrency:
          # One deploy of this app at a time. cancel-in-progress stays false on purpose: interrupting a deploy
          # half-way through a blue-green swap is worse than waiting for it to finish. Other apps deploying to
          # the same host don't wait on this: the host's own locks keep them apart only for the seconds the
          # shared proxy is switched.
          group: rask-deploy-${{ github.ref }}
          cancel-in-progress: false

        jobs:
          deploy:
            # A CI run that passed, for a push (not a pull request from a branch someone named main); a push or a
            # manual run always.
            if: github.event_name != 'workflow_run' || (github.event.workflow_run.conclusion == 'success' && github.event.workflow_run.event == 'push')
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v7
                with:
                  # The commit CI passed, not whatever main has moved on to since.
                  ref: ${{ github.event.workflow_run.head_sha || github.sha }}

              - uses: actions/setup-dotnet@v6
                with:
                  dotnet-version: '10.0.x'

              # Secrets reach the script as environment variables, never as ${{ }} in its text: that is
              # pasted in before the shell parses it, so a quote or a $( in a secret would be run.
              - name: Configure SSH
                env:
                  RASK_SSH_PRIVATE_KEY: ${{ secrets.RASK_SSH_PRIVATE_KEY }}
                  RASK_SSH_KNOWN_HOSTS: ${{ secrets.RASK_SSH_KNOWN_HOSTS }}
                run: |
                  mkdir -p ~/.ssh
                  chmod 700 ~/.ssh
                  printf '%s\n' "$RASK_SSH_PRIVATE_KEY" > ~/.ssh/id_ed25519
                  chmod 600 ~/.ssh/id_ed25519
                  printf '%s\n' "$RASK_SSH_KNOWN_HOSTS" > ~/.ssh/known_hosts
                  chmod 600 ~/.ssh/known_hosts

              - name: Install the Rask CLI
                run: dotnet tool install --global Rask.Cli

              # --no-setup-host is deliberate: a host that isn't ready should fail this job loudly rather
              # than be reconfigured from CI. Prepare the box once from your own machine, where you can
              # see and confirm what's about to change:
              #
              #   rask deploy --setup-host
              #
              # The release is named by the UTC moment the deploy starts (v26.11.11.13.12.02) and reaches the
              # app as Rask__Version, which /version answers (app.MapEndpoints(e => e.MapRaskVersion())).
              #
              # Pass app secrets through here as environment variables, e.g.
              #   env:
              #     SMTP_PASSWORD: ${{ secrets.SMTP_PASSWORD }}
              #   run: rask deploy --no-setup-host --env "Rask:Mail:Smtp:Password=$SMTP_PASSWORD"
              #
              # Every variable the app was last deployed with is recorded by name in .rask/deploy.json, and
              # a deploy that doesn't supply one of them FAILS rather than quietly starting the app without
              # it. So if this job starts failing after you deploy with a new --env from your machine, add
              # the same --env here — that's the check doing its job.
              - name: Deploy
                run: rask deploy --no-setup-host

        """;
}
