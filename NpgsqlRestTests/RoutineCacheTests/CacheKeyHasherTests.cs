using NpgsqlRest;

namespace NpgsqlRestTests;

public class CacheKeyHasherTests
{
    [Fact]
    public void ComputeHash_ReturnsFixed64CharacterString()
    {
        var key = "test-cache-key";
        var hash = CacheKeyHasher.ComputeHash(key);

        hash.Should().HaveLength(64, "SHA256 hash should be 64 hex characters");
    }

    [Fact]
    public void ComputeHash_SameInput_ReturnsSameHash()
    {
        var key = "test-cache-key";
        var hash1 = CacheKeyHasher.ComputeHash(key);
        var hash2 = CacheKeyHasher.ComputeHash(key);

        hash1.Should().Be(hash2, "same input should produce same hash");
    }

    [Fact]
    public void ComputeHash_DifferentInput_ReturnsDifferentHash()
    {
        var hash1 = CacheKeyHasher.ComputeHash("key1");
        var hash2 = CacheKeyHasher.ComputeHash("key2");

        hash1.Should().NotBe(hash2, "different inputs should produce different hashes");
    }

    [Fact]
    public void ComputeHash_LongKey_ReturnsFixed64CharacterString()
    {
        var longKey = new string('x', 10000);
        var hash = CacheKeyHasher.ComputeHash(longKey);

        hash.Should().HaveLength(64, "even long keys should produce 64 character hash");
    }

    [Fact]
    public void ComputeHash_EmptyString_ReturnsValidHash()
    {
        var hash = CacheKeyHasher.ComputeHash(string.Empty);

        hash.Should().HaveLength(64);
        hash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetEffectiveKey_HashingDisabled_ReturnsOriginalKey()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = false,
            HashKeyThreshold = 256
        };
        var longKey = new string('x', 1000);

        var effectiveKey = CacheKeyHasher.GetEffectiveKey(longKey, options);

        effectiveKey.Should().Be(longKey, "when hashing is disabled, original key should be returned");
    }

    [Fact]
    public void GetEffectiveKey_HashingEnabled_KeyBelowThreshold_ReturnsOriginalKey()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 256
        };
        var shortKey = new string('x', 100);

        var effectiveKey = CacheKeyHasher.GetEffectiveKey(shortKey, options);

        effectiveKey.Should().Be(shortKey, "keys below threshold should not be hashed");
    }

    [Fact]
    public void GetEffectiveKey_HashingEnabled_KeyAtThreshold_ReturnsOriginalKey()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 256
        };
        var keyAtThreshold = new string('x', 256);

        var effectiveKey = CacheKeyHasher.GetEffectiveKey(keyAtThreshold, options);

        effectiveKey.Should().Be(keyAtThreshold, "keys at exactly threshold should not be hashed");
    }

    [Fact]
    public void GetEffectiveKey_HashingEnabled_KeyAboveThreshold_ReturnsHashedKey()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 256
        };
        var longKey = new string('x', 257);

        var effectiveKey = CacheKeyHasher.GetEffectiveKey(longKey, options);

        effectiveKey.Should().HaveLength(64, "keys above threshold should be hashed to 64 chars");
        effectiveKey.Should().NotBe(longKey);
    }

    [Fact]
    public void GetEffectiveKey_HashingEnabled_VeryLongKey_ReturnsHashedKey()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 256
        };
        var veryLongKey = new string('x', 10000);

        var effectiveKey = CacheKeyHasher.GetEffectiveKey(veryLongKey, options);

        effectiveKey.Should().HaveLength(64, "very long keys should be hashed to 64 chars");
    }

    [Fact]
    public void GetEffectiveKey_HashingEnabled_DifferentLongKeys_ProduceDifferentHashes()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 256
        };
        var longKey1 = new string('x', 500) + "a";
        var longKey2 = new string('x', 500) + "b";

        var effectiveKey1 = CacheKeyHasher.GetEffectiveKey(longKey1, options);
        var effectiveKey2 = CacheKeyHasher.GetEffectiveKey(longKey2, options);

        effectiveKey1.Should().NotBe(effectiveKey2, "different long keys should produce different hashes");
    }

    [Fact]
    public void GetEffectiveKey_CustomThreshold_RespectsThreshold()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 50
        };
        var key51Chars = new string('x', 51);
        var key50Chars = new string('x', 50);

        var effectiveKey51 = CacheKeyHasher.GetEffectiveKey(key51Chars, options);
        var effectiveKey50 = CacheKeyHasher.GetEffectiveKey(key50Chars, options);

        effectiveKey51.Should().HaveLength(64, "key above custom threshold should be hashed");
        effectiveKey50.Should().HaveLength(50, "key at custom threshold should not be hashed");
    }

    [Fact]
    public void GetEffectiveKey_ConsistentHashing_SameKeyReturnsSameHash()
    {
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 100
        };
        var longKey = new string('x', 500);

        var effectiveKey1 = CacheKeyHasher.GetEffectiveKey(longKey, options);
        var effectiveKey2 = CacheKeyHasher.GetEffectiveKey(longKey, options);

        effectiveKey1.Should().Be(effectiveKey2, "same key should always produce same hash");
    }

    [Fact]
    public void ComputeHash_OnlyHexCharacters()
    {
        var hash = CacheKeyHasher.ComputeHash("test-key");

        hash.Should().MatchRegex("^[0-9A-F]+$", "hash should only contain uppercase hex characters");
    }

    [Fact]
    public void GetEffectiveKey_PreservesCollisionResistance()
    {
        // Ensure that keys that would collide without the separator still produce different hashes
        var options = new CacheOptions
        {
            UseHashedCacheKeys = true,
            HashKeyThreshold = 10
        };

        // These could potentially collide if not handled correctly
        var key1 = "ab" + "\x1F" + "c"; // separator between ab and c
        var key2 = "a" + "\x1F" + "bc"; // separator between a and bc

        // Make them long enough to trigger hashing
        key1 = new string('x', 100) + key1;
        key2 = new string('x', 100) + key2;

        var effectiveKey1 = CacheKeyHasher.GetEffectiveKey(key1, options);
        var effectiveKey2 = CacheKeyHasher.GetEffectiveKey(key2, options);

        effectiveKey1.Should().NotBe(effectiveKey2, "keys with different content should produce different hashes");
    }
}
