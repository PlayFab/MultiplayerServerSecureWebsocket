namespace GameServer.ReverseProxy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlayFab;
using PlayFab.MultiplayerModels;

public class ServerEndpointFactory
{
    private readonly ILogger _logger;
    private readonly PlayFabMultiplayerInstanceAPI _multiplayerApi;
        
    public ServerEndpointFactory(ILoggerFactory loggerFactory, PlayFabMultiplayerInstanceAPI multiplayerApi)
    {
        _logger = loggerFactory.CreateLogger<ServerEndpointFactory>();
        _multiplayerApi = multiplayerApi;
    }
        
    public async Task<string> GetServerEndpoint(Guid sessionId)
    {
        var response = await _multiplayerApi.GetMultiplayerServerDetailsAsync(new GetMultiplayerServerDetailsRequest
        {
            SessionId = sessionId.ToString(),
        });

        if (response.Error?.Error == PlayFabErrorCode.MultiplayerServerNotFound)
        {
            _logger.LogError("Server not found: Session ID = {SessionId}", sessionId);

            return null;
        }

        if (response.Error != null)
        {
            _logger.LogError("{Request} failed: {Message}", nameof(_multiplayerApi.GetMultiplayerServerDetailsAsync),
                response.Error.GenerateErrorReport());

            throw new Exception(response.Error.GenerateErrorReport());
        }

        var uriBuilder = new UriBuilder(response.Result.FQDN)
        {
            Port = GetEndpointPortNumber(response.Result.Ports)
        };

        return uriBuilder.ToString();
    }

    private static int GetEndpointPortNumber(IEnumerable<Port> ports)
    {
        // replace this logic with whatever is configured for your build i.e. getting a port by name
        return ports.First().Num;
    }
}