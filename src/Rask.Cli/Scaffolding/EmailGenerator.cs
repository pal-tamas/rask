namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds an email body under <c>Emails/</c> (or an explicit output dir): a Rask <c>Component</c> rendered
/// to the HTML body by <c>Email.Body(...)</c>, plus the <c>Rask.Mail</c> package and the registration steps.
/// </summary>
internal static class EmailGenerator
{
    public static ScaffoldResult Generate(
        ProjectContext project, string baseDirectory, string name, string? outputOverride,
        (string Name, string Namespace, string FilePath)? context)
    {
        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Emails");
        var file = new ScaffoldFile(Path.Combine(targetDirectory, name + ".cs"), Render(project.NamespaceFor(targetDirectory), name));

        // Rask.Mail pulls in the queue/processor/senders; the email body is a Rask.Core Component (already referenced).
        if (context is not { } ctx)
        {
            // No DbContext to wire into (none in the project, or several with no --context) — scaffold the body
            // and print the full manual setup so the user can pick the context themselves.
            return new ScaffoldResult([file], ManualNotes(name)) { Packages = ["Rask.Mail"] };
        }

        // Auto-wire: register the mail queue against the resolved context and map its table in OnModelCreating —
        // the same "no manual paste" treatment `generate feature` gives its DbContext wiring.
        return new ScaffoldResult([file], WiredNotes(name, ctx.Name))
        {
            Packages = ["Rask.Mail"],
            ProgramUsings = ["Rask.Mail", ctx.Namespace],
            ProgramRegistrations = [$"builder.Services.AddRaskMail<{ctx.Name}>(o => o.From = \"no-reply@example.com\");"],
            ContextFilePath = ctx.FilePath,
            ContextModelLines = ["        modelBuilder.AddRaskMail();"],
        };
    }

    /// <summary>Render the email-body component source. Pure, so it is unit-tested directly.</summary>
    internal static string Render(string @namespace, string name) =>
        $$"""
        namespace {{@namespace}};

        /// <summary>An email body — a Rask component rendered to HTML by <c>Email.Body(this)</c>.</summary>
        public sealed class {{name}} : Component
        {
            protected override Component? Render() =>
            [
                Div()["{{name}} works. Edit Render() to build the email body."]
            ];
        }

        // Send it anywhere IMailQueue is injected:
        //   await mail.SendAsync(Email.To("jane@example.com").Subject("Welcome").Body(new {{name}}()));

        """;

    /// <summary>Next-steps when no DbContext could be resolved — the full manual wiring the user must do.</summary>
    internal static string ManualNotes(string name) =>
        $$"""
        Next steps (no DbContext found to wire into automatically):
          1. Register the services in Program.cs (once):
               builder.Services.AddRaskMail<AppDbContext>(o => { o.From = "no-reply@example.com"; });   // your DbContext
               builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseRaskSqlite("Data Source=app.db"));
          2. Map the mail table in your DbContext's OnModelCreating (once):
               modelBuilder.AddRaskMail();
          3. Create the schema:
               rask db add AddMail && rask db update
          4. Send it anywhere IMailQueue is injected:
               await mail.SendAsync(Email.To("jane@example.com").Subject("Welcome").Body(new {{name}}()));
        """;

    /// <summary>Next-steps when the wiring was applied automatically — only the migration + a send example remain.</summary>
    internal static string WiredNotes(string name, string context) =>
        $$"""
        Wired Rask.Mail into {{context}} (AddRaskMail + the mail table). Create the schema:
          rask db add AddMail && rask db update
        Then send it anywhere IMailQueue is injected:
          await mail.SendAsync(Email.To("jane@example.com").Subject("Welcome").Body(new {{name}}()));
        """;
}
