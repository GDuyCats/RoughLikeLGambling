using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Everything that happened in one boss attack, sent back to the RunManager
public struct AttackOutcome
{
    public int DamageTaken;   // Damage the player takes
    public int BossDamage;    // Damage dealt back to the boss (reflect or counter)
    public int HealAmount;    // Health the player gets back
    public string Message;
}

// The giant dealer's attacks: hand slams, combos, feints, sweeps and flicked chips.
// Each attack is a random string of 1 to 3 of these moves (more when the boss is furious),
// sometimes overlapping: chips can fly while a hand is still winding up.
// The chibi can run out of the shadow to dodge, or stay and parry with right click
// (press = perfect, hold = block). The boss can parry reflected chips and counters back.
public class BossCombat : MonoBehaviour
{
    private enum HitKind
    {
        Slam,
        Sweep,
        Chip
    }

    // One hit coming at the chibi
    private class IncomingHit
    {
        public HitKind Kind;
        public GameObject Bullet;       // Chips only
        public Transform Hand;          // Slams and sweeps only
        public GameObject Shadow;       // Warning marker on the table
        public float StartTime;         // When the wind-up (or the chip's flight) begins
        public float LockTime;          // After this the target stops following the chibi
        public float ImpactTime;
        public float FeintTime = -1f;   // Feints fake a slam at this time
        public float SweepStart;        // Sweeps: when the hand starts crossing the table
        public float Hover = -1f;       // Slams: how high the hand rises first (-1 = the normal height)
        public Vector3 Target;          // Where it lands, on the table surface
        public Vector3 LaunchPoint;     // Chips: where the chip was flicked from
        public bool Launched;
        public int Damage;
        public int Rally;
        public bool Perfected;
        public bool Landed;
        public bool Held;
        public bool InArea;             // Was the chibi inside the hit area when it landed?
        public bool WindupPlayed;       // The wind-up sound has played (slams and sweeps)
        public bool FreezeShown;        // The perfect parry freeze has played for this hit

        public bool IsProjectile => Kind == HitKind.Chip;
    }

    // One parried chip flying back to the boss
    private class OutgoingShot
    {
        public GameObject Bullet;
        public Vector3 From;
        public float StartTime;
        public float ArriveTime;
        public int Damage;
        public int Rally;
    }

    [Header("Scene")]
    [SerializeField] private Transform bossModel;
    [SerializeField] private Renderer bossRenderer;      // The head: flashes before attacks and when hit
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject projectile;      // Template chip, copied for each shot and kept hidden
    [SerializeField] private GameObject shadowDiscPrefab;
    [SerializeField] private GameObject shadowLanePrefab;
    [SerializeField] private GameObject parryCue;
    [SerializeField] private RectTransform shrinkingRing;

    [Header("Slams")]
    [SerializeField] private float hoverHeight = 6f;
    [SerializeField] private float slamDropTime = 0.15f;
    [SerializeField] private float shadowLockTime = 0.3f;
    [SerializeField] private bool allowDodging = false;       // The shadow stops following the chibi this long before the drop
    [SerializeField] private float slamRadius = 1.6f;
    [SerializeField] private int minComboHits = 2;
    [SerializeField] private int maxComboHits = 3;
    [SerializeField] private float comboMinGapTime = 0.3f;    // Time between two slams of a combo
    [SerializeField] private float comboMaxGapTime = 0.5f;
    [SerializeField] private float feintExtraTime = 0.5f;     // How much later the real slam comes after a feint

    [Header("Sweep")]
    [SerializeField] private float sweepWindup = 1.1f;
    [SerializeField] private float sweepTime = 0.3f;
    [SerializeField] private float sweepLaneHalfWidth = 1.2f;
    [SerializeField] private float sweepHalfLength = 15f;

    [Header("Chips")]
    [SerializeField] private float chipFlightTime = 0.6f;
    [SerializeField] private float firstChipMinTime = 0.7f;
    [SerializeField] private float firstChipMaxTime = 1.0f;
    [SerializeField] private float chipHitRadius = 0.9f;
    [SerializeField, Range(0f, 1.5f)] private float chipAimLead = 0.7f;   // How far ahead chips aim at a running chibi
    [SerializeField] private float chipArcHeight = 1.5f;

    [Header("Parry")]
    [SerializeField] private float perfectWindow = 0.08f;
    [SerializeField] private float whiffLockout = 0.25f;
    [SerializeField] private float ringStartScale = 3f;
    [SerializeField] private bool showTimingCue = true;
    [SerializeField] private bool showChipShadows = false;   // Shadows under flying chips. Off = read the chips by eye.
    [SerializeField] private bool showSlamShadows = false;   // Shadows under slams and sweeps. Off = watch the hands.

    [Header("Block")]
    [SerializeField, Range(0f, 1f)] private float projectileBlockReduction = 0.75f;
    [SerializeField, Range(0f, 1f)] private float meleeBlockReduction = 0.4f;

    [Header("Perfect Parry Rewards")]
    [SerializeField, Range(0f, 3f)] private float reflectDamageMultiplier = 1f;
    [SerializeField, Range(0f, 1f)] private float reflectHealPercent = 0.5f;
    [SerializeField, Range(0f, 3f)] private float counterDamageMultiplier = 1.5f;
    [SerializeField] private float reflectFlightTime = 0.3f;

    [Header("Boss Parry (Rally)")]
    [SerializeField, Range(0f, 1f)] private float rallyChanceDecay = 0.1f;
    [SerializeField, Range(0.3f, 1f)] private float rallySpeedUp = 0.8f;
    [SerializeField] private float rallyDamageGrowth = 0.15f;
    [SerializeField] private int maxRallies = 5;

    [Header("Tempo (one attack speeds up as it goes)")]
    [SerializeField] private float tempoPerExchange = 0.1f;     // +10% speed for every hit that lands, is blocked or parried, and every boss parry
    [SerializeField] private float maxTempo = 1.8f;
    [SerializeField] private float shortestWindup = 0.15f;      // However fast it gets, a hand always hovers at least this long (at tempo x1)

    [Header("Boss Counter (after the boss parries your counter)")]
    [SerializeField] private float bossCounterWindup = 0.25f;   // Short wind-up: the same hand bounces up a little and slams again
    [SerializeField] private float bossCounterHover = 2.5f;

    [Header("Colors")]
    [SerializeField] private Color windupColor = Color.white;
    [SerializeField] private Color hitFlashColor = Color.yellow;
    [SerializeField] private Color bossParryColor = Color.cyan;
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
    [SerializeField] private Color lockedShadowColor = new Color(0.85f, 0.1f, 0.1f, 0.6f);

    private readonly List<GameObject> activeObjects = new List<GameObject>();   // Chips and shadows
    private readonly List<IncomingHit> incoming = new List<IncomingHit>();
    private readonly List<OutgoingShot> outgoing = new List<OutgoingShot>();

    private Rigidbody playerBody;
    private float dropTime;        // How fast a hand slams down in this attack (each boss has its own speed)
    private float tempo = 1f;      // Grows during one attack: everything left in it gets quicker. Back to 1 for the next attack.
    private float clock;           // The attack's own time. It runs faster as the tempo grows.
    private float topTempo;
    private bool isEnraged;        // Below half health the boss chains more moves and winds up faster

    // A sweep keeps crossing the table after it has passed the chibi
    private Transform sweepHand;
    private float sweepVisualStart = -1f;
    private float sweepLaneZ;
    private TableDealer currentDealer;   // The dealer whose body is fighting (for its lines)
    private Color bossOriginalColor;
    private Vector3 leftRestLocal;
    private Vector3 rightRestLocal;
    private float flashUntil;
    private Color flashColor;
    private float visualsBusyUntil;

    // Running totals for the current attack
    private AttackOutcome outcome;
    private int perfects;
    private int blocks;
    private int hits;
    private int dodges;
    private int bossParries;

    public bool IsAttacking { get; private set; }
    public bool IsEnraged => isEnraged;
    public float Tempo => tempo;
    public float MaxTempo => maxTempo;

    // Shared with the dealers' playful slap, so it looks the same as a boss slam
    public GameObject ShadowDiscPrefab => shadowDiscPrefab;
    public Color ShadowColor => shadowColor;
    public Color LockedShadowColor => lockedShadowColor;
    public float PerfectWindow => perfectWindow;

    private void Awake()
    {
        if (bossRenderer != null)
        {
            bossOriginalColor = bossRenderer.material.color;
        }

        playerBody = player.GetComponent<Rigidbody>();
        dropTime = slamDropTime;
        leftRestLocal = leftHand.localPosition;
        rightRestLocal = rightHand.localPosition;

        HideVisuals();
    }

    // The final boss fights with the body of the dealer sitting at its table
    public void UseDealer(TableDealer dealer)
    {
        if (dealer == null)
        {
            Debug.LogError("BossCombat: the boss table has no dealer. Run Tools > Build Table Dealers, or fill the Dealer field of the TableStation.");
            return;
        }

        CancelAttack();

        currentDealer = dealer;
        bossModel = dealer.transform;
        head = dealer.Head;
        leftHand = dealer.LeftHand;
        rightHand = dealer.RightHand;
        bossRenderer = dealer.HeadRenderer;

        leftRestLocal = leftHand.localPosition;
        rightRestLocal = rightHand.localPosition;
        bossOriginalColor = bossRenderer != null ? bossRenderer.material.color : Color.white;
    }

    // ---------- Tempo ----------
    // Within one attack, every exchange (a hit landing, blocked or parried, or the boss parrying back)
    // makes the rest of that attack quicker: the attack's own clock simply runs faster.
    // The next attack starts at normal speed again.

    private void AddTempo(float amount)
    {
        tempo = Mathf.Min(maxTempo, tempo + amount);
        topTempo = Mathf.Max(topTempo, tempo);
    }

    // Furious: longer chains and quicker wind-ups. The dealer's eyes blaze and it shouts its line.
    public void SetEnraged(bool enraged, string line)
    {
        bool startsNow = enraged && !isEnraged;
        isEnraged = enraged;

        if (currentDealer != null)
        {
            currentDealer.SetEnraged(enraged, startsNow ? line : null);
        }
    }

    public void StartAttack(int damage, FinalBoss boss, bool canPerfectParry, bool canBlock, float perfectWindowBonus, int rewardBonusPercent, Action<AttackOutcome> onFinished)
    {
        CancelAttack();
        StartCoroutine(AttackRoutine(damage, boss, canPerfectParry, canBlock, perfectWindowBonus, rewardBonusPercent, onFinished));
    }

    public void CancelAttack()
    {
        StopAllCoroutines();
        ClearObjects();
        incoming.Clear();
        outgoing.Clear();
        ResetHands();
        IsAttacking = false;
        ResetBossColor();
        HideVisuals();
    }

    private IEnumerator AttackRoutine(int damage, FinalBoss boss, bool canPerfectParry, bool canBlock, float perfectWindowBonus, int rewardBonusPercent, Action<AttackOutcome> onFinished)
    {
        IsAttacking = true;
        incoming.Clear();
        outgoing.Clear();
        outcome = new AttackOutcome();
        perfects = 0;
        blocks = 0;
        hits = 0;
        dodges = 0;
        bossParries = 0;
        flashUntil = 0f;
        visualsBusyUntil = 0f;
        clock = 0f;
        tempo = 1f;
        topTempo = 1f;

        float window = perfectWindow + perfectWindowBonus;
        float rewardScale = 1f + rewardBonusPercent / 100f;
        float lockoutUntil = 0f;
        dropTime = boss.HandDropTime > 0f ? boss.HandDropTime : slamDropTime;
        List<BossAttackType> chain = QueueChain(damage, boss);

        // Keep going until nothing is flying and every animation has finished
        while (incoming.Count > 0 || outgoing.Count > 0 || clock < visualsBusyUntil)
        {
            // The attack's clock runs faster as the tempo grows. Parry timing stays in real seconds.
            clock += Time.deltaTime * tempo;
            float now = clock;
            float timingWindow = window * tempo;

            // A press counts as a perfect parry for a hit inside the window.
            // A press with no hit nearby is a whiff and locks parrying for a moment.
            if (canPerfectParry && !ParryFreeze.IsFrozen && WasParryPressed() && now >= lockoutUntil)
            {
                IncomingHit hit = FindHitInWindow(now, timingWindow);

                if (hit != null)
                {
                    hit.Perfected = true;
                    AudioManager.Play(SoundId.PerfectParry);
                }
                else
                {
                    lockoutUntil = now + whiffLockout * tempo;
                }
            }

            foreach (IncomingHit hit in incoming.ToArray())
            {
                UpdateTarget(hit, now);

                // A hand starts rising for a slam or a sweep
                if (!hit.IsProjectile && !hit.WindupPlayed && now >= hit.StartTime)
                {
                    hit.WindupPlayed = true;
                    AudioManager.Play(SoundId.BossWindup);
                }

                if (!hit.Landed && now >= hit.ImpactTime)
                {
                    Land(hit);
                }

                // A perfect parry of a hand: everything freezes at the moment of contact (SF6 style)
                if (hit.Perfected && !hit.IsProjectile && !hit.FreezeShown && now >= hit.ImpactTime)
                {
                    hit.FreezeShown = true;
                    Vector3 contact = hit.Hand != null ? Vector3.Lerp(hit.Hand.position, player.position, 0.5f) : player.position;
                    ParryFreeze.PlayerParried(player, contact, true);   // A line on every parry, combo hits included
                }

                // Wait a moment after impact, so a slightly late press can still be perfect
                if (now >= hit.ImpactTime + timingWindow)
                {
                    incoming.Remove(hit);
                    ResolveIncoming(hit, boss, canBlock, rewardScale);
                }
            }

            foreach (OutgoingShot shot in outgoing.ToArray())
            {
                if (now >= shot.ArriveTime)
                {
                    outgoing.Remove(shot);
                    ResolveOutgoing(shot, boss, rewardScale);
                }
            }

            UpdateVisuals(now);
            yield return null;
        }

        ResetHands();
        ResetBossColor();
        ClearObjects();
        HideVisuals();

        outcome.Message = BuildResultMessage(chain);
        IsAttacking = false;
        onFinished?.Invoke(outcome);
    }

    // ---------- Building attacks ----------

    // A random string of moves, one after another with short random pauses.
    // A chip volley and a hand move may overlap (the other hand is free to flick chips).
    // The damage is shared between all the moves.
    private List<BossAttackType> QueueChain(int damage, FinalBoss boss)
    {
        int moves = Random.Range(boss.MinChainMoves, boss.MaxChainMoves + 1) + (isEnraged ? boss.RageExtraMoves : 0);
        moves = Mathf.Clamp(moves, 1, Mathf.Max(1, damage));

        List<BossAttackType> chain = new List<BossAttackType>();
        float previousStart = clock;
        float previousEnd = clock;
        BossAttackType previous = BossAttackType.Slam;

        for (int i = 0; i < moves; i++)
        {
            BossAttackType attack = boss.PickAttack();
            float start = clock;

            if (i > 0)
            {
                bool oneIsChips = (attack == BossAttackType.ChipFlick) != (previous == BossAttackType.ChipFlick);

                start = oneIsChips && Random.value < boss.OverlapChance
                    ? Mathf.Lerp(previousStart, previousEnd, Random.Range(0.2f, 0.6f))
                    : previousEnd + Random.Range(boss.ChainMinGap, boss.ChainMaxGap);
            }

            float end = QueueAttack(attack, GetDamageShare(damage, moves, i), boss, start);

            chain.Add(attack);
            previousStart = start;
            previousEnd = Mathf.Max(previousEnd, end);
            previous = attack;
        }

        SpaceOutImpacts();
        return chain;
    }

    // Returns when the move is over (its last hit, or the end of a sweep)
    private float QueueAttack(BossAttackType attack, int damage, FinalBoss boss, float start)
    {
        return attack switch
        {
            BossAttackType.SlapCombo => QueueCombo(damage, boss, start),
            BossAttackType.Feint => QueueSlam(damage, start, RandomWindup(boss), 0, null, true),
            BossAttackType.Sweep => QueueSweep(damage, start),
            BossAttackType.ChipFlick => QueueChips(damage, boss, start),
            _ => QueueSlam(damage, start, RandomWindup(boss), 0, null, false)
        };
    }

    // Two hits landing almost together can't both be parried: keep them a little apart
    private void SpaceOutImpacts()
    {
        const float minimumGap = 0.22f;
        incoming.Sort((a, b) => a.ImpactTime.CompareTo(b.ImpactTime));

        for (int i = 1; i < incoming.Count; i++)
        {
            IncomingHit hit = incoming[i];
            float earliest = incoming[i - 1].ImpactTime + minimumGap;

            if (hit.ImpactTime >= earliest || hit.Kind == HitKind.Sweep)
            {
                continue;
            }

            float shift = earliest - hit.ImpactTime;
            hit.ImpactTime += shift;
            hit.StartTime += shift;
            hit.LockTime += shift;

            if (hit.FeintTime > 0f)
            {
                hit.FeintTime += shift;
            }
        }
    }

    // How long a hand hovers before slamming. Furious bosses are quicker.
    // (With dodging on, it must also be long enough for the shadow to lock.)
    private float RandomWindup(FinalBoss boss)
    {
        float windup = Random.Range(boss.MinWindup, boss.MaxWindup) * (isEnraged ? boss.RageWindupScale : 1f);
        float shortest = allowDodging ? shadowLockTime + 0.2f : shortestWindup;
        return Mathf.Max(shortest, windup);
    }

    // Returns when the slam lands, so combos can chain one slam after another
    private float QueueSlam(int damage, float startTime, float windup, int rally, Transform hand, bool feint, float hover = -1f)
    {
        float impact = startTime + windup + dropTime;
        float feintTime = -1f;

        if (feint)
        {
            // Fake the drop when the slam "should" land, then slam for real a bit later
            feintTime = impact - dropTime;
            impact += feintExtraTime;
        }

        incoming.Add(new IncomingHit
        {
            Kind = HitKind.Slam,
            Hand = hand != null ? hand : ClosestHand(player.position),
            Shadow = SpawnShadow(shadowDiscPrefab, slamRadius * 2f, slamRadius * 2f),
            StartTime = startTime,
            LockTime = impact - dropTime - shadowLockTime,
            ImpactTime = impact,
            FeintTime = feintTime,
            Hover = hover,
            Target = FlatPlayerPosition(),
            Damage = damage,
            Rally = rally
        });

        return impact;
    }

    // Left and right hands take turns
    private float QueueCombo(int damage, FinalBoss boss, float start)
    {
        int count = Random.Range(minComboHits, maxComboHits + 1);
        float windup = RandomWindup(boss);
        Transform hand = ClosestHand(player.position);
        float impact = start;

        for (int i = 0; i < count; i++)
        {
            impact = QueueSlam(GetDamageShare(damage, count, i), start, windup, 0, hand, false);

            start = impact;
            float gap = Random.Range(comboMinGapTime, comboMaxGapTime) * (isEnraged ? boss.RageWindupScale : 1f);
            windup = Mathf.Max(0.05f, gap - dropTime);
            hand = hand == leftHand ? rightHand : leftHand;
        }

        return impact;
    }

    // One hand sweeps across a whole lane of the table
    private float QueueSweep(int damage, float start)
    {
        float sweepStart = start + sweepWindup * (isEnraged ? 0.8f : 1f);
        Vector3 target = FlatPlayerPosition();

        incoming.Add(new IncomingHit
        {
            Kind = HitKind.Sweep,
            Hand = leftHand,
            Shadow = SpawnShadow(shadowLanePrefab, sweepHalfLength * 2f, sweepLaneHalfWidth * 2f),
            StartTime = start,
            SweepStart = sweepStart,
            LockTime = sweepStart - shadowLockTime,
            ImpactTime = SweepContactTime(sweepStart, target),
            Target = target,
            Damage = damage
        });

        // Keep the attack going until the hand has crossed the whole table
        visualsBusyUntil = Mathf.Max(visualsBusyUntil, sweepStart + sweepTime + 0.1f);
        return sweepStart + sweepTime;
    }

    // The sweep crosses the table from left to right: it reaches the chibi when it passes the chibi's spot,
    // not in the middle of the table. The parry timing and the freeze use this moment.
    private float SweepContactTime(float sweepStart, Vector3 target)
    {
        float laneStartX = bossModel.position.x - sweepHalfLength;
        float progress = Mathf.Clamp((target.x - laneStartX) / (sweepHalfLength * 2f), 0.02f, 0.98f);
        return sweepStart + sweepTime * progress;
    }

    // A volley of chips with random gaps, each one aimed when it is flicked
    private float QueueChips(int damage, FinalBoss boss, float startTime)
    {
        int count = Random.Range(boss.MinChips, boss.MaxChips + 1);
        float nextImpact = startTime + Random.Range(firstChipMinTime, firstChipMaxTime);
        float flight = chipFlightTime;
        float lastImpact = nextImpact;

        for (int i = 0; i < count; i++)
        {
            float start = nextImpact - flight;

            incoming.Add(new IncomingHit
            {
                Kind = HitKind.Chip,
                Bullet = SpawnChip(),
                Shadow = SpawnShadow(shadowDiscPrefab, chipHitRadius * 2f, chipHitRadius * 2f),
                StartTime = start,
                LockTime = start,
                ImpactTime = nextImpact,
                Target = FlatPlayerPosition(),
                Damage = GetDamageShare(damage, count, i)
            });

            lastImpact = nextImpact;
            nextImpact += Mathf.Max(0.18f, Random.Range(boss.ChipMinGap, boss.ChipMaxGap));
        }

        return lastImpact;
    }

    // ---------- Each frame ----------

    private void UpdateTarget(IncomingHit hit, float now)
    {
        if (hit.Kind == HitKind.Chip)
        {
            // Chips are aimed at the moment they are flicked, ahead of a running chibi
            if (!hit.Launched && now >= hit.StartTime)
            {
                hit.Launched = true;
                hit.LaunchPoint = rightHand.position + Vector3.up;
                hit.Target = PredictPlayerPosition(hit.ImpactTime - now);
                AudioManager.Play(SoundId.BossFlick);
            }
            // Without dodging, chips home in on the chibi all the way
            else if (hit.Launched && !hit.Landed && !allowDodging)
            {
                hit.Target = FlatPlayerPosition();
            }

            return;
        }

        // Slams and sweeps follow the chibi until the shadow locks (or until impact, without dodging)
        if (now < hit.LockTime || (!allowDodging && !hit.Landed))
        {
            hit.Target = FlatPlayerPosition();
        }

        // Until the hand starts moving, the sweep's moment of contact follows where the chibi stands
        if (hit.Kind == HitKind.Sweep && now < hit.SweepStart)
        {
            hit.ImpactTime = SweepContactTime(hit.SweepStart, hit.Target);
        }
    }
    private void Land(IncomingHit hit)
    {
        hit.Landed = true;
        hit.Held = IsParryHeld();
        hit.InArea = !allowDodging || IsPlayerInArea(hit);

        if (hit.IsProjectile)
        {
            hit.Bullet.SetActive(false);
        }
        else if (hit.Kind == HitKind.Sweep)
        {
            AudioManager.Play(SoundId.BossSweep);
        }
        else
        {
            AudioManager.Play(SoundId.BossSlam);
        }
    }

    private bool IsPlayerInArea(IncomingHit hit)
    {
        Vector3 position = FlatPlayerPosition();

        return hit.Kind switch
        {
            HitKind.Sweep => Mathf.Abs(position.z - hit.Target.z) <= sweepLaneHalfWidth,
            HitKind.Slam => Vector3.Distance(position, hit.Target) <= slamRadius,
            _ => Vector3.Distance(position, hit.Target) <= chipHitRadius
        };
    }

    // ---------- Results ----------

    private void ResolveIncoming(IncomingHit hit, FinalBoss boss, bool canBlock, float rewardScale)
    {
        RemoveObject(hit.Shadow);

        // Out of the hit area: dodged. Safe, but no reward.
        if (!hit.InArea)
        {
            dodges++;

            if (hit.IsProjectile)
            {
                RemoveObject(hit.Bullet);
            }

            return;
        }

        // Every exchange speeds up the rest of this attack
        AddTempo(tempoPerExchange);

        int damage = GetRallyDamage(hit.Damage, hit.Rally);

        if (hit.Perfected)
        {
            perfects++;

            if (hit.IsProjectile)
            {
                float flight = reflectFlightTime * Mathf.Pow(rallySpeedUp, hit.Rally);
                hit.Bullet.SetActive(true);

                outgoing.Add(new OutgoingShot
                {
                    Bullet = hit.Bullet,
                    From = player.position,
                    StartTime = clock,
                    ArriveTime = clock + flight,
                    Damage = hit.Damage,
                    Rally = hit.Rally
                });
            }
            else
            {
                ResolveCounter(hit, boss, rewardScale);
            }

            return;
        }

        float reduction = hit.IsProjectile ? projectileBlockReduction : meleeBlockReduction;

        if (canBlock && hit.Held)
        {
            blocks++;
            outcome.DamageTaken += Mathf.CeilToInt(damage * (1f - reduction));
            AudioManager.Play(SoundId.Block);
        }
        else
        {
            hits++;
            outcome.DamageTaken += damage;
            AudioManager.Play(SoundId.PlayerHit);
        }

        if (hit.IsProjectile)
        {
            RemoveObject(hit.Bullet);
        }
    }

    // A reflected chip reached the boss: the boss may parry it back, faster
    private void ResolveOutgoing(OutgoingShot shot, FinalBoss boss, float rewardScale)
    {
        if (shot.Rally < maxRallies && Random.value < GetBossParryChance(boss, shot.Rally))
        {
            bossParries++;
            Flash(bossParryColor);
            AudioManager.Play(SoundId.BossParry);
            AddTempo(tempoPerExchange);

            float flight = chipFlightTime * Mathf.Pow(rallySpeedUp, shot.Rally + 1);

            incoming.Add(new IncomingHit
            {
                Kind = HitKind.Chip,
                Bullet = shot.Bullet,
                Shadow = SpawnShadow(shadowDiscPrefab, chipHitRadius * 2f, chipHitRadius * 2f),
                StartTime = clock,
                LockTime = clock,
                ImpactTime = clock + flight,
                LaunchPoint = head.position,
                Launched = true,
                Target = PredictPlayerPosition(flight),
                Damage = shot.Damage,
                Rally = shot.Rally + 1
            });

            return;
        }

        int damage = GetRallyDamage(shot.Damage, shot.Rally);
        outcome.BossDamage += Mathf.Max(1, Mathf.RoundToInt(damage * reflectDamageMultiplier * rewardScale));
        outcome.HealAmount += Mathf.RoundToInt(damage * reflectHealPercent * rewardScale);

        Flash(hitFlashColor);
        AudioManager.Play(SoundId.BossHurt);
        RemoveObject(shot.Bullet);
    }

    // The player perfect parried a slam and counters: the boss may parry and slam again quickly
    private void ResolveCounter(IncomingHit hit, FinalBoss boss, float rewardScale)
    {
        if (hit.Rally < maxRallies && Random.value < GetBossParryChance(boss, hit.Rally))
        {
            bossParries++;
            Flash(bossParryColor);
            AudioManager.Play(SoundId.BossParry);

            // The boss gets its own freeze, glowing red, and answers back
            Vector3 contact = hit.Hand != null ? hit.Hand.position : head.position;
            string line = currentDealer != null ? currentDealer.PickBossParryLine() : null;
            ParryFreeze.BossParried(bossModel, head, contact, line);

            // The same hand bounces up a little and slams again right away: hardly any time to react
            AddTempo(tempoPerExchange);
            QueueSlam(hit.Damage, clock, bossCounterWindup, hit.Rally + 1, hit.Hand, false, bossCounterHover);
            return;
        }

        int damage = GetRallyDamage(hit.Damage, hit.Rally);
        outcome.BossDamage += Mathf.Max(1, Mathf.RoundToInt(damage * counterDamageMultiplier * rewardScale));
        Flash(hitFlashColor);
        AudioManager.Play(SoundId.BossHurt);
    }

    private float GetBossParryChance(FinalBoss boss, int rally)
    {
        return Mathf.Max(0f, boss.ParryChance - rallyChanceDecay * rally);
    }

    private int GetRallyDamage(int damage, int rally)
    {
        return Mathf.RoundToInt(damage * (1f + rallyDamageGrowth * rally));
    }

    private int GetDamageShare(int totalDamage, int hitCount, int index)
    {
        int baseShare = totalDamage / hitCount;
        int remainder = totalDamage - baseShare * hitCount;
        return baseShare + (index < remainder ? 1 : 0);
    }

    private IncomingHit FindHitInWindow(float now, float window)
    {
        IncomingHit best = null;
        float bestGap = window;

        foreach (IncomingHit hit in incoming)
        {
            float gap = Mathf.Abs(now - hit.ImpactTime);

            if (!hit.Perfected && gap <= bestGap)
            {
                best = hit;
                bestGap = gap;
            }
        }

        return best;
    }

    // ---------- Visuals ----------

    private void UpdateVisuals(float now)
    {
        UpdateHand(leftHand, leftRestLocal, now);
        UpdateHand(rightHand, rightRestLocal, now);
        UpdateHeadColor(now);

        foreach (IncomingHit hit in incoming)
        {
            UpdateShadow(hit, now);

            if (hit.IsProjectile)
            {
                UpdateChip(hit, now);
            }
        }

        // Reflected chips fly back into the dealer's face
        foreach (OutgoingShot shot in outgoing)
        {
            float progress = Mathf.Clamp01((now - shot.StartTime) / (shot.ArriveTime - shot.StartTime));
            shot.Bullet.transform.position = Vector3.Lerp(shot.From, head.position, progress);
        }

        UpdateRing(now);
    }

    private void UpdateHand(Transform hand, Vector3 restLocal, float now)
    {
        Vector3 rest = hand.parent.TransformPoint(restLocal);

        // The next slam or sweep this hand has to do
        IncomingHit job = null;

        foreach (IncomingHit hit in incoming)
        {
            if (hit.Hand == hand && !hit.IsProjectile && (job == null || hit.ImpactTime < job.ImpactTime))
            {
                job = hit;
            }
        }

        // A sweep that already passed the chibi keeps going to the other side of the table
        if (job == null && hand == sweepHand && now < sweepVisualStart + sweepTime)
        {
            float progress = Mathf.Clamp01((now - sweepVisualStart) / sweepTime);
            Vector3 laneStart = new Vector3(bossModel.position.x - sweepHalfLength, 0.5f, sweepLaneZ);
            hand.position = Vector3.Lerp(laneStart, laneStart + Vector3.right * sweepHalfLength * 2f, progress);
            return;
        }

        if (job == null || now < job.StartTime)
        {
            hand.position = Vector3.Lerp(hand.position, rest, 10f * tempo * Time.deltaTime);
            return;
        }

        if (job.Kind == HitKind.Sweep)
        {
            Vector3 laneStart = new Vector3(bossModel.position.x - sweepHalfLength, 0.5f, job.Target.z);

            if (now < job.SweepStart)
            {
                // Get into position at the side of the lane, a little raised
                hand.position = Vector3.Lerp(hand.position, laneStart + Vector3.up * 2f, 8f * tempo * Time.deltaTime);
            }
            else
            {
                sweepHand = hand;
                sweepVisualStart = job.SweepStart;
                sweepLaneZ = job.Target.z;

                float progress = Mathf.Clamp01((now - job.SweepStart) / sweepTime);
                hand.position = Vector3.Lerp(laneStart, laneStart + Vector3.right * sweepHalfLength * 2f, progress);
            }

            return;
        }

        // Slam: hover above the target, (maybe fake a drop), then smash down
        float dropStart = job.ImpactTime - dropTime;
        float height = job.Hover > 0f ? job.Hover : hoverHeight;
        Vector3 hover = job.Target + Vector3.up * height;

        if (job.FeintTime > 0f && Mathf.Abs(now - job.FeintTime) < 0.15f)
        {
            float dip = 1f - Mathf.Abs(now - job.FeintTime) / 0.15f;
            hover.y -= height * 0.6f * dip;
        }

        if (now < dropStart)
        {
            hand.position = Vector3.Lerp(hand.position, hover, 12f * tempo * Time.deltaTime);
        }
        else
        {
            float progress = Mathf.Clamp01((now - dropStart) / dropTime);
            hand.position = Vector3.Lerp(hover, job.Target + Vector3.up * 0.5f, progress);
        }
    }

    // The head flickers while a hand is winding up, and flashes on parries and hits
    private void UpdateHeadColor(float now)
    {
        bool windingUp = false;

        foreach (IncomingHit hit in incoming)
        {
            if (!hit.IsProjectile && !hit.Landed && now >= hit.StartTime)
            {
                windingUp = true;
                break;
            }
        }

        if (now < flashUntil)
        {
            SetBossColor(flashColor);
        }
        else if (windingUp)
        {
            bool flashOn = Mathf.Sin(now * 20f) > 0f;
            SetBossColor(flashOn ? windupColor : bossOriginalColor);
        }
        else
        {
            ResetBossColor();
        }
    }

    // Shadows show where a hit will land, and turn red once locked
    private void UpdateShadow(IncomingHit hit, float now)
    {
        if (hit.Shadow == null)
        {
            return;
        }

        // No warning shadows by default: the player has to read the chips and hands by eye
        bool showShadow = hit.IsProjectile ? showChipShadows : showSlamShadows;

        if (!showShadow)
        {
            hit.Shadow.SetActive(false);
            return;
        }

        bool visible = !hit.Landed && (hit.IsProjectile ? hit.Launched : now >= hit.StartTime);
        hit.Shadow.SetActive(visible);

        if (!visible)
        {
            return;
        }

        hit.Shadow.transform.position = hit.Kind == HitKind.Sweep
            ? new Vector3(bossModel.position.x, 0.02f, hit.Target.z)
            : hit.Target + Vector3.up * 0.02f;

        bool locked = now >= hit.LockTime;
        hit.Shadow.GetComponent<Renderer>().material.color = locked ? lockedShadowColor : shadowColor;
    }

    private void UpdateChip(IncomingHit hit, float now)
    {
        bool inFlight = hit.Launched && !hit.Landed;
        hit.Bullet.SetActive(inFlight || outgoing.Exists(shot => shot.Bullet == hit.Bullet));

        if (!inFlight)
        {
            return;
        }

        float progress = Mathf.Clamp01((now - hit.StartTime) / (hit.ImpactTime - hit.StartTime));
        Vector3 position = Vector3.Lerp(hit.LaunchPoint, hit.Target + Vector3.up, progress);
        position.y += Mathf.Sin(progress * Mathf.PI) * chipArcHeight;
        hit.Bullet.transform.position = position;
    }

    // The ring shows the timing of the next chip that hasn't landed
    private void UpdateRing(float now)
    {
        IncomingHit next = null;

        foreach (IncomingHit hit in incoming)
        {
            if (hit.IsProjectile && hit.Launched && !hit.Landed && (next == null || hit.ImpactTime < next.ImpactTime))
            {
                next = hit;
            }
        }

        parryCue.SetActive(showTimingCue && next != null);

        if (next != null)
        {
            float progress = Mathf.Clamp01((now - next.StartTime) / (next.ImpactTime - next.StartTime));
            shrinkingRing.localScale = Vector3.one * Mathf.Lerp(ringStartScale, 1f, progress);
        }
    }

    // ---------- Helpers ----------

    // The chibi's position on the table surface (arena tables are at height 0)
    private Vector3 FlatPlayerPosition()
    {
        return new Vector3(player.position.x, 0f, player.position.z);
    }

    // Where the chibi will be if it keeps running the same way
    private Vector3 PredictPlayerPosition(float timeAhead)
    {
        Vector3 position = FlatPlayerPosition();

        if (playerBody != null)
        {
            Vector3 velocity = playerBody.linearVelocity;
            velocity.y = 0f;
            position += velocity * timeAhead * chipAimLead;
        }

        return position;
    }

    private Transform ClosestHand(Vector3 position)
    {
        float left = Vector3.Distance(leftHand.position, position);
        float right = Vector3.Distance(rightHand.position, position);
        return left <= right ? leftHand : rightHand;
    }

    private void Flash(Color color)
    {
        flashColor = color;
        flashUntil = clock + 0.15f;
        visualsBusyUntil = Mathf.Max(visualsBusyUntil, flashUntil);
    }

    private string BuildResultMessage(List<BossAttackType> chain)
    {
        List<string> parts = new List<string>();

        if (perfects > 0) parts.Add($"{perfects} perfect");
        if (bossParries > 0) parts.Add($"the boss parried back {bossParries}x");
        if (blocks > 0) parts.Add($"{blocks} blocked");
        if (dodges > 0) parts.Add($"{dodges} dodged");
        if (hits > 0) parts.Add($"{hits} hit");

        List<string> names = new List<string>();

        foreach (BossAttackType attack in chain)
        {
            names.Add(attack switch
            {
                BossAttackType.SlapCombo => "Slap combo",
                BossAttackType.Feint => "Feint",
                BossAttackType.Sweep => "Sweep",
                BossAttackType.ChipFlick => "Chip flick",
                _ => "Slam"
            });
        }

        string attackName = (isEnraged ? "FURIOUS " : "") + string.Join(" + ", names);

        string message = $"{attackName}: {string.Join(", ", parts)}. You take {outcome.DamageTaken} damage";

        if (outcome.BossDamage > 0)
        {
            message += $", the boss takes {outcome.BossDamage}";
        }

        if (outcome.HealAmount > 0)
        {
            message += $", you heal {outcome.HealAmount}";
        }

        if (topTempo > 1.05f)
        {
            message += $". Tempo reached x{topTempo:0.0}";
        }

        return message + ".";
    }

    private GameObject SpawnChip()
    {
        GameObject chip = Instantiate(projectile);
        chip.SetActive(false);
        activeObjects.Add(chip);
        return chip;
    }

    private GameObject SpawnShadow(GameObject prefab, float sizeX, float sizeZ)
    {
        GameObject shadow = Instantiate(prefab);
        shadow.transform.localScale = new Vector3(sizeX, prefab.transform.localScale.y, sizeZ);
        shadow.SetActive(false);
        activeObjects.Add(shadow);
        return shadow;
    }

    private void RemoveObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        activeObjects.Remove(target);
        Destroy(target);
    }

    private void ClearObjects()
    {
        foreach (GameObject target in activeObjects)
        {
            if (target != null)
            {
                Destroy(target);
            }
        }

        activeObjects.Clear();
    }

    private void ResetHands()
    {
        sweepHand = null;
        sweepVisualStart = -1f;
        leftHand.localPosition = leftRestLocal;
        rightHand.localPosition = rightRestLocal;
    }

    private void SetBossColor(Color color)
    {
        if (bossRenderer != null)
        {
            bossRenderer.material.color = color;
        }
    }

    private void ResetBossColor()
    {
        SetBossColor(bossOriginalColor);
    }

    private bool WasParryPressed()
    {
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    }

    private bool IsParryHeld()
    {
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
    }

    private void HideVisuals()
    {
        if (parryCue != null)
        {
            parryCue.SetActive(false);
        }

        if (projectile != null)
        {
            projectile.SetActive(false);
        }
    }
}