using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class EditContextEdgeTests
{
    [Fact]
    public void NotifyFieldChanged_DoesNotFireValidationStateChanged()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Name");
        var validationFired = 0;
        ctx.ValidationStateChanged += () => validationFired++;

        ctx.NotifyFieldChanged(fid);

        Assert.Equal(0, validationFired);
    }

    [Fact]
    public void ValidateField_UnregisteredField_DoesNotThrow_AndAddsNoMessages()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "Missing");

        var ok = ctx.ValidateField(fid);

        Assert.True(ok);
        Assert.Empty(ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void ClearMessages_OnlyRemovesTargetFieldMessages()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.AddValidationMessage(a, "ax");
        ctx.AddValidationMessage(b, "bx");

        ctx.ClearMessages(a);

        Assert.Empty(ctx.GetValidationMessages(a));
        Assert.Single(ctx.GetValidationMessages(b));
    }

    [Fact]
    public void ClearMessages_NoExistingMessages_DoesNotFireValidationEvent()
    {
        var ctx = new EditContext(new Model());
        var fid = new FieldIdentifier(ctx.Model, "A");
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.ClearMessages(fid);

        Assert.Equal(0, fired);
    }

    [Fact]
    public void ClearAllMessages_EmptiesAllFields_AndFiresOnce()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.AddValidationMessage(a, "ax");
        ctx.AddValidationMessage(b, "bx");
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.ClearAllMessages();

        Assert.Empty(ctx.GetValidationMessages(a));
        Assert.Empty(ctx.GetValidationMessages(b));
        Assert.False(ctx.HasValidationMessages());
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ClearAllMessages_NothingToClear_DoesNotFire()
    {
        var ctx = new EditContext(new Model());
        var fired = 0;
        ctx.ValidationStateChanged += () => fired++;

        ctx.ClearAllMessages();

        Assert.Equal(0, fired);
    }

    [Fact]
    public void TouchAllRegisteredFields_MarksAllRegisteredAsTouched()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.NotifyFieldChanged(a);
        ctx.NotifyFieldChanged(b);

        ctx.TouchAllRegisteredFields();

        Assert.True(ctx.IsTouched(a));
        Assert.True(ctx.IsTouched(b));
    }

    [Fact]
    public void GetValidationMessages_GlobalEnumerator_FlattensFields()
    {
        var ctx = new EditContext(new Model());
        var a = new FieldIdentifier(ctx.Model, "A");
        var b = new FieldIdentifier(ctx.Model, "B");
        ctx.AddValidationMessage(a, "ax");
        ctx.AddValidationMessage(b, "by");

        var all = ctx.GetValidationMessages().OrderBy(m => m).ToArray();

        Assert.Equal(new[] { "ax", "by" }, all);
    }

    [Fact]
    public void Constructor_NullModel_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new EditContext(null!));

    [Fact]
    public void AddValidator_Null_Throws()
    {
        var ctx = new EditContext(new Model());
        Assert.Throws<ArgumentNullException>(() => ctx.AddValidator(null!));
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
