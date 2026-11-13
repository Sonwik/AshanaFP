namespace Ashana.UnitTests.Functional;

/// <summary>
/// The functor and monad laws for <see cref="Result{T}"/> and <see cref="Option{T}"/>, checked on sample values.
/// If these laws hold, refactoring a pipeline (splitting or merging Map/Bind steps) never changes its meaning.
/// </summary>
public class ResultLawTests
{
    private static readonly Error Boom = Error.Failure("test.boom", "Boom");

    public static TheoryData<int> Samples => new() { 0, 1, -7, 42 };

    private static Result<int> Half(int x) => x % 2 == 0 ? x / 2 : Error.Validation("test.odd", $"{x} is odd");

    private static Result<int> Positive(int x) => x > 0 ? x : Error.Validation("test.not_positive", $"{x} is not positive");

    [Theory]
    [MemberData(nameof(Samples))]
    public void Functor_identity_mapping_identity_changes_nothing(int x)
    {
        Assert.Equal(Result.Success(x), Result.Success(x).Map(F.Identity));
        Assert.Equal(Result.Failure<int>(Boom), Result.Failure<int>(Boom).Map(F.Identity));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Functor_composition_two_maps_equal_one_map_of_the_composition(int x)
    {
        Func<int, int> add = v => v + 3;
        Func<int, string> show = v => $"#{v}";

        Assert.Equal(Result.Success(x).Map(add).Map(show), Result.Success(x).Map(add.Then(show)));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Monad_left_identity(int x) =>
        Assert.Equal(Half(x), Result.Success(x).Bind(Half));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Monad_right_identity(int x)
    {
        Assert.Equal(Half(x), Half(x).Bind(Result.Success));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Monad_associativity(int x) =>
        Assert.Equal(
            Result.Success(x).Bind(Half).Bind(Positive),
            Result.Success(x).Bind(v => Half(v).Bind(Positive)));

    [Fact]
    public void The_first_failure_short_circuits_and_later_steps_never_run()
    {
        var calls = 0;

        var result = Result.Success(3)
            .Bind(Half)                         // fails: 3 is odd
            .Map(v => { calls++; return v; })
            .Bind(v => { calls++; return Positive(v); });

        Assert.Equal("test.odd", result.Error.Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Linq_query_syntax_is_bind_and_map()
    {
        var viaQuery =
            from a in Half(8)
            from b in Half(a)
            let sum = a + b
            select sum * 10;

        Assert.Equal(Result.Success(60), viaQuery);
        Assert.Equal("test.odd", (from a in Half(6) from b in Half(a) select a + b).Error.Code);
    }

    [Fact]
    public void Ensure_switches_to_the_error_track_when_the_rule_fails()
    {
        Assert.Equal(Result.Success(10), Result.Success(10).Ensure(v => v > 5, Boom));
        Assert.Equal(Boom, Result.Success(1).Ensure(v => v > 5, Boom).Error);
        Assert.Equal("1 too small", Result.Success(1).Ensure(v => v > 5, v => Error.Validation("x", $"{v} too small")).Error.Message);
    }

    [Fact]
    public void Match_forces_both_cases_to_be_handled()
    {
        Assert.Equal("ok 4", Result.Success(4).Match(v => $"ok {v}", e => e.Code));
        Assert.Equal("test.boom", Result.Failure<int>(Boom).Match(v => $"ok {v}", e => e.Code));
    }

    [Fact]
    public void Map_error_and_or_else_work_on_the_error_track_only()
    {
        Assert.Equal("wrapped", Result.Failure<int>(Boom).MapError(_ => Error.Conflict("wrapped", "w")).Error.Code);
        Assert.Equal(Result.Success(0), Result.Failure<int>(Boom).OrElse(_ => 0));
        Assert.Equal(Result.Success(5), Result.Success(5).OrElse(_ => 0));
    }

    [Fact]
    public void Tap_runs_side_effects_only_on_its_own_track()
    {
        var log = new List<string>();

        Result.Success(1).Tap(v => log.Add($"ok {v}")).TapError(e => log.Add(e.Code));
        Result.Failure<int>(Boom).Tap(v => log.Add($"ok {v}")).TapError(e => log.Add(e.Code));

        Assert.Equal(["ok 1", "test.boom"], log);
    }

    [Fact]
    public void Traverse_collects_every_value_or_stops_at_the_first_error()
    {
        var calls = new List<int>();
        Result<int> Check(int v)
        {
            calls.Add(v);
            return Positive(v);
        }

        Assert.Equal([1, 2, 3], new[] { 1, 2, 3 }.Traverse(Positive).Value);
        Assert.Equal("test.not_positive", new[] { 1, -2, 3 }.Traverse(Check).Error.Code);
        Assert.Equal([1, -2], calls); // 3 was never checked
    }

    [Fact]
    public void Fold_result_threads_state_and_stops_on_failure()
    {
        Assert.Equal(Result.Success(10), new[] { 1, 2, 3, 4 }.FoldResult(0, (sum, v) => Result.Success(sum + v)));
        Assert.True(new[] { 1, 2, 3 }.FoldResult(0, (sum, v) => sum + v > 2 ? Boom : Result.Success(sum + v)).IsFailure);
    }

    [Fact]
    public void Try_turns_exceptions_into_values()
    {
        var parsed = Result.Try(() => int.Parse("12", System.Globalization.CultureInfo.InvariantCulture), ex => Boom);
        var broken = Result.Try(() => int.Parse("x", System.Globalization.CultureInfo.InvariantCulture), ex => Error.Validation("parse", ex.GetType().Name));

        Assert.Equal(Result.Success(12), parsed);
        Assert.Equal("FormatException", broken.Error.Message);
    }

    [Fact]
    public void Reading_the_wrong_side_is_a_programming_error()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure<int>(Boom).Value);
        Assert.Throws<InvalidOperationException>(() => Result.Success(1).Error);
    }

    [Fact]
    public void Results_and_errors_compare_by_value()
    {
        Assert.Equal(Error.Conflict("menu.sold_out", "Sold out"), Error.Conflict("menu.sold_out", "Sold out"));
        Assert.Equal(Result.Failure<int>(Boom), Result.Failure<int>(Error.Failure("test.boom", "Boom")));
        Assert.NotEqual(Result.Success(1), Result.Success(2));
    }

    [Fact]
    public async Task Async_helpers_compose_tasks_of_results()
    {
        var doubled = await Task.FromResult(Result.Success(21)).MapAsync(v => v * 2);
        var chained = await Result.Success(8).BindAsync(v => Task.FromResult(Half(v)));

        Assert.Equal(Result.Success(42), doubled);
        Assert.Equal(Result.Success(4), chained);
    }
}

public class OptionTests
{
    private static Option<int> ParseAge(string text) =>
        int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var age) && age is >= 0 and < 150 ? age : Option<int>.None;

    [Fact]
    public void Default_is_none_and_null_becomes_none()
    {
        Option<int> missing = default;
        Option<string> fromNull = (string?)null;

        Assert.True(missing.IsNone);
        Assert.True(fromNull.IsNone);
        Assert.Equal(Option.Some("x"), (Option<string>)"x");
    }

    [Fact]
    public void Some_cannot_hold_null() =>
        Assert.Throws<ArgumentNullException>(() => Option.Some<string>(null!));

    [Fact]
    public void Map_bind_and_where_skip_the_none_case()
    {
        Assert.Equal(Option.Some(21), ParseAge("20").Map(a => a + 1));
        Assert.True(ParseAge("abc").Map(a => a + 1).IsNone);
        Assert.True(ParseAge("20").Where(a => a >= 21).IsNone);
        Assert.Equal(Option.Some(40), ParseAge("20").Bind(a => ParseAge((a * 2).ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Theory]
    [InlineData("18")]
    [InlineData("nope")]
    public void Monad_laws_hold_for_options(string input)
    {
        var start = ParseAge(input);
        Option<int> Older(int a) => a + 1 < 150 ? a + 1 : Option<int>.None;

        Assert.Equal(start, start.Bind(Option.Some));                                       // right identity
        Assert.Equal(Older(18), Option.Some(18).Bind(Older));                               // left identity
        Assert.Equal(start.Bind(Older).Bind(Older), start.Bind(a => Older(a).Bind(Older))); // associativity
    }

    [Fact]
    public void Get_or_else_and_to_result_end_the_chain()
    {
        Assert.Equal(0, ParseAge("x").GetOrElse(0));
        Assert.Equal("age.invalid", ParseAge("x").ToResult(Error.Validation("age.invalid", "Invalid age")).Error.Code);
        Assert.Equal(Result.Success(30), ParseAge("30").ToResult(Error.Validation("age.invalid", "Invalid age")));
    }

    [Fact]
    public void Collection_helpers_return_options_instead_of_null()
    {
        var map = new Dictionary<string, int> { ["plov"] = 1800 };

        Assert.Equal(Option.Some(1800), map.Find("plov"));
        Assert.True(map.Find("pizza").IsNone);
        Assert.Equal(Option.Some(4), new[] { 1, 4, 6 }.FirstOrNone(v => v % 2 == 0));
        Assert.True(Array.Empty<int>().FirstOrNone().IsNone);
        Assert.Equal([18, 30], new[] { "18", "x", "30" }.Choose(ParseAge));
    }

    [Fact]
    public void Linq_query_syntax_works_on_options()
    {
        var sum = from a in ParseAge("10") from b in ParseAge("5") select a + b;
        var missing = from a in ParseAge("10") from b in ParseAge("?") select a + b;

        Assert.Equal(Option.Some(15), sum);
        Assert.True(missing.IsNone);
    }
}
