namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientUploadTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.upload_file(_meta json = null)
returns json
language plpgsql
as $$
begin
    return _meta;
end;
$$;
comment on function tsclient_test.upload_file(json) is '
tsclient_module=upload_file
upload for file_system
param _meta is upload metadata
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    [Collection("TestFixture")]
    public class UploadTests
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

interface ITsclientTestUploadFileRequest {
    meta?: string | null;
}

interface ITsclientTestUploadFileResponse {
    type: string;
    fileName: string;
    contentType: string;
    size: number;
    success: boolean;
    status: string;
    [key: string]: string | number | boolean;
}


/**
* function tsclient_test.upload_file(
*     _meta json DEFAULT NULL::json
* )
* returns json
* 
* @remarks
* comment on function tsclient_test.upload_file is 'tsclient_module=upload_file
* upload for file_system
* param _meta is upload metadata';
* 
* @param request - Object containing request parameters.
* @returns {ITsclientTestUploadFileResponse[]}
* 
* @see FUNCTION tsclient_test.upload_file
*/
export async function tsclientTestUploadFile(
    files: FileList | null,
    request: ITsclientTestUploadFileRequest,
    progress?: (loaded: number, total: number) => void,
): Promise<ITsclientTestUploadFileResponse[]> {
    return new Promise((resolve, reject) => {
        if (!files || files.length === 0) {
            reject(new Error("No files to upload"));
            return;
        }
        var xhr = new XMLHttpRequest();
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
                resolve(JSON.parse(this.responseText) as ITsclientTestUploadFileResponse[]);
            } else {
                throw new Error(this.responseText);
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
        xhr.open("POST", baseUrl + "/api/tsclient-test/upload-file" + parseQuery(request));
        const formData = new FormData();
        for(let i = 0; i < files.length; i++) {
            const file = files[i];
            formData.append("file", file, file.name);
        }
        xhr.send(formData);
    });
}


""";

        [Fact]
        public void Test_UploadFile_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "upload_file.ts");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
