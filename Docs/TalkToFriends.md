# Talk To Friends — Logic Reference

Source: `Talk_To_Friends.lua` (VT-MAK VR-Forces, 2016)
Unity port: `Assets/Scripts/Wanderer.cs`

---

## State Machine

```
Searching ──► Asking ──► Moving ──► Talking
    ▲              │          │          │
    └──────────────┴──────────┴──────────┘
         (all paths return to Searching)
```

| State | Lua | Unity |
|---|---|---|
| **Searching** | wander subtask running, check interval | NavMeshAgent wandering, `Time.time` interval |
| **Asking** | sent message, waiting up to `askTimeout` | same |
| **Moving** | wander stopped, moving to midpoint | same |
| **Talking** | DI-Guy `talk1` animation playing | timer counts `interactMinSeconds`–`interactMaxSeconds` |

---

## Message Handshake

```
Agent A (Searching)          Agent B (Searching)
     │                              │
     │──── "Want to talk?" ────────►│
     │                              │  enters Moving
     │◄─── "Okay" ─────────────────│
     │  enters Moving               │
     │                              │
  both navigate to midpoint = (A.pos + B.pos) / 2
     │                              │
  arrived ──► Talking            arrived ──► Talking
  (independently)                (independently)
     │                              │
  timer done ──► Searching       timer done ──► Searching
```

If B is busy:
```
A ──── "Want to talk?" ────► B
A ◄─── "Can't talk"  ──────  B   (A returns to Searching immediately)
```

If B doesn't respond (no timeout reply):
```
A waits askTimeout seconds → returns to Searching automatically
```

---

## Lua → Unity Mapping

| Lua | Unity |
|---|---|
| `vrf:setTickPeriod(1.0)` | runs every frame, time-gated by `searchInterval` |
| `vrf:getSimObjectsNear(pos, dist)` | `Physics.OverlapSphere(pos, friendDistance)` |
| `math.random(1, #nearbyEntities)` | `Random.Range(0, candidates.Count)` |
| `vrf:forcesHostile(...)` | `w.isSmartAgent` check (DUMB agents ignored) |
| `vrf:sendMessage(entity, "...")` | `entity.ReceiveMessage("...", this)` |
| `vrf:startSubtask("move-to-location", {aiming_point})` | `NavMeshAgent.SetDestination(meetingPoint)` |
| `vrf:isSubtaskComplete(moveSubtaskId)` | `remainingDistance <= arrivedThreshold` |
| `vrf:startSubtask("di-guy-animation-task", {kind="talk1"})` | `_talkTimer` countdown |
| `lastSearchTime = vrf:getExerciseTime() - math.random(searchInterval)` | `_lastSearchTime = Time.time - Random.Range(0, searchInterval)` |

---

## Forced Adaptations

| Topic | Lua | Unity | Note |
|---|---|---|---|
| **Tick rate** | 1 s fixed tick (`vrf:setTickPeriod`) | Every frame, time-gated | No meaningful difference — logic is identical |
| **Messaging** | VRF network text messages (async) | Direct method calls (sync, same frame) | **Forced.** VRF runs distributed; Unity is single-process. Sync calls work correctly because Unity executes one Update() at a time. |
| **Hostile check** | `vrf:forcesHostile(typeA, typeB)` | `isSmartAgent` flag | **Forced.** No force-type concept in this sim. DUMB agents serve as the "non-participating" equivalent. |
| **Talk animation** | DI-Guy `talk1` skeleton animation | Color change + timer | **Forced.** No humanoid rig. Color (orange) + duration timer is the visual stand-in. |
| **`nearFriend()` phase** | Separate sub-step: if dist < 2 m, face friend; else move-to friend directly | Implicit — arrivedThreshold covers it | Minor. The NavMesh arrival check handles the same condition. |
| **Independent Talking timers** | Each agent plays animation independently → finishes independently | Each agent has its own `_talkTimer` → `EndTalk()` independently | Faithful to original. No cross-agent coupling in Talking phase. |
| **Statistics** | Not tracked in Lua | `TotalInteractions`, `CompletedInteractions`, `TotalDurationSeconds` | Addition, not in original. Counted on initiator side only (once per pair). |
