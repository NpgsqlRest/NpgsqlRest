namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void TsClientSqlFiles()
    {
        // Single command — simple SELECT with param
        File.WriteAllText(Path.Combine(Dir, "ts_single_select.sql"), """
            -- tsclient_module=ts_single_select
            -- @param $1 id
            select id, name, active from sql_describe_test where id = $1;
            """);

        // Single command — void (DO block)
        File.WriteAllText(Path.Combine(Dir, "ts_do_block.sql"), """
            -- tsclient_module=ts_do_block
            do $$ begin perform 1; end; $$;
            """);

        // Multi command — two SELECTs
        File.WriteAllText(Path.Combine(Dir, "ts_multi_select.sql"), """
            -- tsclient_module=ts_multi_select
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            select count(*) as total from sql_describe_test;
            """);

        // Multi command — mixed (SELECT + void INSERT + SELECT)
        File.WriteAllText(Path.Combine(Dir, "ts_multi_mixed.sql"), """
            -- HTTP POST
            -- tsclient_module=ts_multi_mixed
            -- @param $1 id
            -- @result lookup
            select name from sql_describe_test where id = $1;
            insert into sql_describe_test (id, name) values ($1 + 2000, 'ts_test');
            -- @result verify
            select count(*) as total from sql_describe_test;
            """);

        // Single record — multi-column returns object type (no array)
        File.WriteAllText(Path.Combine(Dir, "ts_single_record.sql"), """
            -- HTTP GET
            -- tsclient_module=ts_single_record
            -- single
            -- @param $1 id
            select id, name, active from sql_describe_test where id = $1;
            """);

        // Single record — unnamed single column returns scalar (no array)
        File.WriteAllText(Path.Combine(Dir, "ts_single_record_scalar.sql"), """
            -- HTTP GET
            -- tsclient_module=ts_single_record_scalar
            -- single
            select name from sql_describe_test limit 1;
            """);

        // Multi-command with per-command @single
        File.WriteAllText(Path.Combine(Dir, "ts_multi_single.sql"), """
            -- HTTP GET
            -- tsclient_module=ts_multi_single
            -- @param $1 id
            -- @single
            -- @result user
            select id, name from sql_describe_test where id = $1;
            -- @result items
            select id, name from sql_describe_test order by id;
            """);
    }
}
