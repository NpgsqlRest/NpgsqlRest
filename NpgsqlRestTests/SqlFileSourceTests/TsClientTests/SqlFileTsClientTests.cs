using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class SqlFileTsClientTests(SqlFileSourceTestFixture test)
{
    private string ReadGeneratedFile()
    {
        // SQL file endpoints get tsclient_module set from directory name,
        // so look for any .ts file in the output directory
        var tsFiles = Directory.GetFiles(test.TsClientDir, "*.ts");
        tsFiles.Should().NotBeEmpty($"Expected TsClient output files in {test.TsClientDir}");
        // Concatenate all files (covers both public.ts and module-specific files)
        return string.Join("\n", tsFiles.Select(File.ReadAllText));
    }

    // Single-command endpoints

    [Fact]
    public void TsClient_SingleCommand_SelectWithParam()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("export async function getById(");
        content.Should().Contain("IGetByIdRequest");
        content.Should().Contain("IGetByIdResponse[]");
    }

    [Fact]
    public void TsClient_SingleCommand_VoidDoBlock()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("export async function doBlock() : Promise<void>");
    }

    [Fact]
    public void TsClient_SingleCommand_NoParams()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("export async function getTime() : Promise<string[]>");
    }

    // Multi-command endpoints

    [Fact]
    public void TsClient_MultiCommand_SelectGeneratesInterface()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("IMultiSelectResponse");
        content.Should().Contain("Promise<IMultiSelectResponse>");
    }

    [Fact]
    public void TsClient_MultiCommand_MixedGeneratesInterface()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("IMultiMixedResponse");
        content.Should().Contain("Promise<IMultiMixedResponse>");
    }

    [Fact]
    public void TsClient_MultiCommand_AllVoidGeneratesInterface()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("IMultiAllVoidResponse");
        content.Should().Contain("Promise<IMultiAllVoidResponse>");
    }

    [Fact]
    public void TsClient_MultiCommand_FiveSelectsGeneratesInterface()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("IMultiFiveSelectsResponse");
        content.Should().Contain("Promise<IMultiFiveSelectsResponse>");
    }

    [Fact]
    public void TsClient_MultiCommand_DifferentShapesGeneratesInterface()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("IMultiDifferentShapesResponse");
        content.Should().Contain("Promise<IMultiDifferentShapesResponse>");
    }

    [Fact]
    public void TsClient_SingleRecord_ReturnsObjectNotArray()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("Promise<ITsSingleRecordResponse>");
        content.Should().NotContain("ITsSingleRecordResponse[]");
    }

    [Fact]
    public void TsClient_SingleRecordScalar_ReturnsScalarNotArray()
    {
        var content = ReadGeneratedFile();
        content.Should().Contain("tsSingleRecordScalar");
        content.Should().Contain("Promise<string>");
    }
}
