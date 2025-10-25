namespace NpgsqlRest;

public interface IRoutineSource
{
    /// <summary>
    /// Comments mode for the current routine source.
    /// </summary>
    CommentsMode? CommentsMode { get; set; }

    /// <summary>
    /// Yield all routines with the formatters from the current source.
    /// </summary>
    /// <param name="serviceProvider">Service provider</param>
    /// <param name="retryStrategy"></param>
    /// <returns></returns>
    IEnumerable<(Routine, IRoutineSourceParameterFormatter)> Read( IServiceProvider? serviceProvider, RetryStrategy? retryStrategy);

    /// <summary>
    /// SQL Query that returns data source.
    /// When it doesn't contain any blanks, it is interpreted as a function name.
    /// Set to NULL to use default source query.
    /// </summary>
    string? Query { get; set; }
    
    /// <summary>
    /// Query parameter
    /// </summary>
    string? SchemaSimilarTo { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string? SchemaNotSimilarTo { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string[]? IncludeSchemas { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string[]? ExcludeSchemas { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string? NameSimilarTo { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string? NameNotSimilarTo { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string[]? IncludeNames { get; set; }

    /// <summary>
    /// Query parameter
    /// </summary>
    string[]? ExcludeNames { get; set; }
}
