namespace Rask.Cli;

/// <summary>One idempotent remote step: what we'd tell the user we're doing, and the shell that does it.</summary>
/// <param name="Undo">
/// For a risky step, the shell that puts this box back as it was — and <em>only</em> what this step
/// changed. The rollback guard is built by joining these, so it can never revert state this run didn't
/// create (a firewall the user already ran, or a previous deploy's sshd drop-in).
/// </param>
internal sealed record BootstrapStep(string Description, string Script, string? Undo = null);
