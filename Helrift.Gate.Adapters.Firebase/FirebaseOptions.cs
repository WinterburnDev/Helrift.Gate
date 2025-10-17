// Helrift.Gate.Adapters.Firebase/FirebaseOptions.cs
namespace Helrift.Gate.Adapters.Firebase;

public sealed class FirebaseOptions
{
    // Realtime Database root URL, e.g. https://<project>-default-rtdb.firebaseio.com/
    public required string DatabaseUrl { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
}
