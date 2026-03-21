namespace NpgsqlRestTests;

public static partial class Database
{
    public static void SqlFileCustomTypeReturnTests()
    {
        script.Append(@"
        create type sql_file_custom_type as (val1 text, val2 int, val3 boolean);

        create table sql_file_custom_table (
            id int primary key,
            data sql_file_custom_type
        );

        insert into sql_file_custom_table values (1, row('hello', 42, true)::sql_file_custom_type);
        insert into sql_file_custom_table values (2, row('world', 99, false)::sql_file_custom_type);

        create table sql_file_custom_array_table (
            id int primary key,
            items sql_file_custom_type[]
        );

        insert into sql_file_custom_array_table values (1, array[
            row('first', 10, true)::sql_file_custom_type,
            row('second', 20, false)::sql_file_custom_type
        ]);
        ");
    }
}
