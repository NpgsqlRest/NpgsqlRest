using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("SqlFileSourceFixture")]
public class SqlFileTsClientTests(SqlFileSourceTestFixture test)
{
    private string ReadGeneratedFile()
    {
        var tsFile = Path.Combine(test.TsClientDir, "public.ts");
        File.Exists(tsFile).Should().BeTrue($"Expected TsClient output at {tsFile}");
        return File.ReadAllText(tsFile);
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
        content.Should().Contain("export async function getTime() : Promise<IGetTimeResponse[]>");
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
}
