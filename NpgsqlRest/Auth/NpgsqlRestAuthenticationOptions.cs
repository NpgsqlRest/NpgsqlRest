
using Microsoft.AspNetCore.DataProtection;

namespace NpgsqlRest.Auth;

/// <summary>
/// Authentication options for the NpgsqlRest middleware.
/// </summary>
public class NpgsqlRestAuthenticationOptions
{
    /// <summary>
    /// Authentication type used with the Login endpoints to set the authentication type for the new `ClaimsIdentity` created by the login.
    ///
    /// This value must be set to non-null when using login endpoints, otherwise, the following error will raise: `SignInAsync when principal.Identity.IsAuthenticated is false is not allowed when AuthenticationOptions.RequireAuthenticatedSignIn is true.`
    /// 
    /// If the value is not set and the login endpoint is present, it will automatically get the database name from the connection string.
    /// </summary>
    public string? DefaultAuthenticationType { get; set; } = null;

    /// <summary>
    /// The default column name in the data reader which will be used to read the value to determine the success or failure of the login operation.
    /// 
    /// - If this column is not present, the success is when the endpoint returns any records.
    /// - If this column is present, it must be either a boolean to indicate success or a numeric value to indicate the HTTP Status Code to return.
    /// - If this column is present and retrieves a numeric value, that value is assigned to the HTTP Status Code and the login will authenticate only when this value is 200.
    /// </summary>
    public string? StatusColumnName { get; set; } = "status";

    /// <summary>
    /// The default column name in the data reader which will be used to read the value of the authentication scheme of the login process.
    /// 
    /// If this column is not present in the login response the default authentication scheme is used. Return new value to use a different authentication scheme with the login endpoint.
    /// </summary>
    public string? SchemeColumnName { get; set; } = "scheme";

    /// <summary>
    /// The default column name in the data reader which will return a response body message for the login operation where writing to body is possible.
    /// </summary>
    public string? BodyColumnName { get; set; } = "body";

    /// <summary>
    /// The default column name in the data reader which will set the response content type for the login operation where writing to body is possible.
    /// </summary>
    public string ResponseTypeColumnName { get; set; } = "application/json";
    
    /// <summary>
    /// Default claim type for user id.
    /// </summary>
    public string DefaultUserIdClaimType { get; set; } = "user_id"; // ClaimTypes.NameIdentifier;

    /// <summary>
    /// Default claim type for user name.
    /// </summary>
    public string DefaultNameClaimType { get; set; } = "user_name"; // ClaimTypes.Name;

    /// <summary>
    /// Default claim type for user roles.
    /// </summary>
    public string DefaultRoleClaimType { get; set; } = "user_roles"; // ClaimTypes.Role;

    /// <summary>
    /// If true, return any response from auth endpoints (login and logout) if response hasn't been written by auth handler.
    /// For cookie auth, this will return full record to response as returned by the routine.
    /// For bearer token auth, this will be ignored because bearer token auth writes it's own response (with tokens).
    /// This option will also be ignored if message column is present (see BodyColumnName option).
    /// Default is false.
    /// </summary>
    public bool SerializeAuthEndpointsResponse { get; set; } = false;

    /// <summary>
    /// Don't write real parameter values when logging parameters from auth endpoints and obfuscate instead.
    /// This prevents user credentials including password to end up in application logs.
    /// Default is true.
    /// </summary>
    public bool ObfuscateAuthParameterLogValues { get; set; } = true;

    /// <summary>
    /// The default column name in the data reader which will be used to read the value of the hash of the password.
    /// If this column is present, the value will be used to verify the password from the password parameter.
    /// Password parameter is the first parameter which name contains the value of PasswordParameterNameContains.
    /// If verification fails, the login will fail and the HTTP Status Code will be set to 404 Not Found.
    /// </summary>
    public string HashColumnName { get; set; } = "hash";

    /// <summary>
    /// The default name of the password parameter. The first parameter which name contains this value will be used as the password parameter.
    /// This is used to verify the password from the password parameter when login endpoint returns a hash of the password (see HashColumnName).
    /// </summary>
    public string PasswordParameterNameContains { get; set; } = "pass";

    /// <summary>
    /// Default password hasher object. Inject custom password hasher object to add default password hasher.
    /// </summary>
    public IPasswordHasher? PasswordHasher { get; set; } = new PasswordHasher();

    /// <summary>
    /// Command that is executed when the password verification fails. There are three text parameters:
    ///     - authentication scheme used for the login (if exists)
    ///     - user id used for the login (if exists)
    ///     - user name used for the login (if exists)
    /// Please use PostgreSQL parameter placeholders for the parameters ($1, $2, $3).
    /// </summary>
    public string? PasswordVerificationFailedCommand { get; set; } = null;

    /// <summary>
    /// Command that is executed when the password verification succeeds. There are three text parameters:
    ///     - authentication scheme used for the login (if exists)
    ///     - user id used for the login (if exists)
    ///     - user name used for the login (if exists)
    /// Please use PostgreSQL parameter placeholders for the parameters ($1, $2, $3).
    /// </summary>
    public string? PasswordVerificationSucceededCommand { get; set; } = null;

    /// <summary>
    /// Enable setting authenticated user claims to context variables automatically.
    /// See ContextKeyClaimsMapping and ClaimsJsonContextKey options.
    /// You can set this individually for each request by using UserContext endpoint property or user_context comment annotation.
    /// </summary>
    public bool UseUserContext { get; set; } = false;

    /// <summary>
    /// Mapping of context keys to user claim names.
    /// Keys are the context variable names and values are the user claim names.
    /// When <see cref="UseUserContext"/> is enabled, the user claims from will be automatically mapped to the context variables.
    /// </summary>
    public Dictionary<string, string> ContextKeyClaimsMapping { get; set; } = new()
    {
        { "request.user_id", "user_id" },
        { "request.user_name", "user_name" },
        { "request.user_roles" , "user_roles" },
    };

    /// <summary>
    /// Context key that is used to set context variable for all available user claims.
    /// When this option is not null, and user is authenticated, the user claims will be serialized to JSON value and set to the context variable.
    /// </summary>
    public string? ClaimsJsonContextKey { get; set; } = null;

    /// <summary>
    /// IP address context key that is used to set context variable for the IP address.
    /// When this option is not null, the IP address will be set to the context variable when <see cref="UseUserContext"/> is enabled and even when user is not authenticated.
    /// </summary>
    public string? IpAddressContextKey { get; set; } = "request.ip_address";

    /// <summary>
    /// Enable mapping authenticated user claims to parameters by name automatically.
    /// See ParameterNameClaimsMapping and ClaimsJsonParameterName options.
    /// You can set this individually for each request by using UseUserParameters endpoint property or user_parameters comment annotation.
    /// </summary>
    public bool UseUserParameters { get; set; } = false;

    /// <summary>
    /// Mapping of parameter names to user claim names.
    /// Keys are the parameter names and values are the user claim names.
    /// When <see cref="UseUserParameters"/> is enabled, the user claims from will be automatically mapped to the parameters.
    /// </summary>
    public Dictionary<string, string> ParameterNameClaimsMapping { get; set; } = new()
    {
        { "_user_id" , "user_id" },
        { "_user_name" , "user_name" },
        { "_user_roles" , "user_roles" },
    };

    /// <summary>
    /// Parameter name that is used to set value for all available user claims.
    /// When this option is not null, and user is authenticated, the user claims will be serialized to JSON value and set to the parameter with this name.
    /// </summary>
    public string? ClaimsJsonParameterName { get; set; } = "_user_claims";

    /// <summary>
    /// IP address parameter name that is used to set parameter value for the IP address.
    /// When this option is not null, the IP address will be set to the parameter when <see cref="UseUserContext"/> is enabled and even when user is not authenticated.
    /// </summary>
    public string? IpAddressParameterName { get; set; } = "_ip_address";

    /// <summary>
    /// Default options for Basic Authentication.
    /// </summary>
    public BasicAuthOptions BasicAuth { get; set; } = new();
    
    /// <summary>
    /// Default data protector used to encrypt and decrypt data.
    /// </summary>
    public IDataProtector? DefaultDataProtector { get; set; } = null;
}