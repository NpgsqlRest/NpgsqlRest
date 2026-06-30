namespace NpgsqlRestClient.Testing;

/// <summary>Kind of a parsed test step.</summary>
public enum TestStepKind
{
    /// <summary>A SQL statement (or DO block) executed on the test connection.</summary>
    Sql,
    /// <summary>An embedded HTTP request (a /* ... */ block whose first line is a request line).</summary>
    Http,
}

/// <summary>One ordered step in a parsed .test.sql file.</summary>
public abstract class TestStep
{
    public TestStepKind Kind { get; }
    /// <summary>1-based line in the source file where this step starts (for error reporting).</summary>
    public int LineNumber { get; }

    protected TestStep(TestStepKind kind, int lineNumber)
    {
        Kind = kind;
        LineNumber = lineNumber;
    }
}

/// <summary>A SQL statement step — executed via ExecuteReader; a boolean first column is an assertion.</summary>
public sealed class SqlStep : TestStep
{
    public string Text { get; }
    public bool IsDoBlock { get; }

    public SqlStep(string text, bool isDoBlock, int lineNumber) : base(TestStepKind.Sql, lineNumber)
    {
        Text = text;
        IsDoBlock = isDoBlock;
    }
}

/// <summary>An embedded single-request HTTP step parsed from a block comment.</summary>
public sealed class HttpStep : TestStep
{
    /// <summary>HTTP method (upper-cased): GET, PUT, POST, DELETE.</summary>
    public string Method { get; }
    /// <summary>Request target verbatim, including any query string. Always starts with '/'.</summary>
    public string Path { get; }
    /// <summary>Request headers in declared order (duplicates allowed).</summary>
    public IReadOnlyList<(string Name, string Value)> Headers { get; }
    /// <summary>Claims for the acting principal (from <c># @claim name=value</c>), in declared order.</summary>
    public IReadOnlyList<(string Name, string Value)> Claims { get; }
    /// <summary>Request body (verbatim), or null when none.</summary>
    public string? Body { get; }
    /// <summary>Expected status from <c># @expect-status NNN</c>, or null.</summary>
    public int? ExpectStatus { get; }
    /// <summary>Response temp-table name override from <c># @response name</c>, or null (use the configured default).</summary>
    public string? ResponseTable { get; }

    public HttpStep(
        string method,
        string path,
        IReadOnlyList<(string Name, string Value)> headers,
        IReadOnlyList<(string Name, string Value)> claims,
        string? body,
        int? expectStatus,
        string? responseTable,
        int lineNumber) : base(TestStepKind.Http, lineNumber)
    {
        Method = method;
        Path = path;
        Headers = headers;
        Claims = claims;
        Body = body;
        ExpectStatus = expectStatus;
        ResponseTable = responseTable;
    }
}
