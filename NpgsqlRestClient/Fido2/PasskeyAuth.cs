using NpgsqlRest;

namespace NpgsqlRestClient.Fido2;

public static class PasskeyAuth
{
    private static ILogger? Logger;

    public static void UsePasskeyAuth(
        this WebApplication app,
        PasskeyConfig? config,
        string connectionString,
        NpgsqlRestOptions options,
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        if (config?.Enabled != true)
        {
            return;
        }

        Logger = app.Services.GetService<ILoggerFactory>()?.CreateLogger("PasskeyAuth");

        var ctx = new PasskeyEndpointContext(
            config,
            connectionString,
            options,
            retryStrategy,
            loggingMode,
            Logger);

        var addPasskeyOptionsEndpoint = new AddPasskeyOptionsEndpoint(ctx);
        var addPasskeyEndpoint = new AddPasskeyEndpoint(ctx);
        var registrationOptionsEndpoint = new RegistrationOptionsEndpoint(ctx);
        var registrationEndpoint = new RegistrationEndpoint(ctx);
        var loginOptionsEndpoint = new LoginOptionsEndpoint(ctx);
        var loginEndpoint = new LoginEndpoint(ctx);
        if (!string.IsNullOrEmpty(config.AddPasskeyOptionsPath))
        {
            app.MapPost(config.AddPasskeyOptionsPath, addPasskeyOptionsEndpoint.InvokeAsync);
        }
        if (!string.IsNullOrEmpty(config.AddPasskeyPath))
        {
            app.MapPost(config.AddPasskeyPath, addPasskeyEndpoint.InvokeAsync);
        }
        if (config.EnableRegister)
        {
            if (!string.IsNullOrEmpty(config.RegistrationOptionsPath))
            {
                app.MapPost(config.RegistrationOptionsPath, registrationOptionsEndpoint.InvokeAsync);
            }
            if (!string.IsNullOrEmpty(config.RegistrationPath))
            {
                app.MapPost(config.RegistrationPath, registrationEndpoint.InvokeAsync);
            }
        }
        if (!string.IsNullOrEmpty(config.LoginOptionsPath))
        {
            app.MapPost(config.LoginOptionsPath, loginOptionsEndpoint.InvokeAsync);
        }
        if (!string.IsNullOrEmpty(config.LoginPath))
        {
            app.MapPost(config.LoginPath, loginEndpoint.InvokeAsync);
        }

        Logger?.LogDebug(
            "Passkey authentication endpoints registered: EnableRegister={EnableRegister}, " +
            "AddPasskeyOptions={AddPasskeyOptions}, AddPasskey={AddPasskey}, " +
            "RegistrationOptions={RegistrationOptions}, Registration={Registration}, " +
            "LoginOptions={LoginOptions}, Login={Login}",
            config.EnableRegister,
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
