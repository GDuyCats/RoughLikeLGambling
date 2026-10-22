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

// Plays the final boss's attacks. The player parries with right click (press = perfect, hold = block),
// and the boss can parry the player's reflected bullets and counters back, like a tennis rally.
public class BossCombat : MonoBehaviour
{
    // One hit coming at the player: a bullet (first shot or sent back by the boss) or a melee strike
    private class IncomingHit
    {
        public GameObject Bullet;   // null for a melee strike
        public float LaunchTime;
        public float ImpactTime;
        public int Damage;
        public int Rally;           // How many times this hit has already gone back and forth
        public bool Perfected;
        public bool Landed;
        public bool Held;

        public bool IsProjectile => Bullet != null;
    }

    // One parried bullet flying back to the boss
    private class OutgoingShot
    {
        public GameObject Bullet;
        public float StartTime;
        public float ArriveTime;
        public int Damage;
        public int Rally;
    }

    [Header("Scene")]
    [SerializeField] private Transform bossModel;
    [SerializeField] private Renderer bossRenderer;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject projectile;          // Template bullet, copied for each shot and kept hidden
    [SerializeField] private GameObject parryCue;
    [SerializeField] private RectTransform shrinkingRing;

    [Header("Timing (the boss's own style is in its FinalBoss file)")]
    [SerializeField] private float bulletFlightTime = 0.6f;
    [SerializeField] private float firstBulletMinTime = 0.7f;
    [SerializeField] private float firstBulletMaxTime = 1.0f;
    [SerializeField] private float meleeLungeTime = 0.2f;
    [SerializeField, Range(0f, 1f)] private float meleeLungeDistance = 0.6f;
    [SerializeField, Range(0f, 0.5f)] private float meleeShakeAmount = 0.15f;
    [SerializeField] private float followUpWindup = 0.25f;   // Wind-up of the quick strike after the boss parries a counter
    [SerializeField] private Color windupColor = Color.white;

    [Header("Parry")]
    [SerializeField] private float perfectWindow = 0.08f;
    [SerializeField] private float whiffLockout = 0.25f;
    [SerializeField] private float ringStartScale = 3f;
    [SerializeField] private bool showTimingCue = true;

    [Header("Block")]
    [SerializeField, Range(0f, 1f)] private float projectileBlockReduction = 0.75f;
    [SerializeField, Range(0f, 1f)] private float meleeBlockReduction = 0.4f;

    [Header("Perfect Parry Rewards")]
    [SerializeField, Range(0f, 3f)] private float reflectDamageMultiplier = 1f;
    [SerializeField, Range(0f, 1f)] private float reflectHealPercent = 0.5f;
    [SerializeField, Range(0f, 3f)] private float counterDamageMultiplier = 1.5f;
    [SerializeField] private float reflectFlightTime = 0.3f;
    [SerializeField] private float knockbackDistance = 0.8f;
    [SerializeField] private float knockbackTime = 0.25f;
    [SerializeField] private Color hitFlashColor = Color.yellow;

    [Header("Boss Parry (Rally)")]
    [SerializeField, Range(0f, 1f)] private float rallyChanceDecay = 0.1f;   // The boss's parry chance drops by this on each return
    [SerializeField, Range(0.3f, 1f)] private float rallySpeedUp = 0.8f;     // Each return takes this times as long, so it gets faster
    [SerializeField] private float rallyDamageGrowth = 0.15f;                    // Each return adds this share of extra damage, for both sides
    [SerializeField] private int maxRallies = 5;
    [SerializeField] private Color bossParryColor = Color.cyan;

    private readonly List<GameObject> activeBullets = new List<GameObject>();
    private readonly List<IncomingHit> incoming = new List<IncomingHit>();
    private readonly List<OutgoingShot> outgoing = new List<OutgoingShot>();

    private Color bossOriginalColor;
    private Vector3 bossHome;
    private Vector3 bossHomeScale;
    private Vector3 target;
    private float visualsBusyUntil;
    private float flashUntil;
    private Color flashColor;
    private float knockbackStart;
    private float knockbackEnd;

    // Running totals for the current attack
    private AttackOutcome outcome;
    private int perfects;
    private int blocks;
    private int hits;
    private int bossParries;

    public bool IsAttacking { get; private set; }

    private void Awake()
    {
        if (bossRenderer != null)
        {
            bossOriginalColor = bossRenderer.material.color;
        }

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
        ClearBullets();
        incoming.Clear();
        outgoing.Clear();

        if (IsAttacking)
        {
            bossModel.position = bossHome;
            bossModel.localScale = bossHomeScale;
        }

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
        bossParries = 0;

        bossHome = bossModel.position;
        bossHomeScale = bossModel.localScale;
        target = player.position;
        visualsBusyUntil = 0f;
        flashUntil = 0f;
        knockbackEnd = 0f;

        float window = perfectWindow + perfectWindowBonus;
        float rewardScale = 1f + rewardBonusPercent / 100f;
        float lockoutUntil = 0f;
        bool isProjectile = Random.value < boss.ProjectileChance;

        if (isProjectile)
        {
            QueueVolley(damage, boss);
        }
        else
        {
            QueueStrike(damage, Random.Range(boss.MeleeMinWindup, boss.MeleeMaxWindup), 0);
        }

        // Keep going until nothing is flying and every animation has finished
        while (incoming.Count > 0 || outgoing.Count > 0 || Time.time < visualsBusyUntil)
        {
            float now = Time.time;

            // A press counts as a perfect parry for a hit inside the window.
            // A press with no hit nearby is a whiff and locks parrying for a moment, so mashing doesn't work.
            if (canPerfectParry && WasParryPressed() && now >= lockoutUntil)
            {
                IncomingHit hit = FindHitInWindow(now, window);

                if (hit != null)
                {
                    hit.Perfected = true;
                }
                else
                {
                    lockoutUntil = now + whiffLockout;
                }
            }

            // ToArray() makes a copy to loop over, because resolving a hit can change the list
            foreach (IncomingHit hit in incoming.ToArray())
            {
                if (!hit.Landed && now >= hit.ImpactTime)
                {
                    hit.Landed = true;
                    hit.Held = IsParryHeld();

                    if (hit.IsProjectile)
                    {
                        hit.Bullet.SetActive(false);
                    }
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

        bossModel.position = bossHome;
        bossModel.localScale = bossHomeScale;
        ResetBossColor();
        ClearBullets();
        HideVisuals();

        outcome.Message = BuildResultMessage(isProjectile);
        IsAttacking = false;
        onFinished?.Invoke(outcome);
    }

    private void QueueVolley(int damage, FinalBoss boss)
    {
        int count = Random.Range(boss.MinBullets, boss.MaxBullets + 1);
        float nextImpact = Time.time + Random.Range(firstBulletMinTime, firstBulletMaxTime);

        for (int i = 0; i < count; i++)
        {
            incoming.Add(new IncomingHit
            {
                Bullet = SpawnBullet(),
                LaunchTime = nextImpact - bulletFlightTime,
                ImpactTime = nextImpact,
                Damage = GetDamageShare(damage, count, i)
            });

            // A random gap before the next bullet, so the rhythm can't be memorized
            nextImpact += Random.Range(boss.BulletMinGap, boss.BulletMaxGap);
        }
    }

    private void QueueStrike(int damage, float windup, int rally)
    {
        incoming.Add(new IncomingHit
        {
            Bullet = null,
            LaunchTime = Time.time,
            ImpactTime = Time.time + windup + meleeLungeTime,
            Damage = damage,
            Rally = rally
        });
    }

    // A hit reached the player: perfect parry sends it back, otherwise block or take the damage
    private void ResolveIncoming(IncomingHit hit, FinalBoss boss, bool canBlock, float rewardScale)
    {
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
        }
        else
        {
            hits++;
            outcome.DamageTaken += damage;
        }

        if (hit.IsProjectile)
        {
            DestroyBullet(hit.Bullet);
        }
    }

    // A reflected bullet reached the boss: the boss may parry it back, faster
    private void ResolveOutgoing(OutgoingShot shot, FinalBoss boss, float rewardScale)
    {
        if (shot.Rally < maxRallies && Random.value < GetBossParryChance(boss, shot.Rally))
        {
            bossParries++;
            Flash(bossParryColor);

            float flight = bulletFlightTime * Mathf.Pow(rallySpeedUp, shot.Rally + 1);

            incoming.Add(new IncomingHit
            {
                Bullet = shot.Bullet,
                LaunchTime = Time.time,
                ImpactTime = Time.time + flight,
                Damage = shot.Damage,
                Rally = shot.Rally + 1
            });

            return;
        }

        int damage = GetRallyDamage(shot.Damage, shot.Rally);
        outcome.BossDamage += Mathf.Max(1, Mathf.RoundToInt(damage * reflectDamageMultiplier * rewardScale));
        outcome.HealAmount += Mathf.RoundToInt(damage * reflectHealPercent * rewardScale);

        Flash(hitFlashColor);
        DestroyBullet(shot.Bullet);
    }

    // The player perfect parried a strike and counters: the boss may parry and strike again quickly
    private void ResolveCounter(IncomingHit hit, FinalBoss boss, float rewardScale)
    {
        if (hit.Rally < maxRallies && Random.value < GetBossParryChance(boss, hit.Rally))
        {
            bossParries++;
            Flash(bossParryColor);
            QueueStrike(hit.Damage, followUpWindup, hit.Rally + 1);
            return;
        }

        int damage = GetRallyDamage(hit.Damage, hit.Rally);
        outcome.BossDamage += Mathf.Max(1, Mathf.RoundToInt(damage * counterDamageMultiplier * rewardScale));

        knockbackStart = Time.time;
        knockbackEnd = Time.time + knockbackTime;
        visualsBusyUntil = Mathf.Max(visualsBusyUntil, knockbackEnd);
        Flash(hitFlashColor);
    }

    private float GetBossParryChance(FinalBoss boss, int rally)
    {
        return Mathf.Max(0f, boss.ParryChance - rallyChanceDecay * rally);
    }

    // Every return makes the hit hurt more, for whoever ends up taking it
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

    private void UpdateVisuals(float now)
    {
        // The boss's body: a melee wind-up, a knockback, or standing still
        IncomingHit strike = null;

        foreach (IncomingHit hit in incoming)
        {
            if (!hit.IsProjectile && !hit.Landed)
            {
                strike = hit;
                break;
            }
        }

        if (strike != null)
        {
            AnimateMelee(strike.ImpactTime, now);
        }
        else if (now < knockbackEnd)
        {
            Vector3 away = bossHome - target;
            away.y = 0f;
            float t = (now - knockbackStart) / knockbackTime;
            float push = t < 0.5f ? t * 2f : (1f - t) * 2f;
            bossModel.position = Vector3.Lerp(bossHome, bossHome + away.normalized * knockbackDistance, push);
            bossModel.localScale = bossHomeScale;
        }
        else
        {
            bossModel.position = bossHome;
            bossModel.localScale = bossHomeScale;
        }

        if (now < flashUntil)
        {
            SetBossColor(flashColor);
        }
        else if (strike == null)
        {
            ResetBossColor();
        }

        // Bullets flying toward the player
        foreach (IncomingHit hit in incoming)
        {
            if (!hit.IsProjectile || hit.Landed)
            {
                continue;
            }

            bool inFlight = now >= hit.LaunchTime;
            hit.Bullet.SetActive(inFlight);

            if (inFlight)
            {
                float progress = Mathf.Clamp01((now - hit.LaunchTime) / (hit.ImpactTime - hit.LaunchTime));
                hit.Bullet.transform.position = Vector3.Lerp(bossHome, target, progress);
            }
        }

        // Reflected bullets flying back to the boss
        foreach (OutgoingShot shot in outgoing)
        {
            float progress = Mathf.Clamp01((now - shot.StartTime) / (shot.ArriveTime - shot.StartTime));
            shot.Bullet.transform.position = Vector3.Lerp(target, bossHome, progress);
        }

        UpdateRing(now);
    }

    // The ring shows the timing of the next bullet that hasn't landed
    private void UpdateRing(float now)
    {
        IncomingHit next = null;

        foreach (IncomingHit hit in incoming)
        {
            if (hit.IsProjectile && !hit.Landed && (next == null || hit.ImpactTime < next.ImpactTime))
            {
                next = hit;
            }
        }

        parryCue.SetActive(showTimingCue && next != null);

        if (next != null)
        {
            float progress = Mathf.Clamp01((now - next.LaunchTime) / (next.ImpactTime - next.LaunchTime));
            shrinkingRing.localScale = Vector3.one * Mathf.Lerp(ringStartScale, 1f, progress);
        }
    }

    private void AnimateMelee(float impactTime, float now)
    {
        float lungeStartTime = impactTime - meleeLungeTime;

        if (now < lungeStartTime)
        {
            float shake = 1f + meleeShakeAmount * Mathf.Sin(now * 30f);
            bossModel.localScale = bossHomeScale * shake;
            bossModel.position = bossHome;

            bool flashOn = Mathf.Sin(now * 20f) > 0f;
            SetBossColor(flashOn ? windupColor : bossOriginalColor);
        }
        else
        {
            bossModel.localScale = bossHomeScale;
            SetBossColor(windupColor);

            float lungeProgress = Mathf.Clamp01((now - lungeStartTime) / meleeLungeTime);
            Vector3 lungeEnd = Vector3.Lerp(bossHome, target, meleeLungeDistance);
            bossModel.position = Vector3.Lerp(bossHome, lungeEnd, lungeProgress);
        }
    }

    private void Flash(Color color)
    {
        flashColor = color;
        flashUntil = Time.time + 0.15f;
        visualsBusyUntil = Mathf.Max(visualsBusyUntil, flashUntil);
    }

    private string BuildResultMessage(bool startedAsProjectile)
    {
        List<string> parts = new List<string>();

        if (perfects > 0)
        {
            parts.Add($"{perfects} perfect");
        }

        if (bossParries > 0)
        {
            parts.Add($"the boss parried back {bossParries}x");
        }

        if (blocks > 0)
        {
            parts.Add($"{blocks} blocked");
        }

        if (hits > 0)
        {
            parts.Add($"{hits} hit");
        }

        string attackName = startedAsProjectile ? "Volley" : "Strike";
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

    private GameObject SpawnBullet()
    {
        GameObject bullet = Instantiate(projectile);
        bullet.SetActive(false);
        activeBullets.Add(bullet);
        return bullet;
    }

    private void DestroyBullet(GameObject bullet)
    {
        activeBullets.Remove(bullet);
        Destroy(bullet);
    }

    private void ClearBullets()
    {
        foreach (GameObject bullet in activeBullets)
        {
            if (bullet != null)
            {
                Destroy(bullet);
            }
        }

        activeBullets.Clear();
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