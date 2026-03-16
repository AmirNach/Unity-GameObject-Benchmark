using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Spawns two kinds of agents under a shared "Agents" parent:
///   • SMART agents — full NavMesh wanderers with detection, vision rays, and interactions.
///   • DUMB  agents — NavMesh wanderers only; no vision, no interactions.
/// Counts are configurable in the Inspector.
/// </summary>
public class AgentSpawner : MonoBehaviour
{
    public GameObject agentPrefab;

    [Header("Agent Counts")]
    [Tooltip("Agents that detect and interact with each other")]
    public int smartCount = 400;
    [Tooltip("Agents that only wander — no detection, no interactions")]
    public int dumbCount  = 100;

    public float spawnAreaHalfSize = 480f;

    void Start() => DoSpawn(smartCount, dumbCount);

    /// <summary>Called by PerformanceManager when the user hits Respawn in the HUD.</summary>
    public void Respawn(int smart, int dumb)
    {
        smartCount = smart;
        dumbCount  = dumb;

        // Destroy all existing agents
        var existing = GameObject.Find("Agents");
        if (existing != null) Destroy(existing);

        DoSpawn(smartCount, dumbCount);
    }

    void DoSpawn(int smart, int dumb)
    {
        var parent = new GameObject("Agents");

        int smartSpawned = SpawnGroup(parent, smart, "Smart", true);
        int dumbSpawned  = SpawnGroup(parent, dumb,  "Dumb",  false);

        Debug.Log($"[AgentSpawner] Spawned {smartSpawned} SMART + {dumbSpawned} DUMB = {smartSpawned + dumbSpawned} total agents.");
    }

    int SpawnGroup(GameObject parent, int count, string prefix, bool smart)
    {
        int spawned  = 0;
        int attempts = 0;
        int maxAttempts = count * 10;

        while (spawned < count && attempts < maxAttempts)
        {
            attempts++;

            Vector3 candidate = new Vector3(
                Random.Range(-spawnAreaHalfSize, spawnAreaHalfSize),
                1f,
                Random.Range(-spawnAreaHalfSize, spawnAreaHalfSize));

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                var go = Instantiate(agentPrefab, hit.position, Quaternion.identity, parent.transform);
                go.name = $"{prefix}_{spawned:000}";

                var w = go.GetComponent<Wanderer>();
                if (w != null) w.isSmartAgent = smart;

                spawned++;
            }
        }

        return spawned;
    }
}
