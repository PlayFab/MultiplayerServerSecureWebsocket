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
using Yarp.ReverseProxy.Forwarder;

namespace MultiplayerServerSecureWebsocket
{
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
            services.AddSingleton<ServerDetailsFactory>();
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
                endpoints.Map("/{matchId}/{**forwardPath}", async context =>
                {
                    var detailsFactory = context.RequestServices.GetRequiredService<ServerDetailsFactory>();

                    var matchId = context.GetRouteValue("matchId")?.ToString();

                    var serverDetails = await detailsFactory.GetServerDetailsAsync(matchId);

                    // couldn't find details for this match ID
                    if (serverDetails == null)
                    {
                        context.Response.StatusCode = (int) HttpStatusCode.NotFound;

                        return;
                    }

                    await forwarder.SendAsync(context,
                        serverDetails.ServerUrl,
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
    /// <example><c>/{matchId}/some/path?test=true</c> is mapped to <c>{serverUrl}/some/path?test=true</c></example>
    internal class GameServerRequestTransformer : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext httpContext,
            HttpRequestMessage proxyRequest, string serverUrl)
        {
            await base.TransformRequestAsync(httpContext, proxyRequest, serverUrl);

            var builder = new UriBuilder(serverUrl)
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