namespace Rask.External;

// The setters each runtime's generated children indexers call. On the runtime base rather than ExternalComponent, so a
// React island's indexer can only store React children: the parameter types are the check, and the generated code is
// the only caller. The static lambdas are cached by the compiler, so storing children allocates the array and nothing
// else.

public abstract partial class ReactComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(ReactChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<ReactChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<ReactComponent?> children) => SetIslandChildren(children, static island => island);
}

public abstract partial class PreactComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(PreactChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<PreactChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<PreactComponent?> children) => SetIslandChildren(children, static island => island);
}

public abstract partial class SolidComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(SolidChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<SolidChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<SolidComponent?> children) => SetIslandChildren(children, static island => island);
}

public abstract partial class VueComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(VueChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<VueChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<VueComponent?> children) => SetIslandChildren(children, static island => island);
}

public abstract partial class SvelteComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(SvelteChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<SvelteChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<SvelteComponent?> children) => SetIslandChildren(children, static island => island);
}

public abstract partial class LitComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(LitChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<LitChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<LitComponent?> children) => SetIslandChildren(children, static island => island);
}

public abstract partial class AngularComponent
{
    /// <summary>Stores this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(AngularChild[] children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores this island's children, materialised now. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<AngularChild> children) => SetIslandChildren(children, static child => child.Value);

    /// <summary>Stores a sequence of islands as this island's children. Called by its generated children indexers.</summary>
    protected void SetChildren(IEnumerable<AngularComponent?> children) => SetIslandChildren(children, static island => island);
}
