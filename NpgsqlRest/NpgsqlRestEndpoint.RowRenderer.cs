using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;

namespace NpgsqlRest;

public partial class NpgsqlRestEndpoint
{
    /// <summary>
    /// Renders the rows of a result set into a UTF-8 buffer (JSON objects / values, or raw text).
    ///
    /// PostgreSQL sends every value as UTF-8 text and the response is UTF-8, so a row is built as bytes: a column's
    /// bytes are read straight from the reader (no string per value) and written with the JSON punctuation around
    /// them. This is the synchronous part of row rendering; the async loop around it only reads rows and flushes.
    ///
    /// The object belongs to the pooled request state and is reused: <see cref="Init"/> starts a request's use of it
    /// and <see cref="Release"/> ends it (buffers go back to their pools, references are cleared).
    /// </summary>
    private sealed class RowRenderer
    {
        private NpgsqlDataReader _reader = null!;
        private Routine _routine = null!;
        private RoutineEndpoint _endpoint = null!;
        private IDataProtector? _protector;
        // True when a column's bytes can be used as they are: see IsUtf8Wire.
        private bool _utf8Wire;

        private string[] _columnNames = [];
        private string[] _jsonColumnNames = [];
        private byte[][] _utf8JsonColumnNames = [];
        private TypeDescriptor[] _descriptors = [];
        private int _columnCount;
        private int _hiddenColumnIndex = -1;
        // Rows are bare values / value lists, not objects (a single unnamed column)
        private bool _returnsUnnamedSet;
        // Raw (text/CSV) output instead of JSON
        private bool _raw;
        private Dictionary<int, (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName)>? _nestedJsonColumnMap;

        // Buffer for the fields of a nested composite, to detect an all-NULL composite
        private Utf8Buffer? _compositeFieldBuffer;
        private bool _compositeHasNonNullValue;
        private string? _currentJsonCompositeName;

        private byte[] _scratch = [];
        private char[]? _charScratch;

        /// <summary>The rows rendered since the last flush.</summary>
        public Utf8Buffer Row = null!;

        /// <summary>Starts rendering the rows of <paramref name="reader"/>. Pair with <see cref="Release"/>.</summary>
        public void Init(NpgsqlDataReader reader, Routine routine, RoutineEndpoint endpoint, NpgsqlConnection connection)
        {
            _reader = reader;
            _routine = routine;
            _endpoint = endpoint;
            _protector = Options.AuthenticationOptions.DefaultDataProtector;
            _utf8Wire = IsUtf8Wire(connection);
            _scratch = ArrayPool<byte>.Shared.Rent(1024);
            Row = Utf8Buffer.Rent();
        }

        /// <summary>Sets the shape of the result set that the following rows belong to.</summary>
        public void Configure(
            string[] columnNames,
            string[] jsonColumnNames,
            TypeDescriptor[] descriptors,
            int columnCount,
            Dictionary<int, (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName)>? nestedJsonColumnMap,
            int hiddenColumnIndex,
            bool returnsUnnamedSet,
            bool raw)
        {
            _columnNames = columnNames;
            _jsonColumnNames = jsonColumnNames;
            _utf8JsonColumnNames = Utf8ColumnNames.Get(jsonColumnNames);
            _descriptors = descriptors;
            _columnCount = columnCount;
            _nestedJsonColumnMap = nestedJsonColumnMap;
            _hiddenColumnIndex = hiddenColumnIndex;
            _returnsUnnamedSet = returnsUnnamedSet;
            _raw = raw;
            if (nestedJsonColumnMap is not null)
            {
                _compositeFieldBuffer ??= Utf8Buffer.Rent();
            }
            _compositeHasNonNullValue = false;
            _currentJsonCompositeName = null;
            Row.Clear();
        }

        /// <summary>
        /// Reads column <paramref name="ordinal"/> of the current row as UTF-8. Returns false for NULL.
        /// The span is valid until the next read.
        /// </summary>
        private bool TryRead(int ordinal, out ReadOnlySpan<byte> utf8)
        {
            if (_utf8Wire)
            {
                if (_reader.IsDBNull(ordinal))
                {
                    utf8 = default;
                    return false;
                }
                // The raw column: with AllResultTypesAreUnknown every column is in text format. The stream object is
                // reused by Npgsql, so this allocates nothing.
                using var stream = _reader.GetStream(ordinal);
                int length = checked((int)stream.Length);
                if (_scratch.Length < length)
                {
                    GrowScratch(length);
                }
                stream.ReadExactly(_scratch, 0, length);
                utf8 = new ReadOnlySpan<byte>(_scratch, 0, length);
                return true;
            }

            object value = _reader.GetValue(ordinal);
            if (value == DBNull.Value)
            {
                utf8 = default;
                return false;
            }
            utf8 = EncodeToScratch((string)value);
            return true;
        }

        private ReadOnlySpan<byte> EncodeToScratch(string text)
        {
            int max = Encoding.UTF8.GetMaxByteCount(text.Length);
            if (_scratch.Length < max)
            {
                int exact = Encoding.UTF8.GetByteCount(text);
                if (_scratch.Length < exact)
                {
                    GrowScratch(exact);
                }
            }
            int written = Encoding.UTF8.GetBytes(text, _scratch);
            return new ReadOnlySpan<byte>(_scratch, 0, written);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowScratch(int length)
        {
            ArrayPool<byte>.Shared.Return(_scratch);
            _scratch = ArrayPool<byte>.Shared.Rent(length);
        }

        /// <summary>Appends the reader's current row to <see cref="Row"/>.</summary>
        public void AppendRow()
        {
            var rowBuilder = Row;
            var endpoint = _endpoint;
            var routine = _routine;
            var routineReturnRecordCount = _columnCount;

            for (var i = 0; i < routineReturnRecordCount; i++)
            {
                ReadOnlySpan<byte> raw = default;
                bool isNull = i == _hiddenColumnIndex || TryRead(i, out raw) is false;

                // Decrypt column value if marked with decrypt annotation
                if (isNull is false &&
                    _protector is not null &&
                    (endpoint.DecryptAllColumns ||
                     (endpoint.DecryptColumns is not null &&
                      i < _columnNames.Length &&
                      endpoint.DecryptColumns.Contains(_columnNames[i]))))
                {
                    try
                    {
                        raw = EncodeToScratch(_protector.Unprotect(Encoding.UTF8.GetString(raw)));
                    }
                    catch (Exception decryptEx)
                    {
                        Logger?.DecryptColumnFailed(decryptEx.Message);
                    }
                }

                // if raw
                if (_raw)
                {
                    // NULL is rendered as an empty value
                    if (endpoint.RawValueSeparator is not null)
                    {
                        var descriptor = _descriptors[i];
                        if ((descriptor.Category & (TypeCategory.Text | TypeCategory.Date | TypeCategory.DateTime)) != 0)
                        {
                            PgUtf8Converters.AppendCsvQuoted(rowBuilder, raw);
                        }
                        else
                        {
                            rowBuilder.Append(raw);
                        }
                        if (i < routineReturnRecordCount - 1)
                        {
                            rowBuilder.Append(endpoint.RawValueSeparator);
                        }
                    }
                    else
                    {
                        rowBuilder.Append(raw);
                    }
                }
                else
                {
                    var descriptor = _descriptors[i];

                    // SQL file composite type column: value is a PostgreSQL tuple string
                    // Expand inline (flat) or wrap as nested JSON object
                    if (descriptor.IsCompositeType &&
                        descriptor.CompositeFieldNames is not null &&
                        descriptor.CompositeFieldDescriptors is not null)
                    {
                        if (_returnsUnnamedSet == false && i == 0)
                        {
                            rowBuilder.Append(Consts.OpenBrace);
                        }

                        if (endpoint.NestedJsonForCompositeTypes == true)
                        {
                            // Nested mode: "columnName": {parsed object} or "columnName": null
                            if (_returnsUnnamedSet == false)
                            {
                                rowBuilder.Append(_utf8JsonColumnNames[i]);
                            }
                            if (isNull)
                            {
                                rowBuilder.Append(Consts.Utf8Null);
                            }
                            else
                            {
                                rowBuilder.Append(PgTupleToJsonObject(
                                    PgUtf8Converters.DecodeChars(raw, ref _charScratch),
                                    descriptor.CompositeFieldNames,
                                    descriptor.CompositeFieldDescriptors));
                            }
                        }
                        else
                        {
                            // Flat mode: splice individual fields inline
                            if (isNull)
                            {
                                // All fields null — emit "field1":null,"field2":null,...
                                for (int fi = 0; fi < descriptor.CompositeFieldNames.Length; fi++)
                                {
                                    if (fi > 0) rowBuilder.Append(Consts.Comma);
                                    rowBuilder.Append(PgConverters.SerializeString(descriptor.CompositeFieldNames[fi]));
                                    rowBuilder.Append(Consts.Colon);
                                    rowBuilder.Append(Consts.Utf8Null);
                                }
                            }
                            else
                            {
                                // Parse tuple to JSON object, strip outer braces to get inline fields
                                var compositeJsonObj = PgTupleToJsonObject(
                                    PgUtf8Converters.DecodeChars(raw, ref _charScratch),
                                    descriptor.CompositeFieldNames,
                                    descriptor.CompositeFieldDescriptors);
                                // PgTupleToJsonObject returns {"field":val,...} — strip { and }
                                if (compositeJsonObj.Length >= 2 && compositeJsonObj[0] == '{' && compositeJsonObj[compositeJsonObj.Length - 1] == '}')
                                {
                                    rowBuilder.Append(compositeJsonObj[1..^1]);
                                }
                                else
                                {
                                    rowBuilder.Append(compositeJsonObj);
                                }
                            }
                        }

                        if (_returnsUnnamedSet == false && i == routineReturnRecordCount - 1)
                        {
                            rowBuilder.Append(Consts.CloseBrace);
                        }
                        if (i < routineReturnRecordCount - 1)
                        {
                            rowBuilder.Append(Consts.Comma);
                        }
                    }
                    else
                    {
                        // Handle nested JSON composite types (routine expanded columns)
                        (string CompositeColumnName, string FieldName, bool IsFirstField, bool IsLastField, int FieldCount, string JsonCompositeColumnName, string JsonFieldName) compositeMapping = default;
                        bool isInComposite = _nestedJsonColumnMap is not null && _nestedJsonColumnMap.TryGetValue(i, out compositeMapping);

                        // Determine which buffer to write to
                        Utf8Buffer outputBuffer = (isInComposite && _compositeFieldBuffer is not null) ? _compositeFieldBuffer : rowBuilder;

                        if (_returnsUnnamedSet == false)
                        {
                            if (i == 0)
                            {
                                rowBuilder.Append(Consts.OpenBrace);
                            }

                            if (isInComposite)
                            {
                                if (compositeMapping.IsFirstField)
                                {
                                    // Start of composite: reset buffer and track column name
                                    _compositeFieldBuffer!.Clear();
                                    _compositeHasNonNullValue = false;
                                    _currentJsonCompositeName = compositeMapping.JsonCompositeColumnName;
                                }
                                // Write field name to the composite buffer
                                outputBuffer.Append(compositeMapping.JsonFieldName);
                                outputBuffer.Append(Consts.Colon);
                            }
                            else
                            {
                                rowBuilder.Append(_utf8JsonColumnNames[i]);
                            }
                        }

                        JsonValueFormatter.FormatValue(
                            raw, isNull, descriptor, outputBuffer,
                            routine.ArrayCompositeColumnInfo, i, ref _charScratch);
                        if (isInComposite && isNull is false)
                        {
                            _compositeHasNonNullValue = true;
                        }

                        // Handle closing braces and commas for nested JSON composite types
                        if (isInComposite && compositeMapping.IsLastField)
                        {
                            // End of composite: decide whether to output null or the buffered object
                            rowBuilder.Append(_currentJsonCompositeName!);
                            rowBuilder.Append(Consts.Colon);

                            if (_compositeHasNonNullValue)
                            {
                                // At least one field has a value, output as object
                                rowBuilder.Append(Consts.OpenBrace);
                                rowBuilder.Append(_compositeFieldBuffer!);
                                rowBuilder.Append(Consts.CloseBrace);
                            }
                            else
                            {
                                // All fields are NULL, output null
                                rowBuilder.Append(Consts.Utf8Null);
                            }
                        }
                        else if (isInComposite)
                        {
                            // Add comma between composite fields
                            outputBuffer.Append(Consts.Comma);
                        }

                        if (_returnsUnnamedSet == false && i == routineReturnRecordCount - 1)
                        {
                            rowBuilder.Append(Consts.CloseBrace);
                        }

                        // Add comma between columns (but not within composite fields which are handled above)
                        if (!isInComposite && i < routineReturnRecordCount - 1)
                        {
                            rowBuilder.Append(Consts.Comma);
                        }
                        else if (isInComposite && compositeMapping.IsLastField && i < routineReturnRecordCount - 1)
                        {
                            rowBuilder.Append(Consts.Comma);
                        }
                    }
                }
            }
        }

        /// <summary>Ends a request's use: returns the buffers to their pools and drops every reference to the request.</summary>
        public void Release()
        {
            if (Row is not null)
            {
                Utf8Buffer.Return(Row);
                Row = null!;
            }
            if (_compositeFieldBuffer is not null)
            {
                Utf8Buffer.Return(_compositeFieldBuffer);
                _compositeFieldBuffer = null;
            }
            if (_scratch.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(_scratch);
                _scratch = [];
            }
            if (_charScratch is not null)
            {
                ArrayPool<char>.Shared.Return(_charScratch);
                _charScratch = null;
            }
            _reader = null!;
            _routine = null!;
            _endpoint = null!;
            _protector = null;
            _columnNames = [];
            _jsonColumnNames = [];
            _utf8JsonColumnNames = [];
            _descriptors = [];
            _nestedJsonColumnMap = null;
            _currentJsonCompositeName = null;
        }
    }

    /// <summary>
    /// UTF-8 form of a result set's JSON property names, each with its colon ("name":), computed once per name array.
    /// </summary>
    private static class Utf8ColumnNames
    {
        private static readonly ConditionalWeakTable<string[], byte[][]> Cache = new();

        public static byte[][] Get(string[] jsonColumnNames) => Cache.GetValue(jsonColumnNames, static names =>
        {
            var result = new byte[names.Length][];
            for (int i = 0; i < names.Length; i++)
            {
                result[i] = Encoding.UTF8.GetBytes(string.Concat(names[i], ":"));
            }
            return result;
        });
    }
}
