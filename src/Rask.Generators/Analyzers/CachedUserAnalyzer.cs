using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Rask.Generators.Analyzers;

/// <summary>
///     RASK122 — <c>IUserProvider.Current</c> kept in a field or property by a type that never subscribes to
///     <c>IUserProvider.Changed</c>.
///     <para>
///         <c>Current</c> is who is signed in NOW; a copy of it (or of something read from it — a name, a role
///         check) is who was signed in when the copy was taken. Signing in, out or as someone else changes the
///         provider and raises <c>Changed</c>, and a type that keeps the copy without listening shows the old user.
///         Reading <c>users.Current</c> where it is used — in <c>Render</c>, in a handler — is never reported, nor is
///         a copy in a local or one kept by a type that subscribes. Rask's counterpart of Blazor's BL0013.
///     </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CachedUserAnalyzer : DiagnosticAnalyzer
{
    private const string UserProviderFullName = "Rask.Core.Authentication.IUserProvider";

    private static readonly DiagnosticDescriptor Rask122 = new(
        "RASK122",
        "The signed-in user is kept without following IUserProvider.Changed",
        "'{0}' keeps IUserProvider.Current, but '{1}' never subscribes to IUserProvider.Changed, so it goes on "
        + "showing whoever was signed in when it was read; read 'Current' where it is used, or subscribe to "
        + "'Changed' and read it again",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "IUserProvider.Current is the user signed in now. A copy kept in a field or property is the user signed "
        + "in when it was taken; signing in, out or as someone else raises IUserProvider.Changed, which a type "
        + "keeping the copy has to follow.",
        DiagnosticHelp.Link("RASK122"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask122);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(UserProviderFullName) is { } provider)
            {
                start.RegisterSymbolStartAction(s => AnalyzeType(s, provider), SymbolKind.NamedType);
            }
        });
    }

    private static void AnalyzeType(SymbolStartAnalysisContext context, INamedTypeSymbol provider)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (IsProvider(type, provider))
        {
            // A provider (or a decorator over one) holds the user by definition.
            return;
        }

        var state = new TypeState();
        context.RegisterOperationAction(
            ctx => OnAssignment(ctx, type, provider, state),
            OperationKind.SimpleAssignment,
            OperationKind.CoalesceAssignment);
        context.RegisterOperationAction(
            ctx => OnInitializer(ctx, provider, state),
            OperationKind.FieldInitializer,
            OperationKind.PropertyInitializer);
        context.RegisterOperationAction(ctx => OnSubscription(ctx, provider, state), OperationKind.EventAssignment);
        context.RegisterSymbolEndAction(end =>
        {
            if (state.Unfollowed() is { } kept)
            {
                end.ReportDiagnostic(Diagnostic.Create(Rask122, kept.Location, kept.Member.Name, type.Name));
            }
        });
    }

    private static void OnAssignment(
        OperationAnalysisContext context, INamedTypeSymbol type, INamedTypeSymbol provider, TypeState state)
    {
        var assignment = (IAssignmentOperation)context.Operation;
        if (KeptMember(assignment.Target, type) is { } member && ReadsCurrent(assignment.Value, provider))
        {
            state.Keep(member, assignment.Syntax.GetLocation());
        }
    }

    private static void OnInitializer(OperationAnalysisContext context, INamedTypeSymbol provider, TypeState state)
    {
        var initializer = (ISymbolInitializerOperation)context.Operation;
        var member = initializer switch
        {
            IFieldInitializerOperation f => f.InitializedFields.FirstOrDefault() as ISymbol,
            IPropertyInitializerOperation p => p.InitializedProperties.FirstOrDefault(),
            _ => null,
        };

        if (member is not null && ReadsCurrent(initializer.Value, provider))
        {
            state.Keep(member, initializer.Value.Syntax.GetLocation());
        }
    }

    private static void OnSubscription(OperationAnalysisContext context, INamedTypeSymbol provider, TypeState state)
    {
        var subscription = (IEventAssignmentOperation)context.Operation;
        if (subscription.Adds
            && subscription.EventReference is IEventReferenceOperation { Event.Name: "Changed" } changed
            && IsProvider(changed.Event.ContainingType, provider))
        {
            state.Subscribed();
        }
    }

    // A field or property of THIS type (an instance's own, or a static of the type) the value is stored into.
    private static ISymbol? KeptMember(IOperation target, INamedTypeSymbol type)
    {
        var member = target switch
        {
            IFieldReferenceOperation f => (ISymbol)f.Field,
            IPropertyReferenceOperation p => p.Property,
            _ => null,
        };

        var instance = target switch
        {
            IMemberReferenceOperation m => m.Instance,
            _ => null,
        };

        return member is not null
               && SymbolEqualityComparer.Default.Equals(member.ContainingType, type)
               && instance is null or IInstanceReferenceOperation
            ? member
            : null;
    }

    // Whether the value reads IUserProvider.Current where it is evaluated — not inside a lambda, which reads
    // it again each time it runs.
    private static bool ReadsCurrent(IOperation value, INamedTypeSymbol provider)
    {
        if (value is IPropertyReferenceOperation { Property.Name: "Current" } direct
            && IsProvider(direct.Property.ContainingType, provider))
        {
            return true;
        }

        return value.ChildOperations.Any(child =>
            child is not IAnonymousFunctionOperation and not IDelegateCreationOperation
            && ReadsCurrent(child, provider));
    }

    private static bool IsProvider(INamedTypeSymbol? type, INamedTypeSymbol provider) =>
        type is not null
        && (SymbolEqualityComparer.Default.Equals(type, provider)
            || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, provider)));

    // Written from operation actions that may run concurrently, read once at the type's end.
    private sealed class TypeState
    {
        private readonly object _gate = new();
        private bool _subscribed;
        private (ISymbol Member, Location Location)? _kept;

        public void Keep(ISymbol member, Location location)
        {
            lock (_gate)
            {
                // The first in source order, so the report does not depend on which operation ran first.
                if (_kept is not { } kept
                    || (location.SourceTree == kept.Location.SourceTree
                        && location.SourceSpan.Start < kept.Location.SourceSpan.Start))
                {
                    _kept = (member, location);
                }
            }
        }

        public void Subscribed()
        {
            lock (_gate)
            {
                _subscribed = true;
            }
        }

        public (ISymbol Member, Location Location)? Unfollowed()
        {
            lock (_gate)
            {
                return _subscribed ? null : _kept;
            }
        }
    }
}
