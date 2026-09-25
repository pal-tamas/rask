using Rask.Batteries;

namespace Rask.Storage;

/// <summary>The steps that narrow what a test asks about its files.</summary>
public static class SavedFileCounting
{
    extension(Counting<SavedFile> saved)
    {
        /// <summary>Only the files saved under the display name <paramref name="name" />.</summary>
        public Counting<SavedFile> Named(string name) =>
            saved.Where(f => string.Equals(f.Name, name, StringComparison.Ordinal), $"named \"{name}\"");

        /// <summary>Only the files saved with <c>.Public()</c>.</summary>
        public Counting<SavedFile> Public() => saved.Where(f => f.IsPublic, "public");

        /// <summary>Only the files saved without <c>.Public()</c>.</summary>
        public Counting<SavedFile> Private() => saved.Where(f => !f.IsPublic, "private");
    }
}
