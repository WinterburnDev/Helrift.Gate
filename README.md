# Helrift.Gate

**Helrift.Gate** is the backend persistence and cross-server coordination server for Helrift. It handles all long-lived state, offline systems, and cross-game-server communication, enabling multiplayer features that persist beyond a player's current session.

---

## Purpose

Helrift.Gate serves as the authoritative backend for:

- **Player Persistence** – accounts, character profiles, progression, unlockables
- **Cross-Server Coordination** – delivery systems, realm events, shared world state
- **Offline Rewards** – escrow and delivery system for items/currency earned while offline
- **Shared Systems** – guilds, parties, leaderboards, town projects, chat
- **Authentication** – JWT-based auth with token management and Steam integration

The Gate server is **NOT** authoritative for moment-to-moment gameplay. The Unity game server owns combat, movement, abilities, and NPC behavior. Gate only learns about outcomes after they occur.

---

## Architecture

### Technology Stack
- **Framework**: ASP.NET Core
- **Database**: Firebase Realtime Database
- **Authentication**: JWT + Google/Steam OAuth
- **Dependency Injection**: ASP.NET Core DI container
- **Resilience**: Polly retry policies

### Project Layout

```
Helrift.Gate.sln
├── Helrift.Gate.Api/
│   ├── Controllers/          (REST endpoints, route to services)
│   ├── Services/             (business logic, organized by domain)
│   ├── Infrastructure/       (common utilities, middleware, config)
│   ├── Program.cs            (DI setup, middleware pipeline)
│   └── appsettings.*.json    (environment-specific config)
│
├── Helrift.Gate.Adapters.Firebase/
│   ├── Providers/            (Firebase read/write implementations)
│   ├── Mappers/              (domain models ↔ Firebase DTOs)
│   └── FirebaseOptions.cs    (connection configuration)
│
├── Helrift.Gate.Contracts/
│   ├── DTOs                  (request/response payloads)
│   └── Domain models         (shared types across projects)
│
└── Helrift.Gate.App/
    └── Repositories/         (data access patterns)
```

### Key Systems

#### 1. **Service Architecture**
Each domain has a corresponding service folder under `Services/`:
- `Services/Accounts/` – account creation, profile management
- `Services/Auth/` – JWT tokens, OAuth flows
- `Services/Characters/` – character CRUD, progression
- `Services/Deliveries/` – offline reward delivery
- `Services/Escrow/` – item/currency holding during transactions
- `Services/TownProjects/` – shared world construction/goals
- `Services/RealmEvents/` – realm-wide events and broadcasts
- `Services/Friends/` – friend relationships and presence
- `Services/Leaderboards/` – ranking and stat tracking

Each service is injected into controllers and other services via DI.

#### 2. **Controllers**
REST endpoints in `Controllers/` map HTTP routes to services. All controllers:
- Are stateless
- Validate requests and permissions
- Delegate to services
- Return DTOs (never raw domain models)

Example flow: `POST /api/v1/deliveries/parcel` → `DeliveriesController` → `IDeliveryService.CreateParcelDeliveryAsync()` → Firebase write.

#### 3. **Firebase Persistence**
- Handled by `Helrift.Gate.Adapters.Firebase`
- `Providers/` implement Firebase-specific read/write logic
- `Mappers/` convert between domain models and Firebase JSON shapes
- All reads/writes are typed, not raw JSON

#### 4. **Authentication**
- JWT tokens issued by `AuthService`
- Google/Steam OAuth handled in `SteamService`
- `Authorize` attributes on controllers enforce permissions
- Token expiry and refresh managed via `TokenService`

#### 5. **Delivery & Escrow System**
The delivery system enables offline rewards:
- **Delivery types**: player messages, parcels (items/currency), system broadcasts, guild broadcasts
- **Escrow**: temporary holding for items in-transit or pending player action
- Unity queries deliveries on login and processes them in-session
- Gate is authoritative for what should be delivered; Unity owns actual inventory mutation

---

## Responsibilities

### ✅ Gate Owns

- **Persistent state** – anything that must survive server restarts or player logoffs
- **Cross-session data** – character stats, unlockables, account info
- **Offline systems** – delivery, escrow, queued actions
- **Cross-server coordination** – realm events, guild state, shared projects
- **Authentication** – issuance and validation of credentials
- **Leaderboards & Rankings** – stat aggregation and comparison

### ❌ Gate Does NOT Own

- **Combat outcomes** – game server determines damage, hits, blocks
- **Moment-to-moment movement** – game server has authority
- **Ability execution** – game server validates and executes
- **NPC behavior** – game server runs AI and scripts
- **Inventory mutations** – game server adds/removes items; Gate only holds escrow
- **Presentation logic** – Gate is data only; UI rendering is client-side

---

## Key Patterns

### Service Injection

```csharp
// Controllers receive services via DI
[ApiController]
public sealed class DeliveriesController(IDeliveryService service) : ControllerBase
{
    [HttpPost("parcel")]
    public async Task<ActionResult<DeliveryRecord>> CreateParcel(
        CreateParcelDeliveryRequest request, 
        CancellationToken ct)
        => Ok(await service.CreateParcelDeliveryAsync(request, ct));
}
```

### Authorization Policies

- `Authorize(Policy = "ServerOnly")` – only game servers can call (e.g., delivery creation)
- `Authorize(Policy = "PlayerOwned")` – player can only access their own data
- `AllowAnonymous` – public endpoints (login, health checks)

### Error Handling

- Services throw `InvalidOperationException` or custom exceptions for known errors
- Controllers catch and return appropriate HTTP status codes
- Polly policies retry transient failures (Firebase timeouts, etc.)

---

## Development Guidance

### Adding a New Feature

1. **Define the domain** – decide which `Services/` folder it belongs in
2. **Create/extend the service** – implement the business logic
3. **Add a controller endpoint** – route HTTP requests to the service
4. **Update Firebase adapter** – if new persistence is needed
5. **Test end-to-end** – verify the service flow and Firebase writes

### Communicating with Unity

- Unity sends results/state via HTTP requests to this server
- Gate responds with persistence records or error codes
- Gate never pushes state to Unity; Unity polls or subscribes to changes
- Use `GateRealtimeClient` in Unity to handle communication

### Versioning APIs

- All routes are prefixed with `/api/v1/`
- When breaking changes occur, create `/api/v2/` endpoints
- Old endpoints remain supported until deprecated

---

## Testing

### Unit Tests
- Service logic should have unit tests using mocked dependencies
- Test authorization, validation, and error cases

### Integration Tests
- Test service + Firebase adapter together
- Use appropriate configuration (dev database or test fixtures)
- Verify end-to-end flows like "create delivery → verify stored → retrieve"

### Running Locally

```bash
# Build
dotnet build Helrift.Gate.sln

# Run API server (requires Firebase config in appsettings.Development.json)
dotnet run --project Helrift.Gate.Api

# Run tests
dotnet test Helrift.Gate.sln
```

---

## Deployment

- Publish to cloud platform (e.g., Google Cloud Run, App Service)
- Config is environment-specific; use `appsettings.Production.json`
- Firebase credentials are injected via environment variables
- Monitor logs and error rates post-deployment

---

## Cross-Project Dependencies

### Depends On
- **FishNet** – referenced in contracts for state sync (via DLLs)
- **Firebase Admin SDK** – for Realtime Database access
- **Google Auth SDK** – for OAuth
- **Steam API** – for Steam authentication

### Depended On By
- **HB World (Unity)** – queries this server for persistence, auth, deliveries
- **Game instances** – register themselves and update realm state
- **Web clients** – if a web UI exists for account/admin features

