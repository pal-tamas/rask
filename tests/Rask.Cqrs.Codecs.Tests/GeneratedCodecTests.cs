using System.Text;
using System.Text.Json;

namespace Rask.Cqrs.Codecs.Tests;

// End-to-end over the real generator: these contracts are compiled by this project, so the codecs under
// test are the ones a consumer would actually get. A round-trip failing here means a message would be
// silently mangled between a browser and its server.
public sealed class GeneratedCodecTests
{
    [Fact]
    public void Every_message_shape_is_registered_with_the_verb_its_interface_implies()
    {
        Assert.Equal(RemoteMessageKind.Query, Contract<ListTodos>().Kind);
        Assert.Equal(RemoteMessageKind.VoidCommand, Contract<ArchiveTodo>().Kind);
        Assert.Equal(RemoteMessageKind.ResultCommand, Contract<AddTodo>().Kind);
        Assert.Equal(RemoteMessageKind.Notification, Contract<TodoArchived>().Kind);
    }

    [Fact]
    public void The_wire_name_is_the_full_type_name_so_two_features_can_share_a_short_one()
    {
        Assert.Equal("Rask.Cqrs.Codecs.Tests.AddTodo", Contract<AddTodo>().Name);
        Assert.True(RemoteContractRegistry.TryGet("Rask.Cqrs.Codecs.Tests.AddTodo", out var byName));
        Assert.Equal(typeof(AddTodo), byName!.MessageType);
    }

    [Fact]
    public void A_message_of_every_supported_shape_survives_the_round_trip()
    {
        var original = new ListTodos(
            Done: true,
            Skip: 40,
            Owner: "ada",
            Note: null,
            Priority: Priority.High,
            Escalation: Priority.Low,
            Batch: Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            Since: new DateOnly(2026, 8, 18),
            Window: new TimeSpan(1, 2, 3, 4),
            Budget: 1234.56m,
            Link: new Uri("/todos?open=1", UriKind.Relative),
            Thumbnail: [1, 2, 3],
            Tags: [new Tag("urgent", 3), new Tag("home", 1)],
            Labels: ["a", "b"],
            Counts: new Dictionary<string, int> { ["open"] = 2, ["done"] = 5 },
            Filter: new Filter { Text = "kitchen", MinPriority = Priority.High });

        var round = RoundTrip(original);

        // Records compare structurally, but the collection members compare by reference, so they are
        // checked explicitly rather than trusting Equals to have covered them.
        Assert.Equal(original.Done, round.Done);
        Assert.Equal(original.Skip, round.Skip);
        Assert.Equal(original.Owner, round.Owner);
        Assert.Null(round.Note);
        Assert.Equal(original.Priority, round.Priority);
        Assert.Equal(original.Escalation, round.Escalation);
        Assert.Equal(original.Batch, round.Batch);
        Assert.Equal(original.Since, round.Since);
        Assert.Equal(original.Window, round.Window);
        Assert.Equal(original.Budget, round.Budget);
        Assert.Equal(original.Link, round.Link);
        Assert.Equal(original.Thumbnail, round.Thumbnail);
        Assert.Equal(original.Tags, round.Tags);
        Assert.Equal(original.Labels, round.Labels);
        Assert.Equal(original.Counts, round.Counts);
        Assert.Equal(original.Filter.Text, round.Filter.Text);
        Assert.Equal(original.Filter.MinPriority, round.Filter.MinPriority);
    }

    [Fact]
    public void An_enum_travels_as_its_number_so_renaming_a_member_does_not_break_the_wire()
    {
        Assert.Contains("\"priority\":2", Write(new AddTodo("x", Priority.High)), StringComparison.Ordinal);
    }

    [Fact]
    public void Property_names_are_camelCase_on_the_wire()
    {
        var json = Write(new TodoArchived(7, DateTimeOffset.UnixEpoch));

        Assert.Contains("\"id\":7", json, StringComparison.Ordinal);
        Assert.Contains("\"at\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_travel_as_base64_rather_than_as_an_array_of_numbers()
    {
        var json = Write(Minimal() with { Thumbnail = "abc"u8.ToArray() });

        Assert.Contains("\"thumbnail\":\"YWJj\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_null_byte_array_stays_null_instead_of_becoming_empty()
    {
        Assert.Contains("\"thumbnail\":null", Write(Minimal()), StringComparison.Ordinal);
        Assert.Null(RoundTrip(Minimal()).Thumbnail);
    }

    [Fact]
    public void A_file_leaves_the_json_and_is_replaced_by_its_index_in_the_body()
    {
        var file = RemoteFile.FromBytes("a.png", "image/png", [1, 2]);
        var files = new List<RemoteFile>();
        var json = Write(new UploadAttachment(7, file, null), files);

        Assert.Contains("\"file\":0", json, StringComparison.Ordinal);
        Assert.Contains("\"extra\":-1", json, StringComparison.Ordinal);
        Assert.Single(files);
        Assert.Same(file, files[0]);

        var round = (UploadAttachment)Read(Contract<UploadAttachment>(), json, files);
        Assert.Same(file, round.File);
        Assert.Null(round.Extra);
        Assert.Equal(7, round.TodoId);
    }

    [Fact]
    public void A_message_carrying_a_file_is_flagged_so_the_transport_sends_multipart()
    {
        Assert.True(Contract<UploadAttachment>().CarriesFiles);
        Assert.False(Contract<AddTodo>().CarriesFiles);
    }

    [Fact]
    public void A_query_returning_a_file_gets_no_json_result_codec()
    {
        var contract = Contract<ExportTodos>();

        Assert.True(contract.ReturnsFile);
        Assert.Equal(typeof(FileDownload), contract.ResultType);

        // A streamed body is not a JSON document; a result codec here would be a codec nothing can call.
        Assert.Null(contract.WriteResult);
        Assert.Null(contract.ReadResult);
    }

    [Fact]
    public void A_void_command_has_no_result_codec_either()
    {
        var contract = Contract<ArchiveTodo>();

        Assert.Equal(typeof(Unit), contract.ResultType);
        Assert.Null(contract.WriteResult);
    }

    [Fact]
    public void A_result_round_trips_through_its_own_codec()
    {
        var contract = Contract<ListTodos>();
        TodoDto[] original = [new(1, "wash up", Priority.High, [new Tag("home", 1)])];

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            contract.WriteResult!(writer, original);
        }

        var reader = new Utf8JsonReader(buffer.ToArray());
        reader.Read();
        var round = (TodoDto[])contract.ReadResult!(ref reader)!;

        // Compared member by member: TodoDto is a record, but its Tags array compares by reference, so
        // Assert.Equal on the DTOs would fail on values that are in fact identical.
        var one = Assert.Single(round);
        Assert.Equal(original[0].Id, one.Id);
        Assert.Equal(original[0].Title, one.Title);
        Assert.Equal(original[0].Priority, one.Priority);
        Assert.Equal(original[0].Tags, one.Tags);
    }

    [Fact]
    public void A_local_only_message_gets_no_contract_at_all()
    {
        // It carries an IComparer, which has no wire encoding — so if [LocalOnly] were ignored this
        // project would not compile. Asserting the absence keeps the intent visible.
        Assert.False(RemoteContractRegistry.TryGet(typeof(RebuildIndex), out _));
    }

    [Fact]
    public void A_property_the_receiver_does_not_know_is_skipped_rather_than_rejected()
    {
        // The compatibility promise: a newer sender adding a field must not break an older receiver.
        var json = """{"id":7,"at":"1970-01-01T00:00:00+00:00","addedLater":{"nested":[1,2]}}""";
        var round = (TodoArchived)Read(Contract<TodoArchived>(), json, []);

        Assert.Equal(7, round.Id);
    }

    [Fact]
    public void A_missing_property_falls_back_to_the_default_rather_than_throwing()
    {
        var round = (AddTodo)Read(Contract<AddTodo>(), """{"title":"only"}""", []);

        Assert.Equal("only", round.Title);
        Assert.Equal(default, round.Priority);
    }

    private static ListTodos Minimal() => new(
        Done: false,
        Skip: 0,
        Owner: "o",
        Note: null,
        Priority: Priority.Low,
        Escalation: null,
        Batch: Guid.Empty,
        Since: new DateOnly(2026, 1, 1),
        Window: TimeSpan.Zero,
        Budget: 0m,
        Link: null,
        Thumbnail: null,
        Tags: [],
        Labels: [],
        Counts: new Dictionary<string, int>(),
        Filter: new Filter());

    private static RemoteContract Contract<TMessage>()
    {
        Assert.True(RemoteContractRegistry.TryGet(typeof(TMessage), out var contract),
            $"No contract was generated for {typeof(TMessage)}.");
        return contract!;
    }

    private static string Write<TMessage>(TMessage message, List<RemoteFile>? files = null)
        where TMessage : notnull
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Contract<TMessage>().WriteMessage(writer, message, files ?? []);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static object Read(RemoteContract contract, string json, IReadOnlyList<RemoteFile> files)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return contract.ReadMessage(ref reader, files);
    }

    private static TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : notnull
    {
        var files = new List<RemoteFile>();
        var json = Write(message, files);
        return (TMessage)Read(Contract<TMessage>(), json, files);
    }
}
