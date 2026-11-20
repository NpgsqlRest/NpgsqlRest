using System.Text.Json.Nodes;
using NpgsqlRest;

namespace NpgsqlRestClient;

/// <summary>
/// The whole annotation reference of the npgsqlrest client: what core reads, and what every plugin and handler the
/// client ships reads, whether or not the configuration enables it. Printed by <c>npgsqlrest --annotations</c>, and
/// registered at startup so that the comment parser knows a disabled plugin's annotation from an unknown one.
/// </summary>
public static class AnnotationReference
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }
        _registered = true;
        CommentAnnotations.Register(NpgsqlRest.UploadHandlers.UploadAnnotations.Excel);
        CommentAnnotations.Register(TableFormatAnnotations.All);
        CommentAnnotations.Register(NpgsqlRest.TsClient.TsClientAnnotations.All);
        CommentAnnotations.Register(NpgsqlRest.DartClient.DartClientAnnotations.All);
        CommentAnnotations.Register(NpgsqlRest.Mcp.McpAnnotations.All);
        CommentAnnotations.Register(NpgsqlRest.OpenAPI.OpenApiAnnotations.All);
        CommentAnnotations.Register(Testing.TestAnnotations.All);
    }

    public static JsonArray All()
    {
        Register();
        return CommentAnnotations.ToJson();
    }
}
