using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NpgsqlRestTests;

public class BadRequestFormatter : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = true;
    public bool RefContext { get; } = true;

    public string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters, HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        return null;
    }
}

public class SelectPathFormatter : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = true;
    public bool RefContext { get; } = true;

    public string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters, HttpContext context)
    {
        return string.Format(routine.Expression, context.Request.Path);
    }
}

public class Metadata
{
    public override string ToString() => "metadata";
}

public class MetadataFormatter : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = true;

    public string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters)
    {
        return string.Format(routine.Expression, routine.Metadata);
    }
}

public class QueryStringFormatter : IRoutineSourceParameterFormatter
{
    public bool IsFormattable { get; } = true;
    public bool RefContext { get; } = true;

    public string? FormatCommand(Routine routine, NpgsqlParameterCollection parameters, HttpContext context)
    {
        return string.Format(routine.Expression, context.Request.QueryString);
    }
}

public class TestSource : IRoutineSource
{
    public string? Query { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string? SchemaSimilarTo { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string? SchemaNotSimilarTo { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string[]? IncludeSchemas { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string[]? ExcludeSchemas { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string? NameSimilarTo { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string? NameNotSimilarTo { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string[]? IncludeNames { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string[]? ExcludeNames { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public CommentsMode? CommentsMode { get => null; set => throw new NotImplementedException(); }

    public IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read(IServiceProvider? serviceProvider, RetryStrategy? retryStrategy, ILogger? logger)
    {
        yield return (
            CreateRoutine(
                name: "test_custom_source_bad_request", 
                expression: ""),
            new BadRequestFormatter());

        yield return (
            CreateRoutine(
                name: "test_custom_source_select_path",
                expression: "select '{0}'",
                isVoid: false,
                columnCount: 1,
                columnsTypeDescriptor: [new TypeDescriptor("text")]),
            new SelectPathFormatter());

        yield return (
            CreateRoutine(
                name: "test_custom_source_metadata",
                expression: "select '{0}'",
                isVoid: false,
                columnCount: 1,
                columnsTypeDescriptor: [new TypeDescriptor("text")],
                metadata: new Metadata()),
            new MetadataFormatter());

        yield return (
            CreateRoutine(
                name: "test_custom_source_query",
                expression: "select '{0}'",
                isVoid: false,
                columnCount: 1,
                columnsTypeDescriptor: [new TypeDescriptor("text")],
                metadata: new Metadata(),
                paramNames: ["foo", "xyz"]),
            new QueryStringFormatter());
    }

    private static Routine CreateRoutine(
        string name, 
        string expression, 
        bool isVoid = default,
        int columnCount = default,
        TypeDescriptor[] columnsTypeDescriptor = default!,
        Metadata metadata = default!,
        List<string>? paramNames = null)
    {
        return new Routine
        {
            Type = default,
            Schema = "",
            Name = name,
            Comment = default,
            IsStrict = default,
            CrudType = default,
            ReturnsRecordType = default,
            ReturnsSet = default,
            ColumnCount = columnCount,
            ColumnNames = default!,
            OriginalColumnNames = default!,
            ColumnsTypeDescriptor = columnsTypeDescriptor,
            ReturnsUnnamedSet = default,
            IsVoid = isVoid,
            ParamCount = paramNames?.Count ?? 0,
            Parameters = [],
            ParamsHash = paramNames?.ToHashSet() ?? [],
            OriginalParamsHash = paramNames?.ToHashSet() ?? [],
            Expression = expression,
            FullDefinition = default!,
            SimpleDefinition = default!,
            FormatUrlPattern = default,
            Tags = default!,
            EndpointHandler = default,
            Metadata = metadata,
            Immutable = default
        };
    }
}

[Collection("TestFixture")]
public class CustomSourceTests(TestFixture test)
{
    [Fact]
    public async Task Test_test_custom_source_bad_request()
    {
        using var response = await test.Client.GetAsync("/api/test-custom-source-bad-request/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_test_custom_source_select_path()
    {
        using var response = await test.Client.GetAsync("/api/test-custom-source-select-path/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        content.Should().Be("/api/test-custom-source-select-path/");
    }

    [Fact]
    public async Task Test_test_custom_source_metadata()
    {
        using var response = await test.Client.GetAsync("/api/test-custom-source-metadata/");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        content.Should().Be("metadata");
    }

    [Fact]
    public async Task Test_test_custom_source_query()
    {
        using var response = await test.Client.GetAsync("/api/test-custom-source-query/?foo=bar&xyz=999");
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        content.Should().Be("?foo=bar&xyz=999");
    }
}