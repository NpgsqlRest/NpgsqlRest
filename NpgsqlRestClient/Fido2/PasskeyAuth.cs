using NpgsqlRest;

namespace NpgsqlRestClient.Fido2;

public static class PasskeyAuth
{
    private static ILogger? Logger;

    public static void UsePasskeyAuth(
        this WebApplication app,
        PasskeyConfig? config,
        NpgsqlRestOptions options,
        CommandRetryOptions commandRetryOptions,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        if (config?.Enabled != true)
        {
            return;
        }

        Logger = app.Services.GetService<ILoggerFactory>()?.CreateLogger(options.LoggerName ?? "NpgsqlRest");

        // Resolve command retry strategy from config
        RetryStrategy? commandRetryStrategy = null;
        if (commandRetryOptions.Enabled && !string.IsNullOrEmpty(config.CommandRetryStrategy))
        {
            commandRetryOptions.Strategies.TryGetValue(config.CommandRetryStrategy, out commandRetryStrategy);
        }

        var ctx = new PasskeyEndpointContext(
            config,
            options,
            commandRetryStrategy,
            loggingMode,
            Logger);

        var addPasskeyOptionsEndpoint = new AddPasskeyOptionsEndpoint(ctx);
        var addPasskeyEndpoint = new AddPasskeyEndpoint(ctx);
        var registrationOptionsEndpoint = new RegistrationOptionsEndpoint(ctx);
        var registrationEndpoint = new RegistrationEndpoint(ctx);
        var loginOptionsEndpoint = new LoginOptionsEndpoint(ctx);
        var loginEndpoint = new LoginEndpoint(ctx);

        var rateLimiterPolicy = config.RateLimiterPolicy;

        if (!string.IsNullOrEmpty(config.AddPasskeyOptionsPath))
        {
            var route = app.MapPost(config.AddPasskeyOptionsPath, addPasskeyOptionsEndpoint.InvokeAsync);
            if (!string.IsNullOrEmpty(rateLimiterPolicy))
            {
                route.RequireRateLimiting(rateLimiterPolicy);
            }
        }
        if (!string.IsNullOrEmpty(config.AddPasskeyPath))
        {
            var route = app.MapPost(config.AddPasskeyPath, addPasskeyEndpoint.InvokeAsync);
            if (!string.IsNullOrEmpty(rateLimiterPolicy))
            {
                route.RequireRateLimiting(rateLimiterPolicy);
            }
        }
        if (config.EnableRegister)
        {
            if (!string.IsNullOrEmpty(config.RegistrationOptionsPath))
            {
                var route = app.MapPost(config.RegistrationOptionsPath, registrationOptionsEndpoint.InvokeAsync);
                if (!string.IsNullOrEmpty(rateLimiterPolicy))
                {
                    route.RequireRateLimiting(rateLimiterPolicy);
                }
            }
            if (!string.IsNullOrEmpty(config.RegistrationPath))
            {
                var route = app.MapPost(config.RegistrationPath, registrationEndpoint.InvokeAsync);
                if (!string.IsNullOrEmpty(rateLimiterPolicy))
                {
                    route.RequireRateLimiting(rateLimiterPolicy);
                }
            }
        }
        if (!string.IsNullOrEmpty(config.LoginOptionsPath))
        {
            var route = app.MapPost(config.LoginOptionsPath, loginOptionsEndpoint.InvokeAsync);
            if (!string.IsNullOrEmpty(rateLimiterPolicy))
            {
                route.RequireRateLimiting(rateLimiterPolicy);
            }
        }
        if (!string.IsNullOrEmpty(config.LoginPath))
        {
            var route = app.MapPost(config.LoginPath, loginEndpoint.InvokeAsync);
            if (!string.IsNullOrEmpty(rateLimiterPolicy))
            {
                route.RequireRateLimiting(rateLimiterPolicy);
            }
        }

        Logger?.LogDebug(
            "Passkey authentication endpoints registered: EnableRegister={EnableRegister}, RateLimiterPolicy={RateLimiterPolicy}, " +
            "AddPasskeyOptions={AddPasskeyOptions}, AddPasskey={AddPasskey}, " +
            "RegistrationOptions={RegistrationOptions}, Registration={Registration}, " +
            "LoginOptions={LoginOptions}, Login={Login}",
            config.EnableRegister,
            rateLimiterPolicy ?? "(none)",
            config.AddPasskeyOptionsPath ?? "(disabled)",
            config.AddPasskeyPath ?? "(disabled)",
            config.EnableRegister ? config.RegistrationOptionsPath ?? "(disabled)" : "(disabled by EnableRegister=false)",
            config.EnableRegister ? config.RegistrationPath ?? "(disabled)" : "(disabled by EnableRegister=false)",
            config.LoginOptionsPath ?? "(disabled)",
            config.LoginPath ?? "(disabled)");

        Logger?.LogDebug(
            "Passkey configuration - RP: {RpId}/{RpName}, Origins: [{Origins}], " +
            "UserVerification: {UV}, ResidentKey: {RK}, ValidateSignCount: {SignCount}",
            config.RelyingPartyId ?? "(auto)",
            config.RelyingPartyName ?? "(auto)",
            config.RelyingPartyOrigins.Length > 0 ? string.Join(", ", config.RelyingPartyOrigins) : "(auto)",
            config.UserVerificationRequirement,
            config.ResidentKeyRequirement,
            config.ValidateSignCount);

        Logger?.LogDebug(
            "Passkey SQL commands - ChallengeAddExisting: {ChallengeAddExisting}, ChallengeRegistration: {ChallengeRegistration}, " +
            "ChallengeAuthentication: {ChallengeAuthentication}",
            config.ChallengeAddExistingUserCommand,
            config.ChallengeRegistrationCommand,
            config.ChallengeAuthenticationCommand);

        Logger?.LogDebug(
            "Passkey SQL commands - ChallengeVerify: {ChallengeVerify}, AuthenticateData: {AuthenticateData}",
            config.VerifyChallengeCommand,
            config.AuthenticateDataCommand);

        Logger?.LogDebug(
            "Passkey SQL commands - AddExistingComplete: {AddExistingComplete}, RegistrationComplete: {RegistrationComplete}, " +
            "AuthenticateComplete: {AuthenticateComplete}",
            config.CompleteAddExistingUserCommand,
            config.CompleteRegistrationCommand,
            config.CompleteAuthenticateCommand);
    }
}
