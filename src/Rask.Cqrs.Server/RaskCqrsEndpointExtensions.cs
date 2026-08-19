using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Rask.Cqrs.Server;

/// <summary>Maps the two endpoints a Rask.Cqrs client dispatches to.</summary>
public static class RaskCqrsEndpointExtensions
{
    /// <summary>
    ///     Maps the <c>GET</c>/<c>POST</c> pair that receives remotely dispatched messages.
    /// </summary>
    /// <param name="endpoints">The app's endpoint route builder.</param>
    /// <returns>
    ///     The mapped endpoints, so an app can attach its own conventions —
    ///     <c>.RequireRateLimiting(…)</c>, CORS, output caching — in one line.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         Two routes, however many messages the app has. The verb carries the meaning
    ///         <see cref="IQuery{TResult}" /> and <see cref="ICommand" /> already declare in C#: a query is
    ///         safe and idempotent so it is a GET, anything that mutates is a POST. The message name is a
    ///         route segment, so logs, metrics and rate-limit partitions all get it without extra work.
    ///     </para>
    ///     <para>
    ///         Authorization is imperative rather than endpoint metadata, because two endpoints cannot
    ///         carry per-message policies. It fails closed: an unknown name never reaches a handler, and a
    ///         message whose author never considered authorization is rejected rather than exposed.
    ///     </para>
    /// </remarks>
    public static IEndpointConventionBuilder MapRaskCqrs(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<RaskCqrsServerOptions>()
                      ?? throw new InvalidOperationException(
                          "MapRaskCqrs() needs AddRaskCqrsServer() to have run during startup.");

        // Mapped bare, without a path base. A sub-path deploy is ASP.NET's job: UsePathBase strips the
        // prefix before routing, so baking it in here would map the endpoints one level too deep. The
        // client is the side that must ADD the prefix, because it is building an outgoing URL. Keeping
        // this package free of Rask.Core is what lets it serve a plain ASP.NET app too.
        var pattern = options.RoutePrefix + "/{name}";

        var group = endpoints.MapGroup(string.Empty);
        group.MapGet(pattern, (RequestDelegate)(context => HandleAsync(context, options, fromQuery: true)));
        group.MapPost(pattern, (RequestDelegate)(context => HandleAsync(context, options, fromQuery: false)));
        return group;
    }

    private static async Task HandleAsync(HttpContext context, RaskCqrsServerOptions options, bool fromQuery)
    {
        // The header is the CSRF control: no form, <img> or <script> can set one, so neither endpoint is
        // reachable by cross-site markup — only by a same-origin fetch. Checked first because it is the
        // cheapest rejection available.
        if (!context.Request.Headers.ContainsKey(RemoteEndpointDefaults.RequestHeader))
        {
            await ProblemAsync(context, StatusCodes.Status400BadRequest, "Not a Rask.Cqrs request",
                $"The {RemoteEndpointDefaults.RequestHeader} header is required.").ConfigureAwait(false);
            return;
        }

        var name = context.Request.RouteValues["name"] as string;
        RemoteContract? contract = null;
        if (!string.IsNullOrEmpty(name))
        {
            RemoteContractRegistry.TryGet(name, out contract);
        }

        // Authentication is checked BEFORE the name is judged, and deliberately. Answering 404 for an
        // unknown name but 401 for a known one would let an anonymous caller enumerate every message the
        // app has, one guess at a time. So an anonymous caller gets the same 401 either way, and only a
        // caller who has already proved who they are can tell a real name from a typo. A message whose
        // handler is [AllowAnonymous] is public by definition and is exempt.
        if (options.RequireAuthenticatedUser
            && contract?.AllowAnonymous != true
            && context.User.Identity?.IsAuthenticated != true)
        {
            await ProblemAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized", null).ConfigureAwait(false);
            return;
        }

        // Unknown, or known but unserviceable here: from outside those are the same thing, and the
        // difference is a map of the server's internals. Both land before anything from the body is
        // deserialized.
        if (contract?.LocalInvoker is null)
        {
            await ProblemAsync(context, StatusCodes.Status404NotFound, "Unknown message", null).ConfigureAwait(false);
            return;
        }

        // Verb integrity: a command is never dispatchable over GET, so a mutating message cannot be
        // triggered by a URL, a prefetch, or a link scanner.
        if (fromQuery && contract.Kind != RemoteMessageKind.Query)
        {
            await ProblemAsync(context, StatusCodes.Status405MethodNotAllowed, "Method not allowed",
                $"'{name}' mutates state, so it must be sent as a POST.").ConfigureAwait(false);
            return;
        }

        if (!await AuthorizedAsync(context, contract, options).ConfigureAwait(false))
        {
            return;
        }

        object message;
        try
        {
            message = fromQuery
                ? DecodeFromQuery(context, contract)
                : await DecodeFromBodyAsync(context, contract, options).ConfigureAwait(false);
        }
        catch (BadRequestException ex)
        {
            await ProblemAsync(context, ex.Status, ex.Title, ex.Detail).ConfigureAwait(false);
            return;
        }
        catch (JsonException ex)
        {
            await ProblemAsync(context, StatusCodes.Status400BadRequest, "Malformed message",
                options.IncludeExceptionDetail ? ex.Message : null).ConfigureAwait(false);
            return;
        }

        object? result;
        try
        {
            result = await contract.LocalInvoker(context.RequestServices, message, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away. Nothing to write to, and nothing went wrong.
            return;
        }
        catch (Exception ex)
        {
            // Opaque by default: an exception message is written for an operator, not for a browser, and
            // routinely names tables, paths and internal identifiers.
            await ProblemAsync(context, StatusCodes.Status500InternalServerError, "Handler failed",
                options.IncludeExceptionDetail ? ex.ToString() : null).ConfigureAwait(false);
            return;
        }

        await WriteResultAsync(context, contract, result).ConfigureAwait(false);
    }

    private static async Task<bool> AuthorizedAsync(
        HttpContext context,
        RemoteContract contract,
        RaskCqrsServerOptions options)
    {
        if (contract.AllowAnonymous)
        {
            return true;
        }

        var user = context.User;
        var authenticated = user.Identity?.IsAuthenticated == true;

        if (contract.Roles is { Length: > 0 } roles)
        {
            var permitted = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!permitted.Any(user.IsInRole))
            {
                await ProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden", null).ConfigureAwait(false);
                return false;
            }
        }

        if (contract.Policy is { Length: > 0 } policy)
        {
            var authorization = context.RequestServices.GetService<IAuthorizationService>()
                                ?? throw new InvalidOperationException(
                                    $"'{contract.Name}' declares the policy '{policy}', but no authorization "
                                    + "services are registered. Call AddAuthorization() during startup — the "
                                    + "alternative would be to ignore the policy, which is not a choice this "
                                    + "endpoint gets to make.");

            var outcome = await authorization.AuthorizeAsync(user, policy).ConfigureAwait(false);
            if (!outcome.Succeeded)
            {
                await ProblemAsync(
                    context,
                    authenticated ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
                    authenticated ? "Forbidden" : "Unauthorized",
                    null).ConfigureAwait(false);
                return false;
            }
        }

        return true;
    }

    private static object DecodeFromQuery(HttpContext context, RemoteContract contract)
    {
        var encoded = context.Request.Query[RemoteEndpointDefaults.MessageQueryParameter].ToString();
        if (string.IsNullOrEmpty(encoded))
        {
            throw new BadRequestException(
                StatusCodes.Status400BadRequest,
                "Missing message",
                $"A query carries its message in the '{RemoteEndpointDefaults.MessageQueryParameter}' parameter.");
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(encoded));
        reader.Read();
        return contract.ReadMessage(ref reader, []);
    }

    private static async Task<object> DecodeFromBodyAsync(
        HttpContext context,
        RemoteContract contract,
        RaskCqrsServerOptions options)
    {
        if (context.Request.HasFormContentType && contract.CarriesFiles)
        {
            return await DecodeMultipartAsync(context, contract, options).ConfigureAwait(false);
        }

        var payload = await ReadCappedAsync(context.Request.Body, options.MaxRequestBytes, context.RequestAborted)
            .ConfigureAwait(false);

        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return contract.ReadMessage(ref reader, []);
    }

    private static async Task<object> DecodeMultipartAsync(
        HttpContext context,
        RemoteContract contract,
        RaskCqrsServerOptions options)
    {
        // The cap has to be applied BEFORE the body is read, not checked after. ReadFormAsync consumes
        // and spools the entire upload, so a limit enforced on the other side of it has already let a
        // sender write as much as they liked to the server's disk — the check would report the attack
        // rather than prevent it. Set here, Kestrel enforces it while reading and aborts mid-stream.
        var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = options.MaxUploadBytes;
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);

        if (form.Files.Count > options.MaxFileCount)
        {
            throw new BadRequestException(
                StatusCodes.Status413PayloadTooLarge,
                "Too many files",
                $"At most {options.MaxFileCount} files may travel with one message.");
        }

        long total = 0;

        // Each part goes to the slot its own name declares, rather than to its position in a sort. The
        // part name is the index the client's JSON wrote where the file's contents would go, and that
        // pairing is the only thing putting a file back on the property it came from. Sorting the names
        // as text mispairs them from ten files up — "10" sorts before "2" — which does not fail, it
        // quietly hands a handler somebody else's file.
        var slots = new RemoteFile?[form.Files.Count];

        foreach (var file in form.Files)
        {
            if (!int.TryParse(file.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index >= slots.Length)
            {
                throw new BadRequestException(
                    StatusCodes.Status400BadRequest,
                    "Malformed upload",
                    "Every file part must be named with the index its message reserved for it.");
            }

            if (slots[index] is not null)
            {
                throw new BadRequestException(
                    StatusCodes.Status400BadRequest,
                    "Malformed upload",
                    $"Two file parts claim index {index}.");
            }

            total += file.Length;
            if (total > options.MaxUploadBytes)
            {
                throw new BadRequestException(
                    StatusCodes.Status413PayloadTooLarge,
                    "Upload too large",
                    $"The upload exceeds the {options.MaxUploadBytes} byte limit.");
            }

            var captured = file;
            slots[index] = RemoteFile.FromStream(
                captured.FileName,
                captured.ContentType,
                captured.Length,
                _ => captured.OpenReadStream());
        }

        // Every reserved index must have arrived. A gap is a truncated or tampered body, and letting it
        // through would hand the handler a null where the message's own shape says a file must be.
        var files = new List<RemoteFile>(slots.Length);
        foreach (var slot in slots)
        {
            files.Add(slot ?? throw new BadRequestException(
                StatusCodes.Status400BadRequest,
                "Malformed upload",
                "The multipart body is missing a file part its message declared."));
        }

        var json = form["message"].ToString();
        if (string.IsNullOrEmpty(json))
        {
            throw new BadRequestException(
                StatusCodes.Status400BadRequest, "Missing message", "The multipart body has no 'message' part.");
        }

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return contract.ReadMessage(ref reader, files);
    }

    // Reads at most `limit` bytes and rejects anything longer, so an oversized request costs a buffer of
    // the limit rather than a buffer of whatever the sender chose.
    private static async Task<byte[]> ReadCappedAsync(Stream body, long limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int read;
            while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    throw new BadRequestException(
                        StatusCodes.Status413PayloadTooLarge,
                        "Request too large",
                        $"The message exceeds the {limit} byte limit.");
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffer.ToArray();
    }

    private static async Task WriteResultAsync(HttpContext context, RemoteContract contract, object? result)
    {
        if (contract.ReturnsFile)
        {
            if (result is not FileDownload download)
            {
                await ProblemAsync(context, StatusCodes.Status500InternalServerError, "Handler failed", null)
                    .ConfigureAwait(false);
                return;
            }

            context.Response.ContentType = download.ContentType;
            context.Response.ContentLength = download.Length;

            // Paired deliberately: an attachment disposition tells the browser to save rather than render,
            // and nosniff stops it second-guessing the content type on a file a user supplied.
            context.Response.Headers[HeaderNames.ContentDisposition] =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = SafeLeaf(download.FileName) }.ToString();
            context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

            await download.WriteToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (contract.WriteResult is null)
        {
            context.Response.StatusCode = contract.Kind == RemoteMessageKind.Notification
                ? StatusCodes.Status202Accepted
                : StatusCodes.Status204NoContent;
            return;
        }

        context.Response.ContentType = "application/json";

        // Per-user by nature, so never storable by a shared cache. A query opts into caching explicitly;
        // it is not something an endpoint should decide on a handler's behalf.
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            contract.WriteResult(writer, result);
        }

        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    // A client-supplied filename reaches a header here, so it is reduced to a leaf: a path separator or a
    // traversal segment in a Content-Disposition is a real attack, not a hypothetical one.
    private static string SafeLeaf(string fileName)
    {
        var leaf = fileName;
        var slash = leaf.LastIndexOfAny(['/', '\\']);
        if (slash >= 0)
        {
            leaf = leaf[(slash + 1)..];
        }

        leaf = leaf.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrEmpty(leaf) || leaf is "." or ".." ? "download" : leaf;
    }

    private static async Task ProblemAsync(HttpContext context, int status, string title, string? detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", $"https://github.com/pal-tamas/rask/blob/main/docs/cqrs.md#remote-errors");
            writer.WriteString("title", title);
            writer.WriteNumber("status", status);
            if (detail is not null)
            {
                writer.WriteString("detail", detail);
            }

            writer.WriteEndObject();
        }

        await context.Response.Body.WriteAsync(buffer.WrittenMemory, context.RequestAborted).ConfigureAwait(false);
    }

    private sealed class BadRequestException(int status, string title, string? detail) : Exception(title)
    {
        public int Status { get; } = status;

        public string Title { get; } = title;

        public string? Detail { get; } = detail;
    }
}
