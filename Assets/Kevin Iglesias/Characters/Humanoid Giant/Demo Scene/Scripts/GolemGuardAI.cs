using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// Castle-guard golem: sees/hears the player, chases with predictive interception,
/// throws them back outside the castle, and LEARNS which approach routes the player
/// favours (lightweight online reinforcement + softmax patrol selection).
[RequireComponent(typeof(NavMeshAgent))]
public class GolemGuardAI : MonoBehaviour
{
    enum State { Patrol, Investigate, Chase }

    [Header("References")]
    public Transform player;              // auto-finds tag "Player" if empty
    public Transform ejectPoint;          // spot outside the castle gate
    public Transform castleGoal;          // what the player is sneaking toward
    public Transform[] guardPoints;       // gaps, wall corners, gate, ruins...
    public Animator animator;             // optional, uses float "Speed"

    [Header("Senses")]
    public float viewDistance = 18f;
    [Range(10, 180)] public float viewAngle = 60f;   // half-angle of the cone
    public float hearingRadius = 7f;      // scaled by how fast the player moves
    public float eyeHeight = 3f;
    public LayerMask sightBlockers = ~0;  // exclude the golem's own layer

    [Header("Behaviour")]
    public float patrolSpeed = 2.5f, chaseSpeed = 6f;
    public float grabRange = 2.5f, breachRadius = 4f;
    public float suspicionRate = 1.5f, suspicionDecay = 0.4f, giveUpTime = 6f;

    [Header("Adaptation (ML-lite)")]
    [Range(0, 1)] public float learnRate = 0.3f;
    public float exploreTemp = 0.6f;      // lower = sticks to learned hotspots
    public float senseGrowth = 1.08f;     // senses sharpen after each breach
    public bool persistLearning = true;

    public UnityEvent onPlayerCaught, onCastleBreached;

    NavMeshAgent agent;
    State state;
    float suspicion, lostTimer, waitTimer, sampleTimer;
    Vector3 lastKnownPos, lastPlayerPos, playerVel;
    float[] weights;
    readonly Vector3[] route = new Vector3[12];   // recent player positions
    int routeCount, routeHead, target = -1;
    bool breached;
    const string SaveKey = "GolemGuardWeights";

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!player) { var p = GameObject.FindWithTag("Player"); if (p) player = p.transform; }
        weights = new float[guardPoints.Length];
        if (persistLearning) Load();
        if (player) lastPlayerPos = player.position;
    }

    void Update()
    {
        if (!player) return;
        TrackPlayer();

        bool seen = CanSee(), heard = CanHear();
        float dist = Vector3.Distance(transform.position, player.position);

        if (seen || heard)
        {
            float closeness = Mathf.Lerp(2.5f, 0.6f, dist / viewDistance);
            suspicion += suspicionRate * (seen ? 1f : 0.5f) * closeness * Time.deltaTime;
            lastKnownPos = player.position;
        }
        else suspicion -= suspicionDecay * Time.deltaTime;
        suspicion = Mathf.Clamp(suspicion, 0f, 1.5f);

        if (suspicion >= 1f && state != State.Chase) { state = State.Chase; lostTimer = 0; Learn(1f); }
        else if (suspicion > 0.3f && state == State.Patrol) state = State.Investigate;

        switch (state)
        {
            case State.Patrol: Patrol(); break;
            case State.Investigate: Investigate(); break;
            case State.Chase: Chase(seen || heard, dist); break;
        }

        CheckBreach();
        if (animator) animator.SetFloat("Speed", agent.velocity.magnitude);
    }

    // ---------- Senses ----------
    bool CanSee()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight + transform.forward * 0.8f;
        Vector3 chest = player.position + Vector3.up * 1f;
        Vector3 to = chest - eye;
        if (to.magnitude > viewDistance || Vector3.Angle(transform.forward, to) > viewAngle) return false;
        if (Physics.Linecast(eye, chest, out RaycastHit hit, sightBlockers, QueryTriggerInteraction.Ignore))
            return hit.transform == player || hit.transform.IsChildOf(player);
        return true;
    }

    bool CanHear()
    {
        float noise = Mathf.Clamp01(playerVel.magnitude / 5f);   // creeping is quiet
        return Vector3.Distance(transform.position, player.position) < hearingRadius * noise;
    }

    void TrackPlayer()
    {
        Vector3 v = (player.position - lastPlayerPos) / Mathf.Max(Time.deltaTime, 0.0001f);
        playerVel = Vector3.Lerp(playerVel, v, 0.2f);
        lastPlayerPos = player.position;

        if ((sampleTimer -= Time.deltaTime) <= 0f)
        {
            sampleTimer = 1f;
            route[routeHead] = player.position;
            routeHead = (routeHead + 1) % route.Length;
            routeCount = Mathf.Min(routeCount + 1, route.Length);
        }
    }

    // ---------- States ----------
    void Patrol()
    {
        agent.speed = patrolSpeed;
        if (guardPoints.Length == 0) return;
        if (target < 0) { GoTo(PickPoint()); return; }
        if (Arrived())
        {
            // Linger longer (ambush) at spots the player has used before
            if (waitTimer <= 0f) waitTimer = 2f + weights[target] * 3f;
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f) GoTo(PickPoint());
            else transform.Rotate(0f, 25f * Time.deltaTime, 0f);   // scan around
        }
    }

    void Investigate()
    {
        agent.speed = (patrolSpeed + chaseSpeed) * 0.5f;
        agent.SetDestination(lastKnownPos);
        if (Arrived())
        {
            transform.Rotate(0f, 60f * Time.deltaTime, 0f);
            if (suspicion <= 0.05f) { state = State.Patrol; target = -1; }
        }
    }

    void Chase(bool sensed, float dist)
    {
        agent.speed = chaseSpeed;
        lostTimer = sensed ? 0f : lostTimer + Time.deltaTime;

        // Cunning: cut the player off instead of following their tail
        Vector3 goal = lastKnownPos;
        if (sensed)
        {
            float lead = Mathf.Clamp(dist / chaseSpeed, 0f, 1.5f);
            goal = player.position + playerVel * lead;
            if (NavMesh.SamplePosition(goal, out NavMeshHit h, 3f, NavMesh.AllAreas)) goal = h.position;
            else goal = player.position;
        }
        agent.SetDestination(goal);

        if (dist <= grabRange) EjectPlayer();
        else if (lostTimer > giveUpTime) { state = State.Investigate; suspicion = 0.6f; }
    }

    void EjectPlayer()
    {
        Learn(1f);
        var cc = player.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        player.SetPositionAndRotation(ejectPoint.position, ejectPoint.rotation);
        if (cc) cc.enabled = true;
        var rb = player.GetComponent<Rigidbody>();
#if UNITY_6000_0_OR_NEWER
        if (rb) rb.linearVelocity = Vector3.zero;
#else
        if (rb) rb.velocity = Vector3.zero;
#endif
        lastPlayerPos = player.position; playerVel = Vector3.zero;
        routeCount = 0; suspicion = 0f;
        state = State.Patrol;
        GoTo(BestPoint());          // head straight to the route they favour most
        onPlayerCaught.Invoke();
    }

    void CheckBreach()
    {
        if (!castleGoal) return;
        bool inside = Vector3.Distance(player.position, castleGoal.position) < breachRadius;
        if (inside && !breached && state != State.Chase)
        {
            breached = true;
            Learn(2f);                                  // a successful sneak teaches more
            viewDistance = Mathf.Min(viewDistance * senseGrowth, 35f);
            hearingRadius = Mathf.Min(hearingRadius * senseGrowth, 14f);
            suspicionRate *= senseGrowth;
            onCastleBreached.Invoke();
        }
        else if (!inside) breached = false;
    }

    // ---------- Learning ----------
    // Credit each guard point by how much of the player's recent route passed near it.
    void Learn(float reward)
    {
        if (guardPoints.Length == 0 || routeCount == 0) return;
        var credit = new float[guardPoints.Length];
        for (int s = 0; s < routeCount; s++) credit[Nearest(route[s])] += 1f / routeCount;
        for (int i = 0; i < weights.Length; i++)
            weights[i] = Mathf.Clamp(weights[i] * (1f - learnRate * 0.2f) + learnRate * reward * credit[i], 0f, 5f);
        if (persistLearning) Save();
    }

    int PickPoint()   // softmax: favours hotspots but stays unpredictable
    {
        var p = new float[weights.Length]; float sum = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = Mathf.Exp(weights[i] / exploreTemp) * (i == target && p.Length > 1 ? 0.15f : 1f);
            sum += p[i];
        }
        float r = Random.value * sum;
        for (int i = 0; i < p.Length; i++) if ((r -= p[i]) <= 0f) return i;
        return p.Length - 1;
    }

    int BestPoint() { int b = 0; for (int i = 1; i < weights.Length; i++) if (weights[i] > weights[b]) b = i; return b; }

    int Nearest(Vector3 pos)
    {
        int b = 0; float d = float.MaxValue;
        for (int i = 0; i < guardPoints.Length; i++)
        {
            float dd = (guardPoints[i].position - pos).sqrMagnitude;
            if (dd < d) { d = dd; b = i; }
        }
        return b;
    }

    // ---------- Helpers ----------
    void GoTo(int i) { if (guardPoints.Length == 0) return; target = i; waitTimer = 0f; agent.SetDestination(guardPoints[i].position); }
    bool Arrived() => !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;

    void Save() => PlayerPrefs.SetString(SaveKey, string.Join(",", weights));
    void Load()
    {
        var parts = PlayerPrefs.GetString(SaveKey, "").Split(',');
        if (parts.Length != weights.Length) return;
        for (int i = 0; i < parts.Length; i++) float.TryParse(parts[i], out weights[i]);
    }

    [ContextMenu("Reset Learning")]
    void ResetLearning() { weights = new float[guardPoints.Length]; PlayerPrefs.DeleteKey(SaveKey); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, viewDistance);
        Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(transform.position, hearingRadius);
        if (guardPoints == null) return;
        for (int i = 0; i < guardPoints.Length; i++)
        {
            if (!guardPoints[i]) continue;
            float w = weights != null && i < weights.Length ? weights[i] : 0f;
            Gizmos.color = Color.Lerp(Color.green, Color.red, w / 5f);
            Gizmos.DrawSphere(guardPoints[i].position, 0.4f + w * 0.2f);
        }
    }
}
