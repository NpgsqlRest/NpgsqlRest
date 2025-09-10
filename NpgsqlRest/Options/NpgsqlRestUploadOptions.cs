using NpgsqlRest.UploadHandlers;
using NpgsqlRest.UploadHandlers.Handlers;

namespace NpgsqlRest;

/// <summary>
/// Upload options for the NpgsqlRest middleware.
/// </summary>
public class NpgsqlRestUploadOptions
{
    /// <summary>
    /// Enables upload functionality.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// When true, logs upload events.
    /// </summary>
    public bool LogUploadEvent { get; set; } = true;
    
    /// <summary>
    /// Log upload parameter values.
    /// </summary>
    public bool LogUploadParameters { get; set; } = false;

    /// <summary>
    /// Default upload handler name. This value is used when the upload handlers are not specified.
    /// </summary>
    public string DefaultUploadHandler { get; set; } = "large_object";

    /// <summary>
    /// Default upload handler options. 
    /// Set this option to null to disable upload handlers or use this to modify upload handler options.
    /// </summary>
    public UploadHandlerOptions DefaultUploadHandlerOptions { get; set; } = new UploadHandlerOptions();

    /// <summary>
    /// Upload handlers dictionary map. 
    /// When the endpoint has set Upload to true, this dictionary will be used to find the upload handlers for the current request. 
    /// Handler will be located by the key values from the endpoint UploadHandlers string array property if set or by the default upload handler (DefaultUploadHandler option).
    /// Set this option to null to use default upload handler from the UploadHandlerOptions property.
    /// Set this option to empty dictionary to disable upload handlers.
    /// Set this option to a dictionary with one or more upload handlers to enable your own custom upload handlers.
    /// </summary>
    public Dictionary<string, Func<RetryStrategy?, ILogger?, IUploadHandler>>? UploadHandlers { get; set; } = null;

    /// <summary>
    /// Gets or sets a value indicating whether the default upload metadata parameter should be used.
    /// </summary>
    public bool UseDefaultUploadMetadataParameter { get; set; } = false;

    /// <summary>
    /// Name of the default upload metadata parameter. 
    /// This parameter will be automatically assigned with the upload metadata JSON string when the upload is completed if UseDefaultUploadMetadataParameter is set to true.
    /// </summary>
    public string DefaultUploadMetadataParameterName { get; set; } = "_upload_metadata";

    /// <summary>
    /// Gets or sets a value indicating whether the default upload metadata context key should be used.
    /// </summary>
    public bool UseDefaultUploadMetadataContextKey { get; set; } = false;

    /// <summary>
    /// Name of the default upload metadata context key.
    /// This context key will be automatically assigned to context with the upload metadata JSON string when the upload is completed if UseDefaultUploadMetadataContextKey is set to true.
    /// </summary>
    public string DefaultUploadMetadataContextKey { get; set; } = "request.upload_metadata";
}
