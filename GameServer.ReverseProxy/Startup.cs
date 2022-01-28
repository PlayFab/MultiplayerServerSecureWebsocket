using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayFab;
using PlayFab.AuthenticationModels;
using PlayFab.MultiplayerModels;
using Yarp.ReverseProxy.Forwarder;

namespace GameServer.ReverseProxy
{
    /// <summary>
    /// Configured in appsettings.json. Don't check in <c>SecretKey</c> to source control.
    /// </summary>
    public class PlayFabSettings
    {
        public string TitleId { get; set; }
        public string SecretKey { get; set; }
    }
    
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder.SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            });

            services.AddHttpForwarder();
            services.AddSingleton<PlayFabAuthenticationInstanceAPI>(_ =>
            {
                var playfabConfig = _configuration.GetSection("PlayFab").Get<PlayFabSettings>();
                
                return new(new PlayFabApiSettings
                {
                    TitleId = playfabConfig.TitleId,
                    DeveloperSecretKey = playfabConfig.SecretKey,
                });
            });
            services.AddTransient<PlayFabMultiplayerInstanceAPI>(context =>
            {
                var authApi = context.GetRequiredService<PlayFabAuthenticationInstanceAPI>();
                
                // TODO: this should be cached until expiration (1 day)
                var entityToken = authApi.GetEntityTokenAsync(new GetEntityTokenRequest());
                
                return new(authApi.apiSettings,
                        new PlayFabAuthenticationContext()
                        {
                            EntityToken = entityToken.Result.Result.EntityToken
                        });
            });
            services.AddSingleton<ServerEndpointFactory>();
            services.AddReverseProxy().LoadFromConfig(_configuration.GetSection("ReverseProxy"));
        }

        public void Configure(IApplicationBuilder app, IConfiguration configuration, IHttpForwarder forwarder)
        {
            var httpClient = new HttpMessageInvoker(new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                UseCookies = true
            });

            var requestOptions = new ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(5)
            };
            var transformer = new GameServerRequestTransformer();

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                var logger = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ProxyEndpointHandler");
                
                endpoints.Map("/{buildId:guid}/{sessionId:guid}/{region}/{**forwardPath}", async context =>
                {
                    var detailsFactory = context.RequestServices.GetRequiredService<ServerEndpointFactory>();

                    var routeValues = context.GetRouteData().Values;

                    // respond with 400 Bad Request when the request path doesn't have the expected format
                    if (!Guid.TryParse(routeValues["buildId"]?.ToString(), out var buildId) ||
                        !Guid.TryParse(routeValues["sessionId"]?.ToString(), out var sessionId) ||
                        !Enum.TryParse(routeValues["region"]?.ToString(), out AzureRegion region))
                    {
                        context.Response.StatusCode = (int) HttpStatusCode.BadRequest;

                        return;
                    }

                    string serverEndpoint = null;

                    try
                    {
                        serverEndpoint = await detailsFactory.GetServerEndpoint(buildId, sessionId, region);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "{Method} failed", nameof(detailsFactory.GetServerEndpoint));
                    }
                    
                    // We couldn't find a server with this build/session/region
                    // The client should use the 404 status code to display a useful message like "This server was not found or is no longer available"
                    if (serverEndpoint == null)
                    {
                        context.Response.StatusCode = (int) HttpStatusCode.NotFound;

                        return;
                    }

                    await forwarder.SendAsync(context,
                        serverEndpoint,
                        httpClient, requestOptions,
                        transformer);
                });
            });

            app.UseCors();
        }
    }

    /// <summary>
    ///     Forwards the request path and query parameters to given game server URL
    /// </summary>
    /// <example><c>/{buildId}/{sessionId}/{region}/some/path?test=true</c> is mapped to <c>{serverUrl}/some/path?test=true</c></example>
    internal class GameServerRequestTransformer : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext httpContext,
            HttpRequestMessage proxyRequest, string serverEndpoint)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, serverEndpoint);

            var builder = new UriBuilder(serverEndpoint)
            {
                Query = httpContext.Request.QueryString.ToString()
            };

            var forwardPath = httpContext.GetRouteValue("forwardPath");

            if (forwardPath != null)
            {
                builder.Path = Path.Combine(builder.Path, forwardPath.ToString() ?? string.Empty);
            }

            proxyRequest.RequestUri = builder.Uri;
        }
    }
}