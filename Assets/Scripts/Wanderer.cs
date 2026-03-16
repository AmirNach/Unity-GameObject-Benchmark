using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class Wanderer : MonoBehaviour
{
    // ─────────────────────────────────────────────
    [Header("Agent Type")]
    [Tooltip("SMART agents detect & interact. DUMB agents wander only — no interactions.")]
    public bool  isSmartAgent = true;
    public Color dumbColor    = new Color(0.55f, 0.55f, 0.55f); // grey

    // ─────────────────────────────────────────────
    [Header("Movement")]
    public float wanderRadius     = 200f;
    public float arrivedThreshold = 2f;

    // ─────────────────────────────────────────────
    [Header("Detection (SMART only)")]
    public float detectionRadius = 30f;

    // ─────────────────────────────────────────────
    [Header("Colors (SMART)")]
    public Color normalColor   = new Color(0.2f,  0.6f,  1.0f);
    public Color interactColor = new Color(1.0f,  0.35f, 0.0f);
    public Color cooldownColor = new Color(1.0f,  0.4f,  0.75f); // pink during cooldown
    public Color rayColor      = new Color(1.0f,  0.95f, 0.2f);
    public Color circleColor   = new Color(1.0f,  1.0f,  1.0f, 0.35f);

    [Header("Line Widths")]
    [Range(0.1f, 3f)] public float rayWidth    = 0.5f;
    [Range(0.1f, 3f)] public float circleWidth = 0.3f;
    [Range(12, 64)]   public int   circleSegments = 36;

    // ─────────────────────────────────────────────
    [Header("Interaction Duration")]
    public float interactMinSeconds = 5f;
    public float interactMaxSeconds = 10f;

    [Header("Interaction Cooldown")]
    [Tooltip("Seconds after an interaction ends before this agent can start a new one")]
    public float interactionCooldown = 3f;

    // ─────────────────────────────────────────────
    [Header("Wall Bounce (stuck detection)")]
    [Tooltip("Seconds of near-zero velocity before triggering bounce")]
    public float stuckTimeout  = 1.5f;
    [Tooltip("Speed (units/s) below which the agent is considered possibly stuck")]
    public float stuckSpeedMin = 0.5f;

    // ── internals ─────────────────────────────────────────────────────────
    private NavMeshAgent          _agent;
    private Renderer              _body;
    private MaterialPropertyBlock _mpb;
    private LineRenderer          _rayLine;
    private LineRenderer          _circleLine;
    private float                 _cooldown        = 0f;
    private float                 _stuckTimer      = 0f;
    private bool                  _wasCoolingDown  = false;

    private static readonly int ColorID = Shader.PropertyToID("_Color");

    public bool IsInteracting { get; private set; }
    public bool CanInteract   => !IsInteracting && _cooldown <= 0f;

    public static bool VisualsEnabled = true;

    // ── Global interaction statistics ─────────────────────────────────────
    public static int   TotalInteractions     = 0;
    public static int   CompletedInteractions = 0;
    public static float TotalDurationSeconds  = 0f;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _body  = GetComponent<Renderer>();
        _mpb   = new MaterialPropertyBlock();

        // SMART agents get vision rays; DUMB agents don't need them
        if (isSmartAgent)
        {
            BuildRayRenderer();
            BuildCircleRenderer();
        }

        SetColor(isSmartAgent ? normalColor : dumbColor);
        PickDestination();
    }

    // ── line renderer setup ───────────────────────────────────────────────
    void BuildRayRenderer()
    {
        var go = new GameObject("VisionRay");
        go.transform.SetParent(transform, false);
        _rayLine = go.AddComponent<LineRenderer>();
        _rayLine.positionCount     = 2;
        _rayLine.useWorldSpace     = true;
        _rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _rayLine.receiveShadows    = false;
        _rayLine.material          = new Material(Shader.Find("Sprites/Default"));
    }

    void BuildCircleRenderer()
    {
        var go = new GameObject("DetectionCircle");
        go.transform.SetParent(transform, false);
        _circleLine = go.AddComponent<LineRenderer>();
        _circleLine.positionCount     = circleSegments + 1;
        _circleLine.loop              = false;
        _circleLine.useWorldSpace     = true;
        _circleLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _circleLine.receiveShadows    = false;
        _circleLine.material          = new Material(Shader.Find("Sprites/Default"));
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        // ── reached destination → pick new one ───────────────────────────
        if (_agent.isOnNavMesh && !_agent.pathPending
            && _agent.remainingDistance <= arrivedThreshold)
            PickDestination();

        // ── cooldown tick ─────────────────────────────────────────────────
        if (_cooldown > 0f)
        {
            _cooldown -= Time.deltaTime;
            _wasCoolingDown = true;
        }
        else if (_wasCoolingDown)
        {
            // Cooldown just expired → restore normal colour
            _wasCoolingDown = false;
            SetColor(isSmartAgent ? normalColor : dumbColor);
        }

        // ── stuck / wall bounce ───────────────────────────────────────────
        if (!IsInteracting && !_agent.isStopped)
            CheckStuck();

        // ── visuals: SMART only, skipped when performance mode is on ──────
        if (VisualsEnabled && isSmartAgent)
        {
            DrawRay();
            DrawCircle();
        }

        // ── vision check: SMART agents only, must be fully free ──────────
        if (CanInteract && isSmartAgent)
            CheckForward();
    }

    // ── draw helpers ──────────────────────────────────────────────────────
    void DrawRay()
    {
        _rayLine.startWidth = rayWidth;
        _rayLine.endWidth   = rayWidth * 0.25f;
        _rayLine.startColor = rayColor;
        _rayLine.endColor   = new Color(rayColor.r, rayColor.g, rayColor.b, 0f);

        Vector3 origin = transform.position + Vector3.up;
        _rayLine.SetPosition(0, origin);
        _rayLine.SetPosition(1, origin + transform.forward * detectionRadius);
    }

    void DrawCircle()
    {
        _circleLine.startWidth = circleWidth;
        _circleLine.endWidth   = circleWidth;
        _circleLine.startColor = circleColor;
        _circleLine.endColor   = circleColor;

        float y = transform.position.y + 0.5f;
        for (int i = 0; i <= circleSegments; i++)
        {
            float t = (float)i / circleSegments * Mathf.PI * 2f;
            _circleLine.SetPosition(i, new Vector3(
                transform.position.x + Mathf.Cos(t) * detectionRadius,
                y,
                transform.position.z + Mathf.Sin(t) * detectionRadius));
        }
    }

    // ── stuck detection + wall bounce ─────────────────────────────────────
    void CheckStuck()
    {
        bool movingSlow = _agent.velocity.magnitude < stuckSpeedMin;
        bool hasFarDest = !_agent.pathPending
                          && _agent.remainingDistance > arrivedThreshold * 2f;

        if (movingSlow && hasFarDest)
        {
            // unscaledDeltaTime — stuck detection must not speed up/freeze with timeScale
            _stuckTimer += Time.unscaledDeltaTime;
            if (_stuckTimer >= stuckTimeout)
            {
                _stuckTimer = 0f;
                BounceOffWall();
            }
        }
        else
        {
            _stuckTimer = 0f;
        }
    }

    /// <summary>
    /// Reflects the agent's movement direction off the wall normal — like a billiard ball.
    /// If no physical wall is found, reverses direction (NavMesh boundary case).
    /// </summary>
    void BounceOffWall()
    {
        // Incoming direction = where the agent was trying to go
        Vector3 incoming = _agent.desiredVelocity;
        if (incoming.sqrMagnitude < 0.01f) incoming = transform.forward;
        incoming.y = 0f;
        incoming.Normalize();

        Vector3 origin = transform.position + Vector3.up * 2f;
        Vector3 reflected;

        // Cast forward to find the wall surface
        if (Physics.Raycast(origin, incoming, out RaycastHit hit, 30f)
            && hit.collider.GetComponent<Wanderer>() == null) // don't reflect off other agents
        {
            Vector3 wallNormal = hit.normal;
            wallNormal.y = 0f;
            if (wallNormal.sqrMagnitude < 0.01f) wallNormal = -incoming;
            wallNormal.Normalize();
            reflected = Vector3.Reflect(incoming, wallNormal);
        }
        else
        {
            // No physical wall — likely NavMesh boundary. Reverse + small jitter.
            reflected = -incoming;
            reflected += new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
        }

        reflected.y = 0f;
        reflected.Normalize();

        // Face the new direction
        if (reflected.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(reflected);

        // Pick destination along reflected direction
        Vector3 target = transform.position + reflected * wanderRadius;
        if (NavMesh.SamplePosition(target, out NavMeshHit navHit, wanderRadius, NavMesh.AllAreas))
            _agent.SetDestination(navHit.position);
        else
            PickDestination();
    }

    // ── detection ─────────────────────────────────────────────────────────
    void CheckForward()
    {
        Vector3 origin = transform.position + Vector3.up;
        if (!Physics.Raycast(origin, transform.forward, out RaycastHit hit, detectionRadius))
            return;

        if (hit.collider.gameObject == gameObject) return;

        var other = hit.collider.GetComponent<Wanderer>();
        // Only SMART ↔ SMART interactions; both must be fully free
        if (other != null && other.isSmartAgent && other.CanInteract)
            StartCoroutine(Interact(other));
    }

    // ── interaction ───────────────────────────────────────────────────────
    IEnumerator Interact(Wanderer other)
    {
        // Lock both immediately — prevents any third agent from grabbing either
        IsInteracting       = true;
        other.IsInteracting = true;
        TotalInteractions++;

        float duration = Random.Range(interactMinSeconds, interactMaxSeconds);

        _agent.isStopped       = true;
        other._agent.isStopped = true;

        SetColor(interactColor);
        other.SetColor(interactColor);
        FaceTarget(other.transform.position);
        other.FaceTarget(transform.position);

        // Manual game-time wait — scales correctly with timeScale, but
        // real-time safety cap prevents infinite freeze when timeScale = 0.
        float gameElapsed = 0f;
        float realElapsed = 0f;
        const float realCap = 120f; // 2-minute real-time hard limit
        while (gameElapsed < duration && realElapsed < realCap)
        {
            gameElapsed += Time.deltaTime;
            realElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        TotalDurationSeconds  += gameElapsed; // record actual elapsed, not target
        CompletedInteractions++;

        // Switch to cooldown colour — will auto-restore to normal when cooldown expires
        SetColor(cooldownColor);
        other.SetColor(other.cooldownColor);

        _agent.isStopped       = false;
        other._agent.isStopped = false;

        IsInteracting       = false;
        other.IsInteracting = false;

        // Cooldown prevents immediate re-detection after they separate
        _cooldown       = interactionCooldown;
        other._cooldown = other.interactionCooldown;

        PickDestination();
        other.PickDestination();
    }

    // ── helpers ───────────────────────────────────────────────────────────
    void PickDestination()
    {
        if (!_agent.isOnNavMesh) return;
        _agent.SetDestination(RandomNavMeshPoint(transform.position, wanderRadius));
    }

    Vector3 RandomNavMeshPoint(Vector3 origin, float radius)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 rnd = Random.insideUnitSphere * radius + origin;
            if (NavMesh.SamplePosition(rnd, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;
        }
        return origin;
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    public void SetColor(Color c)
    {
        _body.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorID, c);
        _body.SetPropertyBlock(_mpb);
    }
}
