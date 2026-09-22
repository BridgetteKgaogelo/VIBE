# VIBE REST API contract

The client (`com.vibe.app.data.remote.VibeApi`) and the server
(`api-container/Vibe.Api`) implement the contract below. Conventions:

* JSON, camelCase field names, **all timestamps are epoch milliseconds (UTC)**.
* `Authorization: Bearer <access token>` on everything except register/login/google/refresh.
* Status codes: `401` unauthenticated · `403` role check failed · `404` not found ·
  `409` the row changed elsewhere (offline conflict) or the round is closed ·
  `422` validation failed.

Base URL: `https://vibe-api.azurewebsites.net/` (configurable through `API_BASE_URL`).

## Authentication

| Method | Path | Body | Answer |
| --- | --- | --- | --- |
| POST | `/api/v1/auth/register` | `{displayName, email, password}` | `AuthResponse` |
| POST | `/api/v1/auth/login` | `{email, password}` | `AuthResponse` |
| POST | `/api/v1/auth/google` | `{idToken}` | `AuthResponse` (validates `aud`/`iss`/`exp`) |
| POST | `/api/v1/auth/refresh` | `{refreshToken}` | `AuthResponse` |
| POST | `/api/v1/auth/logout` | – | `204` |
| POST | `/api/v1/auth/password-reset` | `{email}` | `204` (always, to avoid account probing) |

`AuthResponse = { accessToken, refreshToken, expiresAtEpochMillis, user }`

## Account and settings

| Method | Path | Body | Answer |
| --- | --- | --- | --- |
| GET | `/api/v1/users/me` | – | `UserDto` |
| PATCH | `/api/v1/users/me` | `{displayName?, username?, language?, themeMode?, notificationsEnabled?, privacyMembersOnly?, photoUri?}` | `UserDto` |
| POST | `/api/v1/users/me/password` | `{currentPassword, newPassword}` | `204` / `422` |

## Groups and the Vibe List

| Method | Path | Body | Answer |
| --- | --- | --- | --- |
| GET | `/api/v1/groups` | – | `GroupDto[]` (the caller's groups) |
| GET | `/api/v1/groups/{groupId}` | – | `GroupDto` with members |
| POST | `/api/v1/groups` | `{name, icon, clientId?}` | `GroupDto` (`clientId` = idempotency key) |
| POST | `/api/v1/groups/join` | `{inviteCode}` | `GroupDto` / `404` unknown code |
| POST | `/api/v1/groups/{groupId}/leave` | – | `204` (owner must hand over or delete) |
| DELETE | `/api/v1/groups/{groupId}/members/{userId}` | – | `204`, owner only |
| GET | `/api/v1/groups/{groupId}/activities` | – | `ActivityDto[]` |
| GET | `/api/v1/users/me/activities` | – | `ActivityDto[]` across the caller's groups |
| POST | `/api/v1/groups/{groupId}/activities` | `{title, description?, icon?, favourite?, status?, clientId?}` | `ActivityDto` |
| PATCH | `/api/v1/activities/{activityId}` | same body | `ActivityDto` / `409` conflict |
| DELETE | `/api/v1/activities/{activityId}` | – | `204` |

## Decide For Us

| Method | Path | Body | Answer |
| --- | --- | --- | --- |
| POST | `/api/v1/groups/{groupId}/decisions` | `{activityIds?, deadlineEpochMillis?, clientId?}` | `DecisionDto` (round 1, `OPEN`) · `403` non-owner · `422` fewer than two ideas |
| GET | `/api/v1/decisions/{decisionId}` | – | `DecisionDto` (tallies only when `votesRevealed`) |
| POST | `/api/v1/decisions/{decisionId}/votes` | `{activityId, choice, clientTimestampEpochMillis, clientId}` | `VoteAckDto` · `409` round already closed |
| POST | `/api/v1/decisions/{decisionId}/close` | – | `DecisionDto` — settles the round, `CONTINUE`/`WINNER`/`NONE` |
| POST | `/api/v1/decisions/{decisionId}/surprise` | `{rejectedIds}` | `ActivityDto` (random eligible option) |

`DecisionDto` mirrors `DecisionEngine` exactly:

```json
{
  "id": "…", "groupId": "g-friday", "roundNumber": 2, "state": "OPEN",
  "deadlineEpochMillis": 1790000600000, "winnerActivityId": null,
  "participation": { "memberCount": 4, "votedCount": 3, "pendingMemberIds": ["u-kyle"] },
  "tallies": [], "survivorIds": ["pizza", "bowling"], "eliminatedIds": ["movie"],
  "votesRevealed": false, "canCloseEarly": true, "outcome": "continue"
}
```

## Plans, memories and alerts

| Method | Path | Body | Answer |
| --- | --- | --- | --- |
| GET | `/api/v1/groups/{groupId}/plans` | – | `PlanDto[]` |
| POST | `/api/v1/plans` | `{groupId, activityId, scheduledAtEpochMillis}` | `PlanDto` |
| POST | `/api/v1/plans/{planId}/complete` | `{rating, caption, photoUris}` | `MemoryDto` |
| GET | `/api/v1/groups/{groupId}/memories` | – | `MemoryDto[]` |
| GET | `/api/v1/notifications` | – | `NotificationDto[]` |
| POST | `/api/v1/notifications/{notificationId}/read` | – | `204` |
| POST | `/api/v1/notifications/read-all` | – | `204` |
| POST | `/api/v1/devices/register` | `{fcmToken, platform}` | `204` |

## Offline sync

`POST /api/v1/sync/batch`

```json
{
  "items": [
    { "clientId": "activity.add:act-1:1790000000000", "action": "activity.add",
      "entityId": "act-1", "payloadJson": "{\"title\":\"Bowling night\"}",
      "queuedAtEpochMillis": 1790000000000 }
  ]
}
```

Answer — one result per item, in any order:

```json
{
  "results": [
    { "clientId": "…", "status": "applied", "serverPayload": null, "message": null },
    { "clientId": "…", "status": "conflict", "serverPayload": "{\"title\":\"Bowling\"}", "message": "changed on the server" },
    { "clientId": "…", "status": "rejected", "serverPayload": null, "message": "activity was eliminated" }
  ]
}
```

* `applied` — the change is the server's copy now; the client deletes the queue row.
* `conflict` — the row changed on the server after `queuedAtEpochMillis`; the client keeps
  the row in `CONFLICT` with `serverPayload` attached and asks the member.
* `rejected` — the change can never be applied (validation, removed entity, closed round);
  the client marks it `FAILED` and shows the message.

Action names (`SyncAction.wire`): `group.create`, `group.join`, `group.leave`,
`activity.add`, `activity.update`, `activity.delete`, `decision.start`, `decision.vote`,
`decision.close`, `plan.save`, `plan.complete`, `memory.save`, `profile.update`.

## Error body

```json
{ "error": "This round is already closed.", "field": "activityId" }
```

`field` is present when the message belongs to one form field; the client turns it into a
localised `FieldError` rather than showing raw server text.
