namespace Rask.Cqrs.Codecs.Tests;

// The invoker is the piece that makes "the client is a pure client" possible without reflection. A
// transport only knows a result as a System.Type, so building the Task<TResult> that IDispatcher hands
// back would need MakeGenericType — in the hot path, in the package whose whole point is not doing that.
// The generator emits the call instead, closed over the concrete type. These tests pin that.
public sealed class GeneratedInvokerTests
{
    [Fact]
    public void A_query_invoker_returns_a_task_of_the_declared_result_type()
    {
        var provider = new StubProvider(new RecordingDispatch());
        var task = Contract<ListTodos>().Invoker!(provider, Minimal(), CancellationToken.None);

        // The exact assertion that matters: Dispatcher casts this to Task<TResult>, so a Task<object>
        // here would throw InvalidCastException at the first dispatch of every query in the app.
        Assert.IsType<Task<TodoDto[]>>(task, exactMatch: false);
    }

    [Fact]
    public void A_result_command_invoker_carries_its_own_result_type()
    {
        var provider = new StubProvider(new RecordingDispatch());
        var task = Contract<AddTodo>().Invoker!(provider, new AddTodo("x", Priority.Low), CancellationToken.None);

        Assert.IsType<Task<int>>(task, exactMatch: false);
    }

    [Fact]
    public void The_invoker_hands_the_transport_the_contract_and_the_message()
    {
        var dispatch = new RecordingDispatch();
        var provider = new StubProvider(dispatch);
        var message = new AddTodo("wash up", Priority.High);

        Contract<AddTodo>().Invoker!(provider, message, CancellationToken.None);

        Assert.Same(message, dispatch.Message);
        Assert.Equal("Rask.Cqrs.Codecs.Tests.AddTodo", dispatch.Contract!.Name);
    }

    [Fact]
    public void A_void_command_goes_through_the_untyped_send()
    {
        var dispatch = new RecordingDispatch();
        Contract<ArchiveTodo>().Invoker!(new StubProvider(dispatch), new ArchiveTodo(1), CancellationToken.None);

        Assert.True(dispatch.SentVoid);
    }

    [Fact]
    public void A_notification_gets_no_invoker_because_publishing_needs_no_generic()
    {
        Assert.Null(Contract<TodoArchived>().Invoker);
    }

    [Fact]
    public void Dispatching_with_no_transport_registered_says_what_to_do_about_it()
    {
        // Discarded rather than returned, so this is an Action and xunit does not treat a synchronous
        // throw as an unobserved async one. The invoker resolves the transport before it builds any task,
        // which is exactly why the throw is synchronous.
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = Contract<AddTodo>().Invoker!(
                new StubProvider(null), new AddTodo("x", Priority.Low), CancellationToken.None);
        });

        Assert.Contains("AddRaskCqrsClient", error.Message, StringComparison.Ordinal);
    }

    private static RemoteContract Contract<TMessage>()
    {
        Assert.True(RemoteContractRegistry.TryGet(typeof(TMessage), out var contract));
        return contract!;
    }

    private static ListTodos Minimal() => new(
        false, 0, "o", null, Priority.Low, null, Guid.Empty, new DateOnly(2026, 1, 1), TimeSpan.Zero, 0m,
        null, null, [], [], new Dictionary<string, int>(), new Filter());

    private sealed class StubProvider(IRemoteDispatch? dispatch) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IRemoteDispatch) ? dispatch : null;
    }

    private sealed class RecordingDispatch : IRemoteDispatch
    {
        public RemoteContract? Contract { get; private set; }

        public object? Message { get; private set; }

        public bool SentVoid { get; private set; }

        public Task<TResult> SendAsync<TResult>(RemoteContract contract, object message, CancellationToken cancellationToken)
        {
            Contract = contract;
            Message = message;
            return Task.FromResult<TResult>(default!);
        }

        public Task SendAsync(RemoteContract contract, object message, CancellationToken cancellationToken)
        {
            Contract = contract;
            Message = message;
            SentVoid = true;
            return Task.CompletedTask;
        }

        public Task PublishAsync(RemoteContract contract, object notification, CancellationToken cancellationToken)
        {
            Contract = contract;
            Message = notification;
            return Task.CompletedTask;
        }
    }
}
