namespace Rask.Server;

/// <summary>A page of this site outside the app (Go.Out) the browser is to load, replacing its entry or after it.</summary>
internal sealed record LeavingFor(string Url, bool Replace);
