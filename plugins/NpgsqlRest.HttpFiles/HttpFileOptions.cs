namespace NpgsqlRest.HttpFiles;

public class HttpFileOptions(
    HttpFileOption option = HttpFileOption.Both,
    string namePattern = "{0}_{1}",
    CommentHeader commentHeader = CommentHeader.Simple,
    bool commentHeaderIncludeComments = true,
    HttpFileMode fileMode = HttpFileMode.Database,
    bool fileOverwrite = false,
    string? connectionString = null,
    string? name = null,
    bool omitAutomaticParameters = false)
{
    public static HttpFileOptions CreateBoth() => new(option: HttpFileOption.Both);
    public static HttpFileOptions CreateFile() => new(option: HttpFileOption.File);
    public static HttpFileOptions CreateEndpoint() => new(option: HttpFileOption.Endpoint);

    public HttpFileOptions() : this(option: HttpFileOption.Both) { }

    /// <summary>
    /// Options for HTTP file generation:
    /// Disabled - skip.
    /// File - creates a file on disk.
    /// Endpoint - exposes file content as endpoint.
    /// Both - creates a file on disk and exposes file content as endpoint.
    /// </summary>
    public HttpFileOption Option { get; set; } = option;
    /// <summary>
    /// The pattern to use when generating file names. {0} is database name, {1} is schema suffix with underline when FileMode is set to Schema.
    /// Use this property to set the custom file name.
    /// .http extension will be added automatically.
    /// </summary>
    public string NamePattern { get; set; } = namePattern;
    /// <summary>
    /// Adds comment header to above request based on PostgreSQL routine
    /// Set None to skip.
    /// Set Simple (default) to add name, parameters and return values to comment header.
    /// Set Full to add the entire routine code as comment header.
    /// </summary>
    public CommentHeader CommentHeader { get; set; } = commentHeader;
    /// <summary>
    /// When CommentHeader is set to Simple or Full, set to true to include routine comments in comment header.
    /// </summary>
    public bool CommentHeaderIncludeComments { get; set; } = commentHeaderIncludeComments;
    /// <summary>
    /// Set to Database to create one http file for entire database.
    /// Set to Schema to create new http file for every database schema.
    /// </summary>
    public HttpFileMode FileMode { get; set; } = fileMode;
    /// <summary>
    /// Set to true to overwrite existing files.
    /// </summary>
    public bool FileOverwrite { get; set; } = fileOverwrite;
    /// <summary>
    /// The connection string to the database used in NpgsqlRest.
    /// Used to get the name of the database for the file name.
    /// If Name property is set, this property is ignored.
    /// </summary>
    public string? ConnectionString { get; set; } = connectionString;
    /// <summary>
    /// File name. If not set, the database name will be used if connection string is set.
    /// If neither ConnectionString nor Name is set, the file name will be "npgsqlrest".
    /// </summary>
    public string? Name { get; set; } = name;

    /// <summary>
    /// When true, parameters that are filled by the server and cannot be set by the client are omitted
    /// from the generated HTTP file's query string and request body. Covers optional automatic
    /// parameters: HTTP Custom Type fields, resolved-parameter expressions, upload metadata, and — on
    /// endpoints that use user parameters — IP-address and user-claim parameters. Default is false.
    /// </summary>
    public bool OmitAutomaticParameters { get; set; } = omitAutomaticParameters;
}
