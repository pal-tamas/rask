using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    private static readonly DiagnosticDescriptor Rask001 = new(
        "RASK001",
        "Property is a required chain step",
        "'{0}.{1}' is a required chain step because it is non-nullable with no initializer — mark it 'required' so the language enforces it too, or make it nullable if it is optional",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Hidden,
        true,
        description: "A non-nullable property with no initializer is a REQUIRED chain step: the chain must take "
                     + "it before anything else (RASK095 reports one that is skipped). Marking the property "
                     + "'required' gets you the same guarantee from the language, at the declaration. Declare it "
                     + "nullable instead if the value really is optional.",
        helpLinkUri: DiagnosticHelp.Link("RASK001"));

    private static readonly DiagnosticDescriptor Rask002 = new(
        "RASK002",
        "'required' property cannot be honored by the chain",
        "'{0}.{1}' is 'required' but has an initializer, so it is not a chain step, and '{0}' has a parameterless constructor, so its chain entry builds it with 'new {0}()' — which fails with CS9035 and never runs the DI constructor. Remove the initializer so the property becomes a chain step, or remove 'required'.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Fires in exactly one shape: the component has both a DI constructor AND a parameterless one, and "
                     + "the required property carries a member initializer. The chain entry then builds it with 'new C()', "
                     + "but an initializer-carrying property is not a chain step — so nothing "
                     + "assigns it and the consumer's build fails with CS9035. A DI constructor with no parameterless "
                     + "sibling is fine and does not trip this.",
        helpLinkUri: DiagnosticHelp.Link("RASK002"));

    private static readonly DiagnosticDescriptor Rask036 = new(
        "RASK036",
        "A chain-entry host must be partial",
        "{0}, so {1} cannot be injected into it; writing one of their names unqualified inside it will not compile — add the 'partial' modifier where it is missing",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A generator cannot add members to a type in a referenced assembly, so the entry for each "
                     + "component that is not Rask.Core's is injected into every type that might name one — every "
                     + "component of yours, and every markup host (a test class, a fixture, a factory of demo "
                     + "components, marked by deriving from 'RaskMarkup' or by the '[RaskMarkup]' attribute). That "
                     + "needs somewhere to inject it, and only a 'partial' class has one. For a host that DERIVES "
                     + "from 'RaskMarkup', Rask.Core's own entries are unaffected — those are inherited and need "
                     + "nothing injected — so all that is lost is naming a non-framework component unqualified. An "
                     + "'[RaskMarkup]' host has no such fallback: the generated partial is where its base or its "
                     + "framework entries would have come from, so without 'partial' it gets no surface at all.",
        helpLinkUri: DiagnosticHelp.Link("RASK036"));

    private static readonly DiagnosticDescriptor Rask040 = new(
        "RASK040",
        "Two components share a simple name, so neither can have a chain entry",
        "Components '{1}' share the simple name '{0}', so neither gets a chain entry: an entry is one member named after its type, and one name can only stand for one type — rename one of them",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "An entry is keyed by SIMPLE name — it is one member named after its type — so two components "
                     + "sharing a simple name can have at most one entry between them, and picking a winner would be "
                     + "the generator guessing which type the name means. Neither gets one until you rename. Their "
                     + "namespaces do not separate them here the way they separate the types themselves: a member "
                     + "name has no namespace.",
        helpLinkUri: DiagnosticHelp.Link("RASK040"));

    private static readonly DiagnosticDescriptor Rask041 = new(
        "RASK041",
        "The chain surface's shared pending-bit budget is exhausted",
        "The shared Element/Component surface has {0} folding properties but only {1} pending bits; '{2}' and every later one (ordinal name order) fall back to the eager reset, which reports the property changed on every render and defeats the render cache for it. Raise 'BuilderRuntime.OwnPendingBit' (and the generator's copy of it) together, or make the property non-folding.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A folding setter clears its own PENDING bit as it writes, and whatever is still pending when "
                     + "the parent's Render() returns is reset to what a fresh component would hold. The shared "
                     + "Element/Component surface owns the low bits, up to BuilderRuntime.OwnPendingBit, so a "
                     + "component compiled against one Rask.Core cannot collide with a shared property added in "
                     + "a later one. The bits are handed "
                     + "out in ordinal NAME order, so overflowing the budget does not push the NEW property off the "
                     + "end — it pushes whichever alphabetically-later one was last onto the eager reset, which "
                     + "reports that property changed on every render and defeats the render cache for it. Nothing "
                     + "else fails, which is why this exists.",
        helpLinkUri: DiagnosticHelp.Link("RASK041"));

    // The budget is fixed (16) and handed out in ORDINAL NAME ORDER, so adding one folding prop to
    // Element does not push ITSELF off the end — it pushes whichever alphabetically-later prop was
    // last (Title, TabIndex). That one silently moves to the eager reset, which reports the prop
    // changed on every render and defeats the render cache for it: no compile error, no test failure,
    // just a slower framework. This is the signal.
    private static void ReportSharedBitOverflow(
        SourceProductionContext spc, SetterHost host, Dictionary<string, int> bits)
    {
        // The typed Aria* properties count once: they share one bit (SharedPendingBits).
        var folding = host.Shared
            .Where(s => !s.IsTypedAria && FoldsIntoPropsChanged(s.Name, s.IsDelegate, autoRerender: false))
            .ToList();
        var groups = host.Shared.Any(static s => s.IsTypedAria) ? 1 : 0;
        if (folding.Count + groups <= OwnPendingBit)
        {
            return;
        }

        var first = folding.First(s => !bits.ContainsKey(s.Name));
        spc.ReportDiagnostic(Diagnostic.Create(Rask041, Location.None,
            (folding.Count + groups).ToString(CultureInfo.InvariantCulture),
            OwnPendingBit.ToString(CultureInfo.InvariantCulture),
            first.Name));
    }

    private static void ReportEntryCollision(SourceProductionContext spc, List<Candidate> members)
    {
        var names = string.Join("', '", members.Select(static c => c.FullyQualifiedName));
        foreach (var c in members)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask040, MakeDeclLocation(c), c.TypeName, names));
        }
    }

    // RASK036 for a host the injection cannot re-open; true when it was reported and must be skipped.
    private static bool ReportedUninjectable(SourceProductionContext spc, EntryHostDecl host2)
    {
        // A nested host CAN be injected into — the generated file just has to re-open every enclosing
        // type as a partial around it. That is only possible if the author declared them partial, and
        // it is not optional for a REFERENCED library: the framework's own entries reach a nested
        // component by INHERITANCE from RaskMarkup, where nesting is irrelevant, but a referenced
        // library's can only be injected. Skipping would cost that nested component the chain with
        // nothing said, so the skip reports instead — the same RASK036 a non-partial top-level host
        // gets, naming the enclosing type that has to change.
        if (host2.IsNested && !host2.EnclosingAllPartial)
        {
            // Names the ENCLOSING type as the thing to change, which the comment above has always
            // claimed and the message did not do (#1019). Saying "'NestedHost' is not declared
            // 'partial'" about a type that is plainly declared partial sends the reader to re-read
            // the one line that is already correct; the container is what is missing the modifier.
            // Reported here rather than fixed silently because the alternative — skipping — is how a
            // nested component loses its chain with nothing said at all.
            spc.ReportDiagnostic(Diagnostic.Create(
                Rask036,
                MakeDeclLocation(host2),
                $"'{host2.TypeName}' is nested in a type that is not declared 'partial'",
                Rask036Loses(host2.Delivery)));
            return true;
        }

        if (!host2.IsPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Rask036,
                MakeDeclLocation(host2),
                $"'{host2.TypeName}' is not declared 'partial'",
                Rask036Loses(host2.Delivery)));
            return true;
        }

        return false;
    }

    // What a non-partial host loses, which is not the same for every host: an inheriting one still has
    // the framework tags and loses only the injected half, while an attributed one that cannot inherit
    // loses the entire surface — including the base the generated partial would have given it.
    private static string Rask036Loses(Delivery delivery) =>
        delivery == Delivery.Inherited
            ? "the chain entries for this project's and its referenced libraries' components"
            : "every chain entry — the framework tags as well as this project's and its referenced "
              + "libraries' components";

    private static void ReportPropertyDiagnostics(
        SourceProductionContext spc, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var c in candidates)
        {
            foreach (var p in c.Properties)
            {
                var location = MakeLocation(p);
                // RASK002 only fires when the chain genuinely cannot honor `required`. A DI ctor alone
                // is fine: with no parameterless ctor the entry builds via
                // ActivatorUtilities.CreateInstance (reflection bypasses the CS9035 check) and the steps
                // assign afterwards, so a required no-initializer prop IS set. The one broken shape is a
                // parameterless ctor present *and* a required prop carrying a member initializer: the
                // entry then constructs with `new T()` and the prop is excluded from what the steps can
                // set (IsParamProperty), so the consumer build hits CS9035.
                if (p.UserMarkedRequired && c.HasDIConstructor && c.HasParameterlessCtor && p.HasInitializer)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask002, location, c.FullyQualifiedName, p.Name));
                }
                else if (IsRequiredFactoryParam(p) && !p.UserMarkedRequired)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask001, location, c.FullyQualifiedName, p.Name));
                }
            }
        }
    }
}
