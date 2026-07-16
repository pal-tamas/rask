namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds an email body under <c>Emails/</c> (or an explicit output dir): a Rask <c>Component</c> rendered
/// to the HTML body by <c>Email.Body(...)</c>, plus the <c>Rask.Mail</c> package and the registration steps.
/// </summary>
internal static class EmailGenerator
{
    public static ScaffoldResult Generate(ProjectContext project, string baseDirectory, string name, string? outputOverride)
    {
        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, "Emails");
        var file = new ScaffoldFile(Path.Combine(targetDirectory, name + ".cs"), Render(project.NamespaceFor(targetDirectory), name));
        return new ScaffoldResult([file], Notes(name))
        {
            // Rask.Mail pulls in the queue/processor/senders; the email body is a Rask.Core Component (already referenced).
            Packages = ["Rask.Mail"],
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

    /// <summary>The "register it and create the schema" next-steps printed after scaffolding.</summary>
    internal static string Notes(string name) =>
        $$"""
        Next steps:
          1. Register the services in Program.cs (once):
               builder.Services.AddRaskMail<AppDbContext>(o => { o.From = "no-reply@example.com"; });   // your DbContext
               builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));
          2. Map the mail table in your DbContext's OnModelCreating (once):
               modelBuilder.AddRaskMail();
          3. Create the schema:
               rask db add AddMail && rask db update
          4. Send it anywhere IMailQueue is injected:
               await mail.SendAsync(Email.To("jane@example.com").Subject("Welcome").Body(new {{name}}()));
        """;
}
