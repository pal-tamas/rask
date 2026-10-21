namespace Rask.TypeScript.Tasks.Tests;

public class MoveIntoPlaceTests
{
    [Fact]
    public void A_tool_another_build_put_in_place_first_is_kept_and_the_late_copy_is_not_an_error()
    {
        var root = Directory.CreateTempSubdirectory("rask-move-");
        var directory = Path.Combine(root.FullName, "tool");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "winner"), "first");
        var staging = Path.Combine(root.FullName, "tool.partial-late");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "winner"), "second");

        ResolveTypeScriptToolTask.MoveIntoPlace(staging, directory);

        Assert.Equal("first", File.ReadAllText(Path.Combine(directory, "winner")));
        root.Delete(recursive: true);
    }

    [Fact]
    public void A_tool_nobody_put_in_place_yet_is_moved_there()
    {
        var root = Directory.CreateTempSubdirectory("rask-move-");
        var directory = Path.Combine(root.FullName, "cache", "tool");
        var staging = Path.Combine(root.FullName, "tool.partial-only");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "tsgo"), "binary");

        ResolveTypeScriptToolTask.MoveIntoPlace(staging, directory);

        Assert.True(File.Exists(Path.Combine(directory, "tsgo")));
        Assert.False(Directory.Exists(staging));
        root.Delete(recursive: true);
    }
}
