using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// Pins the validation chain order on EditContext:
//   1. Inline per-field delegate(s)
//   2. Inline form-level delegate (full-form passes only)
//   3. IFieldValidator instances (sync, registration order)
//   4. IAsyncFieldValidator instances (async path, registration order)
//
// Plus the first-error-wins rule: once any stage emits a message for a field, later
// stages skip that field. When the upstream rule passes on re-validate, the next stage
// gets to run again automatically.
public class ValidationOrderTests
{
    [Fact]
    public void Validate_OrdersInline_FormLevel_ThenAttributeValidator()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var trace = new List<string>();
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new TracingValidator("attr", trace));
        ctx.RegisterFieldValidator(fid, (Func<string, IEnumerable<string>>)(_ =>
        {
            trace.Add("inline-field");
            return Array.Empty<string>();
        }));
        ctx.RegisterFormValidator((Func<Model, IEnumerable<string>>)(_ =>
        {
            trace.Add("inline-form");
            return Array.Empty<string>();
        }));

        ctx.Validate();

        Assert.Equal(new[] { "inline-field", "inline-form", "attr" }, trace);
    }

    [Fact]
    public void ValidateField_OrdersInline_ThenAttributeValidator()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var trace = new List<string>();
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new TracingValidator("attr", trace));
        ctx.RegisterFieldValidator(fid, (Func<string, IEnumerable<string>>)(_ =>
        {
            trace.Add("inline-field");
            return Array.Empty<string>();
        }));

        ctx.ValidateField(fid);

        // The form-level inline delegate must NOT fire on a per-field pass.
        Assert.Equal(new[] { "inline-field", "attr" }, trace);
    }

    [Fact]
    public async Task ValidateAsync_OrdersInline_FormLevel_Sync_ThenAsync()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var trace = new List<string>();
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new TracingValidator("attr-sync", trace));
        ctx.AddValidator(new TracingAsyncValidator("attr-async", trace));
        ctx.RegisterFieldValidator(fid, (Func<string, IEnumerable<string>>)(_ =>
        {
            trace.Add("inline-field");
            return Array.Empty<string>();
        }));
        ctx.RegisterFormValidator((Func<Model, IEnumerable<string>>)(_ =>
        {
            trace.Add("inline-form");
            return Array.Empty<string>();
        }));

        await ctx.ValidateAsync();

        Assert.Equal(
            new[] { "inline-field", "inline-form", "attr-sync", "attr-async" },
            trace);
    }

    [Fact]
    public async Task ValidateFieldAsync_SyncInline_OrdersInline_Sync_ThenAsync()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var trace = new List<string>();
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new TracingValidator("attr-sync", trace));
        ctx.AddValidator(new TracingAsyncValidator("attr-async", trace));
        ctx.RegisterFieldValidator(fid, (Func<string, IEnumerable<string>>)(_ =>
        {
            trace.Add("inline-field");
            return Array.Empty<string>();
        }));

        await ctx.ValidateFieldAsync(fid);

        Assert.Equal(new[] { "inline-field", "attr-sync", "attr-async" }, trace);
    }

    [Fact]
    public async Task ValidateFieldAsync_AsyncInline_OrdersInline_Sync_ThenAsync()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var trace = new List<string>();
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new TracingValidator("attr-sync", trace));
        ctx.AddValidator(new TracingAsyncValidator("attr-async", trace));
        ctx.RegisterFieldValidator(fid,
            (Func<string, CancellationToken, ValueTask<IEnumerable<string>>>)(async (_, _) =>
            {
                await Task.Yield();
                trace.Add("inline-field");
                return Array.Empty<string>();
            }));

        await ctx.ValidateFieldAsync(fid);

        Assert.Equal(new[] { "inline-field", "attr-sync", "attr-async" }, trace);
    }

    [Fact]
    public void ValidateField_InlineError_SuppressesLaterValidatorForSameField()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new StaticMessageValidator("attr-msg"));
        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(_ => new[] { "inline-msg" }));

        Assert.False(ctx.ValidateField(fid));

        // First-error-wins: only the inline error survives.
        Assert.Equal(new[] { "inline-msg" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void ValidateField_InlineClean_ReengagesLaterValidator()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));
        var inlineHasError = true;

        ctx.AddValidator(new StaticMessageValidator("attr-msg"));
        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(_ =>
                inlineHasError ? new[] { "inline-msg" } : Array.Empty<string>()));

        Assert.False(ctx.ValidateField(fid));
        Assert.Equal(new[] { "inline-msg" }, ctx.GetValidationMessages(fid));

        // "Fix" the inline rule and re-validate — the downstream validator now runs.
        inlineHasError = false;
        Assert.False(ctx.ValidateField(fid));
        Assert.Equal(new[] { "attr-msg" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void Validate_InlineError_SuppressesLaterValidatorForSameField_FullForm()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new StaticMessageValidator("attr-msg"));
        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(_ => new[] { "inline-msg" }));

        ctx.Validate();

        Assert.Equal(new[] { "inline-msg" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task ValidateFieldAsync_InlineError_SuppressesSyncAndAsyncForSameField()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new StaticMessageValidator("sync-attr"));
        ctx.AddValidator(new StaticAsyncMessageValidator("async-attr"));
        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(_ => new[] { "inline-msg" }));

        Assert.False(await ctx.ValidateFieldAsync(fid));
        Assert.Equal(new[] { "inline-msg" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task ValidateFieldAsync_SyncError_SuppressesAsyncForSameField()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.AddValidator(new StaticMessageValidator("sync-attr"));
        ctx.AddValidator(new StaticAsyncMessageValidator("async-attr"));

        Assert.False(await ctx.ValidateFieldAsync(fid));
        Assert.Equal(new[] { "sync-attr" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void Validate_GatingIsPerField_OtherFieldsStillValidated()
    {
        // Inline delegate flags Name; the IFieldValidator wants to add to both Name AND
        // Email — only the Email message survives, Name stays tied to inline.
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);
        var nameField = new FieldIdentifier(m, nameof(Model.Name));
        var emailField = new FieldIdentifier(m, nameof(Model.Email));

        ctx.AddValidator(new MultiFieldValidator(("Name", "attr-name"), ("Email", "attr-email")));
        ctx.RegisterFieldValidator(nameField,
            (Func<string, IEnumerable<string>>)(_ => new[] { "inline-name" }));

        ctx.Validate();

        Assert.Equal(new[] { "inline-name" }, ctx.GetValidationMessages(nameField));
        Assert.Equal(new[] { "attr-email" }, ctx.GetValidationMessages(emailField));
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    private sealed class StaticMessageValidator(string message) : IFieldValidator
    {
        public void Validate(EditContext context) =>
            context.AddValidationMessage(new FieldIdentifier(context.Model, "Name"), message);

        public void ValidateField(EditContext context, FieldIdentifier field) =>
            context.AddValidationMessage(field, message);
    }

    private sealed class StaticAsyncMessageValidator(string message) : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            context.AddValidationMessage(new FieldIdentifier(context.Model, "Name"), message);
            return ValueTask.CompletedTask;
        }

        public ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
        {
            context.AddValidationMessage(field, message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiFieldValidator(params (string Field, string Message)[] entries) : IFieldValidator
    {
        public void Validate(EditContext context)
        {
            foreach (var (field, message) in entries)
            {
                context.AddValidationMessage(new FieldIdentifier(context.Model, field), message);
            }
        }

        public void ValidateField(EditContext context, FieldIdentifier field)
        {
            foreach (var (name, message) in entries)
            {
                if (name == field.FieldName)
                {
                    context.AddValidationMessage(field, message);
                }
            }
        }
    }

    private sealed class TracingValidator(string tag, List<string> trace) : IFieldValidator
    {
        public void Validate(EditContext context) => trace.Add(tag);

        public void ValidateField(EditContext context, FieldIdentifier field) => trace.Add(tag);
    }

    private sealed class TracingAsyncValidator(string tag, List<string> trace) : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            trace.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field, CancellationToken cancellationToken)
        {
            trace.Add(tag);
            return ValueTask.CompletedTask;
        }
    }
}
