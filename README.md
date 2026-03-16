# Unity GameObject Benchmark

A real-time multi-agent simulation built in Unity for benchmarking GameObject performance at scale.

## Overview

Spawns hundreds to thousands of autonomous agents on a NavMesh, split into two types:

| Type | Behavior |
|---|---|
| **Smart** | Full NavMesh wandering + raycasted detection + timed interactions with other smart agents |
| **Dumb** | NavMesh wandering only — no detection, no interactions |

## Features

- Configurable **Smart / Dumb agent counts** with live respawn from the HUD
- **Time Scale** control (0–1000×) for accelerated simulation
- Per-frame performance HUD: FPS, interaction count, average interaction duration
- Toggle agent visuals (renderers + vision rays) for raw logic benchmarking
- Minimal mode — hides all gizmos for cleanest render
- Stuck-detection with wall-bounce recovery, decoupled from time scale

## Scripts

| Script | Purpose |
|---|---|
| `AgentSpawner.cs` | Spawns Smart/Dumb agents under a shared parent; supports runtime respawn |
| `Wanderer.cs` | Per-agent: NavMesh wandering, vision raycast, interaction coroutines, stuck recovery |
| `TimeManager.cs` | Exposes `Time.timeScale` to the Inspector with a 0–1000 range |
| `PerformanceManager.cs` | Builds the runtime HUD, caches renderers, handles visuals toggle and respawn |

## Getting Started

1. Open the project in **Unity 2022+** (URP)
2. Open `Assets/Scenes/Main.unity`
3. Hit **Play** — agents spawn automatically
4. Use the on-screen HUD (top-left) to control time scale, agent counts, and visuals

## Requirements

- Unity 2022.3 LTS or newer
- AI Navigation package (NavMesh)
- Universal Render Pipeline (URP)
