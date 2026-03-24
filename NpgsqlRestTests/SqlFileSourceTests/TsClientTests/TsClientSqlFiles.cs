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
            -- @result1 lookup
            -- @result3 verify
            -- @param $1 id
            select name from sql_describe_test where id = $1;
            insert into sql_describe_test (id, name) values ($1 + 2000, 'ts_test');
            select count(*) as total from sql_describe_test;
            """);
    }
}
