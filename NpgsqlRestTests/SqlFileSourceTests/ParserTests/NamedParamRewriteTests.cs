using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class NamedParamRewriteTests
{
    private static (List<string> Statements, NamedParamRewriteResult Result) Rewrite(string sql)
    {
        var parsed = SqlFileParser.Parse(sql);
        var result = SqlFileParser.RewriteNamedParameters(parsed.Statements);
        return (parsed.Statements, result);
    }

    [Fact]
    public void SingleNamedParam_RewrittenToPositional()
    {
        var (statements, result) = Rewrite("select * from users where id = :id");
        statements[0].Should().Be("select * from users where id = $1");
        result.Names.Should().Equal("id");
        result.HasNamed.Should().BeTrue();
        result.HasPositional.Should().BeFalse();
    }

    [Fact]
    public void MultipleNamedParams_OrdinalsInFirstAppearanceOrder()
    {
        var (statements, result) = Rewrite("select :b, :a, :c");
        statements[0].Should().Be("select $1, $2, $3");
        result.Names.Should().Equal("b", "a", "c");
    }

    [Fact]
    public void RepeatedName_MapsToSameOrdinal()
    {
        var (statements, result) = Rewrite("select :id where :id > 0 and :other < :id");
        statements[0].Should().Be("select $1 where $1 > 0 and $2 < $1");
        result.Names.Should().Equal("id", "other");
    }

    [Fact]
    public void RepeatedName_CaseInsensitive_KeepsFirstSpelling()
    {
        var (statements, result) = Rewrite("select :UserId, :userid");
        statements[0].Should().Be("select $1, $1");
        result.Names.Should().Equal("UserId");
    }

    [Fact]
    public void SameName_AcrossStatements_SharesParameter()
    {
        var (statements, result) = Rewrite("select :id;\nupdate t set x = :val where id = :id;");
        statements[0].Should().Be("select $1");
        statements[1].Should().Contain("set x = $2 where id = $1");
        result.Names.Should().Equal("id", "val");
    }

    [Fact]
    public void DoubleColonCast_NotAParameter()
    {
        var (statements, result) = Rewrite("select '1'::int, :id::text");
        statements[0].Should().Be("select '1'::int, $1::text");
        result.Names.Should().Equal("id");
    }

    [Fact]
    public void CastOnly_NoNamedParams()
    {
        var (statements, result) = Rewrite("select a::int, b::text from t");
        statements[0].Should().Be("select a::int, b::text from t");
        result.HasNamed.Should().BeFalse();
    }

    [Fact]
    public void ColonEquals_NotAParameter()
    {
        var (statements, result) = Rewrite("select f(a := 1)");
        statements[0].Should().Be("select f(a := 1)");
        result.HasNamed.Should().BeFalse();
    }

    [Fact]
    public void NumericSliceBound_NotAParameter()
    {
        var (statements, result) = Rewrite("select arr[1:3] from t");
        statements[0].Should().Be("select arr[1:3] from t");
        result.HasNamed.Should().BeFalse();
    }

    [Fact]
    public void InsideSingleQuotedString_Untouched()
    {
        var (statements, result) = Rewrite("select ':notaparam', :real");
        statements[0].Should().Be("select ':notaparam', $1");
        result.Names.Should().Equal("real");
    }

    [Fact]
    public void InsideEscapedQuotes_Untouched()
    {
        var (statements, result) = Rewrite("select 'it''s :x here', :y");
        statements[0].Should().Be("select 'it''s :x here', $1");
        result.Names.Should().Equal("y");
    }

    [Fact]
    public void InsideDoubleQuotedIdentifier_Untouched()
    {
        var (statements, result) = Rewrite("select \"a:b\" from t where c = :c");
        statements[0].Should().Be("select \"a:b\" from t where c = $1");
        result.Names.Should().Equal("c");
    }

    [Fact]
    public void InsideDollarQuote_Untouched()
    {
        var (statements, result) = Rewrite("do $$ begin perform :x; end $$;");
        statements[0].Should().Contain("perform :x;");
        result.HasNamed.Should().BeFalse();
    }

    [Fact]
    public void InsideTaggedDollarQuote_Untouched()
    {
        var (statements, result) = Rewrite("select $tag$ :x $tag$, :y");
        statements[0].Should().Be("select $tag$ :x $tag$, $1");
        result.Names.Should().Equal("y");
    }

    [Fact]
    public void PositionalParams_Detected_NotRewritten()
    {
        var (statements, result) = Rewrite("select * from t where a = $1 and b = $2");
        statements[0].Should().Be("select * from t where a = $1 and b = $2");
        result.HasPositional.Should().BeTrue();
        result.HasNamed.Should().BeFalse();
    }

    [Fact]
    public void MixedStyles_BothFlagsSet()
    {
        var (_, result) = Rewrite("select $1, :id");
        result.HasPositional.Should().BeTrue();
        result.HasNamed.Should().BeTrue();
    }

    [Fact]
    public void UnderscoreName_Recognized()
    {
        var (statements, result) = Rewrite("select :_user_id");
        statements[0].Should().Be("select $1");
        result.Names.Should().Equal("_user_id");
    }

    [Fact]
    public void JsonbPathOperators_ColonInsideString_Untouched()
    {
        var (statements, result) = Rewrite("select data @? '$.a ? (@ > :x)' from t where id = :id");
        statements[0].Should().Be("select data @? '$.a ? (@ > :x)' from t where id = $1");
        result.Names.Should().Equal("id");
    }

    [Fact]
    public void TypeHints_NamedColonForm_ResolvedByName()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("param :user_id integer", ["email", "user_id"]);
        hints.Should().NotBeNull();
        hints![1].Should().Be("integer");
    }

    [Fact]
    public void TypeHints_TypeIsForm_ResolvedByName()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("param user_id type is integer", ["user_id"]);
        hints.Should().NotBeNull();
        hints![0].Should().Be("integer");
    }

    [Fact]
    public void TypeHints_UnknownName_Ignored()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("param :nope integer", ["user_id"]);
        hints.Should().BeNull();
    }

    [Fact]
    public void TypeHints_PositionalForm_StillWorks()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("param $1 name integer");
        hints.Should().NotBeNull();
        hints![0].Should().Be("integer");
    }
}
