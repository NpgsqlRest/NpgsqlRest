using System.Text;

namespace NpgsqlRestTests.ParserTests;

/// <summary>
/// Regression: a returned builder is reused by the next Rent. Rent used to read the slot below the top of the pool, so
/// the most recently returned builder was never reused (with one pooled builder, every Rent allocated a new one).
/// </summary>
public class StringBuilderPoolTests
{
    [Fact]
    public void Returned_builder_is_reused_by_the_next_rent()
    {
        var first = StringBuilderPool.Rent();
        first.Append("used");
        StringBuilderPool.Return(first);

        var second = StringBuilderPool.Rent();
        second.Should().BeSameAs(first);
        second.Length.Should().Be(0, "a pooled builder is cleared on return");
        StringBuilderPool.Return(second);
    }

    [Fact]
    public void Builders_come_back_in_last_in_first_out_order()
    {
        var a = StringBuilderPool.Rent();
        var b = StringBuilderPool.Rent();
        StringBuilderPool.Return(a);
        StringBuilderPool.Return(b);

        StringBuilderPool.Rent().Should().BeSameAs(b);
        StringBuilderPool.Rent().Should().BeSameAs(a);
        StringBuilderPool.Return(a);
        StringBuilderPool.Return(b);
    }

    [Fact]
    public void Oversized_builders_are_not_pooled()
    {
        var big = new StringBuilder(64 * 1024);
        StringBuilderPool.Return(big);
        var next = StringBuilderPool.Rent();
        next.Should().NotBeSameAs(big);
        StringBuilderPool.Return(next);
    }
}
