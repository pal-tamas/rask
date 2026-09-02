using Microsoft.AspNetCore.Mvc;

namespace Rask.Api.Client.Tests;

/// <summary>A post, as the API sends it.</summary>
/// <param name="Id">The post's id.</param>
/// <param name="Title">Its title.</param>
/// <param name="Tags">Its tags.</param>
public sealed record Post(int Id, string Title, IReadOnlyList<string> Tags);

/// <summary>What a caller sends to create a post.</summary>
/// <param name="Title">The title.</param>
/// <param name="Tags">The tags.</param>
public sealed record NewPost(string Title, IReadOnlyList<string> Tags);

/// <summary>The store the controllers read and write, so a round trip has something to observe.</summary>
public sealed class PostStore
{
    private readonly Dictionary<int, Post> _posts = new()
    {
        [1] = new Post(1, "first", ["intro"]),
        [2] = new Post(2, "second", ["intro", "deep"]),
    };

    public int LastPageAsked { get; private set; }

    public Post? Find(int id) => _posts.TryGetValue(id, out var post) ? post : null;

    public IReadOnlyList<Post> All(int page)
    {
        LastPageAsked = page;
        return [.. _posts.Values];
    }

    public Post Add(NewPost draft)
    {
        var post = new Post(_posts.Count + 1, draft.Title, draft.Tags);
        _posts[post.Id] = post;
        return post;
    }

    public bool Remove(int id) => _posts.Remove(id);
}

/// <summary>
///     An ordinary API controller. Nothing here is Rask-specific — that is the whole claim being tested.
/// </summary>
[ApiController]
[Route("api/posts")]
public sealed class PostsController(PostStore store) : ControllerBase
{
    /// <summary>A route parameter and a typed result.</summary>
    [HttpGet("{id:int}")]
    public ActionResult<Post> Get(int id)
    {
        var post = store.Find(id);
        return post is null ? NotFound() : post;
    }

    /// <summary>An optional query parameter, and a Task-wrapped collection result.</summary>
    [HttpGet("")]
    public Task<ActionResult<IReadOnlyList<Post>>> List(int page = 1) =>
        Task.FromResult<ActionResult<IReadOnlyList<Post>>>(store.All(page).ToList());

    /// <summary>A JSON request body.</summary>
    [HttpPost("")]
    public ActionResult<Post> Create(NewPost body) => store.Add(body);

    /// <summary>A void action: the client method returns a bare Task.</summary>
    [HttpDelete("{id:int}")]
    public Task Remove(int id)
    {
        store.Remove(id);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     An injected service alongside a bound parameter. The service must not reach the client's
    ///     signature — it is filled from the container, not by the caller.
    /// </summary>
    [HttpGet("{id:int}/title")]
    public ActionResult<string> Title(int id, [FromServices] PostStore injected) =>
        injected.Find(id) is { } post ? post.Title : NotFound();
}

/// <summary>
///     A second controller, to prove clients are per-controller and their codecs do not collide.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HealthController : ControllerBase
{
    /// <summary>A [controller] token in the route, and a result with no parameters.</summary>
    [HttpGet("")]
    public ActionResult<string> Get() => "ok";

    /// <summary>A string route value, so escaping has something to get wrong.</summary>
    [HttpGet("echo/{value}")]
    public ActionResult<string> Echo(string value) => value;
}
