using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NpgsqlRest.Mcp;

// AOT-safe serialization of the JSON-RPC response objects (built as System.Text.Json.Nodes).
[JsonSerializable(typeof(JsonObject))]
internal partial class McpJsonContext : JsonSerializerContext;
