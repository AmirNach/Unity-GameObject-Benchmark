using UnityEngine;

/// <summary>
/// Exposes Time.timeScale to the Inspector.
/// OnValidate fires only when the value is changed in the Inspector — zero runtime cost.
/// </summary>
public class TimeManager : MonoBehaviour
{
    [Range(0f, 1000f)]
    [Tooltip("Controls Time.timeScale. 1 = normal speed, 0 = paused, 2 = double speed.")]
    public float timeScale = 1f;

    // Applied once on scene start
    void Start() => Apply();

    // Fires only when the Inspector field is edited — no per-frame cost
    void OnValidate() => Apply();

    void Apply() => Time.timeScale = timeScale;
}
