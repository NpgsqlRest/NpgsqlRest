using System.Security.Cryptography;

namespace NpgsqlRest.Auth;

public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a password.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <returns>A hashed representation of the password.</returns>
    string HashPassword(string password);

    /// <summary>
    /// Verifies a provided password against a stored hash.
    /// </summary>
    /// <param name="hashedPassword">The stored hashed password.</param>
    /// <param name="providedPassword">The password to verify.</param>
    /// <returns>True if the password matches, false otherwise.</returns>
    bool VerifyHashedPassword(string hashedPassword, string providedPassword);
}

public class PasswordHasher : IPasswordHasher
{
    // Configuration constants
    private const int SaltByteSize = 16; // 128-bit salt
    private const int HashByteSize = 32; // 256-bit hash
    private const int Iterations = 600_000; // OWASP-recommended iteration count for PBKDF2-SHA256 (2025)

    /// <summary>
    /// Hashes a password.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <returns>A hashed representation of the password.</returns>
    /// <exception cref="ArgumentException">Thrown if the password is null or empty.</exception>
    public string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty.", nameof(password));

        // Generate a random salt
        byte[] salt = RandomNumberGenerator.GetBytes(SaltByteSize);

        // Hash the password using PBKDF2
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashByteSize);

        // Combine salt and hash into a single byte array
        byte[] hashBytes = new byte[SaltByteSize + HashByteSize];
        Array.Copy(salt, 0, hashBytes, 0, SaltByteSize);
        Array.Copy(hash, 0, hashBytes, SaltByteSize, HashByteSize);

        // Convert to base64 for storage
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Verifies a provided password against a stored hash.
    /// </summary>
    /// <param name="hashedPassword">The stored hashed password.</param>
    /// <param name="providedPassword">The password to verify.</param>
    /// <returns>True if the password matches, false otherwise.</returns>
    public bool VerifyHashedPassword(string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(providedPassword) || string.IsNullOrEmpty(hashedPassword))
            return false;

        try
        {
            // Decode the stored hash
            byte[] hashBytes = Convert.FromBase64String(hashedPassword);

            // Validate the stored hash length
            if (hashBytes.Length != SaltByteSize + HashByteSize)
                return false;

            // Extract the salt
            byte[] salt = new byte[SaltByteSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltByteSize);

            // Extract the hash
            byte[] storedSubHash = new byte[HashByteSize];
            Array.Copy(hashBytes, SaltByteSize, storedSubHash, 0, HashByteSize);

            // Compute the hash of the provided password
            byte[] computedHash = Rfc2898DeriveBytes.Pbkdf2(providedPassword, salt, Iterations, HashAlgorithmName.SHA256, HashByteSize);

            // Compare the hashes in constant time
            return CryptographicOperations.FixedTimeEquals(computedHash, storedSubHash);
        }
        catch
        {
            // Handle invalid base64 or other errors
            return false;
        }
    }
}

