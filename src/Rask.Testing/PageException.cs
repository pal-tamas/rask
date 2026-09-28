namespace Rask.Testing;

/// <summary>A page did not do what a test expected of it. The message says what it did instead.</summary>
public sealed class PageException(string message) : Exception(message);
