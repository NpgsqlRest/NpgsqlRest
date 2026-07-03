using Npgsql;
using NpgsqlRest;
using NpgsqlRestClient;

namespace NpgsqlRestTests;

[Collection("TestFixture")]
public class WatchDbFingerprintTests
{
    [Fact]
    public void Fingerprint_TracksDiscoveryResult_Exactly()
    {
        using var conn = Database.CreateConnection();
        conn.Open();
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }
        // The fingerprint is the routine discovery query itself, hashed server-side — so it tracks the
        // discovered endpoints by definition, with the source's configured filters applied.
        var source = new RoutineSource(nameSimilarTo: "watch\\_fp\\_%");
        string Fp() => WatchDbPoller.GetFingerprint(conn, source)!;

        Exec("drop function if exists watch_fp_test_fn(int)");
        Exec("drop table if exists watch_fp_typed cascade");
        var fp0 = Fp();
        fp0.Should().NotBeNullOrEmpty();

        // create function → changes
        Exec("create function watch_fp_test_fn(a int) returns int language sql as 'select a'");
        var fp1 = Fp();
        fp1.Should().NotBe(fp0, "creating a function must change the fingerprint");

        // replace body → changes (the result includes pg_get_functiondef)
        Exec("create or replace function watch_fp_test_fn(a int) returns int language sql as 'select a + 1'");
        var fp2 = Fp();
        fp2.Should().NotBe(fp1, "replacing a function body must change the fingerprint");

        // comment = annotations → changes
        Exec("comment on function watch_fp_test_fn(int) is 'HTTP GET'");
        var fp3 = Fp();
        fp3.Should().NotBe(fp2, "COMMENT ON a function (annotations) must change the fingerprint");

        // temp table → must NOT change
        Exec("create temp table watch_fp_tmp(id int)");
        var fp4 = Fp();
        fp4.Should().Be(fp3, "temp objects must not look like a database change");

        // a table NOT used by any discovered routine → must NOT change (precision: the discovery
        // result doesn't mention it, so the hash cannot move)
        Exec("drop table if exists watch_fp_unrelated");
        Exec("create table watch_fp_unrelated(id int)");
        var fp5 = Fp();
        fp5.Should().Be(fp4, "an unrelated table is not part of the discovery result");
        Exec("drop table watch_fp_unrelated");

        // a table USED as a return type → its shape is part of the result: altering it changes the endpoint
        Exec("create table watch_fp_typed(id int)");
        Exec("create function watch_fp_typed_fn() returns setof watch_fp_typed language sql as 'select * from watch_fp_typed'");
        var fp6 = Fp();
        fp6.Should().NotBe(fp5, "a new routine (returning a table type) must change the fingerprint");
        Exec("alter table watch_fp_typed add column name text");
        var fp7 = Fp();
        fp7.Should().NotBe(fp6, "adding a column to a table used as a return type reshapes the endpoint");

        // cleanup; drops → change
        Exec("drop function watch_fp_typed_fn()");
        Exec("drop table watch_fp_typed");
        Exec("drop function watch_fp_test_fn(int)");
        var fp8 = Fp();
        fp8.Should().NotBe(fp7, "dropping functions must change the fingerprint");
    }
}
