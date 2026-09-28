namespace Rask.Generators.Tests;

/// <summary>
///     A project gets ONE <c>Routes</c> class, in its root namespace: a page whose type name is unique is a
///     flat <c>Routes.HomePage()</c>, and pages that share a type name nest by the folder that tells them
///     apart (<c>Routes.Admin.HomePage()</c>).
/// </summary>
public class OneRoutesClassTests
{
    private const string Home = """
                                using Rask.Core;
                                using Rask.Core.Routing;
                                namespace Company.App.Features.Home;
                                [Route("/")]
                                public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                                """;

    [Fact]
    public void Pages_in_different_namespaces_share_one_Routes_class_in_the_root_namespace()
    {
        var about = """
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Company.App.Features.About;
                    [Route("/about")]
                    public sealed partial class AboutPage : Component { protected override Component? Render() => this; }
                    """;

        var run = GeneratorDriverFixture.RunRoutes("Company.App", ("Home.cs", Home), ("About.cs", about));
        var routes = run.GeneratedSource("Routes.g.cs");

        Assert.Single(run.RunResult.Results.SelectMany(r => r.GeneratedSources), s => s.HintName == "Routes.g.cs");
        Assert.Contains("namespace Company.App\n{", routes.Replace("\r\n", "\n"));
        Assert.Contains("RouteUrl HomePage()", routes);
        Assert.Contains("RouteUrl AboutPage()", routes);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_page_in_Features_Shared_reaches_the_home_page_with_no_using()
    {
        var error = """
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Company.App.Features.Shared;
                    [Route("/error")]
                    public sealed partial class ErrorPage : Component
                    {
                        public static Rask.Core.Routing.RouteUrl Home => Routes.HomePage();
                        protected override Component? Render() => this;
                    }
                    """;

        var run = GeneratorDriverFixture.RunRoutes("Company.App", ("Home.cs", Home), ("Error.cs", error));

        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Pages_sharing_a_type_name_nest_by_the_folder_that_differs()
    {
        var pages = """
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Company.App.Features.Admin
                    {
                        [Route("/admin")]
                        public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                    }
                    namespace Company.App.Features.Shop
                    {
                        [Route("/shop")]
                        public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                    }
                    namespace Company.App.Features.Admin.Users
                    {
                        [Route("/admin/users")]
                        public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                    }
                    namespace Company.App
                    {
                        public static class Links
                        {
                            public static Rask.Core.Routing.RouteUrl[] All =>
                                [Routes.Admin.HomePage(), Routes.Shop.HomePage(), Routes.Admin.Users.HomePage()];
                        }
                    }
                    """;

        var run = GeneratorDriverFixture.RunRoutes("Company.App", ("Pages.cs", pages));
        var routes = run.GeneratedSource("Routes.g.cs");

        Assert.Contains("public static partial class Admin", routes);
        Assert.Contains("public static partial class Shop", routes);
        Assert.Contains("public static partial class Users", routes);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_page_in_the_common_prefix_namespace_stays_flat_beside_a_nested_clash()
    {
        var admin = """
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Company.App.Features.Home.Admin;
                    [Route("/admin")]
                    public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                    """;
        var links = """
                    namespace Company.App;
                    public static class Links
                    {
                        public static Rask.Core.Routing.RouteUrl[] All => [Routes.HomePage(), Routes.Admin.HomePage()];
                    }
                    """;

        var run = GeneratorDriverFixture.RunRoutes("Company.App", ("Home.cs", Home), ("Admin.cs", admin), ("Links.cs", links));

        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void The_per_page_Url_and_Go_forward_to_the_one_Routes_class()
    {
        var admin = """
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Company.App.Features.Admin;
                    [Route("/admin")]
                    public sealed partial class HomePage : Component
                    {
                        public static RouteUrl Self => HomePage.Url();
                        public static void Open() => HomePage.Go();
                        protected override Component? Render() => this;
                    }
                    """;

        var run = GeneratorDriverFixture.RunRoutes("Company.App", ("Home.cs", Home), ("Admin.cs", admin));
        var routes = run.GeneratedSource("Routes.g.cs");

        Assert.Contains("=> global::Company.App.Routes.Admin.HomePage();", routes);
        Assert.Contains("=> global::Company.App.Routes.Home.HomePage();", routes);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_project_without_a_RootNamespace_falls_back_to_the_assembly_name()
    {
        var run = GeneratorDriverFixture.RunRoutes(Home);

        var routes = run.GeneratedSource("Routes.g.cs");

        Assert.Contains("namespace TestAssembly\n{", routes.Replace("\r\n", "\n"));
        Assert.Contains("=> global::TestAssembly.Routes.HomePage();", routes);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_page_named_like_a_clash_folder_reports_RASK097_instead_of_broken_code()
    {
        var pages = """
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Company.App.Features.Admin
                    {
                        [Route("/admin")]
                        public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                    }
                    namespace Company.App.Features.Shop
                    {
                        [Route("/shop")]
                        public sealed partial class HomePage : Component { protected override Component? Render() => this; }
                    }
                    namespace Company.App.Features.Settings
                    {
                        [Route("/settings")]
                        public sealed partial class Admin : Component { protected override Component? Render() => this; }
                    }
                    """;

        var run = GeneratorDriverFixture.RunRoutes("Company.App", ("Pages.cs", pages));

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK097");
        Assert.Contains("Company.App.Routes.Admin()", diagnostic.GetMessage());
        Assert.Empty(run.GeneratedCompileErrors());
    }
}
