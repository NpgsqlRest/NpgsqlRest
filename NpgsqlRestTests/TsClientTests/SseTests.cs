namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientSseTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.sse_endpoint(_message text)
returns text
language plpgsql
as $$
begin
    raise notice '%', _message;
    return 'done';
end;
$$;
comment on function tsclient_test.sse_endpoint(text) is '
tsclient_module=sse_endpoint
HTTP POST
sse_events_path /api/tsclient-test/sse-endpoint/events
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class SseTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";
export const createTsclientTestSseEndpointEventSource = (id: string = "") => new EventSource(baseUrl + "/api/tsclient-test/sse-endpoint/events?" + id);

interface ITsclientTestSseEndpointRequest {
    message: string | null;
}


/**
* function tsclient_test.sse_endpoint(
*     _message text
* )
* returns text
* 
* @remarks
* comment on function tsclient_test.sse_endpoint is 'tsclient_module=sse_endpoint
* HTTP POST
* sse_events_path /api/tsclient-test/sse-endpoint/events';
* 
* @param request - Object containing request parameters.
* @param onMessage - Optional callback function to handle incoming SSE messages.
* @param id - Optional execution ID for SSE connection. When supplied, only EventSource object with this ID in query string will will receive events.
* @param closeAfterMs - Time in milliseconds to wait before closing the EventSource connection. Used only when onMessage callback is provided.
* @param awaitConnectionMs - Time in milliseconds to wait after opening the EventSource connection before sending the request. Used only when onMessage callback is provided.
* @returns {string}
* 
* @see FUNCTION tsclient_test.sse_endpoint
*/
export async function tsclientTestSseEndpoint(
    request: ITsclientTestSseEndpointRequest,
    onMessage?: (message: string) => void,
    id: string | undefined = undefined,
    closeAfterMs = 1000,
    awaitConnectionMs: number | undefined = 0
) : Promise<string> {
    const executionId = id ? id : window.crypto.randomUUID();
    let eventSource: EventSource;
    if (onMessage) {
        eventSource = createTsclientTestSseEndpointEventSource(executionId);
        eventSource.onmessage = (event: MessageEvent) => {
            onMessage(event.data);
        };
        if (awaitConnectionMs !== undefined) {
            await new Promise(resolve => setTimeout(resolve, awaitConnectionMs));
        }
    }
    try {
        const response = await fetch(baseUrl + "/api/tsclient-test/sse-endpoint", {
            method: "POST",
            headers: {
                "X-NpgsqlRest-ID": executionId
            },
            body: JSON.stringify(request)
        });
        return await response.text();
    }
    finally {
        if (onMessage) {
            setTimeout(() => eventSource.close(), closeAfterMs);
        }
    }
}


""";

        [Fact]
        public void Test_SseEndpoint_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "sse_endpoint.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
