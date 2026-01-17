using Microsoft.AspNetCore.Builder;

namespace NpgsqlRestTests.Setup;

/// <summary>
/// Marker class for PoLP (Principle of Least Privilege) tests WebApplicationFactory.
/// The actual configuration is done in PolpTestFixture via ConfigureWebHost.
/// </summary>
public class PolpProgram
{
    // This is intentionally empty - it's just a marker type for WebApplicationFactory<PolpProgram>
    // The actual app configuration happens in PolpTestFixture
}
