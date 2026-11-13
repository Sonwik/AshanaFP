namespace Ashana.UnitTests.Functional;

public class FunctionsTests
{
    private static readonly Func<int, int, int> Add = (a, b) => a + b;
    private static readonly Func<int, int, int, int> Volume = (a, b, c) => a * b * c;

    [Fact]
    public void Pipe_reads_left_to_right() =>
        Assert.Equal("#6", 5.Pipe(v => v + 1).Pipe(v => $"#{v}"));

    [Fact]
    public void Then_composes_two_functions()
    {
        Func<int, int> twice = v => v * 2;
        Func<int, string> show = v => $"={v}";

        var composed = twice.Then(show);

        Assert.Equal("=14", composed(7));
        Assert.Equal(show(twice(7)), composed(7));
    }

    [Fact]
    public void Compose_builds_a_pipeline_from_a_list_of_steps()
    {
        var steps = new List<Func<string, string>> { s => s.Trim(), s => s.ToUpperInvariant(), s => s + "!" };

        Assert.Equal("PLOV!", F.Compose(steps)("  plov "));
        Assert.Equal("same", F.Compose(Array.Empty<Func<string, string>>())("same")); // no steps = identity
    }

    [Fact]
    public void Curry_turns_a_two_argument_function_into_a_chain()
    {
        var curried = Add.Curry();
        var addTen = curried(10);

        Assert.Equal(15, addTen(5));
        Assert.Equal(Add(3, 4), curried(3)(4));
        Assert.Equal(Add(3, 4), curried.Uncurry()(3, 4));
        Assert.Equal(24, Volume.Curry()(2)(3)(4));
    }

    [Fact]
    public void Apply_fixes_the_first_argument()
    {
        var fromHundred = Add.Apply(100);
        var flat = Volume.Apply(2);

        Assert.Equal(101, fromHundred(1));
        Assert.Equal(new[] { 101, 102 }, new[] { 1, 2 }.Select(fromHundred));
        Assert.Equal(30, flat(3, 5));
    }

    [Fact]
    public void Memoize_calls_a_pure_function_once_per_argument()
    {
        var calls = 0;
        var square = F.Memoize<int, int>(x =>
        {
            calls++;
            return x * x;
        });

        Assert.Equal(new[] { 9, 9, 16, 9 }, new[] { 3, 3, 4, 3 }.Select(square));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Memoize_last_recomputes_only_for_a_new_snapshot()
    {
        var calls = 0;
        var count = F.MemoizeLast<int[], int>(snapshot =>
        {
            calls++;
            return snapshot.Length;
        });
        var first = new[] { 1, 2 };
        var second = new[] { 1, 2, 3 };

        Assert.Equal(new[] { 2, 2, 3, 3, 2 }, new[] { first, first, second, second, first }.Select(count));
        Assert.Equal(3, calls); // first, second, then first again (only the last snapshot is remembered)
    }
}

public class TrampolineTests
{
    /// <summary>Sum of 1..n written as tail recursion over (n, acc).</summary>
    private static Trampoline<long> Sum(long n, long acc) =>
        n == 0 ? Trampoline.Done(acc) : Trampoline.More(() => Sum(n - 1, acc + n));

    /// <summary>Mutual recursion, a classic case where a trampoline is needed.</summary>
    private static Trampoline<bool> IsEven(int n) => n == 0 ? Trampoline.Done(true) : Trampoline.More(() => IsOdd(n - 1));

    private static Trampoline<bool> IsOdd(int n) => n == 0 ? Trampoline.Done(false) : Trampoline.More(() => IsEven(n - 1));

    [Fact]
    public void A_million_recursive_steps_do_not_overflow_the_stack() =>
        Assert.Equal(500_000_500_000L, Sum(1_000_000, 0).Run());

    [Fact]
    public void Mutual_recursion_runs_in_constant_stack()
    {
        Assert.True(IsEven(1_000_000).Run());
        Assert.True(IsOdd(777_777).Run());
    }

    [Fact]
    public void The_union_has_exactly_two_cases()
    {
        Assert.IsType<Trampoline<int>.Done>(Trampoline.Done(1));
        Assert.IsType<Trampoline<int>.More>(Trampoline.More(() => Trampoline.Done(1)));
    }
}

public class SeqTests
{
    [Fact]
    public void Unfold_grows_a_sequence_from_a_seed()
    {
        var countdown = Seq.Unfold(3, n => n > 0 ? Option.Some((n, n - 1)) : Option<(int, int)>.None);

        Assert.Equal([3, 2, 1], countdown);
    }

    [Fact]
    public void Infinite_sequences_are_fine_as_long_as_we_take_a_part()
    {
        var powersOfTwo = Seq.Iterate(1, x => x * 2);

        Assert.Equal([1, 2, 4, 8, 16], powersOfTwo.Take(5));
        Assert.Equal(1024, powersOfTwo.First(x => x > 1000));
    }

    [Fact]
    public void Nothing_runs_until_the_sequence_is_enumerated()
    {
        var evaluated = 0;
        var lazy = Seq.Iterate(0, x =>
        {
            evaluated++;
            return x + 1;
        });

        var query = lazy.Where(x => x % 2 == 0).Select(x => x * 10); // just a description of work
        Assert.Equal(0, evaluated);

        Assert.Equal([0, 20, 40], query.Take(3));
        Assert.Equal(4, evaluated);
    }

    [Fact]
    public void Scan_gives_every_running_total() =>
        Assert.Equal([1800m, 2100m, 3900m], new[] { 1800m, 300m, 1800m }.Scan(0m, (sum, price) => sum + price));
}

public class ValidationRuleTests
{
    private static readonly Rule<string>[] NameRules =
    [
        Validation.Rule<string>(s => s.Length > 0, Error.Validation("name.empty", "Required")),
        Validation.Rule<string>(s => s.Length <= 5, Error.Validation("name.long", "Too long")),
        Validation.Rule<string>(s => char.IsUpper(s, 0), Error.Validation("name.case", "Capital first")),
    ];

    [Theory]
    [InlineData("Plov", null)]
    [InlineData("", "name.empty")]      // the first rule fails; later rules are not even evaluated
    [InlineData("Lagmans", "name.long")]
    [InlineData("tea", "name.case")]
    public void Check_returns_the_value_or_the_first_broken_rule(string name, string? expected) =>
        Assert.Equal(expected, name.Check(NameRules).Match(_ => (string?)null, e => e.Code));

    [Fact]
    public void All_errors_lists_every_broken_rule() =>
        Assert.Equal(["name.long", "name.case"], "beshbarmak".AllErrors(NameRules).Select(e => e.Code));
}
