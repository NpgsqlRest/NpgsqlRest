using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class ParamTypeHintTests
{
    [Fact]
    public void SimpleParamType_ExtractsHint()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name text");
        hints.Should().NotBeNull();
        hints![0].Should().Be("text");
    }

    [Fact]
    public void MultipleParams_ExtractsAll()
    {
        var comment = """
            @param $1 message_text text
            @param $2 user_id integer
            @param $3 active boolean
            """;
        var hints = SqlFileParser.ExtractParamTypeHints(comment);
        hints.Should().NotBeNull();
        hints!.Should().HaveCount(3);
        hints[0].Should().Be("text");
        hints[1].Should().Be("integer");
        hints[2].Should().Be("boolean");
    }

    [Fact]
    public void IsStyle_ExtractsHint()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 is user_id integer");
        hints.Should().NotBeNull();
        hints![0].Should().Be("integer");
    }

    [Fact]
    public void NoType_ReturnsNull()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name");
        hints.Should().BeNull();
    }

    [Fact]
    public void DefaultAfterName_NotTreatedAsType()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name default null");
        hints.Should().BeNull();
    }

    [Fact]
    public void EqualsAfterName_NotTreatedAsType()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name = null");
        hints.Should().BeNull();
    }

    [Fact]
    public void TypeWithDefault_ExtractsType()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name text default null");
        hints.Should().NotBeNull();
        hints![0].Should().Be("text");
    }

    [Fact]
    public void TypeWithEquals_ExtractsType()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name text = null");
        hints.Should().NotBeNull();
        hints![0].Should().Be("text");
    }

    [Fact]
    public void NullComment_ReturnsNull()
    {
        var hints = SqlFileParser.ExtractParamTypeHints(null);
        hints.Should().BeNull();
    }

    [Fact]
    public void EmptyComment_ReturnsNull()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("");
        hints.Should().BeNull();
    }

    [Fact]
    public void NonPositionalParam_Ignored()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param user_name default null");
        hints.Should().BeNull();
    }

    [Fact]
    public void UnknownType_Ignored()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@param $1 name somefaketype");
        hints.Should().BeNull();
    }

    [Fact]
    public void ParameterLongForm_Works()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("@parameter $1 name integer");
        hints.Should().NotBeNull();
        hints![0].Should().Be("integer");
    }

    [Fact]
    public void MixedWithAndWithoutTypes_OnlyExtractsTyped()
    {
        var comment = """
            @param $1 message_text text
            @param $2 user_id
            @param $3 active boolean
            """;
        var hints = SqlFileParser.ExtractParamTypeHints(comment);
        hints.Should().NotBeNull();
        hints!.Should().HaveCount(2);
        hints[0].Should().Be("text");
        hints[2].Should().Be("boolean");
    }

    [Fact]
    public void WithAtPrefix_Works()
    {
        var hints = SqlFileParser.ExtractParamTypeHints("param $1 name text");
        hints.Should().NotBeNull();
        hints![0].Should().Be("text");
    }

    [Fact]
    public void TypeDescriptor_Jsonb_IsJson()
    {
        var td = new NpgsqlRest.TypeDescriptor("jsonb");
        td.IsJson.Should().BeTrue();
    }

    [Fact]
    public void TypeDescriptor_Json_IsJson()
    {
        var td = new NpgsqlRest.TypeDescriptor("json");
        td.IsJson.Should().BeTrue();
    }

    [Theory]
    [InlineData("@param $1 type is integer", "integer")]
    [InlineData("param $1 TYPE IS uuid;", "uuid")]
    [InlineData("@param $1 type is integer default 5", "integer")]
    public void Positional_retype_without_rename_gives_a_hint(string comment, string type)
    {
        var hints = SqlFileParser.ExtractParamTypeHints(comment);
        hints.Should().NotBeNull();
        hints![0].Should().Be(type);
    }

    [Theory]
    [InlineData("@param $1 type is")]
    [InlineData("@param $1 type is not_a_type")]
    [InlineData("@param $0 type is integer")]
    public void Positional_retype_without_a_usable_type_gives_no_hint(string comment)
    {
        SqlFileParser.ExtractParamTypeHints(comment).Should().BeNull();
    }
}
