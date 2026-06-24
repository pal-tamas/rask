namespace Rask.Core;

// Named delegate types for the framework's event/callback props, paired sync (`X`) + async (`XAsync`) the
// same way the validator types are (Rask.Core.Forms.Validate / ValidateAsync). They replace the inline
// `Action`/`Func<…, Task>` spellings on the built-in components so the public surface reads as intentional
// callback types rather than raw delegates, and so the factory generator's auto-rerender wrapping and the
// live dispatcher can resolve them by name.
//
// Contravariant in T so a handler over a base type accepts a more-derived argument. Both
// AutoCallback.Wrap and Component.TryInvokeHandlerAsync carry typed overloads/cases for these, so the
// re-render and DOM-dispatch hot paths stay reflection-free. Standard Action/Func handlers remain
// supported for consumer code — these are additive, not a replacement of the delegate model.
//
// Named CallbackAsync (not AsyncCallback) deliberately: System.AsyncCallback is a non-generic BCL delegate,
// so an unqualified `AsyncCallback` would be ambiguous. `CallbackAsync` also mirrors the `XAsync` suffix.

/// <summary>A parameterless synchronous event callback (the shape of <c>OnClick</c>, <c>OnDrop</c>, …).</summary>
public delegate void Callback();

/// <summary>A one-argument synchronous event callback (e.g. <c>OnInput</c>, <c>OnKeyDown</c>).</summary>
public delegate void Callback<in T>(T arg);

/// <summary>A parameterless asynchronous event callback — the async sibling of <see cref="Callback" />.</summary>
public delegate Task CallbackAsync();

/// <summary>A one-argument asynchronous event callback — the async sibling of <see cref="Callback{T}" />.</summary>
public delegate Task CallbackAsync<in T>(T arg);
