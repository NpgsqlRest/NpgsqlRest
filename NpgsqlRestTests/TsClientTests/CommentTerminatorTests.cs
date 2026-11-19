namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void TsClientCommentTerminatorTests()
        {
            script.Append("""
create schema if not exists tsclient_test;
create function tsclient_test.comment_terminator()
returns void
language sql
as $$
select null;
$$;
comment on function tsclient_test.comment_terminator() is '
tsclient_module=comment_terminator
Ends early */ export const injected = 1; /*
';
""");
        }
    }
}

namespace NpgsqlRestTests.TsClientTests
{
    /// <summary>
    /// A "*/" in a routine comment (or, with CommentHeader Full, a /* */ comment in its body) is escaped inside the
    /// generated JSDoc block. It used to end the block early, so the rest of the line became TypeScript code.
    /// </summary>
    [Collection("TestFixture")]
    public class CommentTerminatorTests
    {
        private const string Expected = """
const baseUrl = "";


/**
* function tsclient_test.comment_terminator()
* returns void
* 
* @remarks
* comment on function tsclient_test.comment_terminator is 'tsclient_module=comment_terminator
* Ends early *\/ export const injected = 1; /*';
* 
* @returns {void}
* 
* @see FUNCTION tsclient_test.comment_terminator
*/
export async function tsclientTestCommentTerminator() : Promise<void> {
    await fetch(baseUrl + "/api/tsclient-test/comment-terminator", {
        method: "POST",
    });
}


""";

        [Fact]
        public void Test_CommentTerminator_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.TsClientOutputPath, "comment_terminator.ts");
            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
