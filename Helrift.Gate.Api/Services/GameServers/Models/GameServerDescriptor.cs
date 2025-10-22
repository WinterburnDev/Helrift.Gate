namespace Helrift.Gate.Api.Services.GameServers.Models
{
    public sealed class GameServerDescriptor
    {
        public string Id { get; set; }             // "gs-01"
        public string PublicIp { get; set; }       // connection IP for clients
        public int GamePort { get; set; }          // FishNet port
        public int QueryPort { get; set; }         // optional
        public string InternalUrl { get; set; }    // Gate->GS backchannel base URL
    }

}
