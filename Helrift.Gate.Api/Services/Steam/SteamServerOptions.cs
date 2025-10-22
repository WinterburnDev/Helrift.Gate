namespace Helrift.Gate.Api.Services.Steam
{
    public sealed class SteamServerOptions
    {
        public uint AppId { get; set; } = 480;        // dev default
        public ushort GamePort { get; set; } = 27015; // UDP game
        public ushort QueryPort { get; set; } = 27016;// A2S
        public bool Secure { get; set; } = true;
        public string VersionString { get; set; } = "1.0.0.0";
    }
}
