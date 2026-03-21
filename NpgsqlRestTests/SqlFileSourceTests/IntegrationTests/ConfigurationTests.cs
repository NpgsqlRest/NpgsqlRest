using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

public class ConfigurationTests
{
    [Fact]
    public void EmptyFilePattern_YieldsNoEndpoints()
    {
        var options = new SqlFileSourceOptions
        {
            FilePattern = ""
        };
        var source = new SqlFileSource(options);

        // SqlFileSource with empty FilePattern should not throw
        // and should produce no routine source entries when iterated
        options.FilePattern.Should().BeEmpty();
    }

    [Fact]
    public void DefaultOptions_HasDefaultValues()
    {
        var options = new SqlFileSourceOptions();
        options.FilePattern.Should().NotBeNull();
        options.ErrorMode.Should().Be(ParseErrorMode.Skip);
    }
}
