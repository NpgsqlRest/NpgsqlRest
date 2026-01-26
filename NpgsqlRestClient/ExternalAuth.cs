using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.Auth;
using static NpgsqlRest.NpgsqlRestOptions;
using static System.Net.Mime.MediaTypeNames;

namespace NpgsqlRestClient;

public class ExternalAuthClientConfig
{
    public bool Enabled { get; set; } = false;
    public string ClientId { get; set; } = default!;
    public string ClientSecret { get; set; } = default!;
    public string AuthUrl { get; set; } = default!;
    public string TokenUrl { get; set; } = default!;
    public string InfoUrl { get; set; } = default!;
    public string? EmailUrl { get; set; } = null;
    public string SigninUrl { get; set; } = default!;
    public string ExternalType { get; set; } = default!;
}

public class ExternalAuthConfig
{
    public bool Enabled { get; set; } = false;
    public string BrowserSessionStatusKey { get; private set; } = default!;
    public string BrowserSessionMessageKey { get; private set; } = default!;
    public string SignInHtmlTemplate { get; private set; } = default!;
    public string? RedirectUrl { get; private set; } = null;
    public string ReturnToPath { get; private set; } = default!;
    public string ReturnToPathQueryStringKey { get; private set; } = default!;
    public string LoginCommand { get; private set; } = default!;
    public string ClientAnalyticsData { get; private set; } = default!;
    public string ClientAnalyticsIpKey { get; private set; } = default!;

    private readonly Dictionary<string, ExternalAuthClientConfig> DefaultClientConfigs = 
        new()
        {
        { "google", new ExternalAuthClientConfig
            {
                AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={0}&redirect_uri={1}&scope=openid profile email&state={2}",
                TokenUrl = "https://oauth2.googleapis.com/token",
                InfoUrl = "https://www.googleapis.com/oauth2/v3/userinfo",
            }
        },
        { "linkedin", new ExternalAuthClientConfig
            {
                AuthUrl = "https://www.linkedin.com/oauth/v2/authorization?response_type=code&client_id={0}&redirect_uri={1}&state={2}&scope=r_liteprofile%20r_emailaddress",
                TokenUrl = "https://www.linkedin.com/oauth/v2/accessToken",
                InfoUrl = "https://api.linkedin.com/v2/me",
                EmailUrl = "https://api.linkedin.com/v2/emailAddress?q=members&projection=(elements//(handle~))"
            }
        },
        { "github", new ExternalAuthClientConfig
            {
                AuthUrl = "https://github.com/login/oauth/authorize?client_id={0}&redirect_uri={1}&state={2}&allow_signup=false",
                TokenUrl = "https://github.com/login/oauth/access_token",
                InfoUrl = "https://api.github.com/user",
            }
        },
        { "microsoft", new ExternalAuthClientConfig
            {
                AuthUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?response_type=code&client_id={0}&redirect_uri={1}&scope=openid%20profile%20email&state={2}",
                TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                InfoUrl = "https://graph.microsoft.com/oidc/userinfo",
            }
        },
        { "facebook", new ExternalAuthClientConfig
            {
                AuthUrl = "https://www.facebook.com/v20.0/dialog/oauth?response_type=code&client_id={0}&redirect_uri={1}&scope=public_profile%20email&state={2}",
                TokenUrl = "https://graph.facebook.com/v20.0/oauth/access_token",
                InfoUrl = "https://graph.facebook.com/me?fields=id,name,email",
            }
        }
    };

    public Dictionary<string, ExternalAuthClientConfig> ClientConfigs { get; private set; } = [];

    public void Build(IConfigurationSection authConfig, Config config, Builder builder)
    {
        var externalCfg = authConfig.GetSection("External");
        if (config.Exists(externalCfg) is false || config.GetConfigBool("Enabled", externalCfg) is false)
        {
            return;
        }
        Enabled = true;
        foreach (var section in externalCfg.GetChildren())
        {
            if (section.GetChildren().Any() is false)
            {
                continue;
            }
            var signinUrlTemplate = config.GetConfigStr("SigninUrl", externalCfg) ?? "/signin-{0}";

            if (config.GetConfigBool("Enabled", section) is false)
            {
                continue;
            }
            var key = section.Key.ToLowerInvariant();
            var signinUrl = string.Format(signinUrlTemplate, key);

            if (DefaultClientConfigs.TryGetValue(key, out var clientConfig) is false)
            {
                clientConfig = new ExternalAuthClientConfig();
            }

            clientConfig.Enabled = config.GetConfigBool("Enabled", section, clientConfig.Enabled);
            clientConfig.ClientId = config.GetConfigStr("ClientId", section) ?? clientConfig.ClientId;
            clientConfig.ClientSecret = config.GetConfigStr("ClientSecret", section) ?? clientConfig.ClientSecret;
            clientConfig.AuthUrl = config.GetConfigStr("AuthUrl", section) ?? clientConfig.AuthUrl;
            clientConfig.TokenUrl = config.GetConfigStr("TokenUrl", section) ?? clientConfig.TokenUrl;
            clientConfig.InfoUrl = config.GetConfigStr("InfoUrl", section) ?? clientConfig.InfoUrl;
            clientConfig.EmailUrl = config.GetConfigStr("EmailUrl", section) ?? clientConfig.EmailUrl;
            clientConfig.SigninUrl = signinUrl;
            clientConfig.ExternalType = section.Key;

            ClientConfigs.Add(clientConfig.SigninUrl, clientConfig);
            builder.Logger?.LogDebug("External login available for {Key} available on path: {signinUrl}", section.Key, signinUrl);
        }

        if (ClientConfigs.Where(c => c.Value.Enabled).Any() is false)
        {
            Enabled = false;
            return;
        }

        BrowserSessionStatusKey = config.GetConfigStr("BrowserSessionStatusKey", externalCfg) ?? "__external_status";
        BrowserSessionMessageKey = config.GetConfigStr("BrowserSessionMessageKey", externalCfg) ?? "__external_message";

        SignInHtmlTemplate = config.GetConfigStr("SignInHtmlTemplate", externalCfg) ?? 
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><title>Talking To {0}</title></head><body>Loading...{1}</body></html>";
        RedirectUrl = config.GetConfigStr("RedirectUrl", externalCfg);
        ReturnToPath = config.GetConfigStr("ReturnToPath", externalCfg) ?? "/";
        ReturnToPathQueryStringKey = config.GetConfigStr("ReturnToPathQueryStringKey", externalCfg) ?? "return_to";
        LoginCommand = config.GetConfigStr("LoginCommand", externalCfg) ?? "select * from auth.login($1,$2,$3,$4)";

        ClientAnalyticsData = config.GetConfigStr("ClientAnalyticsData", externalCfg) ?? "{timestamp:new Date().toISOString(),timezone:Intl.DateTimeFormat().resolvedOptions().timeZone,screen:{width:window.screen.width,height:window.screen.height,colorDepth:window.screen.colorDepth,pixelRatio:window.devicePixelRatio,orientation:screen.orientation.type},browser:{userAgent:navigator.userAgent,language:navigator.language,languages:navigator.languages,cookiesEnabled:navigator.cookieEnabled,doNotTrack:navigator.doNotTrack,onLine:navigator.onLine,platform:navigator.platform,vendor:navigator.vendor},memory:{deviceMemory:navigator.deviceMemory,hardwareConcurrency:navigator.hardwareConcurrency},window:{innerWidth:window.innerWidth,innerHeight:window.innerHeight,outerWidth:window.outerWidth,outerHeight:window.outerHeight},location:{href:window.location.href,hostname:window.location.hostname,pathname:window.location.pathname,protocol:window.location.protocol,referrer:document.referrer},performance:{navigation:{type:performance.navigation?.type,redirectCount:performance.navigation?.redirectCount},timing:performance.timing?{loadEventEnd:performance.timing.loadEventEnd,loadEventStart:performance.timing.loadEventStart,domComplete:performance.timing.domComplete,domInteractive:performance.timing.domInteractive,domContentLoadedEventEnd:performance.timing.domContentLoadedEventEnd}:null}}";
        ClientAnalyticsIpKey = config.GetConfigStr("ClientAnalyticsIpKey", externalCfg) ?? "ip";
    }
}

public class ExternalAuth
{
    public ExternalAuth(
        ExternalAuthConfig? externalAuthConfig, 
        string connectionString, 
        WebApplication app, 
        NpgsqlRestOptions options, 
        RetryStrategy? retryStrategy,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        if ((externalAuthConfig?.ClientConfigs!).Any(c => c.Value.Enabled) is false)
        {
            return;
        }
        
        app.Use(async (context, next) =>
        {
            if (externalAuthConfig?.ClientConfigs.TryGetValue(context.Request.Path, out var config) is not true)
            {
                await next(context);
                return;
            }

            if (config.Enabled is false)
            {
                await next(context);
                return;
            }

            if (string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                PrepareResponse(Text.Html, context);
                await context.Response.WriteAsync(BuildHtmlTemplate(config, context.Request, externalAuthConfig));
                await context.Response.CompleteAsync();
                return;
            }

            if (string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                PrepareResponse(Text.Plain, context);
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                try
                {
                    var body = await reader.ReadToEndAsync();
                    JsonNode node = JsonNode.Parse(body) ??
                        throw new ArgumentException("json node is null");
                    string code = (node["code"]?.ToString()) ??
                        throw new ArgumentException("code retrieved from the external provider is null");

                    await ProcessAsync(
                        code, 
                        config, 
                        context, 
                        options, 
                        connectionString,
                        retryStrategy,
                        externalAuthConfig, 
                        loggingMode);
                    return;
                }
                catch (Exception e)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    await context.Response.WriteAsync(string.Format("Error talking to {0}", config.ExternalType));
                    await context.Response.CompleteAsync();
                    Logger?.LogError(e, "Failed to parse external provider response: {ExternalType}", config.ExternalType);
                    return;
                }
            }

            await next(context);
        });
    }
    
    private void PrepareResponse(string contentType, HttpContext context)
    {
        context.Response.ContentType = contentType;
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    private static HttpClient? _httpClient = null;
    private static readonly string _agent = $"{Guid.NewGuid().ToString()[..8]}";

    private const string browserSessionStateKey = "__external_state";
    private const string browserSessionParamsKey = "__external_params";

    private async Task ProcessAsync(
        string code,
        ExternalAuthClientConfig config, 
        HttpContext context, 
        NpgsqlRestOptions options,
        string connectionString,
        RetryStrategy? retryStrategy,
        ExternalAuthConfig externalAuthConfig,
        PostgresConnectionNoticeLoggingMode loggingMode)
    {
        string? email;
        string? name;
        string token;

        _httpClient ??= new();

        using var requestTokenMessage = new HttpRequestMessage(HttpMethod.Post, config.TokenUrl);
        requestTokenMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(Application.Json));
        requestTokenMessage.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["redirect_uri"] = GetRedirectUrl(config, context.Request, externalAuthConfig),
            ["code"] = code,
            ["client_id"] = config.ClientId,
            ["client_secret"] = config.ClientSecret,
            ["grant_type"] = "authorization_code"
        });
        requestTokenMessage.Headers.UserAgent.ParseAdd(_agent);

        using var tokenResponse = await _httpClient.SendAsync(requestTokenMessage);
        var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Token endpoint {config.TokenUrl} returned {tokenResponse.StatusCode} with following content: {tokenContent}");
        }
        if (string.IsNullOrEmpty(tokenContent))
        {
            throw new HttpRequestException($"Token endpoint {config.TokenUrl} returned {tokenResponse.StatusCode} with empty content.");
        }

        try
        {
            JsonNode node = JsonNode.Parse(tokenContent) ??
                throw new ArgumentException("token json node is null");
            token = (node["access_token"]?.ToString()) ??
                throw new ArgumentException("access_token retrieved from the external provider is null");
        }
        catch (Exception e)
        {
            throw new ArgumentException(string.Format("Failed to parse the token response: {0}", e.Message), e);
        }

        using var infoRequest = new HttpRequestMessage(HttpMethod.Get, config.InfoUrl);
        infoRequest.Headers.UserAgent.ParseAdd(_agent);
        infoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var infoResponse = await _httpClient.SendAsync(infoRequest);
        var infoContent = await infoResponse.Content.ReadAsStringAsync();
        JsonNode infoNode;
        if (!infoResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Info endpoint {config.InfoUrl} returned {infoResponse.StatusCode} with following content: {infoContent}");
        }
        if (string.IsNullOrEmpty(infoContent))
        {
            throw new HttpRequestException($"Info endpoint {config.InfoUrl} returned {infoResponse.StatusCode} with empty content.");
        }

        try
        {
            infoNode = JsonNode.Parse(infoContent) ??
                throw new ArgumentException("info json node is null");

            // Email parsing
            email = infoNode["email"]?.ToString() ?? // Google, GitHub, Facebook, Microsoft
                    infoNode["userPrincipalName"]?.ToString() ?? // Microsoft (organizational accounts)
                    infoNode?["elements"]?[0]?["handle~"]?["emailAddress"]?.ToString(); // LinkedIn

#pragma warning disable CS8602 // Dereference of a possibly null reference.
            name = infoNode["localizedLastName"] is not null ?
                $"{infoNode["localizedFirstName"]} {infoNode["localizedLastName"]}".Trim() : // linkedin format
                infoNode["name"]?.ToString(); // normal format
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        catch (Exception e)
        {
            throw new ArgumentException(string.Format("Failed to parse the info response: {0}", e.Message), e);
        }

        if (email is null && config.EmailUrl is not null)
        {
            using var emailRequest = new HttpRequestMessage(HttpMethod.Get, config.EmailUrl);
            infoRequest.Headers.UserAgent.ParseAdd(_agent);
            infoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var emailResponse = await _httpClient.SendAsync(emailRequest);
            var emailContent = await emailResponse.Content.ReadAsStringAsync();
            if (!emailResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Email endpoint {config.EmailUrl} returned {emailResponse.StatusCode} with following content: {emailResponse}");
            }
            if (string.IsNullOrEmpty(emailContent))
            {
                throw new HttpRequestException($"Email endpoint {config.EmailUrl} returned {emailResponse.StatusCode} with empty content.");
            }

            try
            {
                JsonNode node = JsonNode.Parse(emailContent) ??
                    throw new ArgumentException("email json node is null");
                infoNode["emailRequest"] = node;

                email = node["email"]?.ToString() ??
                    node?["elements"]?[0]?["handle~"]?["emailAddress"]?.ToString(); // linkedin format
            }
            catch (Exception e)
            {
                throw new ArgumentException(string.Format("Failed to parse the email response: {0}", e.Message), e);
            }
        }

        if (email is null)
        {
            throw new ArgumentException("email retrieved from the external provider is null");
        }

        //var middlewareLogger = NpgsqlRestMiddleware.Logger;
        await using var connection = new NpgsqlConnection(connectionString);
        if (options.LogConnectionNoticeEvents)
        {
            connection.Notice += (sender, args) =>
            {
                NpgsqlRestLogger.LogConnectionNotice(args.Notice, loggingMode);
            };
        }

        await connection.OpenRetryAsync(options.ConnectionRetryOptions, context.RequestAborted);

        await using var command = connection.CreateCommand();
        command.CommandText = externalAuthConfig!.LoginCommand;

        var paramCount = command.CommandText.PgCountParams();
        if (paramCount >= 1) command.Parameters.Add(new NpgsqlParameter()
        {
            Value = config.ExternalType,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text
        });
        if (paramCount >= 2) command.Parameters.Add(new NpgsqlParameter()
        {
            Value = email,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text
        });
        if (paramCount >= 3) command.Parameters.Add(new NpgsqlParameter()
        {
            Value = name is not null ? name : DBNull.Value,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text
        });
        if (paramCount >= 4) command.Parameters.Add(new NpgsqlParameter()
        {
            Value = infoNode is not null ? infoContent.ToString() : DBNull.Value,
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
        });
        if (paramCount >= 5)
        {
            var query = context.Request.Query["analyticsData"].ToString();
            if (string.IsNullOrEmpty(query) is false)
            {
                var analyticsData = JsonNode.Parse(query);
                if (analyticsData is not null)
                {
                    if (string.IsNullOrEmpty(externalAuthConfig!.ClientAnalyticsIpKey) is false)
                    {
                        analyticsData[externalAuthConfig!.ClientAnalyticsIpKey] = context.Request.GetClientIpAddress();
                    }
                    command.Parameters.Add(new NpgsqlParameter()
                    {
                        Value = analyticsData.ToJsonString(),
                        NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Json
                    });
                }
            }
        }

        CommandLogger.LogCommand(command, Logger, "ExternalAuth.Login");

        await LoginHandler.HandleAsync(
            command,
            context,
            retryStrategy,
            tracePath: "ExternalAuth.ProcessAsync",
            performHashVerification: false,
            assignUserPrincipalToContext: false);
    }
    
    private static string BuildHtmlTemplate(ExternalAuthClientConfig config, HttpRequest request, ExternalAuthConfig externalAuthConfig)
    {
        string redirectUrl = GetRedirectUrl(config, request, externalAuthConfig);
        var state = Guid.NewGuid().ToString();
        var redirectTo = "/";
        var authUrl = string.Format(config.AuthUrl, config.ClientId, redirectUrl, state);
        var analyticsData = externalAuthConfig?.ClientAnalyticsData is null ? "null" : externalAuthConfig!.ClientAnalyticsData;
        return string.Format(externalAuthConfig!.SignInHtmlTemplate,
            config.ExternalType,
            string.Format(_jsTemplate,
                browserSessionStateKey,//0
                state,//1
                browserSessionParamsKey,//2
                externalAuthConfig!.BrowserSessionStatusKey,//3
                externalAuthConfig!.BrowserSessionMessageKey,//4
                redirectTo,//5
                authUrl,//6
                config.SigninUrl,//7
                externalAuthConfig!.ReturnToPathQueryStringKey,//8
                config.ExternalType,//9
                analyticsData//10
            ));
    }

    private static string GetRedirectUrl(ExternalAuthClientConfig clientConfig, HttpRequest request, ExternalAuthConfig externalAuthConfig)
    {
        return externalAuthConfig!.RedirectUrl is null ?
            string.Concat(request.Scheme, "://", request.Host, clientConfig.SigninUrl) :
            string.Concat(externalAuthConfig!.RedirectUrl, clientConfig.SigninUrl);
    }

    private const string _jsTemplate =
        """
        <script>
        (async function () {{
            function paramsToObject(search) {{
                const hashes = search.slice(search.indexOf('?') + 1).split('&');
                const params = {{}};
                hashes.map(hash => {{
                    const [key, val] = hash.split('=');
                    if (key && val) {{
                        params[key] = decodeURIComponent(val);
                    }}
                }});
                return params;
            }}
            var stateKey = "{0}";
            var stateValue = "{1}";
            var paramsKey = "{2}";
            var statusKey = "{3}";
            var messageKey = "{4}";
            var returnToKey = "_return_to";
            var redirectTo = "{5}";
            var authUrl = "{6}";
            var signinUrl = "{7}";
            var returnToPathQueryStringKey = "{8}";
            var externalType = "{9}";
            var analyticsData = {10};
            if (analyticsData) {{
                signinUrl += "?analyticsData=" + encodeURIComponent(JSON.stringify(analyticsData));
            }}
            var params = paramsToObject(window.location.search);
            var keys = Object.keys(params);
            if (!params.error && !params.state) {{
                if (params[returnToPathQueryStringKey]) {{
                    sessionStorage.setItem(returnToKey, params[returnToPathQueryStringKey]);
                    delete params[returnToPathQueryStringKey];
                }} else {{
                    sessionStorage.removeItem(returnToKey);
                }}
                sessionStorage.setItem(stateKey, stateValue);
                sessionStorage.setItem(paramsKey, JSON.stringify(params));
                document.location = authUrl;
                return;
            }} else {{
                var state = sessionStorage.getItem(stateKey);
                params = Object.assign(params, JSON.parse(sessionStorage.getItem(paramsKey) == null ? {{}} : sessionStorage.getItem(paramsKey)));
                sessionStorage.removeItem(stateKey);
                var returnTo = sessionStorage.getItem(returnToKey) || redirectTo;
                if (!params.error && params.state !== state) {{
                    params.error = "Invalid state code received from external provider " + externalType + ".";
                }}
                if (params.error) {{
                    sessionStorage.setItem(statusKey, 503);
                    sessionStorage.setItem(messageKey, params.error);
                    document.location = returnTo;
                    return;
                }}
                var response = await fetch(signinUrl, {{
                    method: "POST",
                    headers: {{"Accept": "application/json","Content-Type": "application/json"}},
                    body: JSON.stringify(params)
                }});
                sessionStorage.setItem(statusKey, response.status);
                sessionStorage.setItem(messageKey, await response.text());
                document.location = returnTo;
            }}
        }})();
        </script>
        """;
}