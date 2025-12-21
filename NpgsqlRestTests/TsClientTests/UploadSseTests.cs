namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientUploadSseTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.upload_with_sse(_meta json = null)
returns json
language plpgsql
as $$
begin
    raise notice 'Processing upload...';
    return _meta;
end;
$$;
comment on function tsclient_test.upload_with_sse(json) is '
tsclient_module=upload_with_sse
upload for file_system
param _meta is upload metadata
sse_events_path /api/tsclient-test/upload-with-sse/events
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class UploadSseTests(TestFixture test)
    {
        private const string Expected = """
const baseUrl = "";
const parseQuery = (query: Record<any, any>) => "?" + Object.keys(query ? query : {})
    .map(key => {
        const value = (query[key] != null ? query[key] : "") as string;
        if (Array.isArray(value)) {
            return value.map((s: string) => s ? `${key}=${encodeURIComponent(s)}` : `${key}=`).join("&");
        }
        return `${key}=${encodeURIComponent(value)}`;
    })
    .join("&");
export const createTsclientTestUploadWithSseEventSource = (id: string = "") => new EventSource(baseUrl + "/api/tsclient-test/upload-with-sse/events?" + id);

interface ITsclientTestUploadWithSseRequest {
    meta?: string | null;
}

interface ITsclientTestUploadWithSseResponse {
      type: string;
      fileName: string;
      contentType: string;
      size: number;
      success: boolean;
      status: string;
      [key: string]: string | number | boolean;
}


/**
* function tsclient_test.upload_with_sse(
*     _meta json DEFAULT NULL::json
* )
* returns json
* 
* @remarks
* comment on function tsclient_test.upload_with_sse is 'tsclient_module=upload_with_sse
* upload for file_system
* param _meta is upload metadata
* sse_events_path /api/tsclient-test/upload-with-sse/events';
* 
* @param request - Object containing request parameters.
* @param onMessage - Optional callback function to handle incoming SSE messages.
* @param id - Optional execution ID for SSE connection. When supplied, only EventSource object with this ID in query string will will receive events.
* @param closeAfterMs - Time in milliseconds to wait before closing the EventSource connection. Used only when onMessage callback is provided.
* @param awaitConnectionMs - Time in milliseconds to wait after opening the EventSource connection before sending the request. Used only when onMessage callback is provided.
* @returns {ITsclientTestUploadWithSseResponse[]}
* 
* @see FUNCTION tsclient_test.upload_with_sse
*/
export async function tsclientTestUploadWithSse(
    files: FileList | null,
    request: ITsclientTestUploadWithSseRequest,
    onMessage?: (message: string) => void,
    id: string | undefined = undefined,
    closeAfterMs = 1000,
    awaitConnectionMs: number | undefined = 0,
    progress?: (loaded: number, total: number) => void,
): Promise<ITsclientTestUploadWithSseResponse[]> {
    return new Promise((resolve, reject) => {
        if (!files || files.length === 0) {
            reject(new Error("No files to upload"));
            return;
        }
        var xhr = new XMLHttpRequest();
        const executionId = id ? id : window.crypto.randomUUID();
        if (progress) {
            xhr.upload.addEventListener(
                "progress",
                (event) => {
                    if (event.lengthComputable && progress) {
                        progress(event.loaded, event.total);
                    }
                },
                false
            );
        }
        xhr.onload = function () {
            if (this.status >= 200 && this.status < 300) {
                resolve();
            } else {
                resolve({status: this.status, response: undefined!, error: JSON.parse(this.responseText) as {{status: number; title: string; detail?: string | null}}});
            }
        };
        xhr.onerror = function () {
            reject({
                xhr: this, 
                status: this.status,
                statusText: this.statusText || 'Network error occurred',
                response: this.response
            });
        };
        xhr.open("POST", baseUrl + "/api/tsclient-test/upload-with-sse" + parseQuery(request));
        xhr.setRequestHeader("X-NpgsqlRest-ID", executionId);
        const formData = new FormData();
        for(let i = 0; i < files.length; i++) {
            const file = files[i];
            formData.append("file", file, file.name);
        }
        let eventSource: EventSource;
        if (onMessage) {
            eventSource = createTsclientTestUploadWithSseEventSource(executionId);
            eventSource.onmessage = (event: MessageEvent) => {
                onMessage(event.data);
            };
            if (awaitConnectionMs !== undefined) {
                await new Promise(resolve => setTimeout(resolve, awaitConnectionMs));
            }
        }
        try {
            xhr.send(formData);
        }
        finally {
            if (onMessage) {
                setTimeout(() => eventSource.close(), closeAfterMs);
            }
        }
    });
}


""";

        [Fact]
        public void Test_UploadWithSse_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "upload_with_sse.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
