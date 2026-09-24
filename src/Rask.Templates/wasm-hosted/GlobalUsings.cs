// Every file sees these — no `using` needed.
global using Rask;
global using static Rask.Markup;

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
