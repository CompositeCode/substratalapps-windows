using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Bundle;
using Polly;
using Substratal.Hub.Core.Api;
using Substratal.Hub.Core.Api.Generated;
using Substratal.Hub.Core.Auth;
using Substratal.Hub.Core.Navigation;

namespace Substratal.Hub.Core;

/// <summary>Identifies this client to the API. The API shows the user agent in Sessions (USR-04, ACC-04).</summary>
public sealed record HubClientInfo(string Platform, string AppVersion, string OsDescription)
{
    public IEnumerable<ProductInfoHeaderValue> UserAgent()
    {
        yield return new ProductInfoHeaderValue($"Substratal-{Platform}", AppVersion);
        yield return new ProductInfoHeaderValue($"({OsDescription.Replace('(', '[').Replace(')', ']')})");
    }
}

public static class HubServices
{
    internal const string AuthClient = "substratal-auth";
    internal const string ApiClient = "substratal-api";

    /// <summary>
    /// Registers the API pipeline: resilience (outermost, so each attempt re-runs the handlers below it) →
    /// <see cref="ApiVersionHandler"/> → <see cref="BearerHandler"/> → <see cref="SocketsHttpHandler"/>.
    /// The platform supplies <see cref="ITokenStore"/>.
    /// </summary>
    public static IServiceCollection AddSubstratalHubCore(this IServiceCollection services, HubClientInfo client)
    {
        services.AddSingleton(client);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ApiVersionStatus>();
        services.AddSingleton<FeatureFlags>();
        services.AddTransient(sp => new ApiVersionHandlerFactory(sp.GetRequiredService<ApiVersionStatus>(), client).Create());
        services.AddTransient<BearerHandler>();

        services.AddHttpClient(AuthClient, http => Configure(http))
            .ConfigurePrimaryHttpMessageHandler(Transport)
            .AddHttpMessageHandler<ApiVersionHandler>();

        var api = services.AddHttpClient(ApiClient, http => Configure(http)).ConfigurePrimaryHttpMessageHandler(Transport);
        api.AddResilienceHandler("substratal", ConfigureResilience);
        api.AddHttpMessageHandler<ApiVersionHandler>().AddHttpMessageHandler<BearerHandler>();

        services.AddSingleton(sp => new SessionManager(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(AuthClient),
            sp.GetRequiredService<ITokenStore>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddSingleton(sp =>
        {
            var session = sp.GetRequiredService<SessionManager>();
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClient);
            // Auth is the BearerHandler's job, so Kiota's own provider is anonymous.
            var adapter = new DefaultRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http)
            {
                BaseUrl = session.Environment.BaseUrl.ToString().TrimEnd('/'),
            };
            return new ApiAdapterHolder(adapter, session);
        });
        services.AddSingleton(sp => new SubstratalApiClient(sp.GetRequiredService<ApiAdapterHolder>().Adapter));
        services.AddSingleton<Capabilities.CapabilitiesService>();
        return services;
    }

    static void Configure(HttpClient http)
    {
        http.DefaultRequestVersion = HttpVersion.Version20;
        http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        http.Timeout = Timeout.InfiniteTimeSpan; // per-attempt timeouts come from the resilience pipeline
        http.BaseAddress = ApiEnvironment.Production.BaseUrl;
    }

    // One pooled HTTP/2 connection to API Gateway carries the dashboard's parallel reads (PLAN.md §6.4).
    static SocketsHttpHandler Transport() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        EnableMultipleHttp2Connections = true,
    };

    /// <summary>
    /// CLIENT-ARCHITECTURE §4: reads retry twice with jitter; writes retry only with an <c>Idempotency-Key</c>;
    /// <c>Retry-After</c> is honored; 30 s per attempt.
    /// </summary>
    internal static void ConfigureResilience(ResiliencePipelineBuilder<HttpResponseMessage> pipeline)
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(500),
            ShouldRetryAfterHeader = true,
            ShouldHandle = args => ValueTask.FromResult(IsRetryable(args.Outcome, args.Context.GetRequestMessage())),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(30));
    }

    internal static bool IsRetryable(Outcome<HttpResponseMessage> outcome, HttpRequestMessage? request)
    {
        request ??= outcome.Result?.RequestMessage;
        var transient = outcome.Exception is HttpRequestException or TimeoutException
            || outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        if (!transient) return false;
        if (request is null) return false; // can't tell the method, so don't risk repeating a write
        return request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Headers.Contains("Idempotency-Key");
    }

    sealed class ApiVersionHandlerFactory(ApiVersionStatus status, HubClientInfo client)
    {
        public ApiVersionHandler Create() => new(status, client.UserAgent().ToArray());
    }
}

/// <summary>Keeps the generated client's base URL in step with the environment chosen at sign-in.</summary>
public sealed class ApiAdapterHolder
{
    public DefaultRequestAdapter Adapter { get; }

    public ApiAdapterHolder(DefaultRequestAdapter adapter, SessionManager session)
    {
        Adapter = adapter;
        session.EnvironmentChanged += (_, env) => adapter.BaseUrl = env.BaseUrl.ToString().TrimEnd('/');
    }
}
