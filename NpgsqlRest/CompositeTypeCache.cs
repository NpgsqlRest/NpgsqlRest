using System.Collections.Concurrent;
using Npgsql;

namespace NpgsqlRest;

/// <summary>
/// Metadata for a composite type including field names, types, and nested composite info.
/// </summary>
public class CompositeTypeMetadata
{
    /// <summary>
    /// Fully qualified type name, e.g., "public.my_type"
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Field names in order, e.g., ["id", "name", "nested_val"]
    /// </summary>
    public required string[] FieldNames { get; init; }

    /// <summary>
    /// Field type names in order, e.g., ["integer", "text", "public.inner_type"]
    /// </summary>
    public required string[] FieldTypeNames { get; init; }

    /// <summary>
    /// Converted field names using the name converter
    /// </summary>
    public string[]? ConvertedFieldNames { get; set; }

    /// <summary>
    /// Type descriptors for each field
    /// </summary>
    public TypeDescriptor[]? FieldDescriptors { get; set; }

    /// <summary>
    /// For each field, if it's a composite type, contains its metadata; null otherwise
    /// </summary>
    public CompositeTypeMetadata?[]? NestedFieldTypes { get; set; }

    /// <summary>
    /// For each field, true if the field is an array type
    /// </summary>
    public required bool[] IsArrayField { get; init; }

    /// <summary>
    /// For each field that is an array of composites, contains the element type metadata
    /// </summary>
    public CompositeTypeMetadata?[]? ArrayElementTypes { get; set; }
}

/// <summary>
/// Thread-safe cache for composite type metadata.
/// Used to resolve nested composite types for deep JSON serialization.
/// </summary>
public static class CompositeTypeCache
{
    private static readonly ConcurrentDictionary<string, CompositeTypeMetadata> _cache = new(StringComparer.Ordinal);
    private static readonly object _initLock = new();
    private static volatile bool _initialized = false;
    private static Func<string, string?>? _nameConverter;

    /// <summary>
    /// SQL query to fetch all composite types and their fields.
    /// </summary>
    private const string TypeQuery = """
        select
            (quote_ident(n.nspname) || '.' || quote_ident(t.typname))::regtype::text as type_name,
            a.attnum as field_pos,
            a.attname as field_name,
            pg_catalog.format_type(a.atttypid, a.atttypmod) as field_type,
            ft.typrelid <> 0 as is_field_composite,
            a.atttypid = any(array(select oid from pg_type where typelem <> 0)) as is_array,
            case
                when ft.typelem <> 0 then
                    (select eft.typrelid <> 0
                     from pg_catalog.pg_type eft
                     where eft.oid = ft.typelem)
                else false
            end as is_array_of_composite
        from pg_catalog.pg_type t
        join pg_catalog.pg_namespace n on n.oid = t.typnamespace
        join pg_catalog.pg_class c on t.typrelid = c.oid and c.relkind in ('r', 'c')
        join pg_catalog.pg_attribute a on t.typrelid = a.attrelid
            and a.attisdropped is false and a.attnum > 0
        left join pg_catalog.pg_type ft on a.atttypid = ft.oid
        where n.nspname not like 'pg_%'
            and n.nspname <> 'information_schema'
            and has_schema_privilege(current_user, n.nspname, 'USAGE')
        order by type_name, field_pos
        """;

    /// <summary>
    /// Initialize the composite type cache from the database.
    /// Thread-safe and idempotent - only loads once.
    /// </summary>
    /// <param name="connection">Open database connection</param>
    /// <param name="nameConverter">Name converter function for field names</param>
    public static void Initialize(NpgsqlConnection connection, Func<string, string?>? nameConverter = null)
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            _nameConverter = nameConverter;
            LoadAllCompositeTypes(connection);
            ResolveNestedTypes();
            _initialized = true;
        }
    }

    /// <summary>
    /// Clear the cache. Useful for testing or when schema changes.
    /// </summary>
    public static void Clear()
    {
        lock (_initLock)
        {
            _cache.Clear();
            _initialized = false;
            _nameConverter = null;
        }
    }

    /// <summary>
    /// Check if the cache is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Get metadata for a composite type by name.
    /// </summary>
    /// <param name="typeName">Type name (e.g., "public.my_type" or "my_type")</param>
    /// <returns>Composite type metadata, or null if not a composite type</returns>
    public static CompositeTypeMetadata? GetType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        var normalizedName = NormalizeTypeName(typeName);
        return _cache.GetValueOrDefault(normalizedName);
    }

    /// <summary>
    /// Get metadata for the element type of an array of composites.
    /// </summary>
    /// <param name="arrayTypeName">Array type name (e.g., "public.my_type[]")</param>
    /// <returns>Element composite type metadata, or null if not an array of composites</returns>
    public static CompositeTypeMetadata? GetArrayElementType(string? arrayTypeName)
    {
        if (string.IsNullOrEmpty(arrayTypeName)) return null;

        // Strip array suffix(es) - handles both [] and [][] etc.
        var elementTypeName = arrayTypeName;
        while (elementTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            elementTypeName = elementTypeName[..^2];
        }

        return GetType(elementTypeName);
    }

    /// <summary>
    /// Normalize type name by removing quotes and handling schema prefixes.
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        // Remove surrounding quotes if present
        var result = typeName.Trim();
        if (result.StartsWith('"') && result.EndsWith('"'))
        {
            result = result[1..^1];
        }

        // Handle quoted identifiers within the name
        result = result.Replace("\"", "");

        return result;
    }

    /// <summary>
    /// Load all composite types from the database into the cache.
    /// </summary>
    private static void LoadAllCompositeTypes(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = TypeQuery;
        command.LogCommand(nameof(CompositeTypeCache));
        using var reader = command.ExecuteReader();

        // Group rows by type name to build metadata
        string? currentTypeName = null;
        var fieldNames = new List<string>();
        var fieldTypes = new List<string>();
        var isArrayField = new List<bool>();
        var isFieldComposite = new List<bool>();
        var isArrayOfComposite = new List<bool>();

        while (reader.Read())
        {
            var typeName = reader.GetString(0);

            // If we've moved to a new type, save the previous one
            if (currentTypeName != null && typeName != currentTypeName)
            {
                SaveTypeMetadata(currentTypeName, fieldNames, fieldTypes, isArrayField, isFieldComposite, isArrayOfComposite);
                fieldNames.Clear();
                fieldTypes.Clear();
                isArrayField.Clear();
                isFieldComposite.Clear();
                isArrayOfComposite.Clear();
            }

            currentTypeName = typeName;
            fieldNames.Add(reader.GetString(2));
            fieldTypes.Add(reader.GetString(3));
            isArrayField.Add(reader.GetBoolean(5));
            isFieldComposite.Add(reader.GetBoolean(4));
            isArrayOfComposite.Add(reader.GetBoolean(6));
        }

        // Don't forget the last type
        if (currentTypeName != null)
        {
            SaveTypeMetadata(currentTypeName, fieldNames, fieldTypes, isArrayField, isFieldComposite, isArrayOfComposite);
        }
    }

    /// <summary>
    /// Save a single type's metadata to the cache.
    /// </summary>
    private static void SaveTypeMetadata(
        string typeName,
        List<string> fieldNames,
        List<string> fieldTypes,
        List<bool> isArrayField,
        List<bool> isFieldComposite,
        List<bool> isArrayOfComposite)
    {
        var fieldNamesArray = fieldNames.ToArray();
        var fieldTypesArray = fieldTypes.ToArray();
        var count = fieldNamesArray.Length;

        // Apply name converter
        var convertedNames = new string[count];
        for (var i = 0; i < count; i++)
        {
            convertedNames[i] = _nameConverter?.Invoke(fieldNamesArray[i]) ?? fieldNamesArray[i];
        }

        // Create type descriptors for each field
        var descriptors = new TypeDescriptor[count];
        for (var i = 0; i < count; i++)
        {
            descriptors[i] = new TypeDescriptor(fieldTypesArray[i]);
        }

        var metadata = new CompositeTypeMetadata
        {
            TypeName = typeName,
            FieldNames = fieldNamesArray,
            FieldTypeNames = fieldTypesArray,
            ConvertedFieldNames = convertedNames,
            FieldDescriptors = descriptors,
            IsArrayField = isArrayField.ToArray(),
            // These will be populated in ResolveNestedTypes()
            NestedFieldTypes = new CompositeTypeMetadata?[count],
            ArrayElementTypes = new CompositeTypeMetadata?[count]
        };

        var normalizedName = NormalizeTypeName(typeName);
        _cache[normalizedName] = metadata;
    }

    /// <summary>
    /// After all types are loaded, resolve nested type references.
    /// Uses cycle detection to handle self-referencing types.
    /// </summary>
    private static void ResolveNestedTypes()
    {
        foreach (var metadata in _cache.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            ResolveNestedTypesForType(metadata, visited);
        }
    }

    /// <summary>
    /// Recursively resolve nested types for a single type.
    /// </summary>
    private static void ResolveNestedTypesForType(CompositeTypeMetadata metadata, HashSet<string> visited)
    {
        // Cycle detection
        var normalizedName = NormalizeTypeName(metadata.TypeName);
        if (!visited.Add(normalizedName))
        {
            return; // Already processing this type - circular reference
        }

        try
        {
            for (var i = 0; i < metadata.FieldTypeNames.Length; i++)
            {
                var fieldType = metadata.FieldTypeNames[i];

                // Check if this field is a composite type
                var nestedType = GetType(fieldType);
                if (nestedType != null)
                {
                    metadata.NestedFieldTypes![i] = nestedType;

                    // Also set the nested info on the TypeDescriptor
                    if (metadata.FieldDescriptors != null)
                    {
                        metadata.FieldDescriptors[i].CompositeFieldNames = nestedType.ConvertedFieldNames;
                        metadata.FieldDescriptors[i].CompositeFieldDescriptors = nestedType.FieldDescriptors;
                    }

                    // Recursively resolve (with cycle detection)
                    ResolveNestedTypesForType(nestedType, visited);
                }

                // Check if this field is an array of composites
                if (metadata.IsArrayField[i])
                {
                    var elementType = GetArrayElementType(fieldType);
                    if (elementType != null)
                    {
                        metadata.ArrayElementTypes![i] = elementType;

                        // Also set the array composite info on the TypeDescriptor
                        if (metadata.FieldDescriptors != null)
                        {
                            metadata.FieldDescriptors[i].ArrayCompositeFieldNames = elementType.ConvertedFieldNames;
                            metadata.FieldDescriptors[i].ArrayCompositeFieldDescriptors = elementType.FieldDescriptors;
                        }

                        // Recursively resolve the element type
                        ResolveNestedTypesForType(elementType, visited);
                    }
                }
            }
        }
        finally
        {
            visited.Remove(normalizedName);
        }
    }
}
