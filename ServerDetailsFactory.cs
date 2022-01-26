using System.Threading.Tasks;

namespace MultiplayerServerSecureWebsocket
{
    public class ServerDetails
    {
        /// <summary>
        ///     FQDN (including game port) from RequestMultiplayerServer response
        /// </summary>
        /// <example>
        ///     http://dns00000000-0000-0000-0000-000000000000-azurebatch-cloudservice.westus.cloudapp.azure.com:30201
        /// </example>
        /// <see
        ///     href="https://docs.microsoft.com/en-us/rest/api/playfab/multiplayer/multiplayer-server/request-multiplayer-server?view=playfab-rest#requestmultiplayerserverresponse" />
        public string ServerUrl { get; set; }
    }

    public class ServerDetailsFactory
    {
        /// <summary>
        ///     Fetches the server details for a given match ID
        /// </summary>
        /// <param name="matchId">
        ///     An opaque identifier used to store server details for each REST call to PlayFab's
        ///     <c>RequestMultiplayerServer</c>
        /// </param>
        public Task<ServerDetails> GetServerDetailsAsync(string matchId)
        {
            // TODO: bring your own cache or data store
            return Task.FromResult(new ServerDetails
            {
                ServerUrl =
                    "http://dns00000000-0000-0000-0000-000000000000-azurebatch-cloudservice.westus.cloudapp.azure.com:30201"
            });
        }
    }
}