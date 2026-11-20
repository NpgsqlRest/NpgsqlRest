using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CommentParserFixesTests()
    {
        script.Append("""
        create schema if not exists cmt;

        create function cmt.key_case() returns text language sql as $$ select 'x' $$;
        comment on function cmt.key_case() is 'HTTP GET
        TsClient_Module = Users
        @ROW_COMMAND = select 1
        @Cmt_Key = known whatever its case';

        create function cmt.escapes() returns text language sql as $$ select 'x' $$;
        comment on function cmt.escapes() is 'HTTP GET
        @file_system_path = C:\uploads\new
        separator = \t
        @cmt_key = a\\b';

        create function cmt.switches() returns text language sql as $$ select 'x' $$;
        comment on function cmt.switches() is 'HTTP GET
        raw = ON
        single = 1
        user_context = enabled
        user_parameters = Enable
        columns = off
        void = 0
        encrypt = disabled';

        create function cmt.bad_values() returns text language sql as $$ select 'x' $$;
        comment on function cmt.bad_values() is 'HTTP GET
        raw = maybe
        buffer_rows = many
        timeout = soon
        sse_level = loud
        retry_strategy = no_such_strategy
        single = 2';

        create function cmt.basic_auth_user() returns text language sql as $$ select 'x' $$;
        comment on function cmt.basic_auth_user() is 'HTTP GET
        basic_auth = alice secret';

        create function cmt.basic_auth_on() returns text language sql as $$ select 'x' $$;
        comment on function cmt.basic_auth_on() is 'HTTP GET
        @basic_auth = true';

        create function cmt.resolved(_token text) returns text language sql as $$ select _token $$;
        comment on function cmt.resolved(text) is 'HTTP GET
        @_token = select ''resolved''';

        create function cmt.keyword_as_key() returns text language sql as $$ select 'x' $$;
        comment on function cmt.keyword_as_key() is 'HTTP GET
        @authorize = admin
        @cached = true';

        -- a volatile function: "for immutable" does not match, "for volatile" does
        create function cmt.plugin_tag_miss() returns text language sql as $$ select 'x' $$;
        comment on function cmt.plugin_tag_miss() is 'HTTP GET
        for immutable
        cmtplugin
        authorize';

        create function cmt.upload_named(_meta json = null) returns text language sql as $$ select 'x' $$;
        comment on function cmt.upload_named(json) is 'HTTP POST
        upload for csv, lo, no_such_handler
        @Csv_delimiters = ;
        @lo_check_text = true';

        create function cmt.plugin_tag_hit() returns text language sql as $$ select 'x' $$;
        comment on function cmt.plugin_tag_hit() is 'HTTP GET
        for volatile
        @cmtplugin
        authorize';
        """);
    }
}

/// <summary>
/// Fixes in how a comment's "key = value" lines are read (4.0.0): keys in any case, values that are not valid
/// escapes, switch values, values that are not valid, basic_auth, the @ on a resolved parameter, and plugin lines
/// under a tag.
/// </summary>
[Collection("UnhandledCommentLinesFixture")]
public class CommentParserFixesTests(UnhandledCommentLinesTestFixture test)
{
    [Fact]
    public void Custom_parameter_keys_are_found_in_any_case()
    {
        var e = test.Endpoints["key_case"];
        // stored as written ...
        e.CustomParameters!.Keys.Should().BeEquivalentTo(["TsClient_Module", "ROW_COMMAND", "Cmt_Key"]);
        // ... and found by the lower case names that readers (client generators, upload handlers) ask for
        e.CustomParameters["tsclient_module"].Should().Be("Users");
        e.CustomParameters.TryGetValue("row_command", out var rowCommand).Should().BeTrue();
        rowCommand.Should().Be("select 1");
        e.CustomParameters.ContainsKey("cmt_key").Should().BeTrue();
        e.UnknownAnnotationLines.Should().BeNull();
        e.Diagnostics.Should().BeNull();
    }

    [Fact]
    public void Value_that_is_not_a_valid_escape_sequence_is_taken_as_written()
    {
        var e = test.Endpoints["escapes"];
        // \u is an incomplete escape: this used to throw while the endpoints were built
        e.CustomParameters!["file_system_path"].Should().Be(@"C:\uploads\new");
        // valid escapes still work
        e.RawValueSeparator.Should().Be("\t");
        e.CustomParameters["cmt_key"].Should().Be(@"a\b");
    }

    [Fact]
    public void Switches_accept_on_off_enabled_disabled_1_and_0()
    {
        var e = test.Endpoints["switches"];
        e.Raw.Should().BeTrue();
        e.ReturnSingleRecord.Should().BeTrue();
        e.UserContext.Should().BeTrue();
        e.UseUserParameters.Should().BeTrue();
        e.RawColumnNames.Should().BeFalse();
        e.Void.Should().BeFalse();
        e.EncryptAllParameters.Should().BeFalse();
        e.Diagnostics.Should().BeNull();
    }

    [Fact]
    public void Value_that_is_not_valid_is_ignored_and_said()
    {
        var e = test.Endpoints["bad_values"];
        e.Raw.Should().BeFalse();
        e.BufferRows.Should().BeNull();
        e.CommandTimeout.Should().BeNull();
        e.SseEventNoticeLevel.Should().BeNull();
        // (RetryStrategy keeps the default strategy; the unknown name is only said)
        e.ReturnSingleRecord.Should().BeFalse();
        e.Diagnostics.Should().HaveCount(6);
        e.Diagnostics![0].Should().EndWith("raw = maybe - the value is not valid and is ignored. Expected: true or false.");
        e.Diagnostics[1].Should().EndWith("buffer_rows = many - the value is not valid and is ignored. Expected: a whole number, 0 or more.");
        e.Diagnostics[2].Should().EndWith("timeout = soon - the value is not valid and is ignored. Expected: an interval, such as 30s or 5 minutes.");
        e.Diagnostics[3].Should().EndWith("sse_level = loud - the value is not valid and is ignored. Expected: INFO, NOTICE, WARNING.");
        e.Diagnostics[4].Should().Contain("no_such_strategy");
        e.Diagnostics[5].Should().EndWith("single = 2 - the value is not valid and is ignored. Expected: true or false.");
    }

    [Fact]
    public void Basic_auth_key_value_form_adds_the_user()
    {
        // the words were read one position too far, so this form never added a user
        var e = test.Endpoints["basic_auth_user"];
        e.BasicAuth.Should().NotBeNull();
        e.BasicAuth!.Enabled.Should().BeTrue();
        e.BasicAuth.Users.Should().Equal(new Dictionary<string, string> { ["alice"] = "secret" });

        var on = test.Endpoints["basic_auth_on"];
        on.BasicAuth!.Enabled.Should().BeTrue();
        on.BasicAuth.Users.Should().BeEmpty();
    }

    [Fact]
    public void Resolved_parameter_may_be_written_with_the_prefix()
    {
        var e = test.Endpoints["resolved"];
        e.ResolvedParameterExpressions.Should().Equal(new Dictionary<string, string> { ["_token"] = "select 'resolved'" });
        e.CustomParameters.Should().BeNull();
        e.UnknownAnnotationLines.Should().BeNull();
    }

    [Fact]
    public void Keyword_written_as_a_key_is_reported()
    {
        // "authorize" and "cached" are keywords; as "key = value" they are stored and read by nothing
        var e = test.Endpoints["keyword_as_key"];
        e.RequiresAuthorization.Should().BeFalse();
        e.Cached.Should().BeFalse();
        e.UnknownAnnotationLines.Should().Equal("@authorize = admin", "@cached = true");
        e.Diagnostics.Should().HaveCount(2);
    }

    [Fact]
    public void Upload_handlers_are_named_in_any_case_and_their_prefixed_parameters_are_known()
    {
        // the fixture configures the handler keys "Csv" and "LO"; "upload for csv, lo" used to find neither
        var e = test.Endpoints["upload_named"];
        e.Upload.Should().BeTrue();
        e.UploadHandlers.Should().Equal("Csv", "LO");
        e.CustomParameters!["csv_delimiters"].Should().Be(";");
        e.CustomParameters["LO_check_text"].Should().Be("true");
        // parameters prefixed with a configured (not default) handler name are known
        e.UnknownAnnotationLines.Should().BeNull();
        // the one warning is about the handler that does not exist
        e.Diagnostics.Should().ContainSingle().Which.Should().Contain("upload handler no_such_handler doesn't exists");
    }

    [Fact]
    public void Plugin_annotation_applies_only_under_a_matching_tag()
    {
        var miss = test.Endpoints["plugin_tag_miss"];
        miss.Items.ContainsKey("cmtplugin").Should().BeFalse();
        miss.RequiresAuthorization.Should().BeFalse();

        var hit = test.Endpoints["plugin_tag_hit"];
        hit.Items["cmtplugin"].Should().Be(true);
        hit.RequiresAuthorization.Should().BeTrue();
    }

    [Theory]
    [InlineData("true", true)] [InlineData("TRUE", true)] [InlineData("on", true)] [InlineData("On", true)]
    [InlineData("enabled", true)] [InlineData("enable", true)] [InlineData("1", true)] [InlineData(" true ", true)]
    [InlineData("false", false)] [InlineData("off", false)] [InlineData("OFF", false)]
    [InlineData("disabled", false)] [InlineData("disable", false)] [InlineData("0", false)]
    public void Switch_values(string value, bool expected)
    {
        Ext.TryParseSwitch(value, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")] [InlineData("yes")] [InlineData("no")] [InlineData("2")] [InlineData("truthy")] [InlineData(null)]
    public void Not_a_switch_value(string? value)
    {
        Ext.TryParseSwitch(value, out _).Should().BeFalse();
    }
}
