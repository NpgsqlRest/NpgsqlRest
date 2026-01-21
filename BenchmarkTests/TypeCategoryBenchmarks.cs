using BenchmarkDotNet.Attributes;
using NpgsqlRest;
using NpgsqlTypes;
using System.Text;

namespace BenchmarkTests;

/// <summary>
/// Benchmarks for TypeCategory lookup and TypeDescriptor operations.
/// Tests the optimization of type dispatch in hot paths.
/// </summary>
[MemoryDiagnoser]
public class TypeCategoryBenchmarks
{
    // Common PostgreSQL types for realistic benchmarking
    private static readonly string[] CommonTypes =
    [
        "integer", "bigint", "text", "varchar", "boolean", "timestamp",
        "timestamptz", "date", "numeric", "uuid", "jsonb", "bytea"
    ];

    private TypeDescriptor[] _descriptors = null!;
    private NpgsqlDbType[] _dbTypes = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create type descriptors for common types
        _descriptors = CommonTypes.Select(t => new TypeDescriptor(t)).ToArray();

        // Direct NpgsqlDbType values for lookup benchmarks
        _dbTypes =
        [
            NpgsqlDbType.Integer, NpgsqlDbType.Bigint, NpgsqlDbType.Text,
            NpgsqlDbType.Varchar, NpgsqlDbType.Boolean, NpgsqlDbType.Timestamp,
            NpgsqlDbType.TimestampTz, NpgsqlDbType.Date, NpgsqlDbType.Numeric,
            NpgsqlDbType.Uuid, NpgsqlDbType.Jsonb, NpgsqlDbType.Bytea,
            NpgsqlDbType.Real, NpgsqlDbType.Double, NpgsqlDbType.Money,
            NpgsqlDbType.Xml, NpgsqlDbType.Json, NpgsqlDbType.Char
        ];
    }

    /// <summary>
    /// Benchmark: TypeCategory lookup via GetCategory
    /// This is the core lookup operation used in hot paths.
    /// </summary>
    [Benchmark]
    public TypeCategory TypeCategoryLookup_AllTypes()
    {
        TypeCategory result = TypeCategory.None;
        foreach (var dbType in _dbTypes)
        {
            result |= TypeCategoryLookup.GetCategory(dbType);
        }
        return result;
    }

    /// <summary>
    /// Benchmark: TypeDescriptor construction
    /// Tests the cost of creating type descriptors at startup.
    /// </summary>
    [Benchmark]
    public TypeDescriptor[] TypeDescriptor_Construction()
    {
        var result = new TypeDescriptor[CommonTypes.Length];
        for (int i = 0; i < CommonTypes.Length; i++)
        {
            result[i] = new TypeDescriptor(CommonTypes[i]);
        }
        return result;
    }

    /// <summary>
    /// Benchmark: Accessing Category property and doing bitwise checks
    /// Simulates the hot path in result serialization.
    /// </summary>
    [Benchmark]
    public int TypeDescriptor_CategoryBitwiseChecks()
    {
        int count = 0;
        foreach (var descriptor in _descriptors)
        {
            // Simulate serialization hot path checks
            if ((descriptor.Category & TypeCategory.Numeric) != 0) count++;
            if ((descriptor.Category & TypeCategory.Boolean) != 0) count++;
            if ((descriptor.Category & TypeCategory.Json) != 0) count++;
            if ((descriptor.Category & TypeCategory.Text) != 0) count++;
            if ((descriptor.Category & TypeCategory.DateTime) != 0) count++;
            if ((descriptor.Category & TypeCategory.NeedsEscape) != 0) count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: Accessing individual boolean properties (backward compat)
    /// Compares to using Category bitwise checks directly.
    /// </summary>
    [Benchmark]
    public int TypeDescriptor_BooleanPropertyAccess()
    {
        int count = 0;
        foreach (var descriptor in _descriptors)
        {
            // Using the backward-compatible boolean properties
            if (descriptor.IsNumeric) count++;
            if (descriptor.IsBoolean) count++;
            if (descriptor.IsJson) count++;
            if (descriptor.IsText) count++;
            if (descriptor.IsDateTime) count++;
            if (descriptor.NeedsEscape) count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: Combined check pattern used in serialization
    /// (descriptor.IsNumeric || descriptor.IsBoolean || descriptor.IsJson)
    /// </summary>
    [Benchmark]
    public int TypeDescriptor_CombinedCheck_Properties()
    {
        int count = 0;
        foreach (var descriptor in _descriptors)
        {
            // Pattern used in NpgsqlRestEndpoint.cs serialization
            if (descriptor.IsNumeric || descriptor.IsBoolean || descriptor.IsJson) count++;
            if (descriptor.IsText || descriptor.IsDate || descriptor.IsDateTime) count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: Combined check using direct Category bitwise
    /// </summary>
    [Benchmark]
    public int TypeDescriptor_CombinedCheck_Bitwise()
    {
        int count = 0;
        foreach (var descriptor in _descriptors)
        {
            // Optimized pattern using bitwise
            if ((descriptor.Category & (TypeCategory.Numeric | TypeCategory.Boolean | TypeCategory.Json)) != 0) count++;
            if ((descriptor.Category & (TypeCategory.Text | TypeCategory.Date | TypeCategory.DateTime)) != 0) count++;
        }
        return count;
    }
}

/// <summary>
/// Benchmarks for parameter parsing operations.
/// Tests the delegate lookup optimization vs original if-chain.
/// </summary>
[MemoryDiagnoser]
public class ParameterParserBenchmarks
{
    private NpgsqlDbType[] _parseableTypes = null!;
    private string[] _testValues = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parseableTypes =
        [
            NpgsqlDbType.Integer, NpgsqlDbType.Bigint, NpgsqlDbType.Smallint,
            NpgsqlDbType.Double, NpgsqlDbType.Real, NpgsqlDbType.Numeric,
            NpgsqlDbType.Boolean, NpgsqlDbType.Timestamp, NpgsqlDbType.TimestampTz,
            NpgsqlDbType.Date, NpgsqlDbType.Time, NpgsqlDbType.Uuid
        ];

        _testValues =
        [
            "42", "9223372036854775807", "32767",
            "3.14159", "2.71828", "123.456",
            "true", "2024-01-15T10:30:00", "2024-01-15T10:30:00Z",
            "2024-01-15", "10:30:00", "550e8400-e29b-41d4-a716-446655440000"
        ];
    }

    /// <summary>
    /// Benchmark: GetParser delegate lookup
    /// Tests the O(1) array lookup for parser delegates.
    /// </summary>
    [Benchmark]
    public int ParameterParsers_GetParser_AllTypes()
    {
        int count = 0;
        foreach (var dbType in _parseableTypes)
        {
            var parser = ParameterParsers.GetParser(dbType);
            if (parser != null) count++;
        }
        return count;
    }

    /// <summary>
    /// Benchmark: Full parse operation using delegate lookup
    /// Simulates realistic parameter parsing workload.
    /// </summary>
    [Benchmark]
    public int ParameterParsers_ParseValues()
    {
        int successCount = 0;
        for (int i = 0; i < _parseableTypes.Length; i++)
        {
            var parser = ParameterParsers.GetParser(_parseableTypes[i]);
            if (parser != null && parser(_testValues[i], out var result) && result != null)
            {
                successCount++;
            }
        }
        return successCount;
    }
}

/// <summary>
/// Benchmarks simulating realistic endpoint serialization workloads.
/// Tests the combined effect of all optimizations.
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private TypeDescriptor[] _columnDescriptors = null!;
    private string[][] _rowData = null!;
    private StringBuilder _outputBuffer = null!;

    [Params(10, 100, 1000)]
    public int RowCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Typical result set: 10 columns of mixed types
        _columnDescriptors =
        [
            new TypeDescriptor("integer"),      // id
            new TypeDescriptor("text"),         // name
            new TypeDescriptor("varchar"),      // email
            new TypeDescriptor("boolean"),      // is_active
            new TypeDescriptor("timestamptz"),  // created_at
            new TypeDescriptor("numeric"),      // balance
            new TypeDescriptor("jsonb"),        // metadata
            new TypeDescriptor("uuid"),         // external_id
            new TypeDescriptor("date"),         // birth_date
            new TypeDescriptor("bigint")        // count
        ];

        // Generate test data
        _rowData = new string[RowCount][];
        for (int i = 0; i < RowCount; i++)
        {
            _rowData[i] =
            [
                i.ToString(),
                $"User {i}",
                $"user{i}@example.com",
                (i % 2 == 0) ? "t" : "f",
                "2024-01-15 10:30:00+00",
                $"{i * 100.50m}",
                $"{{\"key\": {i}}}",
                Guid.NewGuid().ToString(),
                "2024-01-15",
                (i * 1000L).ToString()
            ];
        }

        _outputBuffer = new StringBuilder(RowCount * 500);
    }

    /// <summary>
    /// Benchmark: Simulates the type dispatch logic in serialization hot path.
    /// This is the core operation that happens per-column per-row.
    /// </summary>
    [Benchmark]
    public int Serialization_TypeDispatch_Simulation()
    {
        int operations = 0;
        _outputBuffer.Clear();

        for (int row = 0; row < RowCount; row++)
        {
            for (int col = 0; col < _columnDescriptors.Length; col++)
            {
                var descriptor = _columnDescriptors[col];
                var value = _rowData[row][col];

                // Simulate the type dispatch in NpgsqlRestEndpoint.cs lines 2114-2174
                if ((descriptor.Category & (TypeCategory.Numeric | TypeCategory.Boolean | TypeCategory.Json)) != 0)
                {
                    if ((descriptor.Category & TypeCategory.Boolean) != 0)
                    {
                        // Boolean conversion
                        if (value == "t") _outputBuffer.Append("true");
                        else if (value == "f") _outputBuffer.Append("false");
                        else _outputBuffer.Append(value);
                    }
                    else
                    {
                        // Numeric or JSON - append directly
                        _outputBuffer.Append(value);
                    }
                    operations++;
                }
                else if (descriptor.NeedsEscape)
                {
                    // String that needs escaping
                    _outputBuffer.Append('"');
                    _outputBuffer.Append(value); // Simplified - real code escapes
                    _outputBuffer.Append('"');
                    operations++;
                }
                else if ((descriptor.Category & TypeCategory.DateTime) != 0)
                {
                    // DateTime formatting
                    _outputBuffer.Append('"');
                    _outputBuffer.Append(value.Replace(' ', 'T'));
                    _outputBuffer.Append('"');
                    operations++;
                }
                else
                {
                    // Default quoted
                    _outputBuffer.Append('"');
                    _outputBuffer.Append(value);
                    _outputBuffer.Append('"');
                    operations++;
                }

                if (col < _columnDescriptors.Length - 1)
                    _outputBuffer.Append(',');
            }
            _outputBuffer.AppendLine();
        }

        return operations;
    }

    /// <summary>
    /// Benchmark: Just the category lookup portion of serialization.
    /// Isolates the optimization impact.
    /// </summary>
    [Benchmark]
    public int Serialization_CategoryLookup_Only()
    {
        int matches = 0;

        for (int row = 0; row < RowCount; row++)
        {
            for (int col = 0; col < _columnDescriptors.Length; col++)
            {
                var descriptor = _columnDescriptors[col];

                // Just the category checks
                if ((descriptor.Category & (TypeCategory.Numeric | TypeCategory.Boolean | TypeCategory.Json)) != 0)
                    matches++;
                else if ((descriptor.Category & TypeCategory.NeedsEscape) != 0)
                    matches++;
                else if ((descriptor.Category & TypeCategory.DateTime) != 0)
                    matches++;
            }
        }

        return matches;
    }
}
