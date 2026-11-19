using System.Net.Http.Headers;
using NpgsqlRest.UploadHandlers.Handlers;

namespace NpgsqlRestTests;

/// <summary>
/// Security regression: file system uploads must never write outside the upload directory - not through the client's
/// multipart file name, and not through "path" / "file" values templated from request parameters.
/// Uses fs_custom_parameter_upload and fs_upload1 from FileSystemUploadTests.
/// </summary>
[Collection("TestFixture")]
public class FileSystemUploadTraversalTests(TestFixture test)
{
    private static MultipartFormDataContent Form(string fileName, string content)
    {
        var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(bytes, "file", fileName);
        return form;
    }

    private static (string status, bool success, string? filePath) Result(string json)
    {
        var item = JsonDocument.Parse(json).RootElement[0];
        var filePath = item.GetProperty("filePath");
        return (item.GetProperty("status").GetString()!, item.GetProperty("success").GetBoolean(),
            filePath.ValueKind == JsonValueKind.Null ? null : filePath.GetString());
    }

    [Fact]
    public async Task Templated_file_name_with_parent_segments_is_rejected()
    {
        var escaped = Path.GetFullPath("./escaped-by-file-param.csv");
        File.Delete(escaped);
        using var form = Form("data.csv", "a,b\n1,2\n");
        var query = new QueryBuilder { { "path", "./test" }, { "file", "../escaped-by-file-param.csv" }, { "uniqueName", "false" }, { "createPath", "true" } };

        using var response = await test.Client.PostAsync($"/api/fs-custom-parameter-upload/{query}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Result(await response.Content.ReadAsStringAsync()).Should().Be(("InvalidFileName", false, (string?)null));
        File.Exists(escaped).Should().BeFalse();
    }

    [Fact]
    public async Task Templated_absolute_file_name_is_rejected()
    {
        var target = Path.Combine(Path.GetTempPath(), "npgsqlrest-absolute-upload.csv");
        File.Delete(target);
        using var form = Form("data.csv", "a,b\n1,2\n");
        var query = new QueryBuilder { { "path", "./test" }, { "file", target }, { "uniqueName", "false" }, { "createPath", "true" } };

        using var response = await test.Client.PostAsync($"/api/fs-custom-parameter-upload/{query}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Result(await response.Content.ReadAsStringAsync()).Should().Be(("InvalidFileName", false, (string?)null));
        File.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task Templated_upload_path_with_parent_segments_is_rejected()
    {
        var outside = Path.GetFullPath("../npgsqlrest-upload-outside");
        if (Directory.Exists(outside))
        {
            Directory.Delete(outside, recursive: true);
        }
        using var form = Form("data.csv", "a,b\n1,2\n");
        var query = new QueryBuilder { { "path", "../npgsqlrest-upload-outside" }, { "file", "data.csv" }, { "uniqueName", "false" }, { "createPath", "true" } };

        using var response = await test.Client.PostAsync($"/api/fs-custom-parameter-upload/{query}", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Result(await response.Content.ReadAsStringAsync()).Should().Be(("InvalidFileName", false, (string?)null));
        Directory.Exists(outside).Should().BeFalse();
    }

    [Fact]
    public async Task Client_file_name_is_reduced_to_its_last_segment()
    {
        var escaped = Path.GetFullPath("../../client-escape-test.csv");
        File.Delete(escaped);
        File.Delete("./client-escape-test.csv");
        using var form = Form("../../client-escape-test.csv", "a,b\n1,2\n");

        using var response = await test.Client.PostAsync("/api/fs-upload1/", form);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var (status, success, filePath) = Result(await response.Content.ReadAsStringAsync());
        (status, success).Should().Be(("Ok", true));
        filePath.Should().BeOneOf("./client-escape-test.csv", ".\\client-escape-test.csv");
        File.Exists("./client-escape-test.csv").Should().BeTrue();
        File.Exists(escaped).Should().BeFalse();
        File.Delete("./client-escape-test.csv");
    }

    [Theory]
    [InlineData(null, "report.csv", false, "uploads/report.csv")]
    [InlineData(null, "../../etc/passwd", false, "uploads/passwd")]
    [InlineData(null, "..\\..\\windows\\win.ini", false, "uploads/win.ini")]
    [InlineData(null, "C:\\Users\\me\\report.csv", false, "uploads/report.csv")]
    [InlineData(null, "..", false, null)]
    [InlineData(null, "", false, null)]
    [InlineData("sub/report.csv", "x.csv", false, "uploads/sub/report.csv")]
    [InlineData("../report.csv", "x.csv", false, null)]
    [InlineData("sub/../../report.csv", "x.csv", false, null)]
    [InlineData("/etc/report.csv", "x.csv", false, null)]
    public void Resolves_only_paths_inside_the_upload_directory(string? configured, string client, bool unique, string? expected)
    {
        var resolved = FileSystemUploadHandler.ResolveSafeFilePath("uploads", configured, client, unique);
        resolved?.Replace('\\', '/').Should().Be(expected);
        if (expected is null)
        {
            resolved.Should().BeNull();
        }
    }

    [Fact]
    public void Unique_name_keeps_only_the_extension()
    {
        var resolved = FileSystemUploadHandler.ResolveSafeFilePath("uploads", null, "../../evil.csv", useUniqueFileName: true);
        resolved.Should().NotBeNull();
        Path.GetDirectoryName(resolved).Should().Be("uploads");
        Path.GetExtension(resolved).Should().Be(".csv");
    }
}
