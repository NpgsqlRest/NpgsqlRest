using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void SqlFileDescribeTests()
        {
            script.Append(@"
            create table if not exists sql_describe_test (
                id int primary key,
                name text not null,
                active boolean default true,
                created_at timestamptz default now()
            );
            insert into sql_describe_test values (1, 'test1', true, '2024-01-01');
            insert into sql_describe_test values (2, 'test2', false, '2024-01-02');
            ");
        }
    }
}

namespace NpgsqlRestTests.SqlFileSourceTests
{
    [Collection("TestFixture")]
    public class SelectDescribeTests
    {
        [Fact]
        public void Select_NoParams_ReturnsColumnsAndZeroParams()
        {
            using var conn = Database.CreateConnection();
            conn.Open();

            var sql = "select id, name from sql_describe_test";
            var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
            var result = SqlFileDescriber.Describe(conn, sql, paramCount);

            result.HasError.Should().BeFalse();
            paramCount.Should().Be(0);
            result.ParameterTypes.Should().NotBeNull();
            result.ParameterTypes!.Length.Should().Be(0);
            result.Columns.Should().NotBeNull();
            result.Columns!.Length.Should().Be(2);
            result.Columns[0].Name.Should().Be("id");
            result.Columns[1].Name.Should().Be("name");
        }

        [Fact]
        public void Select_WithOneParam_ReturnsOneParamTypeInferred()
        {
            using var conn = Database.CreateConnection();
            conn.Open();

            var sql = "select id, name from sql_describe_test where id = $1";
            var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
            var result = SqlFileDescriber.Describe(conn, sql, paramCount);

            result.HasError.Should().BeFalse();
            paramCount.Should().Be(1);
            result.ParameterTypes.Should().NotBeNull();
            result.ParameterTypes!.Length.Should().Be(1);
            result.Columns.Should().NotBeNull();
            result.Columns!.Length.Should().Be(2);
        }

        [Fact]
        public void Select_WithMultipleParams_ReturnsCorrectParamCount()
        {
            using var conn = Database.CreateConnection();
            conn.Open();

            var sql = "select id, name from sql_describe_test where id = $1 and name = $2 and active = $3";
            var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
            var result = SqlFileDescriber.Describe(conn, sql, paramCount);

            result.HasError.Should().BeFalse();
            paramCount.Should().Be(3);
            result.ParameterTypes.Should().NotBeNull();
            result.ParameterTypes!.Length.Should().Be(3);
        }

        [Fact]
        public void Select_Star_ReturnsAllColumns()
        {
            using var conn = Database.CreateConnection();
            conn.Open();

            var sql = "select * from sql_describe_test";
            var paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
            var result = SqlFileDescriber.Describe(conn, sql, paramCount);

            result.HasError.Should().BeFalse();
            result.Columns.Should().NotBeNull();
            result.Columns!.Length.Should().Be(4);
            result.Columns[0].Name.Should().Be("id");
            result.Columns[1].Name.Should().Be("name");
            result.Columns[2].Name.Should().Be("active");
            result.Columns[3].Name.Should().Be("created_at");
        }
    }
}
