using System.Formats.Cbor;

namespace NpgsqlRestClient.Fido2;

public static class CborDecoder
{
    internal static ILogger? Logger;

    public static AttestationObject? DecodeAttestationObject(byte[] data)
    {
        try
        {
            var reader = new CborReader(data, CborConformanceMode.Lax);

            string fmt = "none";
            byte[] authData = [];
            Dictionary<object, object>? attStmt = null;

            var mapLength = reader.ReadStartMap();

            for (int i = 0; i < mapLength; i++)
            {
                var key = reader.ReadTextString();

                switch (key)
                {
                    case "fmt":
                        fmt = reader.ReadTextString();
                        break;
                    case "authData":
                        authData = reader.ReadByteString();
                        break;
                    case "attStmt":
                        attStmt = ReadMap(reader);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }

            reader.ReadEndMap();

            return new AttestationObject
            {
                Fmt = fmt,
                AuthData = authData,
                AttStmt = attStmt
            };
        }
        catch (Exception e)
        {
            // Never log the payload itself (attacker-controlled); length + exception is enough to diagnose.
            Logger?.LogWarning(e,
                "Failed to decode CBOR attestation object ({Length} bytes): {Message}",
                data?.Length ?? 0, e.Message);
            return null;
        }
    }

    private static Dictionary<object, object> ReadMap(CborReader reader)
    {
        var result = new Dictionary<object, object>();
        var mapLength = reader.ReadStartMap();

        for (int i = 0; i < mapLength; i++)
        {
            var key = ReadValue(reader);
            var value = ReadValue(reader);
            if (key != null)
                result[key] = value!;
        }

        reader.ReadEndMap();
        return result;
    }

    private static object? ReadValue(CborReader reader)
    {
        var state = reader.PeekState();

        return state switch
        {
            CborReaderState.UnsignedInteger => reader.ReadInt64(),
            CborReaderState.NegativeInteger => reader.ReadInt64(),
            CborReaderState.ByteString => reader.ReadByteString(),
            CborReaderState.TextString => reader.ReadTextString(),
            CborReaderState.StartArray => ReadArray(reader),
            CborReaderState.StartMap => ReadMap(reader),
            CborReaderState.Boolean => reader.ReadBoolean(),
            CborReaderState.Null => ReadNull(reader),
            CborReaderState.Tag => ReadTaggedValue(reader),
            CborReaderState.HalfPrecisionFloat or
            CborReaderState.SinglePrecisionFloat or
            CborReaderState.DoublePrecisionFloat => reader.ReadDouble(),
            _ => SkipAndReturnNull(reader)
        };
    }

    private static object? ReadNull(CborReader reader)
    {
        reader.ReadNull();
        return null;
    }

    private static object? SkipAndReturnNull(CborReader reader)
    {
        reader.SkipValue();
        return null;
    }

    private static object?[] ReadArray(CborReader reader)
    {
        var length = reader.ReadStartArray();
        if (length is null)
        {
            // Indefinite-length array (legal in lax conformance mode): read until end marker.
            var items = new List<object?>();
            while (reader.PeekState() != CborReaderState.EndArray)
            {
                items.Add(ReadValue(reader));
            }
            reader.ReadEndArray();
            return [.. items];
        }

        var result = new object?[length.Value];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ReadValue(reader);
        }

        reader.ReadEndArray();
        return result;
    }

    private static object? ReadTaggedValue(CborReader reader)
    {
        reader.ReadTag(); // Read and discard the tag
        return ReadValue(reader);
    }
}

public class AttestationObject
{
    public string Fmt { get; set; } = "none";

    public byte[] AuthData { get; set; } = [];

    public Dictionary<object, object>? AttStmt { get; set; }
}
