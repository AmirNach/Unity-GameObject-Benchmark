using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Port of Talk_To_Friends.lua (VT-MAK VR-Forces, 2016) to Unity NavMesh agents.
/// State machine: Searching → Asking → Moving → Talking → Searching
/// Message passing is direct method calls instead of VRF text messages.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Wanderer : MonoBehaviour
{
    // ── Agent type ────────────────────────────────────────────────────────
    [Header("Agent Type")]
    [Tooltip("SMART agents run the full state machine. DUMB agents wander only.")]
    public bool  isSmartAgent = true;
    public Color dumbColor    = new Color(0.55f, 0.55f, 0.55f);

    // ── Movement ──────────────────────────────────────────────────────────
    [Header("Movement")]
    public float wanderRadius     = 200f;
    public float arrivedThreshold = 2f;

    // ── Social behaviour (SMART only) ─────────────────────────────────────
    [Header("Social Behaviour — matches Talk_To_Friends.lua")]
    [Tooltip("Seconds between search attempts  (lua: searchInterval = 20)")]
    public float searchInterval  = 20f;
    [Tooltip("Seconds to wait for a reply      (lua: askTimeout = 2)")]
    public float askTimeout      = 2f;
    [Tooltip("Radius used to find a nearby friend (lua: taskParameters.FriendDistance)")]
    public float friendDistance  = 30f;
    [Tooltip("Seconds spent talking at the meeting point")]
    public float interactMinSeconds = 5f;
    public float interactMaxSeconds = 10f;

    // ── Colors (one per state) ────────────────────────────────────────────
    [Header("State Colors (SMART)")]
    public Color searchingColor = new Color(0.2f,  0.6f,  1.0f);   // blue
    public Color askingColor    = new Color(1.0f,  0.9f,  0.0f);   // yellow
    public Color movingColor    = new Color(0.0f,  0.85f, 0.4f);   // green
    public Color talkingColor   = new Color(1.0f,  0.35f, 0.0f);   // orange

    // ── Visuals ───────────────────────────────────────────────────────────
    [Header("Vision Visuals (SMART)")]
    public Color rayColor      = new Color(1.0f, 0.95f, 0.2f);
    public Color circleColor   = new Color(1.0f, 1.0f,  1.0f, 0.35f);
    [Range(0.1f, 3f)] public float rayWidth      = 0.5f;
    [Range(0.1f, 3f)] public float circleWidth   = 0.3f;
    [Range(12,   64)] public int   circleSegments = 36;

    // ── Stuck detection ───────────────────────────────────────────────────
    [Header("Wall Bounce")]
    public float stuckTimeout  = 1.5f;
    public float stuckSpeedMin = 0.5f;

    // ── State machine ─────────────────────────────────────────────────────
    public enum AgentState { Searching, Asking, Moving, Talking }

    private AgentState _state          = AgentState.Searching;
    private Wanderer   _friend         = null;
    private float      _lastSearchTime;        // game time of last FindFriend call
    private float      _askTime;               // game time when "Want to talk?" was sent
    private Vector3    _meetingPoint;           // midpoint cached on entering Moving
    private bool       _movingStarted  = false;
    private float      _talkTimer      = 0f;
    private float      _talkDuration   = 0f;
    private bool       _isInitiator    = false; // true for the agent that sent the first message
    private float      _stuckTimer     = 0f;

    // ── Components ────────────────────────────────────────────────────────
    private NavMeshAgent          _agent;
    private Renderer              _body;
    private MaterialPropertyBlock _mpb;
    private LineRenderer          _rayLine;
    private LineRenderer          _circleLine;

    private static readonly int ColorID = Shader.PropertyToID("_Color");

    // ── Public accessors ──────────────────────────────────────────────────
    public AgentState State        => _state;
    /// <summary>True while not in Searching — matches original IsInteracting usage in PerformanceManager.</summary>
    public bool IsInteracting      => _state != AgentState.Searching;
    public bool CanInteract        => _state == AgentState.Searching;

    public static bool VisualsEnabled = true;

    // ── Global statistics ─────────────────────────────────────────────────
    public static int   TotalInteractions     = 0;
    public static int   CompletedInteractions = 0;
    public static float TotalDurationSeconds  = 0f;

    // ── Live state counters (SMART agents only) ────────────────────────────
    public static int CountSearching = 0;
    public static int CountAsking    = 0;
    public static int CountMoving    = 0;
    public static int CountTalking   = 0;

    // ─────────────────────────────────────────────────────────────────────
    void Start()
    {
        _agent = GetComponent<NavMeshAgent>();
        _body  = GetComponent<Renderer>();
        _mpb   = new MaterialPropertyBlock();

        if (isSmartAgent)
        {
            BuildRayRenderer();
            BuildCircleRenderer();
        }

        SetColor(isSmartAgent ? searchingColor : dumbColor);

        // Stagger first search so all agents don't fire at the same frame
        // (matches lua: lastSearchTime = vrf:getExerciseTime() - math.random(searchInterval))
        _lastSearchTime = Time.time - Random.Range(0f, searchInterval);

        // Register initial state in live counters
        if (isSmartAgent) CountSearching++;

        PickDestination();
    }

    void OnDestroy()
    {
        if (isSmartAgent) UpdateCounter(_state, -1);
    }

    // ── State counter helpers ──────────────────────────────────────────────
    void ChangeState(AgentState next)
    {
        if (isSmartAgent) UpdateCounter(_state, -1);
        _state = next;
        if (isSmartAgent) UpdateCounter(_state, +1);
    }

    static void UpdateCounter(AgentState s, int delta)
    {
        switch (s)
        {
            case AgentState.Searching: CountSearching += delta; break;
            case AgentState.Asking:    CountAsking    += delta; break;
            case AgentState.Moving:    CountMoving    += delta; break;
            case AgentState.Talking:   CountTalking   += delta; break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        // Arrived at wander destination → pick a new one (Searching only)
        if (_state == AgentState.Searching
            && _agent.isOnNavMesh && !_agent.pathPending
            && _agent.remainingDistance <= arrivedThreshold)
            PickDestination();

        // Stuck detection only while freely wandering
        if (_state == AgentState.Searching && _agent.isOnNavMesh && !_agent.isStopped)
            CheckStuck();

        // Visuals
        if (VisualsEnabled && isSmartAgent)
        {
            DrawRay();
            DrawCircle();
        }

        // State machine tick — SMART only (matches lua: function tick())
        if (isSmartAgent)
            Tick();
    }

    // ── State machine (lua: function tick()) ──────────────────────────────
    void Tick()
    {
        switch (_state)
        {
            // lua: if (myState == "searching") → startWander + findFriend interval
            case AgentState.Searching:
                if (Time.time > _lastSearchTime + searchInterval)
                    FindFriend();
                break;

            // lua: if (myState == "asking") → check askTimeout
            case AgentState.Asking:
                if (Time.time > _askTime + askTimeout)
                {
                    // Target didn't respond (not running this task / busy)
                    _friend = null;
                    EnterSearching();
                }
                break;

            // lua: if (myState == "moving") → stopWander + moveToFriend
            case AgentState.Moving:
                if (_friend == null) { EnterSearching(); break; }
                if (MoveToFriend())
                    EnterTalking();
                break;

            // lua: if (myState == "talking") → talkToFriend → back to searching
            case AgentState.Talking:
                _talkTimer += Time.deltaTime;
                if (_talkTimer >= _talkDuration)
                    EndTalk();
                break;
        }
    }

    // ── State transitions ─────────────────────────────────────────────────

    /// <summary>lua: function findFriend() — OverlapSphere replaces getSimObjectsNear.</summary>
    void FindFriend()
    {
        _lastSearchTime = Time.time;

        var hits       = Physics.OverlapSphere(transform.position, friendDistance);
        var candidates = new List<Wanderer>(16);

        foreach (var h in hits)
        {
            if (h.gameObject == gameObject) continue;           // skip self
            var w = h.GetComponent<Wanderer>();
            // lua: skip hostile forces → we skip non-smart agents (equivalent filter)
            if (w != null && w.isSmartAgent && w.CanInteract)
                candidates.Add(w);
        }

        if (candidates.Count == 0) return;

        // lua: local choice = math.random(1, #nearbyEntities)
        _friend      = candidates[Random.Range(0, candidates.Count)];
        ChangeState(AgentState.Asking);
        _askTime     = Time.time;
        _isInitiator = true;

        SetColor(askingColor);

        // lua: vrf:sendMessage(myFriend, "Want to talk?")
        _friend.ReceiveMessage("Want to talk?", this);
    }

    void EnterSearching()
    {
        ChangeState(AgentState.Searching);
        _movingStarted = false;
        _isInitiator   = false;
        _friend        = null;
        _agent.isStopped = false;
        SetColor(isSmartAgent ? searchingColor : dumbColor);
        PickDestination();
    }

    void EnterMoving(Vector3 friendPos)
    {
        ChangeState(AgentState.Moving);
        _movingStarted = false;
        // Cache midpoint now — lua: a:setBearingInclRange(delta:getBearing(), 0, delta:getRange()/2)
        _meetingPoint    = (transform.position + friendPos) * 0.5f;
        _agent.isStopped = false;
        SetColor(movingColor);
    }

    void EnterTalking()
    {
        ChangeState(AgentState.Talking);
        _talkTimer    = 0f;
        _talkDuration = Random.Range(interactMinSeconds, interactMaxSeconds);

        _agent.isStopped = true;
        if (_friend != null) FaceTarget(_friend.transform.position);
        SetColor(talkingColor);

        // Count once per pair (initiator only)
        if (_isInitiator) TotalInteractions++;
    }

    void EndTalk()
    {
        // lua: talkToFriend() complete → myState = "searching"
        if (_isInitiator)
        {
            TotalDurationSeconds  += _talkTimer;
            CompletedInteractions++;
        }

        EnterSearching();
        // Friend ends independently via its own _talkTimer — no coupling needed
    }

    // ── Meeting point movement (lua: function moveToFriend()) ─────────────
    /// <returns>True when arrived at meeting point.</returns>
    bool MoveToFriend()
    {
        if (!_movingStarted)
        {
            _movingStarted = true;
            // lua: vrf:startSubtask("move-to-location", {aiming_point = midpoint})
            if (NavMesh.SamplePosition(_meetingPoint, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
            else
                _agent.SetDestination(_friend.transform.position); // fallback
            return false; // don't evaluate arrival on the same frame
        }

        return _agent.isOnNavMesh
            && !_agent.pathPending
            && _agent.remainingDistance <= arrivedThreshold;
    }

    // ── Message handler (lua: function receiveTextMessage(message, sender)) ─
    /// <summary>
    /// Direct method call — replaces VRF text messaging.
    /// Handles: "Want to talk?", "Okay", "Can't talk"
    /// </summary>
    public void ReceiveMessage(string message, Wanderer sender)
    {
        switch (message)
        {
            case "Want to talk?":
                if (_state == AgentState.Searching)
                {
                    // lua: myState = "moving"; vrf:sendMessage(sender, "Okay")
                    _friend = sender;
                    EnterMoving(sender.transform.position);
                    sender.ReceiveMessage("Okay", this);
                }
                else
                {
                    // lua: vrf:sendMessage(sender, "Can't talk")
                    sender.ReceiveMessage("Can't talk", this);
                }
                break;

            case "Okay":
                if (_state == AgentState.Asking)
                {
                    // lua: myState = "moving"
                    _friend = sender;
                    EnterMoving(sender.transform.position);
                }
                break;

            case "Can't talk":
                if (_state == AgentState.Asking)
                {
                    // lua: myState = "searching" (immediate, no timeout wait)
                    _friend = null;
                    EnterSearching();
                }
                break;
        }
    }

    // ── Stuck detection + wall bounce ─────────────────────────────────────
    void CheckStuck()
    {
        bool movingSlow = _agent.velocity.magnitude < stuckSpeedMin;
        bool hasFarDest = !_agent.pathPending
                          && _agent.remainingDistance > arrivedThreshold * 2f;

        if (movingSlow && hasFarDest)
        {
            // unscaledDeltaTime — detection must not speed up with timeScale
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

    void BounceOffWall()
    {
        Vector3 incoming = _agent.desiredVelocity;
        if (incoming.sqrMagnitude < 0.01f) incoming = transform.forward;
        incoming.y = 0f;
        incoming.Normalize();

        Vector3 origin = transform.position + Vector3.up * 2f;
        Vector3 reflected;

        if (Physics.Raycast(origin, incoming, out RaycastHit hit, 30f)
            && hit.collider.GetComponent<Wanderer>() == null)
        {
            Vector3 wallNormal = hit.normal;
            wallNormal.y = 0f;
            if (wallNormal.sqrMagnitude < 0.01f) wallNormal = -incoming;
            wallNormal.Normalize();
            reflected = Vector3.Reflect(incoming, wallNormal);
        }
        else
        {
            reflected  = -incoming;
            reflected += new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
        }

        reflected.y = 0f;
        reflected.Normalize();

        if (reflected.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(reflected);

        Vector3 target = transform.position + reflected * wanderRadius;
        if (NavMesh.SamplePosition(target, out NavMeshHit navHit, wanderRadius, NavMesh.AllAreas))
            _agent.SetDestination(navHit.position);
        else
            PickDestination();
    }

    // ── Line renderer setup ───────────────────────────────────────────────
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

    void DrawRay()
    {
        _rayLine.startWidth = rayWidth;
        _rayLine.endWidth   = rayWidth * 0.25f;
        _rayLine.startColor = rayColor;
        _rayLine.endColor   = new Color(rayColor.r, rayColor.g, rayColor.b, 0f);

        Vector3 origin = transform.position + Vector3.up;
        _rayLine.SetPosition(0, origin);
        _rayLine.SetPosition(1, origin + transform.forward * friendDistance);
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
                transform.position.x + Mathf.Cos(t) * friendDistance,
                y,
                transform.position.z + Mathf.Sin(t) * friendDistance));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
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
