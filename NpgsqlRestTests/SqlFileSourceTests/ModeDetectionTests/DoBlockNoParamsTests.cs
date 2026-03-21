using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class DoBlockNoParamsTests
{
    [Fact]
    public void DoBlock_IsDoBlockFlagSet()
    {
        var result = SqlFileParser.Parse("DO $$ BEGIN RAISE NOTICE 'hello'; END; $$");
        result.IsDoBlock.Should().BeTrue();
    }

    [Fact]
    public void DoBlockMixedCase_IsDoBlockFlagSet()
    {
        var result = SqlFileParser.Parse("Do $$ BEGIN END; $$");
        result.IsDoBlock.Should().BeTrue();
    }

    [Fact]
    public void DoBlockWithNewlines_IsDoBlockFlagSet()
    {
        var sql = """
            DO $$
            BEGIN
                NULL;
            END;
            $$
            """;
        var result = SqlFileParser.Parse(sql);
        result.IsDoBlock.Should().BeTrue();
    }

    [Fact]
    public void NotADoBlock_FlagNotSet()
    {
        var result = SqlFileParser.Parse("SELECT 1");
        result.IsDoBlock.Should().BeFalse();
    }
}
