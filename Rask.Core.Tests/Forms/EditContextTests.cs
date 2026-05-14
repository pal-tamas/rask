using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class EditContextTests
{
    [Fact]
    public void NotifyFieldChanged_MarksDirty_AndFiresEvent()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");

        var fired = 0;
        ctx.FieldChanged += _ => fired++;
        Assert.False(ctx.IsModified(fid));

        ctx.NotifyFieldChanged(fid);

        Assert.True(ctx.IsModified(fid));
        Assert.Equal(1, fired);
    }

    [Fact]
    public void NotifyFieldTouched_MarksTouched()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        ctx.NotifyFieldTouched(fid);
        Assert.True(ctx.IsTouched(fid));
    }

    [Fact]
    public void AddValidator_DedupesByType()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Name");
        ctx.AddValidator(new MessageStampingValidator());
        ctx.AddValidator(new MessageStampingValidator()); // distinct instance, same type
        ctx.Validate();
        // If the validator was added twice, we'd see two copies of "stamp" — dedup means one.
        Assert.Single(ctx.GetValidationMessages(fid));
    }

    private sealed class MessageStampingValidator : IFieldValidator
    {
        public void Validate(EditContext context) =>
            context.AddValidationMessage(new FieldIdentifier(context.Model, "Name"), "stamp");

        public void ValidateField(EditContext context, FieldIdentifier field) { }
    }

    [Fact]
    public void AddValidationMessage_FiresValidationStateChanged()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Name");
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.AddValidationMessage(fid, "bad");

        Assert.Equal(1, fired);
        Assert.Single(ctx.GetValidationMessages(fid));
        Assert.True(ctx.HasValidationMessages());
    }

    [Fact]
    public void Validate_ClearsMessagesFirst()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Name");
        ctx.AddValidationMessage(fid, "stale");
        ctx.Validate(); // no validators registered → no messages
        Assert.Empty(ctx.GetValidationMessages(fid));
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
