using BenchmarkDotNet.Attributes;
using Npgsql;

namespace BenchmarkTests;

public class ConnectionParametersTests
{
    private const string _baseConnection = "Host=127.0.0.1;Port=5437;Database=perftests;Username=postgres;Password=postgres";
    private string _connectionStr = $"{_baseConnection};Pooling=true";
    private string _connectionPoolingDisabled = $"{_baseConnection};Pooling=false";
    private string _connectionFullPreparePoolingEnabled = $"{_baseConnection};Pooling=true;Max Auto Prepare=5000;Auto Prepare Min Usages=1";
    private string _connectionFullPreparePoolingEnabledNoReset = $"{_baseConnection};Pooling=true;Max Auto Prepare=5000;Auto Prepare Min Usages=1;No Reset On Close=true";

    private string _query = """
    with regional_stats as (
        select 
            c.region_id,
            date_trunc('month', o.order_date) as month,
            count(distinct c.customer_id) as unique_customers,
            count(distinct o.store_id) as unique_stores,
            sum(o.amount) as total_amount,
            avg(o.amount) as avg_amount,
            percentile_cont(0.5) within group (order by o.amount) as median_amount
        from orders o
        join customers c on o.customer_id = c.customer_id
        group by c.region_id, date_trunc('month', o.order_date)
    ),
    store_rankings as (
        select 
            s.store_id,
            s.country_id,
            date_trunc('month', o.order_date) as month,
            sum(o.amount) as store_amount,
            rank() over (partition by s.country_id, date_trunc('month', o.order_date) 
                        order by sum(o.amount) desc) as country_rank,
            rank() over (partition by date_trunc('month', o.order_date) 
                        order by sum(o.amount) desc) as global_rank
        from orders o
        join stores s on o.store_id = s.store_id
        group by s.store_id, s.country_id, date_trunc('month', o.order_date)
    ),
    customer_segments as (
        select 
            c.customer_id,
            c.region_id,
            ntile(5) over (order by sum(o.amount)) as spending_quintile,
            ntile(5) over (order by count(*)) as frequency_quintile,
            sum(o.amount) as total_spent,
            count(*) as total_orders
        from orders o
        join customers c on o.customer_id = c.customer_id
        where o.order_date >= $1 and o.order_date < $2
        group by c.customer_id, c.region_id
    )
    select 
        rs.region_id,
        rs.month,
        rs.unique_customers,
        rs.unique_stores,
        rs.total_amount,
        rs.avg_amount,
        rs.median_amount,
        count(distinct case when sr.country_rank = 1 then sr.store_id end) as top_stores_in_countries,
        count(distinct case when sr.global_rank <= 10 then sr.store_id end) as top_10_global_stores,
        sum(case when cs.spending_quintile = 5 and cs.frequency_quintile >= 4 then 1 else 0 end) as vip_customers,
        array_agg(distinct sr.store_id) filter (where sr.global_rank = 1) as top_global_stores
    from regional_stats rs
    left join store_rankings sr on rs.month = sr.month
    left join customer_segments cs on rs.region_id = cs.region_id
    where rs.month between $1 and $2
    group by rs.region_id, rs.month, rs.unique_customers, rs.unique_stores, 
             rs.total_amount, rs.avg_amount, rs.median_amount
    order by rs.region_id, rs.month;
    """;

    [GlobalSetup]
    public void Setup()
    {
        using var connection = new NpgsqlConnection(_connectionPoolingDisabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
        drop table if exists orders;
        drop table if exists customers;
        drop table if exists stores;
        drop function if exists complex_analysis(timestamp, timestamp);

        create table orders as
        select 
            generate_series(1, 1000000) as order_id,
            (random() * 1000000)::int as customer_id,
            (random() * 1000)::int as store_id,
            (random() * 10000)::decimal(10,2) as amount,
            timestamp '2020-01-01' + random() * interval '3 years' as order_date;

        create table customers as
        select 
            generate_series(1, 1000000) as customer_id,
            md5(random()::text) as name,
            (random() * 1000)::int as region_id;

        create table stores as
        select 
            generate_series(1, 1000) as store_id,
            md5(random()::text) as name,
            (random() * 100)::int as country_id;


        create index idx_orders_customer on orders(customer_id);
        create index idx_orders_store on orders(store_id);
        create index idx_orders_date on orders(order_date);
        create index idx_customers_region on customers(region_id);
        create index idx_stores_country on stores(country_id);
        
        create function complex_analysis(
            timestamp, timestamp
        ) 
        returns table (
            region_id int,
            month date,
            unique_customers int,
            unique_stores int,
            total_amount decimal,
            avg_amount decimal,
            median_amount decimal,
            top_stores_in_countries int,
            top_10_global_stores int,
            vip_customers int,
            top_global_stores int[]
        )
        language sql as 
        $$
        {_query}
        $$;
        """;

        cmd.ExecuteNonQuery();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
    }

    [Benchmark(Baseline = true)]
    public async Task Query_PoolingDisabled()
    {
        using var connection = new NpgsqlConnection(_connectionPoolingDisabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _query;
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Query_PoolingDisabled_SequentialAccess()
    {
        using var connection = new NpgsqlConnection(_connectionPoolingDisabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _query;
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark()]
    public async Task Query_PoolingEnabled()
    {
        using var connection = new NpgsqlConnection(_connectionStr);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _query;
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Query_FullPreparePoolingEnabled()
    {
        using var connection = new NpgsqlConnection(_connectionFullPreparePoolingEnabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _query;
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Query_FullPreparePoolingEnabledNoReset()
    {
        using var connection = new NpgsqlConnection(_connectionFullPreparePoolingEnabledNoReset);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _query;
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Query_FullPreparePoolingEnabledNoReset_SequentialAccess()
    {
        using var connection = new NpgsqlConnection(_connectionFullPreparePoolingEnabledNoReset);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _query;
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark()]
    public async Task Func_PoolingDisabled()
    {
        using var connection = new NpgsqlConnection(_connectionPoolingDisabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select region_id,month,unique_customers,unique_stores,total_amount,avg_amount,median_amount,top_stores_in_countries,top_10_global_stores,vip_customers,top_global_stores from complex_analysis($1, $2);";
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Func_PoolingDisabled_SequentialAccess()
    {
        using var connection = new NpgsqlConnection(_connectionPoolingDisabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select region_id,month,unique_customers,unique_stores,total_amount,avg_amount,median_amount,top_stores_in_countries,top_10_global_stores,vip_customers,top_global_stores from complex_analysis($1, $2);";
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark()]
    public async Task Func_PoolingEnabled()
    {
        using var connection = new NpgsqlConnection(_connectionStr);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select region_id,month,unique_customers,unique_stores,total_amount,avg_amount,median_amount,top_stores_in_countries,top_10_global_stores,vip_customers,top_global_stores from complex_analysis($1, $2);";
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Func_FullPreparePoolingEnabled()
    {
        using var connection = new NpgsqlConnection(_connectionFullPreparePoolingEnabled);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select region_id,month,unique_customers,unique_stores,total_amount,avg_amount,median_amount,top_stores_in_countries,top_10_global_stores,vip_customers,top_global_stores from complex_analysis($1, $2);";
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Func_FullPreparePoolingEnabledNoReset()
    {
        using var connection = new NpgsqlConnection(_connectionFullPreparePoolingEnabledNoReset);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select region_id,month,unique_customers,unique_stores,total_amount,avg_amount,median_amount,top_stores_in_countries,top_10_global_stores,vip_customers,top_global_stores from complex_analysis($1, $2);";
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }

    [Benchmark]
    public async Task Func_FullPreparePoolingEnabledNoReset_SequentialAccess()
    {
        using var connection = new NpgsqlConnection(_connectionFullPreparePoolingEnabledNoReset);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "select region_id,month,unique_customers,unique_stores,total_amount,avg_amount,median_amount,top_stores_in_countries,top_10_global_stores,vip_customers,top_global_stores from complex_analysis($1, $2);";
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        cmd.Parameters.Add(new NpgsqlParameter{ Value = new DateTime(2020, 1, 1) });
        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync())
        {
            var regionId = reader.GetInt32(0);
            var month = reader.GetDateTime(1);
            var uniqueCustomers = reader.GetInt32(2);
            var uniqueStores = reader.GetInt32(3);
            var totalAmount = reader.GetDouble(4);
            var avgAmount = reader.GetDouble(5);
            var medianAmount = reader.GetDouble(6);
            var topStoresInCountries = reader.GetInt32(7);
            var top10GlobalStores = reader.GetInt32(8);
            var vipCustomers = reader.GetInt32(9);
            var topGlobalStores = reader.GetFieldValue<int[]>(10);
        }
    }
}