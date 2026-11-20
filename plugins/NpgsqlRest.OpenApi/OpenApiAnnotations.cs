namespace NpgsqlRest.OpenAPI;

/// <summary>The comment annotations the OpenAPI plugin reads, as annotation reference data.</summary>
public static class OpenApiAnnotations
{
    public static CommentAnnotation[] All =>
    [
        new()
        {
            Name = "openapi",
            Aliases = ["openapi"],
            Syntax = "openapi [hide|hidden|ignore] | openapi tag <name> [, <name> ...]",
            Description = "Control this endpoint in the OpenAPI document: hide it (also what a bare openapi does; the endpoint itself is unaffected), or replace its default tag, the schema name, with one or more tags. A tag is one word.",
            Owner = "openapi",
            Example = "openapi tag partners"
        },
    ];
}
