namespace Helrift.Gate.Api.Services
{
    public sealed class PartyExperienceOptions
    {
        public float ShareRange { get; set; } = 35f; // tune for Helrift scale
        public bool RemainderToEarner { get; set; } = true;
    }
}
