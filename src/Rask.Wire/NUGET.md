# Rask.Wire

The primitives Rask's **generated wire codecs** are written against. You do not normally call these
yourself — a source generator emits the code that does — but the package is public because that
generated code lands in *your* compilation and has to have something to call.

Three things live here:

- **`WireJson`** — reflection-free readers and writers over `Utf8JsonReader`/`Utf8JsonWriter`, one per
  primitive shape. Each reader takes the property name it is reading so a mismatch reports *"wire said
  X, expected Y"* naming the property, rather than a stack trace from inside the serializer. They live
  in a package instead of being emitted so the message is written once and the generated file stays
  readable.
- **`RemoteFile`** — a file travelling *to* a server. The carrier only: a message declares the
  app-level file type, and the generated codec adapts it.
- **`FileDownload`** — a file travelling *back*. Named this rather than `FileResult` so it does not
  collide with `Microsoft.AspNetCore.Mvc` in an app that has both.

## Why it is its own package

`Rask.Cqrs` and `Rask.Api` both generate codecs, and a codec should read the same on either wire. The
alternatives were both worse: duplicating these types puts two of each on the path of any app using
both packages, and making one depend on the other drags a mediator into an app that only wanted REST.

No reflection and no assembly scanning, so it publishes clean under the WASM/AOT trimmer.

Documentation: <https://rask.sh>
