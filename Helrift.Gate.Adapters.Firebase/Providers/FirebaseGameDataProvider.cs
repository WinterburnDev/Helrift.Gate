using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Helrift.Gate.App;
using Helrift.Gate.Contracts;

namespace Helrift.Gate.Adapters.Firebase;

/// <summary>
/// Firebase RTDB implementation for character data using Admin OAuth (no ?auth=).
/// Reads/writes snake_case documents via FirebaseCharacterMapper.
/// </summary>
public sealed class FirebaseGameDataProvider : IGameDataProvider
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FirebaseGameDataProvider(IHttpClientFactory httpFactory)
    {
        _http = httpFactory.CreateClient("firebase-admin");
    }

    // --------------------------------------------------------------------
    // Reads
    // --------------------------------------------------------------------

    public async Task<IReadOnlyList<CharacterData>> GetCharactersAsync(string accountId, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"accounts/{accountId}/characters.json", ct);
        if (!res.IsSuccessStatusCode)
            return Array.Empty<CharacterData>();

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return Array.Empty<CharacterData>();

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return Array.Empty<CharacterData>();

        var list = new List<CharacterData>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                list.Add(FirebaseCharacterMapper.FromFirebase(accountId, prop.Name, prop.Value));
        }
        return list;
    }

    public async Task<CharacterData?> GetCharacterAsync(string accountId, string charId, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"accounts/{accountId}/characters/{charId}.json", ct);
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        return FirebaseCharacterMapper.FromFirebase(accountId, charId, root);
    }

    public async Task<CharacterNameRecord?> GetCharacterByNameAsync(string characterName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return null;

        var norm = NormalizeNameKey(characterName);
        var path = $"character_names/{norm}.json";

        using var res = await _http.GetAsync(path, ct);
        if (!res.IsSuccessStatusCode)
            return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null")
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        // expected shape:
        // {
        //   "character_name": "...",
        //   "character_id": "...",
        //   "account_id": "...",
        //   "created_at": 1731328800000
        // }
        var accountId = root.TryGetProperty("account_id", out var accEl) && accEl.ValueKind == JsonValueKind.String
            ? accEl.GetString()
            : null;

        var charId = root.TryGetProperty("character_id", out var charEl) && charEl.ValueKind == JsonValueKind.String
            ? charEl.GetString()
            : null;

        var canonicalName = root.TryGetProperty("character_name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()
            : characterName;

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(charId))
            return null;

        return new CharacterNameRecord
        {
            AccountId = accountId,
            CharacterId = charId,
            CharacterName = canonicalName ?? string.Empty
        };
    }

    /// <summary>
    /// Optional convenience if your interface includes it:
    /// Returns username + character count from /accounts/{accountId}.
    /// </summary>
    public async Task<AccountData?> GetAccountAsync(string accountId, CancellationToken ct)
    {
        using var res = await _http.GetAsync($"accounts/{accountId}.json", ct);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        return FirebaseAccountMapper.FromFirebase(accountId, root);
    }

    public async Task<AccountData?> GetAccountBySteamIdAsync(string steamId64, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(steamId64)) return null;

        // -------- Canonical RTDB child-path query across ALL accounts --------
        // orderBy must be a JSON-quoted string and URL-encoded.
        var orderBy = Uri.EscapeDataString("\"links/steam_id\"");
        var equalTo = Uri.EscapeDataString($"\"{steamId64}\"");

        var url = $"accounts.json?orderBy={orderBy}&equalTo={equalTo}&limitToFirst=1";

        using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;

        var json = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json) || json == "null") return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        // The result shape is: { "<accountId>": { ...account... } }
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                return FirebaseAccountMapper.FromFirebase(prop.Name, prop.Value);
        }

        return null;
    }

    public async Task<AccountData> CreateAccountAsync(NewAccountRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.SteamId64))
            throw new ArgumentException("SteamId64 is required.", nameof(req));

        // Idempotency: if already exists, return it
        var existing = await GetAccountBySteamIdAsync(req.SteamId64, ct).ConfigureAwait(false);
        if (existing != null) return existing;

        var accountId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        var payload = FirebaseAccountMapper.ToFirebaseCreatePayload(accountId, req, now);

        var json = JsonSerializer.Serialize(payload);
        using var reqMsg = new HttpRequestMessage(new HttpMethod("PATCH"), $".json")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(reqMsg, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Firebase create failed: {(int)res.StatusCode} {res.ReasonPhrase}\n{body}");
        }

        var created = await GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (created == null)
            throw new InvalidOperationException("Created account could not be reloaded.");

        return created;
    }

    // --------------------------------------------------------------------
    // Writes
    // --------------------------------------------------------------------

    public async Task CreateCharacterAsync(CharacterData c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Username))
            throw new ArgumentException("CharacterData.Username (account id) is required.");
        if (string.IsNullOrWhiteSpace(c.CharacterName))
            throw new ArgumentException("CharacterData.CharacterName is required.");

        // 1) Generate id if missing (firebase push keys aren’t required here)
        var charId = string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("n") : c.Id;

        // 2) Reserve the character name to prevent duplicates
        var norm = NormalizeNameKey(c.CharacterName);
        var namePath = $"character_names/{norm}.json";

        // If reservation exists -> conflict
        using (var check = await _http.GetAsync(namePath, ct))
        {
            if (check.IsSuccessStatusCode)
            {
                var b = await check.Content.ReadAsStringAsync(ct);
                if (!string.IsNullOrWhiteSpace(b) && b != "null")
                    throw new InvalidOperationException("Character name already taken.");
            }
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nameDoc = new
        {
            character_name = c.CharacterName,
            character_id = charId,
            account_id = c.Username,
            created_at = nowMs
        };
        var namePut = await _http.PutAsJsonAsync(namePath, nameDoc, Json, ct);
        namePut.EnsureSuccessStatusCode();

        // 3) Write character document (snake_case)
        var charDocPath = $"accounts/{c.Username}/characters/{charId}.json";
        var toWrite = CloneWithId(c, charId);
        var body = FirebaseCharacterMapper.ToFirebaseDict(toWrite);

        var put = await _http.PutAsJsonAsync(charDocPath, body, Json, ct);
        put.EnsureSuccessStatusCode();

        // 4) Patch timestamps (best-effort)
        var patch = new { created_at = nowMs, updated_at = nowMs, schema_version = 1 };
        var req = new HttpRequestMessage(HttpMethod.Patch, $"{charDocPath}?print=silent")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), System.Text.Encoding.UTF8, "application/json")
        };
        await _http.SendAsync(req, ct);
    }

    public async Task SaveCharacterAsync(CharacterData c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Username) || string.IsNullOrWhiteSpace(c.Id))
            throw new ArgumentException("Username and Id are required to save a character.");

        var path = $"accounts/{c.Username}/characters/{c.Id}.json";

        // map out of band (non game server) data
        if (c.Friends == null)
        {
            try
            {
                var existing = await GetCharacterAsync(c.Username, c.Id, ct);
                if (existing?.Friends != null)
                {
                    if(c.Friends == null && existing.Friends != null)
                        c.Friends = existing.Friends;

                    if (c.FriendRequests == null && existing.FriendRequests != null)
                        c.FriendRequests = existing.FriendRequests;
                }
            }
            catch (Exception ex)
            {
                // log but don't fail the whole save because of friends merge
                // (optional depending on how strict you want this)
                Console.WriteLine($"[FirebaseGameDataProvider] Failed to merge Friends: {ex}");
            }
        }

        var body = FirebaseCharacterMapper.ToFirebaseDict(c);

        var put = await _http.PutAsJsonAsync(path, body, Json, ct);
        put.EnsureSuccessStatusCode();

        var patch = new { updated_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), schema_version = 1 };
        var req = new HttpRequestMessage(HttpMethod.Patch, $"{path}?print=silent")
        {
            Content = new StringContent(JsonSerializer.Serialize(patch, Json), System.Text.Encoding.UTF8, "application/json")
        };
        await _http.SendAsync(req, ct);
    }

    public async Task DeleteCharacterAsync(string accountId, string charId, string characterName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(charId))
            throw new ArgumentException("accountId and charId are required.");

        var charPath = $"accounts/{accountId}/characters/{charId}.json";
        var delChar = await _http.DeleteAsync(charPath, ct);
        delChar.EnsureSuccessStatusCode();

        // Remove name reservation (best-effort)
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            var norm = NormalizeNameKey(characterName);
            var namePath = $"character_names/{norm}.json";
            using (var res = await _http.DeleteAsync(namePath, ct))
            {
                if (!res.IsSuccessStatusCode)
                {
                    
                }
            }
        }
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private static string NormalizeNameKey(string name)
        => Regex.Replace((name ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

    private static CharacterData CloneWithId(CharacterData c, string newId)
    {
        return new CharacterData
        {
            Id = newId,
            Username = c.Username,
            CharacterName = c.CharacterName,
            MapId = c.MapId,
            Position = c.Position,
            Rotation = c.Rotation,
            Side = c.Side,
            SideStatus = c.SideStatus,
            Gender = c.Gender,
            Appearance = c.Appearance,
            Hp = c.Hp,
            Mp = c.Mp,
            Sp = c.Sp,
            Level = c.Level,
            Strength = c.Strength,
            Dexterity = c.Dexterity,
            Vitality = c.Vitality,
            Magic = c.Magic,
            Intelligence = c.Intelligence,
            Finesse = c.Finesse,
            Experience = c.Experience,
            Criticals = c.Criticals,
            EnemyKillPoints = c.EnemyKillPoints,
            MajesticPoints = c.MajesticPoints,
            Inventory = c.Inventory,
            Warehouse = c.Warehouse,
            Skills = c.Skills,
            Quests = c.Quests,
            Titles = c.Titles,
            Guild = c.Guild,
            Beastiary = c.Beastiary,
            Research = c.Research,
            Spells = c.Spells,
            Cosmetics = c.Cosmetics,
            LastLoggedIn = c.LastLoggedIn,
            Friends = c.Friends
        };
    }
}
