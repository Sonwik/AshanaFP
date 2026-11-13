namespace Ashana.Functional;

/// <summary>
/// Stack-safe recursion. A recursive function returns either the final answer (<see cref="Done"/>)
/// or "one more step" (<see cref="More"/>) instead of calling itself directly; <see cref="Run"/>
/// then executes the steps in a loop, so even a million recursive steps never overflow the stack.
/// C# does not guarantee tail-call optimization, so this is how deep recursion is made safe.
/// </summary>
/// <remarks>
/// This is a closed discriminated union: the constructor is private, so <see cref="Done"/> and
/// <see cref="More"/> are the only possible cases and a <c>switch</c> over them is complete.
/// </remarks>
/// <typeparam name="T">Type of the final answer.</typeparam>
public abstract record Trampoline<T>
{
    private Trampoline()
    {
    }

    /// <summary>Recursion finished with this value.</summary>
    public sealed record Done(T Value) : Trampoline<T>;

    /// <summary>Recursion continues with the next step (not evaluated yet: lazy).</summary>
    public sealed record More(Func<Trampoline<T>> Next) : Trampoline<T>;

    /// <summary>Runs the steps in a loop until a <see cref="Done"/> is reached.</summary>
    public T Run()
    {
        var step = this;
        while (true)
        {
            switch (step)
            {
                case Done done:
                    return done.Value;
                case More more:
                    step = more.Next();
                    break;
                default:
                    throw new InvalidOperationException("Unknown trampoline step.");
            }
        }
    }
}

/// <summary>Short constructors for <see cref="Trampoline{T}"/>.</summary>
public static class Trampoline
{
    public static Trampoline<T> Done<T>(T value) => new Trampoline<T>.Done(value);

    public static Trampoline<T> More<T>(Func<Trampoline<T>> next) => new Trampoline<T>.More(next);
}
