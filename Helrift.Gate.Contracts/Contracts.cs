namespace Helrift.Gate.Contracts;

public record Vec3(float x, float y, float z);
public record Euler(float x, float y, float z);

public record AccountSummary(string AccountId, string Username, int Characters);

public record Character(
    string Id,
    string AccountId,
    string Name,
    int Level,
    string Gender,
    string MapId,
    Vec3 Position,
    Euler Rotation,
    int Hp, int Mp, int Sp
);
