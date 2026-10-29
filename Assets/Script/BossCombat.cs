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
    [SerializeField] private float comboMinGap = 0.7f;        // Time between two slams of a combo
    [SerializeField] private float comboMaxGap = 1.0f;
    [SerializeField] private float feintExtraTime = 0.5f;     // How much later the real slam comes after a feint
    [SerializeField] private float followUpWindup = 0.5f;     // Quick slam after the boss parries a counter

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

    private void Awake()
    {
        if (bossRenderer != null)
        {
            bossOriginalColor = bossRenderer.material.color;
        }

        playerBody = player.GetComponent<Rigidbody>();
        leftRestLocal = leftHand.localPosition;
        rightRestLocal = rightHand.localPosition;

        HideVisuals();
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

        float window = perfectWindow + perfectWindowBonus;
        float rewardScale = 1f + rewardBonusPercent / 100f;
        float lockoutUntil = 0f;

        BossAttackType attack = boss.PickAttack();
        QueueAttack(attack, damage, boss);

        // Keep going until nothing is flying and every animation has finished
        while (incoming.Count > 0 || outgoing.Count > 0 || Time.time < visualsBusyUntil)
        {
            float now = Time.time;

            // A press counts as a perfect parry for a hit inside the window.
            // A press with no hit nearby is a whiff and locks parrying for a moment.
            if (canPerfectParry && WasParryPressed() && now >= lockoutUntil)
            {
                IncomingHit hit = FindHitInWindow(now, window);

                if (hit != null)
                {
                    hit.Perfected = true;
                    AudioManager.Play(SoundId.PerfectParry);
                }
                else
                {
                    lockoutUntil = now + whiffLockout;
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

                // Wait a moment after impact, so a slightly late press can still be perfect
                if (now >= hit.ImpactTime + window)
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

        outcome.Message = BuildResultMessage(attack);
        IsAttacking = false;
        onFinished?.Invoke(outcome);
    }

    // ---------- Building attacks ----------

    private void QueueAttack(BossAttackType attack, int damage, FinalBoss boss)
    {
        switch (attack)
        {
            case BossAttackType.SlapCombo:
                QueueCombo(damage, boss);
                break;
            case BossAttackType.Feint:
                QueueSlam(damage, Time.time, RandomWindup(boss), 0, null, true);
                break;
            case BossAttackType.Sweep:
                QueueSweep(damage);
                break;
            case BossAttackType.ChipFlick:
                QueueChips(damage, boss);
                break;
            default:
                QueueSlam(damage, Time.time, RandomWindup(boss), 0, null, false);
                break;
        }
    }

    // The wind-up must be long enough for the hand to rise and the shadow to lock
    private float RandomWindup(FinalBoss boss)
    {
        return Mathf.Max(shadowLockTime + 0.2f, Random.Range(boss.MinWindup, boss.MaxWindup));
    }

    // Returns when the slam lands, so combos can chain one slam after another
    private float QueueSlam(int damage, float startTime, float windup, int rally, Transform hand, bool feint)
    {
        float impact = startTime + windup + slamDropTime;
        float feintTime = -1f;

        if (feint)
        {
            // Fake the drop when the slam "should" land, then slam for real a bit later
            feintTime = impact - slamDropTime;
            impact += feintExtraTime;
        }

        incoming.Add(new IncomingHit
        {
            Kind = HitKind.Slam,
            Hand = hand != null ? hand : ClosestHand(player.position),
            Shadow = SpawnShadow(shadowDiscPrefab, slamRadius * 2f, slamRadius * 2f),
            StartTime = startTime,
            LockTime = impact - slamDropTime - shadowLockTime,
            ImpactTime = impact,
            FeintTime = feintTime,
            Target = FlatPlayerPosition(),
            Damage = damage,
            Rally = rally
        });

        return impact;
    }

    // Left and right hands take turns
    private void QueueCombo(int damage, FinalBoss boss)
    {
        int count = Random.Range(minComboHits, maxComboHits + 1);
        float start = Time.time;
        float windup = RandomWindup(boss);
        Transform hand = ClosestHand(player.position);

        for (int i = 0; i < count; i++)
        {
            float impact = QueueSlam(GetDamageShare(damage, count, i), start, windup, 0, hand, false);

            start = impact;
            windup = Random.Range(comboMinGap, comboMaxGap) - slamDropTime;
            hand = hand == leftHand ? rightHand : leftHand;
        }
    }

    // One hand sweeps across a whole lane of the table
    private void QueueSweep(int damage)
    {
        float start = Time.time;
        float impact = start + sweepWindup + sweepTime * 0.5f;

        incoming.Add(new IncomingHit
        {
            Kind = HitKind.Sweep,
            Hand = leftHand,
            Shadow = SpawnShadow(shadowLanePrefab, sweepHalfLength * 2f, sweepLaneHalfWidth * 2f),
            StartTime = start,
            LockTime = impact - sweepTime * 0.5f - shadowLockTime,
            ImpactTime = impact,
            Target = FlatPlayerPosition(),
            Damage = damage
        });
    }

    // A volley of chips with random gaps, each one aimed when it is flicked
    private void QueueChips(int damage, FinalBoss boss)
    {
        int count = Random.Range(boss.MinChips, boss.MaxChips + 1);
        float nextImpact = Time.time + Random.Range(firstChipMinTime, firstChipMaxTime);

        for (int i = 0; i < count; i++)
        {
            float start = nextImpact - chipFlightTime;

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

            nextImpact += Random.Range(boss.ChipMinGap, boss.ChipMaxGap);
        }
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
                    StartTime = Time.time,
                    ArriveTime = Time.time + flight,
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

            float flight = chipFlightTime * Mathf.Pow(rallySpeedUp, shot.Rally + 1);

            incoming.Add(new IncomingHit
            {
                Kind = HitKind.Chip,
                Bullet = shot.Bullet,
                Shadow = SpawnShadow(shadowDiscPrefab, chipHitRadius * 2f, chipHitRadius * 2f),
                StartTime = Time.time,
                LockTime = Time.time,
                ImpactTime = Time.time + flight,
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

            Transform otherHand = hit.Hand == leftHand ? rightHand : leftHand;
            QueueSlam(hit.Damage, Time.time, followUpWindup, hit.Rally + 1, otherHand, false);
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

        if (job == null || now < job.StartTime)
        {
            hand.position = Vector3.Lerp(hand.position, rest, 10f * Time.deltaTime);
            return;
        }

        if (job.Kind == HitKind.Sweep)
        {
            float sweepStart = job.ImpactTime - sweepTime * 0.5f;
            Vector3 laneStart = new Vector3(bossModel.position.x - sweepHalfLength, 0.5f, job.Target.z);

            if (now < sweepStart)
            {
                // Get into position at the side of the lane, a little raised
                hand.position = Vector3.Lerp(hand.position, laneStart + Vector3.up * 2f, 8f * Time.deltaTime);
            }
            else
            {
                float progress = Mathf.Clamp01((now - sweepStart) / sweepTime);
                hand.position = Vector3.Lerp(laneStart, laneStart + Vector3.right * sweepHalfLength * 2f, progress);
            }

            return;
        }

        // Slam: hover above the target, (maybe fake a drop), then smash down
        float dropStart = job.ImpactTime - slamDropTime;
        Vector3 hover = job.Target + Vector3.up * hoverHeight;

        if (job.FeintTime > 0f && Mathf.Abs(now - job.FeintTime) < 0.15f)
        {
            float dip = 1f - Mathf.Abs(now - job.FeintTime) / 0.15f;
            hover.y -= hoverHeight * 0.6f * dip;
        }

        if (now < dropStart)
        {
            hand.position = Vector3.Lerp(hand.position, hover, 12f * Time.deltaTime);
        }
        else
        {
            float progress = Mathf.Clamp01((now - dropStart) / slamDropTime);
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

        // Chips can have no warning at all, for a harder fight
        if (hit.IsProjectile && !showChipShadows)
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
        flashUntil = Time.time + 0.15f;
        visualsBusyUntil = Mathf.Max(visualsBusyUntil, flashUntil);
    }

    private string BuildResultMessage(BossAttackType attack)
    {
        List<string> parts = new List<string>();

        if (perfects > 0) parts.Add($"{perfects} perfect");
        if (bossParries > 0) parts.Add($"the boss parried back {bossParries}x");
        if (blocks > 0) parts.Add($"{blocks} blocked");
        if (dodges > 0) parts.Add($"{dodges} dodged");
        if (hits > 0) parts.Add($"{hits} hit");

        string attackName = attack switch
        {
            BossAttackType.SlapCombo => "Slap combo",
            BossAttackType.Feint => "Feint",
            BossAttackType.Sweep => "Sweep",
            BossAttackType.ChipFlick => "Chip flick",
            _ => "Slam"
        };

        string message = $"{attackName}: {string.Join(", ", parts)}. You take {outcome.DamageTaken} damage";

        if (outcome.BossDamage > 0)
        {
            message += $", the boss takes {outcome.BossDamage}";
        }

        if (outcome.HealAmount > 0)
        {
            message += $", you heal {outcome.HealAmount}";
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