namespace Rask.Core.Browser;

/// <summary>A handle to one picked file. Dispose to release the JS-side reference.</summary>
public interface IFileHandle : IAsyncDisposable
{
    /// <summary>The file name (without path).</summary>
    string Name { get; }

    /// <summary>Reads the file's current contents as text (UTF-8).</summary>
    ValueTask<string> ReadTextAsync();

    /// <summary>Reads the file's current contents as bytes.</summary>
    ValueTask<byte[]> ReadBytesAsync();

    /// <summary>Overwrites the file with <paramref name="text" /> (UTF-8). Needs read-write permission.</summary>
    ValueTask WriteTextAsync(string text);

    /// <summary>Overwrites the file with <paramref name="bytes" />. Needs read-write permission.</summary>
    ValueTask WriteBytesAsync(byte[] bytes);
}
