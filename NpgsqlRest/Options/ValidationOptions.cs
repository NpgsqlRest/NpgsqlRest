namespace NpgsqlRest;

/// <summary>
/// Options for parameter validation rules that can be referenced in comment annotations.
/// </summary>
public class ValidationOptions
{
    /// <summary>
    /// Named validation rules that can be referenced in comment annotations using "validate _param using rule_name" syntax.
    /// </summary>
    public Dictionary<string, ValidationRule> Rules { get; set; } = new()
    {
        ["not_null"] = new ValidationRule
        {
            Type = ValidationType.NotNull,
            Message = "Parameter '{0}' cannot be null"
        },
        ["not_empty"] = new ValidationRule
        {
            Type = ValidationType.NotEmpty,
            Message = "Parameter '{0}' cannot be empty"
        },
        ["required"] = new ValidationRule
        {
            Type = ValidationType.Required,
            Message = "Parameter '{0}' is required"
        },
        ["email"] = new ValidationRule
        {
            Type = ValidationType.Regex,
            Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            Message = "Parameter '{0}' must be a valid email address"
        }
    };
}

/// <summary>
/// Defines a validation rule that can be applied to endpoint parameters.
/// </summary>
public class ValidationRule
{
    /// <summary>
    /// The type of validation to perform.
    /// </summary>
    public ValidationType Type { get; set; }

    /// <summary>
    /// Regular expression pattern for Regex validation type.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Minimum length for MinLength validation type.
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Maximum length for MaxLength validation type.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Error message returned when validation fails.
    /// Supports the following placeholders:
    /// <list type="bullet">
    ///   <item><description>{0} - Original PostgreSQL parameter name (e.g., "_email")</description></item>
    ///   <item><description>{1} - Converted parameter name (e.g., "email")</description></item>
    ///   <item><description>{2} - Validation rule name (e.g., "not_empty")</description></item>
    /// </list>
    /// Example: "Parameter '{1}' failed validation rule '{2}'" produces "Parameter 'email' failed validation rule 'not_empty'"
    /// </summary>
    public string Message { get; set; } = "Validation failed for parameter '{0}'";

    /// <summary>
    /// HTTP status code to return when validation fails. Default is 400 (Bad Request).
    /// </summary>
    public int StatusCode { get; set; } = 400;

    public override string ToString()
    {
        return Type switch
        {
            ValidationType.NotNull => $"NotNull (StatusCode: {StatusCode})",
            ValidationType.NotEmpty => $"NotEmpty (StatusCode: {StatusCode})",
            ValidationType.Required => $"Required (StatusCode: {StatusCode})",
            ValidationType.Regex => $"Regex: {Pattern} (StatusCode: {StatusCode})",
            ValidationType.MinLength => $"MinLength: {MinLength} (StatusCode: {StatusCode})",
            ValidationType.MaxLength => $"MaxLength: {MaxLength} (StatusCode: {StatusCode})",
            _ => $"Unknown (StatusCode: {StatusCode})"
        };
    }
}

/// <summary>
/// Types of validation that can be performed on parameters.
/// </summary>
public enum ValidationType
{
    /// <summary>
    /// Parameter value cannot be null (DBNull.Value).
    /// </summary>
    NotNull,

    /// <summary>
    /// Parameter value cannot be an empty string.
    /// </summary>
    NotEmpty,

    /// <summary>
    /// Parameter value cannot be null or empty string. Combines NotNull and NotEmpty.
    /// </summary>
    Required,

    /// <summary>
    /// Parameter value must match the specified regular expression pattern.
    /// </summary>
    Regex,

    /// <summary>
    /// Parameter value must have at least MinLength characters.
    /// </summary>
    MinLength,

    /// <summary>
    /// Parameter value must have at most MaxLength characters.
    /// </summary>
    MaxLength
}
