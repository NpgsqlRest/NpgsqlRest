using System.Collections.Frozen;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NpgsqlRestClient;

namespace NpgsqlRestTests;

/// <summary>
/// The Retry-After header on requests the rate limiter rejects (RateLimiterOptions:RetryAfterHeader), through
/// the client's own rejection handler (Builder.RateLimiterRejectedAsync).
/// </summary>
public class RateLimiterRetryAfterTests
{
    private const string Message = "Too many requests. Please try again later.";

    private sealed class Server : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask DisposeAsync()
        {
            Release.TrySetResult();
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static async Task<Server> StartAsync(
        bool retryAfterHeader = true,
        string? message = Message,
        Dictionary<string, (int? StatusCode, string? Message)>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var frozen = (overrides ?? []).ToFrozenDictionary();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.OnRejected = (context, cancellationToken) =>
                Builder.RateLimiterRejectedAsync(context, frozen, 429, message, retryAfterHeader, cancellationToken);

            options.AddFixedWindowLimiter("fixed", o =>
            {
                o.PermitLimit = 1;
                o.Window = TimeSpan.FromSeconds(30);
                o.QueueLimit = 0;
            });
            options.AddSlidingWindowLimiter("sliding", o =>
            {
                o.PermitLimit = 1;
                o.Window = TimeSpan.FromSeconds(40);
                o.SegmentsPerWindow = 4;
                o.QueueLimit = 0;
            });
            options.AddTokenBucketLimiter("bucket", o =>
            {
                o.TokenLimit = 1;
                o.TokensPerPeriod = 1;
                o.ReplenishmentPeriod = TimeSpan.FromSeconds(20);
                o.QueueLimit = 0;
            });
            options.AddConcurrencyLimiter("concurrency", o =>
            {
                o.PermitLimit = 1;
                o.QueueLimit = 0;
            });
            options.AddPolicy("partitioned", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Request.Headers["X-Tenant"].ToString(),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(50), QueueLimit = 0 }));
        });

        var app = builder.Build();
        app.UseRateLimiter();
        Server? server = null;
        foreach (var policy in new[] { "fixed", "sliding", "bucket", "partitioned" })
        {
            app.MapGet("/" + policy, () => "ok").RequireRateLimiting(policy);
        }
        app.MapGet("/concurrency", async () =>
        {
            server!.Entered.TrySetResult();
            await server.Release.Task;
            return "ok";
        }).RequireRateLimiting("concurrency");
        app.MapGet("/unlimited", () => "ok");
        await app.StartAsync();
        server = new Server
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(app.Urls.First()), Timeout = TimeSpan.FromMinutes(1) }
        };
        return server;
    }

    private static int? RetryAfter(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Retry-After", out var values) ? int.Parse(values.Single()) : null;

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(200, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(59_500, 60)]
    [InlineData(600_000, 600)]
    public void A_wait_is_whole_seconds_rounded_up_and_at_least_one(int milliseconds, int expected)
    {
        Builder.RetryAfterSeconds(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(expected);
    }

    [Fact]
    public void A_wait_without_end_does_not_overflow()
    {
        Builder.RetryAfterSeconds(TimeSpan.MaxValue).Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task A_request_that_is_let_through_has_no_retry_after()
    {
        await using var server = await StartAsync();
        using var ok = await server.Client.GetAsync("/fixed");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Be("ok");
        RetryAfter(ok).Should().BeNull();

        using var unlimited = await server.Client.GetAsync("/unlimited");
        RetryAfter(unlimited).Should().BeNull();
    }

    [Fact]
    public async Task Fixed_window_says_how_long_until_the_window_ends()
    {
        await using var server = await StartAsync();
        using var ok = await server.Client.GetAsync("/fixed");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        using var rejected = await server.Client.GetAsync("/fixed");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(Message);
        // the 30 second window began with the first request, a moment ago
        RetryAfter(rejected).Should().BeInRange(25, 30);
    }

    [Fact]
    public async Task Fixed_window_counts_down()
    {
        await using var server = await StartAsync();
        using var ok = await server.Client.GetAsync("/fixed");
        using var first = await server.Client.GetAsync("/fixed");
        await Task.Delay(2200);
        using var second = await server.Client.GetAsync("/fixed");
        first.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
#if NET11_0_OR_GREATER
        RetryAfter(second)!.Value.Should().BeLessThan(RetryAfter(first)!.Value);
        RetryAfter(second)!.Value.Should().BeInRange(22, 28);
#else
        // .NET 10 reports the whole window, whatever part of it has passed
        RetryAfter(first).Should().Be(30);
        RetryAfter(second).Should().Be(30);
#endif
    }

    [Fact]
    public async Task Sliding_window_says_when_the_used_permit_comes_back()
    {
        await using var server = await StartAsync();
        using var ok = await server.Client.GetAsync("/sliding");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        using var rejected = await server.Client.GetAsync("/sliding");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(Message);
#if NET11_0_OR_GREATER
        // the one permit was used in the current segment; it comes back when that segment leaves the
        // 40 second window (4 segments of 10 seconds)
        RetryAfter(rejected).Should().BeInRange(30, 40);
#else
        // the .NET 10 sliding window limiter does not say when to retry
        RetryAfter(rejected).Should().BeNull();
#endif
    }

    [Fact]
    public async Task Token_bucket_says_when_the_next_token_arrives()
    {
        await using var server = await StartAsync();
        using var ok = await server.Client.GetAsync("/bucket");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        using var rejected = await server.Client.GetAsync("/bucket");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(Message);
        RetryAfter(rejected).Should().BeInRange(15, 20);
    }

    [Fact]
    public async Task Concurrency_cannot_say_and_sends_no_header()
    {
        await using var server = await StartAsync();
        var running = server.Client.GetAsync("/concurrency");
        await server.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        using var rejected = await server.Client.GetAsync("/concurrency");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be(Message);
        RetryAfter(rejected).Should().BeNull();

        server.Release.SetResult();
        using var finished = await running;
        (await finished.Content.ReadAsStringAsync()).Should().Be("ok");
    }

    [Fact]
    public async Task Each_partition_has_its_own_wait()
    {
        await using var server = await StartAsync();
        async Task<HttpResponseMessage> Get(string tenant)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/partitioned");
            request.Headers.Add("X-Tenant", tenant);
            return await server.Client.SendAsync(request);
        }
        using var a1 = await Get("a");
        using var a2 = await Get("a");
        using var b1 = await Get("b");
        a1.StatusCode.Should().Be(HttpStatusCode.OK);
        a2.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        RetryAfter(a2).Should().BeInRange(45, 50);
        b1.StatusCode.Should().Be(HttpStatusCode.OK);
        RetryAfter(b1).Should().BeNull();
    }

    [Fact]
    public async Task The_header_can_be_turned_off()
    {
        await using var server = await StartAsync(retryAfterHeader: false);
        foreach (var path in new[] { "/fixed", "/sliding", "/bucket" })
        {
            using var ok = await server.Client.GetAsync(path);
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
            using var rejected = await server.Client.GetAsync(path);
            rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await rejected.Content.ReadAsStringAsync()).Should().Be(Message);
            RetryAfter(rejected).Should().BeNull();
        }
    }

    [Fact]
    public async Task Without_a_message_the_body_is_empty_and_the_header_is_there()
    {
        await using var server = await StartAsync(message: null);
        using var ok = await server.Client.GetAsync("/fixed");
        using var rejected = await server.Client.GetAsync("/fixed");
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Be("");
        RetryAfter(rejected).Should().BeInRange(25, 30);
    }

    [Fact]
    public async Task A_policy_with_its_own_status_and_message_keeps_them_and_gets_the_header()
    {
        await using var server = await StartAsync(overrides: new()
        {
            ["bucket"] = (503, "Service busy. Retry shortly."),
            ["sliding"] = (null, "Slow down.")
        });
        using var okBucket = await server.Client.GetAsync("/bucket");
        using var bucket = await server.Client.GetAsync("/bucket");
        bucket.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await bucket.Content.ReadAsStringAsync()).Should().Be("Service busy. Retry shortly.");
        RetryAfter(bucket).Should().BeInRange(15, 20);

        using var okSliding = await server.Client.GetAsync("/sliding");
        using var sliding = await server.Client.GetAsync("/sliding");
        sliding.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await sliding.Content.ReadAsStringAsync()).Should().Be("Slow down.");
#if NET11_0_OR_GREATER
        RetryAfter(sliding).Should().BeInRange(30, 40);
#else
        RetryAfter(sliding).Should().BeNull();
#endif

        using var okFixed = await server.Client.GetAsync("/fixed");
        using var fixedWindow = await server.Client.GetAsync("/fixed");
        fixedWindow.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await fixedWindow.Content.ReadAsStringAsync()).Should().Be(Message);
        RetryAfter(fixedWindow).Should().BeInRange(25, 30);
    }

    [Fact]
    public async Task The_header_is_a_whole_number_of_seconds()
    {
        await using var server = await StartAsync();
        using var ok = await server.Client.GetAsync("/bucket");
        using var rejected = await server.Client.GetAsync("/bucket");
        rejected.Headers.GetValues("Retry-After").Single().Should().MatchRegex("^[1-9][0-9]*$");
        rejected.Headers.RetryAfter!.Delta.Should().NotBeNull();
    }
}
