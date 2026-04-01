# Helrift.Gate — AGENTS.md

**Instructions for AI agents (Copilot, ChatGPT, etc.) interacting with Helrift.Gate.**

This file defines the boundaries and rules for safe, consistent development in this backend persistence server. Follow these guidelines to maintain architecture integrity and prevent common mistakes.

---

## Project Boundaries

### ✅ This Project IS Responsible For

- Persistence (Firebase Realtime Database)
- Cross-server coordination (realm events, shared state)
- Offline reward systems (escrow, delivery)
- Authentication and authorization
- Account and character management
- Long-lived state (guilds, leaderboards, town projects)
- API contracts and data validation

### ❌ This Project MUST NEVER Own

- **Gameplay logic** – damage formulas, combat resolution, ability execution
- **Real-time action** – movement validation, collision, physics
- **NPC behavior** – AI logic, dialogue trees, spawning decisions
- **Inventory mutations** – item addition/removal happens in Unity only
- **Moment-to-moment networking** – that's FishNet in the Unity project
- **Config loading/versioning** – not yet implemented; docs state this is a future direction

---

## Authority Rules (CRITICAL)

### Gate is Authoritative For
- ✅ Persistent character data (stats, gold, unlocks)
- ✅ Cross-session state (guild membership, reputation)
- ✅ What should be delivered to players (escrow/items in transit)
- ✅ Access control and authentication
- ✅ Shared world state (town projects, realm events)

### Gate is NOT Authoritative For
- ❌ Whether an attack hit (game server decides)
- ❌ Damage amounts (game server calculates)
- ❌ Player position (game server owns)
- ❌ Whether an item should be used (game server executes)
- ❌ NPC state or actions
- ❌ Inventory contents while player is online (Unity is authoritative)

### When Unity and Gate Disagree
1. Game server is authoritative for gameplay
2. Gate verifies persistence is valid
3. Example: Player deals 100 damage → game server records result → sends to Gate → Gate stores in DB

---

## Common Mistakes to Avoid

### ❌ Never Do This

#### 1. **Implement gameplay logic in Gate**
```csharp
// ❌ WRONG: Gate calculating combat
public async Task<DamageResult> CalculateDamageAsync(AttackRequest request)
{
    var baseDamage = request.Attacker.Strength * 1.5f;
    var defense = request.Target.Defense * 0.8f;
    return new DamageResult { Damage = baseDamage - defense };
}
```
**Why:** Gate is not real-time. Game server must validate all actions.

#### 2. **Trust client input for gameplay**
```csharp
// ❌ WRONG: Trusting damage from client
[HttpPost("apply-damage")]
public async Task<ActionResult> ApplyDamage([FromBody] DamageRequest request)
{
    // request.Damage came from who? Don't trust it.
    await damageService.ApplyDamageAsync(request.TargetId, request.Damage);
}
```
**Why:** Client can be hacked/spoofed. Game server calculates; Gate only records.

#### 3. **Mutate inventory directly**
```csharp
// ❌ WRONG: Gate removing items
[HttpPost("use-item")]
public async Task ApplyItemEffect([FromBody] UseItemRequest request)
{
    var character = await characterService.GetCharacterAsync(request.CharacterId);
    character.Inventory.Remove(request.ItemId);  // NO!
    await characterService.SaveAsync(character);
}
```
**Why:** Unity owns inventory operations. Gate only holds escrow items for delivery.

#### 4. **Create a parallel delivery system**
```csharp
// ❌ WRONG: New reward flow that bypasses Delivery/Escrow
[HttpPost("grant-reward")]
public async Task GrantRewardDirectly(GrantRequest request)
{
    var character = await characterService.GetCharacterAsync(request.CharacterId);
    character.Gold += request.Amount;  // Bypass delivery system entirely
}
```
**Why:** Use the existing `DeliveryService` and `EscrowService`. They handle offline/online cases.

#### 5. **Put authorization logic in services**
```csharp
// ❌ WRONG: Service making auth decisions
public async Task<CharacterData> GetCharacterAsync(string characterId, string userId)
{
    // Mixing auth with business logic
    if (userId != GetOwnerId(characterId)) throw new UnauthorizedAccessException();
    // ...
}
```
**Why:** Use controller-level `[Authorize]` attributes. Services assume caller is authorized.

#### 6. **Add new fields to domain models without checking contracts**
```csharp
// ❌ WRONG: Changing schema mid-flight
public class Character
{
    public string Name { get; set; }
    public int Level { get; set; }
    public List<Tag> Tags { get; set; }  // New field, but clients don't expect it!
}
```
**Why:** Check `Helrift.Gate.Contracts/` to see what Unity expects. Breaking changes require versioning.

---

## Reuse Before Build

### Before Adding a New Feature, Check:

1. **Does a service already exist?**
   - Browse `Services/` folders
   - Example: Need to track a stat? Check `LevelProgressionService`, `LeaderboardService`

2. **Is there an existing controller endpoint?**
   - Example: Need to update character data? Don't create a new controller; use `CharactersController`

3. **Does an existing delivery mechanism cover this?**
   - Use `DeliveryService` (player messages, parcels, broadcasts)
   - Use `EscrowService` (temporary holding)
   - Don't invent new reward flows

4. **Is there a comparable service in another domain?**
   - Example: Friends service and guild service both need "send to player" logic
   - Consider extracting a shared pattern before duplicating

---

## Data Ownership

### Unity Owns (In-Session, Real-Time)
- Current location (position/rotation)
- Active combat stats (current HP, stamina, buffs)
- Inventory in-hand
- Currently active quests/objectives

### Gate Owns (Persistent, Cross-Session)
- Character progression (level, experience, unlocks)
- Permanent unlockables (achievements, skills, titles)
- Relationships (guild, party, friends, reputation)
- Cross-server state (town projects, realm events)
- Offline items/currency (delivery inbox)

**Rule of thumb:** If it survives a server restart and isn't specific to one game session, Gate owns it.

---

## Networking Rules

### Communication Pattern  
```
Unity → HTTP POST to Gate ← (JSON response)
        (e.g., "I dealt 50 damage to NPC")
        
Gate responds with:
        - Success/failure status
        - Persistence confirmation
        - Any side effects (e.g., "NPCs now respawn in 5m")
```

### Never

- ❌ Spawn RPC-like "apply effect" calls from Gate to Unity
- ❌ Try to push real-time state from Gate to Unity
- ❌ Use WebSocket for gameplay sync (FishNet handles that)
- ❌ Call Unity endpoints from Gate

### Use Existing Flows

- Look at `DeliveriesController` and `RealmEventsController`
- Follow the same request→service→database→response pattern
- Return only what Unity needs to know

---

## Authorization

### Enforce at Controller Level

```csharp
[ApiController]
[Route("api/v1/characters")]
[Authorize]  // ← Use globally or per-action
public sealed class CharactersController : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin")]  // ← Specific policy
    public async Task<ActionResult> CreateCharacter(...) { }
    
    [HttpPost("{id}/items")]
    [Authorize(Policy = "ServerOnly")]  // ← Game server only
    public async Task<ActionResult> AddItem(...) { }
}
```

### Never

- ❌ Do auth checks inside services (they assume caller is authorized)
- ❌ Skip authorization "because it's just test code"
- ❌ Trust `userId` from request body without validating the JWT
- ❌ Let services talk directly to databases; always go through services

---

## Adding Features Safely

### Step 1: Define Authority
- Who owns this data? Gate or Unity?
- Who is the source of truth?

### Step 2: Find the Existing Anchor
- Is there a related service? (e.g., "I'm adding prestige levels" → check `LevelProgressionService`)
- Is there a related controller? (e.g., "I'm adding guild perks" → check `GuildsController`)

### Step 3: Extend, Don't Duplicate
- Add to existing services rather than creating new ones
- Reuse the Delivery system for rewards
- Reuse the Escrow system for transactions

### Step 4: Update Contracts
- Add DTOs to `Helrift.Gate.Contracts/`
- Make sure Unity can parse the response
- Document breaking changes

### Step 5: Test Authorization
- Verify unauthorized users cannot call it
- Verify server-only endpoints reject client tokens

---

## Refactoring Expectations

### When Touching Old Code, Consider:

1. **Can I extract a shared service?**
   - If `CharacterService` and `NpcService` both do X, create `SharedService.X()`

2. **Is there dead code?**
   - Example: old Firebase queries that are unused → remove them

3. **Does this service violate single responsibility?**
   - Example: `AuthService` doing both tokens AND user creation → split it

4. **Are there magic numbers or unclear names?**
   - Replace with constants or clearer variable names

### Never

- ❌ Leave a service >500 lines without considering a split
- ❌ Add "just one more thing" to a controller; move it to a service
- ❌ Copy-paste logic instead of extracting a method

---

## Configuration & Secrets

### In Code

```csharp
// ✅ RIGHT: Config injected via DI
public sealed class AuthService(IConfiguration config)
{
    private readonly string _jwtSecret = config["Auth:JwtSecret"];
}

// ❌ WRONG: Hard-coded secrets
private const string JwtSecret = "my-super-secret-key-12345";
```

### Never

- ❌ Commit `.json` files with real Firebase credentials
- ❌ Log tokens or passwords
- ❌ Put secrets in code comments
- ❌ Use the same secret across dev/staging/prod

---

## Testing

### When Adding/Changing a Service

- Add unit tests for business logic
- Test authorization (happy path + rejection cases)
- Test error cases (not found, conflict, invalid input)
- Mock Firebase adapter; don't hit real DB in unit tests

### When Adding a Controller Endpoint

- Test the happy path (correct input → success)
- Test authorization (unauthenticated + wrong role → 403)
- Test bad input (malformed JSON, missing fields → 400)
- Test business logic error cases (resource not found → 404)

---

## Naming Conventions

- Services: `ICharacterService`, `IDeliveryService`
- Controllers: `CharactersController`, `DeliveriesController`
- DTOs: `CreateCharacterRequest`, `CharacterResponse`
- Methods: `GetCharacterAsync()`, `CreateDeliveryAsync()`
- Folders: `Services/Characters/`, `Services/Deliveries/`

**Pattern:** Domain → Action → Async suffix for I/O operations

---

## Deployment & Versioning

### API Versioning
- Current: `/api/v1/`
- When breaking changes occur, add `/api/v2/`
- Old versions remain supported until officially sunset

### Feature Flags
- Use configuration to enable/disable new features
- Example: `"Features:NewLeaderboard": false` in config

---

## When in Doubt

1. **Read the README.md** – project structure and systems
2. **Check an existing service** – patterns and conventions
3. **Look at a similar controller** – how responses are structured
4. **Test locally** – does the flow work end-to-end?
5. **Ask the team** – better to clarify than guess

