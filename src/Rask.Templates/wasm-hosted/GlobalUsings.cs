// Namespaces every file in this app sees without a `using` of its own. `Rask` is the framework's front door:
// the UI kit (`Ui.Button`, `Ui.Tone`) and everything else a page reaches for by name. `Rask.Html` is
// every element and markup primitive, so `Div[…]` reads the same in a helper class as in a component.
global using Rask;
global using static Rask.Html;

// Your batteries, by name: Cache.Remember(…), Mail.Send(…), Jobs.Enqueue(…), Files.Save(…).
// rask:if cqrs
global using Rask.Cqrs;
global using Rask.Query;
// rask:end
// rask:if jobs
global using Rask.Jobs;
// rask:end
// rask:if mail
global using Rask.Mail;
// rask:end
// rask:if cache
global using Rask.Cache;
// rask:end
// rask:if storage
global using Rask.Storage;
// rask:end
// rask:if logs
global using Rask.Logging;
// rask:end
