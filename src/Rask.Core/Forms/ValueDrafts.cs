using System.Reflection;
using System.Runtime.CompilerServices;
using Rask.Values;

namespace Rask.Core.Forms;

// The drafts of the value objects of several values a model holds, one per model and property, kept as long as the
// model is: every render binds the same draft, so what was typed into it and not yet taken stays.
internal static class ValueDrafts
{
    private static readonly ConditionalWeakTable<object, Dictionary<string, ValueDraft>> Held = [];

    // The draft of `owner.Property`, made the first time it is bound and following the property after.
    internal static ValueDraft For(object owner, PropertyInfo property, ValueGroup group)
    {
        var drafts = Held.GetOrCreateValue(owner);
        lock (drafts)
        {
            if (drafts.TryGetValue(property.Name, out var draft))
            {
                draft.Binding!.Follow();
                return draft;
            }

            draft = group.NewDraft();
            draft.Binding = new DraftBinding(draft, owner, property, group);
            drafts[property.Name] = draft;
            return draft;
        }
    }

    // The draft a bound control made of `owner.Property`, as a field's path walks through it; null for none.
    internal static ValueDraft? Find(object owner, string property)
    {
        if (!Held.TryGetValue(owner, out var drafts))
        {
            return null;
        }

        lock (drafts)
        {
            return drafts.GetValueOrDefault(property);
        }
    }
}
