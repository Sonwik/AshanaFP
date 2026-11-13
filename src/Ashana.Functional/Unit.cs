namespace Ashana.Functional;

/// <summary>
/// The type with exactly one value. Functional code uses it instead of <c>void</c>,
/// so "an operation that can fail but returns nothing" is simply <c>Result&lt;Unit&gt;</c>
/// and works with every combinator (Map, Bind, LINQ) like any other result.
/// </summary>
public readonly record struct Unit
{
    public static readonly Unit Value;

    public override string ToString() => "()";
}
