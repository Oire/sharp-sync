# Push Notification Listener

## Overview

Add real-time remote change detection to SharpSync via push notifications. Currently SharpSync detects remote changes only through periodic polling (PROPFIND / listing). This feature adds WebSocket-based listening for Nextcloud (via notify_push app) and SSE-based listening for OCIS, so consuming applications get notified of remote changes instantly without polling.

**Why this belongs in SharpSync:** Push notification listening is a sync concern — it detects remote file changes, which is core sync engine functionality. The protocol details (Nextcloud notify_push WebSocket, OCIS SSE) are tightly coupled to the storage backends SharpSync already knows about (WebDavStorage with Nextcloud/OCIS capability detection).

**How apps use it:** The app creates a push listener (providing server URL and auth credentials), subscribes to its `RemoteChangeDetected` event, and calls `NotifyRemoteChangeAsync()` on the engine when it fires. If push is unavailable (server doesn't support it), the app falls back to more frequent polling (every 2 minutes instead of 30). The listener is fully decoupled from SyncEngine — the app wires them together.

## Acceptance Criteria

- Nextcloud: listener connects to notify_push WebSocket, fires `RemoteChangeDetected` when `notify_file` received
- Nextcloud without notify_push: `IsSupported` returns `false`, no connection attempt, no error
- OCIS: listener connects to SSE endpoint, fires `RemoteChangeDetected` on file change events
- Graceful degradation: listener exposes `IsSupported` and `IsConnected` so app can fall back to polling
- Automatic reconnection with exponential backoff on disconnect
- Clean disposal of all resources (WebSocket, HttpClient, background tasks, CancellationTokenSources)
- All new classes are `sealed`, accept `ILogger? logger = null`, use `[LoggerMessage]` source-generated logging

## Context

**SharpSync already has:**
- `WebDavStorage` with Nextcloud/OCIS server capability detection (`/status.php`, `/ocs/v1.php/cloud/capabilities`)
- `ServerCapabilities` class (in `Storage/`) with `IsNextcloud`, `IsOcis` properties
- `OAuth2Config.ForNextcloud()` and `OAuth2Config.ForOcis()` for authentication
- `IOAuth2Provider` / `OAuth2Result` for token management (access token, refresh token, expiry)
- `ISyncEngine` with `NotifyRemoteChangeAsync()`, `NotifyRemoteChangeBatchAsync()`, `NotifyRemoteRenameAsync()`
- `ChangeType` enum (`Created`, `Deleted`, `Changed`, `Renamed`) and `ChangeInfo` record in `Core/`
- All public interfaces and event args live in `Core/`; implementations in `Sync/` or `Storage/`
- `[LoggerMessage]` source-generated logging in `Logging/LogMessages.cs` (EventIds 1-48 used, next: 49)
- `FileProgressEventArgs` pattern: `class : EventArgs` with constructor params and computed properties

**Nextcloud notify_push:** A Nextcloud server app that provides a WebSocket endpoint. After authentication, the server pushes plain text messages when files change. Requires the notify_push app to be installed on the server — not all servers have it. **Does NOT send file paths** — only signals that something changed for the user.

**OCIS notifications:** OCIS has built-in SSE (Server-Sent Events) for change notifications via the `clientlog` service. Sends typed events with resource IDs (not file paths). Authenticated via OAuth2 bearer token.

## Architecture Decision: Listener Decoupled from SyncEngine

The push listener is **not** a SyncEngine constructor parameter. Instead:
1. The consuming app creates the listener directly (providing server URL + auth)
2. The app subscribes to the listener's `RemoteChangeDetected` event
3. In the event handler, the app calls `engine.NotifyRemoteChangeAsync()` or triggers a full sync
4. The app manages the listener's lifecycle (start/stop/dispose)

**Rationale:**
- Avoids breaking SyncEngine's constructor API (already 9 parameters)
- Keeps SyncEngine focused on sync logic, not push protocol concerns
- The app already knows whether it's Nextcloud or OCIS (it configured OAuth2)
- The app can wire multiple listeners to one engine, or one listener to multiple engines
- Clean separation: listener knows about push protocol, SyncEngine knows about sync

## Development Approach

- Complete each task fully before moving to the next
- Make small, focused changes
- **CRITICAL: every task MUST include new/updated tests**
- **CRITICAL: all tests must pass before starting next task**
- Run tests after each change

## Testing Strategy

- Unit tests with mocked WebSocket/SSE connections for protocol handling
- Unit tests for capability detection, reconnection logic, backoff calculation
- Integration test stubs with `Skip.If()` for when test servers aren't available
- Integration tests against real Nextcloud/OCIS instances (manual or CI with test server)

## Progress Tracking

- Mark completed items with `[x]` immediately when done
- Add newly discovered tasks with + prefix
- Document issues/blockers with ! prefix

---

## Implementation Steps

### Task 1: Push notification interfaces, event args, and log messages

**Files:**
- Create: `src/SharpSync/Core/IPushNotificationListener.cs`
- Create: `src/SharpSync/Core/PushNotificationEventArgs.cs`
- Create: `src/SharpSync/Core/PushNotificationType.cs`
- Modify: `src/SharpSync/Logging/LogMessages.cs` (add EventIds 49-63)
- Create: `tests/SharpSync.Tests/Core/PushNotificationEventArgsTests.cs`

**Interface design:**
```csharp
// IPushNotificationListener : IAsyncDisposable
// - StartAsync(CancellationToken)
// - StopAsync(CancellationToken)
// - event EventHandler<PushNotificationEventArgs>? RemoteChangeDetected
// - bool IsConnected { get; }
// - bool IsSupported { get; }
```

- [ ] Create `PushNotificationType` enum: `FilesChanged`, `Activity`, `Notification` (maps to the minimal set both protocols support — no `FileChanged` vs `FolderChanged` since Nextcloud doesn't distinguish)
- [ ] Create `PushNotificationEventArgs` class extending `EventArgs` with constructor params: `PushNotificationType Type`, `string? ResourceId` (OCIS item ID, null for Nextcloud), `IReadOnlyList<long>? FileIds` (Nextcloud file IDs from `notify_file_id`, null when not available), `DateTimeOffset Timestamp`
- [ ] Create `IPushNotificationListener` interface extending `IAsyncDisposable`: `StartAsync(CancellationToken)`, `StopAsync(CancellationToken)`, `event RemoteChangeDetected`, `bool IsConnected`, `bool IsSupported`
- [ ] Add `[LoggerMessage]` entries in `LogMessages.cs` for push notification subsystem (EventIds 49-63): push capability detected/not detected, WebSocket connecting/connected/disconnected/auth failed/message received/error, SSE connecting/connected/disconnected/event received/error, reconnection attempt, reconnection gave up
- [ ] Write construction tests for `PushNotificationEventArgs` to verify property values
- [ ] Run tests — must pass before next task

### Task 2: Reconnection strategy

**Files:**
- Create: `src/SharpSync/Sync/PushListeners/ReconnectionStrategy.cs`
- Create: `tests/SharpSync.Tests/Sync/PushListeners/ReconnectionStrategyTests.cs`

**Note:** The `Sync/PushListeners/` subdirectory is a deliberate new pattern — these 3+ related files are cohesive enough to warrant grouping rather than placing flat in `Sync/`.

- [ ] Create `sealed` class `ReconnectionStrategy`: calculates delay between reconnection attempts using exponential backoff (1s, 2s, 4s, 8s... capped at 5 minutes). Resets delay on successful connection.
- [ ] Constructor: `ReconnectionStrategy(int maxConsecutiveFailures = 10, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)`
- [ ] Methods: `TimeSpan GetNextDelay()` (returns delay and increments failure count), `void Reset()` (resets to initial state), `bool ShouldGiveUp` property (true after max consecutive failures)
- [ ] Write unit tests: delay progression, cap at max, reset on success, max retries, custom constructor params
- [ ] Run tests — must pass before next task

### Task 3: Nextcloud notify_push WebSocket listener

**Files:**
- Create: `src/SharpSync/Sync/PushListeners/NextcloudPushListener.cs`
- Create: `tests/SharpSync.Tests/Sync/PushListeners/NextcloudPushListenerTests.cs`

**Protocol (verified from notify_push source):**

The notify_push protocol works as follows:
1. **Capability detection**: `GET /ocs/v2.php/cloud/capabilities` — check for `capabilities.notify_push.endpoints.websocket` in response. If present, use that URL directly.
2. **Connect**: Open `ClientWebSocket` to the WebSocket URL from capabilities.
3. **Authenticate** (two options):
   - **Username + password/app-password**: Send username as first message, password as second message.
   - **Pre-auth token**: POST to `capabilities.notify_push.endpoints.pre_auth` to get a token, then send empty string as first message and the token as second message.
4. **Auth response**: Server sends `"authenticated"` on success, `"err: <message>"` on failure. 15-second timeout.
5. **Listen**: Server sends plain text messages (NOT JSON):
   - `"notify_file"` — files changed for this user (no path info)
   - `"notify_activity"` — activity feed updated
   - `"notify_notification"` — notification created/dismissed
6. **Optional file ID opt-in**: Send `"listen notify_file_id"` after auth to receive `"notify_file_id [1,2,3]"` (JSON array of numeric Nextcloud file IDs) instead of bare `"notify_file"` when the server knows which files changed. Falls back to `"notify_file"` when unknown.
7. **Keepalive**: Server sends WebSocket ping frames every 30 seconds.

**Constructors:**

Constructor A — direct auth (username + password sent over WebSocket):
```csharp
NextcloudPushListener(
    string serverUrl,              // e.g., "https://cloud.example.com"
    string username,               // Nextcloud username
    string password,               // Password or app password
    ILogger? logger = null)
```

Constructor B — pre-auth token flow (for OAuth2 sessions without raw credentials):
```csharp
NextcloudPushListener(
    string serverUrl,
    Func<CancellationToken, Task<string>> accessTokenProvider,  // Returns current OAuth2 access token
    ILogger? logger = null)
```
The pre-auth flow: POST to `capabilities.notify_push.endpoints.pre_auth` with `Authorization: Bearer {token}` to get a 32-char pre-auth token (valid 15s), then send `""` + pre-auth token over WebSocket.

**Testability:** Accept an optional `Func<Uri, CancellationToken, Task<ClientWebSocket>>` factory for WebSocket creation (defaults to `new ClientWebSocket()` + `ConnectAsync`). This allows unit tests to inject a mock WebSocket. Similarly, accept an optional `HttpClient` for capability detection HTTP requests.

- [ ] Create `sealed` class `NextcloudPushListener` implementing `IPushNotificationListener`
- [ ] On `StartAsync`: fetch capabilities endpoint to check for `notify_push`. If not present, set `IsSupported = false` and return (no error).
- [ ] If supported: extract WebSocket URL from `capabilities.notify_push.endpoints.websocket`. Connect via `ClientWebSocket` (or injected factory).
- [ ] Authenticate: Constructor A sends username then password. Constructor B POSTs to the pre-auth endpoint with bearer token, then sends `""` + pre-auth token. Wait for `"authenticated"` response. On `"err: ..."`, log and set `IsSupported = false`.
- [ ] After auth: optionally send `"listen notify_file_id"` to opt into file ID notifications.
- [ ] Listen loop: parse plain text messages. `"notify_file"` and `"notify_file_id ..."` fire `RemoteChangeDetected` with `PushNotificationType.FilesChanged`. `"notify_activity"` → `Activity`. `"notify_notification"` → `Notification`.
- [ ] For `"notify_file_id [...]"` messages: parse the space-separated JSON array of file IDs into `IReadOnlyList<long>`. Pass as `FileIds` in `PushNotificationEventArgs`.
- [ ] Handle connection drops: log and trigger reconnection using `ReconnectionStrategy`.
- [ ] Implement `IAsyncDisposable`: cancel background listen task, close WebSocket, dispose CancellationTokenSource.
- [ ] On `StopAsync`: close WebSocket cleanly with `CloseAsync(WebSocketCloseStatus.NormalClosure, ...)`.
- [ ] Write unit tests with mocked WebSocket: successful auth, auth failure, message parsing for all types, `notify_file_id` parsing, capability detection (supported vs not supported), reconnection on disconnect
- [ ] Run tests — must pass before next task

### Task 4: OCIS SSE listener

**Files:**
- Create: `src/SharpSync/Sync/PushListeners/OcisPushListener.cs`
- Create: `tests/SharpSync.Tests/Sync/PushListeners/OcisPushListenerTests.cs`

**Protocol (verified from OCIS source):**

1. **Endpoint**: `GET {serverUrl}/ocs/v2.php/apps/notifications/api/v1/notifications/sse`
2. **Authentication**: `Authorization: Bearer {accessToken}` header. Standard browser `EventSource` doesn't support custom headers, so use `HttpClient` with streaming response.
3. **SSE format**: Standard W3C SSE (`event:` + `data:` fields, separated by blank lines).
4. **File change event types** (from `clientlog` service):
   - `postprocessing-finished` — file upload completed
   - `item-renamed` — file/folder renamed
   - `item-moved` — file/folder moved
   - `item-trashed` — file/folder trashed
   - `item-restored` — file/folder restored from trash
   - `folder-created` — new folder created
   - `file-touched` — file metadata touched
   - `file-locked` / `file-unlocked` — file lock changed
   - `userlog-notification` — human-readable notification
5. **Data payload** (for file events): JSON with `itemid`, `parentitemid`, `spaceid`, `initiatorid`, `etag`. **No file paths** — only resource IDs. Client must resolve paths via WebDAV/Graph API.
6. **Self-event filtering**: The `initiatorid` field matches the client's `X-Request-ID` header, allowing the client to skip events it caused.
7. **Keepalive**: Server sends SSE comments (`:keepalive\n\n`) at configurable intervals.

**Constructor:**
```csharp
OcisPushListener(
    string serverUrl,                                          // e.g., "https://ocis.example.com"
    Func<CancellationToken, Task<string>> accessTokenProvider, // Returns current OAuth2 access token (app handles refresh)
    string? initiatorId = null,                                // For self-event filtering (matches X-Request-ID on writes)
    ILogger? logger = null)
```
The `accessTokenProvider` is a callback the app provides. It returns a valid access token, handling token refresh internally. This avoids the listener needing to know about `IOAuth2Provider`/`OAuth2Config` and avoids triggering browser-based OAuth2 flows from a background listener.

**Testability:** Accept an optional `HttpClient` (or `HttpMessageHandler`) for SSE HTTP requests. This allows unit tests to inject a mock HTTP response stream.

- [ ] Create `sealed` class `OcisPushListener` implementing `IPushNotificationListener`
- [ ] `IsSupported` starts as `true` (SSE is built-in to OCIS). Updated to `false` if `StartAsync` gets 404 or other non-auth error indicating the endpoint is disabled/unavailable.
- [ ] On `StartAsync`: get access token via `accessTokenProvider`. Connect to SSE endpoint with `HttpClient` using `HttpCompletionOption.ResponseHeadersRead` for streaming.
- [ ] Parse SSE format line by line: accumulate `event:` and `data:` fields, fire event on blank line separator.
- [ ] Map file change SSE event types (`postprocessing-finished`, `item-renamed`, `item-moved`, `item-trashed`, `item-restored`, `folder-created`, `file-touched`) → `PushNotificationType.FilesChanged`. Parse JSON payload to extract `itemid` and `initiatorid`.
- [ ] Map `userlog-notification` → `PushNotificationType.Notification`.
- [ ] Self-event filtering: if `initiatorid` matches the configured `initiatorId`, skip the event (don't fire `RemoteChangeDetected`).
- [ ] Token refresh: if the SSE connection returns 401, call `accessTokenProvider` again for a fresh token and reconnect.
- [ ] Reuse `ReconnectionStrategy` from Task 2 for reconnection on disconnect.
- [ ] Implement `IAsyncDisposable`: cancel streaming request, dispose HttpClient, dispose CancellationTokenSource.
- [ ] On `StopAsync`: cancel the streaming request.
- [ ] Write unit tests with mocked HTTP response stream: SSE parsing, event type mapping, self-event filtering, JSON payload parsing, token refresh on 401, connection handling
- [ ] Run tests — must pass before next task

### Task 5: ServerCapabilities additions

**Files:**
- Modify: `src/SharpSync/Storage/ServerCapabilities.cs`
- Modify: `src/SharpSync/Storage/WebDavStorage.cs` (capability detection)

**Note:** These properties are informational — apps can query them to show push support status. The listeners do their own capability detection independently, so this task provides convenience metadata, not a hard dependency.

- [ ] Add to `ServerCapabilities`: `bool SupportsNotifyPush`, `string? NotifyPushWebSocketUrl`, `string? NotifyPushPreAuthUrl`
- [ ] Update WebDavStorage's capability detection (`DetectServerCapabilitiesAsync`) to check for `notify_push` in the capabilities response and populate the new fields
- [ ] Write unit tests for capability detection with/without notify_push
- [ ] Run tests — must pass before next task

### Task 6: Integration test stubs

**Files:**
- Create: `tests/SharpSync.Tests/Sync/PushListeners/NextcloudPushIntegrationTests.cs`
- Create: `tests/SharpSync.Tests/Sync/PushListeners/OcisPushIntegrationTests.cs`

- [ ] Create integration test classes with `Skip.If()` for when test server env vars aren't set (`NEXTCLOUD_TEST_URL`, `NEXTCLOUD_TEST_USER`, `NEXTCLOUD_TEST_PASS`, `OCIS_TEST_URL`, `OCIS_TEST_TOKEN`)
- [ ] Nextcloud tests: connect to real server, verify `IsSupported` detection, verify message receipt on file change
- [ ] OCIS tests: connect to real server, verify SSE connection, verify event receipt on file change
- [ ] Reconnection test: verify automatic reconnection with backoff after disconnect
- [ ] Run full test suite: `dotnet test` (integration tests will skip without env vars)

### Task 7: [Final] Update documentation

- [ ] Update SharpSync README.md with push notification documentation: supported servers, usage pattern (create listener → subscribe → wire to engine), configuration, fallback behavior
- [ ] Update CLAUDE.md: add push listener section to Architecture (new `Sync/PushListeners/` subdirectory, `IPushNotificationListener` in `Core/`), EventId range (49-63), and note about `Func<CancellationToken, Task<string>>` token provider pattern
- [ ] Move this plan to `docs/plans/completed/`

## Technical Details

### Nextcloud notify_push protocol

**Source:** [github.com/nextcloud/notify_push](https://github.com/nextcloud/notify_push)

**Capability detection:** `GET /ocs/v2.php/cloud/capabilities` — check for `capabilities.notify_push` in response. If present, push is available.

Response when notify_push is installed:
```json
{
  "ocs": {
    "data": {
      "capabilities": {
        "notify_push": {
          "type": ["files", "activities", "notifications"],
          "endpoints": {
            "websocket": "wss://cloud.example.com/push/ws",
            "pre_auth": "https://cloud.example.com/apps/notify_push/pre_auth"
          }
        }
      }
    }
  }
}
```

**WebSocket endpoint:** The URL from `capabilities.notify_push.endpoints.websocket` (typically `wss://{server}/push/ws`).

**Authentication flow (username + password):**
1. Connect to WebSocket endpoint
2. Send username as first text message
3. Send password (or app password) as second text message
4. Server responds with `"authenticated"` or `"err: <message>"`
5. Authentication must complete within 15 seconds

**Authentication flow (pre-auth token):**
1. POST to `capabilities.notify_push.endpoints.pre_auth` with user credentials
2. Server returns a random 32-character token (plain text), valid for 15 seconds
3. Connect to WebSocket endpoint
4. Send empty string `""` as first message
5. Send the pre-auth token as second message
6. Server responds with `"authenticated"` or `"err: <message>"`

**Message format (server → client):**

Messages are **plain text**, not JSON. Format: `{event_name}` or `{event_name} {json_body}`.

| Message | Format | Meaning |
|---------|--------|---------|
| `authenticated` | Plain text | Authentication successful |
| `err: {message}` | Plain text | Authentication/connection error |
| `notify_file` | Plain text | Files changed for this user (no specifics) |
| `notify_file_id [{id1},{id2}]` | Text + JSON array | Files changed, with Nextcloud internal file IDs (opt-in) |
| `notify_activity` | Plain text | Activity feed updated |
| `notify_notification` | Plain text | Notification created/dismissed |

**File ID opt-in:** After authentication, send `"listen notify_file_id"` to receive file IDs when known. Falls back to plain `"notify_file"` when the server doesn't know which files changed.

**Connection limits:** Max 64 concurrent WebSocket connections per user. Server sends WebSocket ping frames every 30 seconds.

**Key point:** notify_push does NOT send file paths. The client must perform a sync/PROPFIND to discover what actually changed. This means `PushNotificationEventArgs.AffectedPath` will be null for Nextcloud — the app should trigger a full sync on `RemoteChangeDetected`.

### OCIS SSE protocol

**Source:** [github.com/owncloud/ocis](https://github.com/owncloud/ocis) (services/sse, services/clientlog, services/proxy)

**Endpoint:** `GET {serverUrl}/ocs/v2.php/apps/notifications/api/v1/notifications/sse`

**Authentication:** `Authorization: Bearer {accessToken}` header. Requires an HTTP client that supports custom headers with SSE (not browser `EventSource`).

**Headers:**
```
Authorization: Bearer {accessToken}
Accept-Language: {languageCode}
X-Request-ID: {uuid}
X-Requested-With: XMLHttpRequest
```

**SSE event format:** Standard W3C SSE with `event:` and `data:` fields.

**File change events (from `clientlog` service):**

| SSE Event Type | Trigger |
|---|---|
| `postprocessing-finished` | File upload completed |
| `item-renamed` | File/folder renamed (same parent) |
| `item-moved` | File/folder moved (different parent) |
| `item-trashed` | File/folder moved to trash |
| `item-restored` | File/folder restored from trash |
| `folder-created` | New folder created |
| `file-touched` | File metadata touched |
| `file-locked` | File lock acquired |
| `file-unlocked` | File lock released |

**Data payload for file events:**
```json
{
  "parentitemid": "storageid$spaceid!nodeid",
  "itemid": "storageid$spaceid!nodeid",
  "spaceid": "storageid$spaceid",
  "initiatorid": "uuid-of-user-who-caused-the-event",
  "etag": "\"etag-value\"",
  "affecteduserids": []
}
```

**Self-event filtering:** The `initiatorid` field matches the `X-Request-ID` header that the client sends on write requests. This allows the client to skip events it caused.

**Other events:**
- `userlog-notification` — human-readable notification (localized)
- `backchannel-logout` — OIDC backchannel logout signal

**Key point:** OCIS SSE sends resource IDs, not file paths. The `itemid` is an opaque resource ID in the format `storageid$spaceid!nodeid`. The client can use the `etag` for efficient change detection but must resolve paths via WebDAV/Graph API. The `initiatorid` allows self-event filtering.

**Keepalive:** Server sends SSE comments (`:keepalive\n\n`) at configurable intervals.

### Exponential backoff parameters

- Initial delay: 1 second
- Multiplier: 2x
- Maximum delay: 5 minutes (300 seconds)
- Maximum consecutive failures before giving up: 10 (configurable)
- Reset: delay resets to initial on successful connection

### LogMessages.cs EventId allocation (49-63)

| EventId | Level | Message |
|---------|-------|---------|
| 49 | Debug | Push notification capability detected for {ServerType} |
| 50 | Debug | Push notification not supported by server |
| 51 | Debug | WebSocket connecting to {Url} |
| 52 | Information | WebSocket connected and authenticated |
| 53 | Warning | WebSocket disconnected |
| 54 | Warning | WebSocket authentication failed: {Error} |
| 55 | Debug | WebSocket message received: {MessageType} |
| 56 | Warning | WebSocket error |
| 57 | Debug | SSE connecting to {Url} |
| 58 | Information | SSE connected |
| 59 | Warning | SSE disconnected |
| 60 | Debug | SSE event received: {EventType} |
| 61 | Warning | SSE error |
| 62 | Debug | Reconnection attempt {Attempt} after {Delay} delay |
| 63 | Warning | Reconnection gave up after {MaxAttempts} consecutive failures |

## App Integration Pattern

```csharp
// 1. Create the push listener (app knows its server type)
// Nextcloud with username/password:
IPushNotificationListener listener = new NextcloudPushListener(serverUrl, username, appPassword, logger);
// Or Nextcloud with OAuth2 (pre-auth token flow):
IPushNotificationListener listener = new NextcloudPushListener(serverUrl, ct => GetAccessTokenAsync(ct), logger);
// Or OCIS:
IPushNotificationListener listener = new OcisPushListener(serverUrl, ct => GetAccessTokenAsync(ct), initiatorId, logger);

// 2. Subscribe to push events and wire to engine
// Note: async void event handlers can swallow exceptions — wrap in try/catch
listener.RemoteChangeDetected += async (s, e) => {
    // Neither Nextcloud nor OCIS sends file paths — trigger a full sync
    // e.FileIds (Nextcloud) or e.ResourceId (OCIS) are available for future path resolution
    await engine.NotifyRemoteChangeAsync("/", ChangeType.Changed);
    await engine.SynchronizeAsync();
};

// 3. Start listening
await listener.StartAsync(cancellationToken);

// 4. Check if push is actually available and adjust polling interval
var pollingInterval = listener.IsSupported
    ? TimeSpan.FromMinutes(30)   // Push available — infrequent polling as safety net
    : TimeSpan.FromMinutes(2);   // No push — fall back to frequent polling

// 5. App can query listener.IsConnected at any time to decide behavior
// (e.g., adjust polling frequency, log status, etc.)

// 6. Clean up
await listener.DisposeAsync();
```

## Post-Completion

**Manual verification:**
- Test with real Nextcloud server (with and without notify_push app)
- Test with real OCIS instance
- Test reconnection by restarting the server during active connection
- Verify no resource leaks (WebSocket/HTTP connections properly disposed)
- Verify self-event filtering works on OCIS (events caused by this client are skipped)
