namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contracts for two hooks the kit leans on: the phone hook lends its engine <c>unasked</c>, so the
///     number a page writes is not taken for the reader's typing, and a menu's type-ahead with no cursor yet looks
///     from the first row (its C# twin is <c>UiListSearch</c>).
/// </summary>
public class KitHookLeftoversContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string Read(string file) => File.ReadAllText(Path.Combine(_repoRoot, "src", "Rask.Core", "Resources", file));

    [Fact]
    public void The_phone_hook_lends_its_engine_the_runtimes_unasked()
    {
        var phone = Read("rask-phone.ts");

        Assert.Contains("import {listen, own, page, unasked} from \"./rask-owned.js\";", phone, StringComparison.Ordinal);
        Assert.Contains("const kit: Kit = {announce, own, unasked};", phone, StringComparison.Ordinal);
        Assert.Contains("unasked: typeof unasked;", phone, StringComparison.Ordinal);
    }

    [Fact]
    public void A_menus_type_ahead_with_no_cursor_looks_from_the_first_row()
    {
        var keys = Read("rask-menu-keys.ts");

        Assert.Contains("const start = needle.length === 1 ? cursor + 1 : Math.max(cursor, 0);", keys, StringComparison.Ordinal);
    }

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }
}
