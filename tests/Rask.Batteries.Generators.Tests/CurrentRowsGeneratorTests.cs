namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// <c>Current.Tenant</c> and <c>Current.User</c>, generated from the aggregate that says <c>ITenant</c> or <c>IUser</c>,
/// typed as its read face, and the build errors that keep the answer from being a guess.
/// </summary>
public class CurrentRowsGeneratorTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("System.Guid")]
    [InlineData("string")]
    public void A_tenant_of_each_key_type_gets_a_typed_Current_Tenant_that_compiles(string key)
    {
        var source = $$"""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Tenant : Aggregate<{{key}}, Deletable>, ITenant
                {
                    public string Name { get; private set; } = "";
                }

                public static class Reads
                {
                    public static {{key}}? Id() => Rask.Current.Tenant?.Id;
                    public static TenantRead Required() => Rask.Current.RequiredTenant;
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        var generated = run.GeneratedSource("__RaskCurrentTenant");
        Assert.Contains("public static global::Shop.TenantRead? Tenant => global::Rask.Data.CurrentRows.Tenant<global::Shop.TenantRead>();", generated, StringComparison.Ordinal);
        Assert.Contains("CurrentRows.UseTenant<global::Shop.Tenant, global::Shop.TenantRead, ", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_aggregate_that_says_so_is_read_as_the_current_user()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Member : Aggregate<System.Guid>, IUser
                {
                    public string Email { get; private set; } = "";
                }

                public static class Reads
                {
                    public static string? Email() => Rask.Current.User?.Email;
                    public static MemberRead Required() => Rask.Current.RequiredUser;
                }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("CurrentRows.UseUser<global::Shop.Member, global::Shop.MemberRead, global::System.Guid>",
            run.GeneratedSource("__RaskCurrentUser"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_user_deriving_from_a_base_that_says_IUser_is_the_current_user_with_no_line_of_its_own()
    {
        var run = Run("""
            using Rask.Data;

            namespace Rask.Auth
            {
                public abstract class Authenticatable : Rask.Data.Aggregate<System.Guid, Rask.Data.Deletable>, Rask.Data.IUser
                {
                    public string Email { get; private set; } = "";
                }
            }

            namespace Shop
            {
                public sealed class User : Rask.Auth.Authenticatable
                {
                    public string DisplayName { get; private set; } = "";
                }

                public static class Reads
                {
                    public static string? Name() => Rask.Current.User?.DisplayName;
                }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("CurrentRows.UseUser<global::Shop.User, global::Shop.UserRead, global::System.Guid>",
            run.GeneratedSource("__RaskCurrentUser"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    public void A_user_keyed_by_a_number_reads_Current_UserId_as_that_number(string key)
    {
        var source = $$"""
            using Rask.Data;

            namespace Shop
            {
                public sealed class User : Aggregate<{{key}}>, IUser
                {
                    public string Email { get; private set; } = "";
                }

                public static class Reads
                {
                    public static {{key}}? Id() => Rask.Current.UserId;
                    public static {{key}} Required() => Rask.Current.RequiredUserId;
                    public static System.IDisposable AsUser() => Rask.Current.UseUser(12);
                    public static System.IDisposable AsNobody() => Rask.Current.UseUser(null);
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        var generated = run.GeneratedSource("__RaskCurrentUserId");
        Assert.Contains($"public static {key}? UserId => global::Rask.Data.CurrentRows.UserId<{key}>();", generated, StringComparison.Ordinal);
        Assert.Contains($"UseUser({key}? userId) => global::Rask.Data.CurrentRows.WorkFor<{key}>(userId);", generated, StringComparison.Ordinal);
        Assert.Contains("public static class RaskCurrentUserId", generated, StringComparison.Ordinal);
    }

    // The binding the generated Current.UserId stands on: emitted into the global namespace, it is found before the
    // Guid? one Rask.Data's import brings, wherever that import sits outside the namespace — and a Guid still finds
    // Rask.Data's UseUser.
    [Theory]
    [InlineData("using Rask.Data;\nnamespace Shop;\n")]
    [InlineData("global using Rask.Data;\nnamespace Shop.Features.Orders;\n")]
    [InlineData("using Rask.Data;\n")]
    public void The_generated_number_beats_the_Guid_Current_UserId_a_using_imports(string head)
    {
        var source = head + """
            public sealed class User : Aggregate<int>, IUser { }

            public static class Reads
            {
                public static int? Id() => Rask.Current.UserId;
                public static int Required() => Rask.Current.RequiredUserId;
                public static System.IDisposable AsGuid() => Rask.Current.UseUser(System.Guid.NewGuid());
            }
            """;

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Theory]
    [InlineData("System.Guid")]
    [InlineData("string")]
    public void A_user_keyed_by_a_Guid_keeps_the_Guid_Current_UserId_and_gets_nothing_new(string key)
    {
        var run = Run($$"""
            using Rask.Data;

            namespace Shop
            {
                public sealed class User : Aggregate<{{key}}>, IUser { }

                public static class Reads
                {
                    public static System.Guid? Id() => Rask.Current.UserId;
                }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.True(run.HasGeneratedSource("__RaskCurrentUser"));
        Assert.False(run.HasGeneratedSource("__RaskCurrentUserId"));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("System.Guid")]
    [InlineData("string")]
    public void A_tenant_of_each_key_type_reads_Current_TenantId_as_its_key(string key)
    {
        var source = $$"""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Tenant : Aggregate<{{key}}>, ITenant { }

                public static class Reads
                {
                    public static {{key}}? Id() => Rask.Current.TenantId;
                    public static {{key}} Required() => Rask.Current.RequiredTenantId;
                }
            }
            """;

        var run = Run(source);

        Assert.Empty(run.Diagnostics);
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public static class RaskCurrentTenantId", run.GeneratedSource("__RaskCurrentTenantId"), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_users_are_RASK113_and_generate_nothing()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class User : Aggregate<System.Guid>, IUser { }
                public sealed class Admin : Aggregate<System.Guid>, IUser { }
            }
            """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK113");
        Assert.False(run.HasGeneratedSource("__RaskCurrentUser"));
    }

    [Fact]
    public void Two_tenants_are_RASK113_and_generate_nothing()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Tenant : Aggregate<int>, ITenant { }
                public sealed class Partner : Aggregate<int>, ITenant { }
            }
            """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK113" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.False(run.HasGeneratedSource("__RaskCurrentTenant"));
    }

    [Fact]
    public void A_key_the_row_cannot_be_read_back_by_is_RASK114()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Tenant : Aggregate<short>, ITenant { }
            }
            """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK114" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("keyed by short", StringComparison.Ordinal));
        Assert.False(run.HasGeneratedSource("__RaskCurrentTenant"));
    }

    [Fact]
    public void A_tenant_keyed_unlike_its_tables_is_RASK114()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Tenant : Aggregate<System.Guid>, ITenant { }
                public sealed class Invoice : Aggregate<int, PerTenant<int>> { }
            }
            """);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK114" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("'Invoice' keeps its tenant as int", StringComparison.Ordinal));
        Assert.False(run.HasGeneratedSource("__RaskCurrentTenant"));
    }

    [Theory]
    [InlineData("int", "PerTenant<int>", "")]
    [InlineData("string", "PerTenant", "")]
    [InlineData("long", "PerTenant", "public long? TenantId { get; private set; }")]
    public void A_tenant_keyed_like_its_tables_is_accepted(string key, string trait, string column)
    {
        var run = Run($$"""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Tenant : Aggregate<{{key}}>, ITenant { }
                public sealed class Invoice : Aggregate<int, {{trait}}> { {{column}} }
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id is "RASK114" or "RASK115");
        Assert.True(run.HasGeneratedSource("__RaskCurrentTenant"));
    }

    [Fact]
    public void Tenant_scoped_tables_with_no_tenant_aggregate_are_RASK115_naming_the_fix()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Invoice : Aggregate<int, PerTenant<int>> { }
            }
            """);

        var warning = Assert.Single(run.Diagnostics, d => d.Id == "RASK115");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("Tenant : Aggregate<int>, ITenant", warning.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_with_neither_says_nothing()
    {
        var run = Run("""
            using Rask.Data;

            namespace Shop
            {
                public sealed class Invoice : Aggregate<int> { }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.False(run.HasGeneratedSource("__RaskCurrentTenant"));
        Assert.False(run.HasGeneratedSource("__RaskCurrentUser"));
    }

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new CurrentRowsGenerator(), new ReadModelGenerator()],
            "Rask.Data", "Rask.Wire", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");
}
