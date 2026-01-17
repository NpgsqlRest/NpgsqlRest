using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests;

/// <summary>
/// Tests for Principle of Least Privilege (PoLP) scenarios.
/// Tests that a user with minimal permissions (only USAGE on a schema)
/// can successfully call security definer functions that use user-defined types.
/// </summary>
public static partial class Database
{
    public static void PolpTests()
    {
        script.Append("""

-- =====================================================
-- Principle of Least Privilege (PoLP) Test Setup
-- =====================================================

-- Drop and recreate the restricted user to ensure clean state each test session
drop role if exists test_user;
create role test_user with
    login
    nosuperuser
    nocreatedb
    nocreaterole
    noinherit
    noreplication
    connection limit -1
    password 'test_pass';

-- Create the schema for PoLP testing
create schema polp_schema;

-- Create unprivileged schema objects owned by postgres which test_user cannot access
create schema polp_schema_unauthorized;

-- Create a user-defined composite type in the polp schema
create type polp_schema.polp_request as (
    id int,
    name text,
    amount numeric(10,2)
);

create type polp_schema_unauthorized.secret_data as (
    secret_id int,
    secret_value text
);

-- Create a function in the unauthorized schema that uses the secret type
-- This should cause the metadata query to fail when test_user tries to resolve the type
create function polp_schema_unauthorized.get_secret_data()
returns polp_schema_unauthorized.secret_data
language sql
as $$
    select row(1, 'secret')::polp_schema_unauthorized.secret_data;
$$;

-- Create a table in the unauthorized schema to create array types
-- PostgreSQL automatically creates array types for composite types and tables
create table polp_schema_unauthorized.secret_table (
    id int,
    value text
);

-- Create a user-defined composite type for the response
create type polp_schema.polp_response as (
    request_id int,
    processed_name text,
    calculated_amount numeric(10,2),
    status text
);

-- Create a security definer function that uses the user-defined types
-- This function runs with the privileges of the owner (postgres), not the caller (test_user)
create function polp_schema.process_polp_request(
    _request polp_schema.polp_request
)
returns polp_schema.polp_response
language plpgsql
security definer
as $$
begin
    return row(
        _request.id,
        'Processed: ' || _request.name,
        _request.amount * 1.1,
        'SUCCESS'
    )::polp_schema.polp_response;
end;
$$;

-- Create another security definer function that returns the type directly
create function polp_schema.get_polp_status(
    _id int,
    _name text
)
returns polp_schema.polp_response
language sql
security definer
as $$
    select row(
        _id,
        _name,
        0.00,
        'ACTIVE'
    )::polp_schema.polp_response;
$$;

-- Create a function that takes simple parameters but returns the composite type
create function polp_schema.create_polp_response(
    _id int,
    _name text,
    _amount numeric
)
returns polp_schema.polp_response
language sql
security definer
as $$
    select row(
        _id,
        'Created: ' || _name,
        _amount,
        'CREATED'
    )::polp_schema.polp_response;
$$;

-- Create a function that returns an array of composite types
create function polp_schema.get_polp_responses()
returns polp_schema.polp_response[]
language sql
security definer
as $$
    select array[
        row(1, 'Item1', 100.00, 'ACTIVE')::polp_schema.polp_response,
        row(2, 'Item2', 200.00, 'ACTIVE')::polp_schema.polp_response
    ];
$$;

-- Create a function in polp_schema (which test_user can access via USAGE)
-- that returns a type from polp_schema_unauthorized (which test_user CANNOT access).
-- This will trigger the bug: when the metadata query tries to resolve the return type
-- using ::regtype cast, it will fail with "permission denied for schema polp_schema_unauthorized"
create function polp_schema.get_secret_wrapper()
returns polp_schema_unauthorized.secret_data
language sql
security definer
as $$
    select (1, 'secret')::polp_schema_unauthorized.secret_data;
$$;

-- Grant USAGE on schema to test_user
grant usage on schema polp_schema to test_user;
""");
    }
}

/// <summary>
/// Tests that verify the PoLP (Principle of Least Privilege) scenario works correctly.
/// These tests use a SEPARATE fixture (PolpTestFixture) that connects as test_user
/// for BOTH metadata discovery AND function execution.
///
/// test_user has ONLY "USAGE" on polp_schema - no EXECUTE grants.
/// The security definer functions run with owner (postgres) privileges.
///
/// NOTE: These tests will FAIL until the metadata query is fixed to discover
/// SECURITY DEFINER functions that the user can call via schema USAGE privilege.
/// </summary>
[Collection("PolpTestFixture")]
public class PolpTests(PolpTestFixture test)
{
    /// <summary>
    /// Test that test_user can call a security definer function with simple parameters.
    /// </summary>
    [Fact]
    public async Task Test_polp_get_status_with_simple_parameters()
    {
        var query = new QueryBuilder
        {
            { "id", "1" },
            { "name", "PolpUser" }
        };

        using var response = await test.Client.GetAsync($"/api/polp-schema/get-polp-status/{query}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);

        json.RootElement.GetProperty("requestId").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("processedName").GetString().Should().Be("PolpUser");
        json.RootElement.GetProperty("status").GetString().Should().Be("ACTIVE");
    }

    /// <summary>
    /// Test that test_user can call a security definer function with composite type parameter.
    /// </summary>
    [Fact]
    public async Task Test_polp_process_request_with_composite_type()
    {
        var requestBody = new
        {
            requestId = 777,
            requestName = "PolpRequest",
            requestAmount = 500.00
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await test.Client.PostAsync("/api/polp-schema/process-polp-request/", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);

        json.RootElement.GetProperty("requestId").GetInt32().Should().Be(777);
        json.RootElement.GetProperty("processedName").GetString().Should().Be("Processed: PolpRequest");
        json.RootElement.GetProperty("calculatedAmount").GetDecimal().Should().Be(550.00m);
        json.RootElement.GetProperty("status").GetString().Should().Be("SUCCESS");
    }

    /// <summary>
    /// Test that test_user can call a security definer function that creates a response.
    /// </summary>
    [Fact]
    public async Task Test_polp_create_response_with_simple_parameters()
    {
        var requestBody = new
        {
            id = 999,
            name = "NewItem",
            amount = 250.50
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await test.Client.PostAsync("/api/polp-schema/create-polp-response/", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);

        json.RootElement.GetProperty("requestId").GetInt32().Should().Be(999);
        json.RootElement.GetProperty("processedName").GetString().Should().Be("Created: NewItem");
        json.RootElement.GetProperty("calculatedAmount").GetDecimal().Should().Be(250.50m);
        json.RootElement.GetProperty("status").GetString().Should().Be("CREATED");
    }
}
