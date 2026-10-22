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

// Plays the final boss's attacks: a volley of bullets or one melee strike.
// Like Street Fighter 6: press right click as a hit lands for a perfect parry, hold it to block.
public class BossCombat : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private Transform bossModel;
    [SerializeField] private Renderer bossRenderer;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject projectile;          // Template bullet, copied for each shot and kept hidden
    [SerializeField] private GameObject parryCue;
    [SerializeField] private RectTransform shrinkingRing;

    [Header("Attacks")]
    [SerializeField, Range(0f, 1f)] private float projectileChance = 0.5f;   // The rest are melee strikes

    [Header("Projectile Volley")]
    [SerializeField] private int minBullets = 2;
    [SerializeField] private int maxBullets = 4;
    [SerializeField] private float bulletFlightTime = 0.6f;     // How long each bullet takes to reach the player
    [SerializeField] private float firstBulletMinTime = 0.7f;   // When the first bullet lands, after the attack starts
    [SerializeField] private float firstBulletMaxTime = 1.0f;
    [SerializeField] private float bulletMinGap = 0.2f;         // Shortest time between two bullets landing
    [SerializeField] private float bulletMaxGap = 0.6f;         // Longest time between two bullets landing        // Time between two bullets landing

    [Header("Melee Strike")]
    [SerializeField] private float meleeMinWindup = 0.5f;
    [SerializeField] private float meleeMaxWindup = 1.2f;
    [SerializeField] private float meleeLungeTime = 0.2f;
    [SerializeField, Range(0f, 1f)] private float meleeLungeDistance = 0.6f;
    [SerializeField, Range(0f, 0.5f)] private float meleeShakeAmount = 0.15f;
    [SerializeField] private Color windupColor = Color.white;

    [Header("Parry")]
    [SerializeField] private float perfectWindow = 0.08f;   // A press within this time of a hit landing: perfect parry
    [SerializeField] private float whiffLockout = 0.25f;    // A press with no hit nearby blocks perfect parries for this long
    [SerializeField] private float ringStartScale = 3f;
    [SerializeField] private bool showTimingCue = true;

    [Header("Block")]
    [SerializeField, Range(0f, 1f)] private float projectileBlockReduction = 0.75f;   // Blocking a bullet stops this share of its damage
    [SerializeField, Range(0f, 1f)] private float meleeBlockReduction = 0.4f;         // Blocking a strike stops this share of its damage

    [Header("Perfect Parry Rewards")]
    [SerializeField, Range(0f, 3f)] private float reflectDamageMultiplier = 1f;
    [SerializeField, Range(0f, 1f)] private float reflectHealPercent = 0.5f;
    [SerializeField, Range(0f, 3f)] private float counterDamageMultiplier = 1.5f;
    [SerializeField] private float reflectFlightTime = 0.3f;
    [SerializeField] private float knockbackDistance = 0.8f;
    [SerializeField] private float knockbackTime = 0.25f;
    [SerializeField] private Color hitFlashColor = Color.yellow;

    private readonly List<GameObject> activeBullets = new List<GameObject>();
    private Color bossOriginalColor;

    public bool IsAttacking { get; private set; }

    private void Awake()
    {
        if (bossRenderer != null)
        {
            bossOriginalColor = bossRenderer.material.color;
        }

        HideVisuals();
    }

    public void StartAttack(int damage, bool canPerfectParry, bool canBlock, float perfectWindowBonus, int rewardBonusPercent, Action<AttackOutcome> onFinished)
    {
        CancelAttack();
        StartCoroutine(AttackRoutine(damage, canPerfectParry, canBlock, perfectWindowBonus, rewardBonusPercent, onFinished));
    }

    public void CancelAttack()
    {
        StopAllCoroutines();
        ClearBullets();
        IsAttacking = false;
        ResetBossColor();
        HideVisuals();
    }

    private IEnumerator AttackRoutine(int damage, bool canPerfectParry, bool canBlock, float perfectWindowBonus, int rewardBonusPercent, Action<AttackOutcome> onFinished)
    {
        IsAttacking = true;

        bool isProjectile = Random.value < projectileChance;
        float window = perfectWindow + perfectWindowBonus;
        float startTime = Time.time;

        // When each hit lands: a volley has several bullets, a strike has one hit
        int hitCount = isProjectile ? Random.Range(minBullets, maxBullets + 1) : 1;
        float[] impactTimes = new float[hitCount];

        if (isProjectile)
        {
            float firstImpact = startTime + Random.Range(firstBulletMinTime, firstBulletMaxTime);

            float nextImpact = firstImpact;

            for (int i = 0; i < hitCount; i++)
            {
                impactTimes[i] = nextImpact;

                // A random gap before the next bullet, so the rhythm can't be memorized
                nextImpact += Random.Range(bulletMinGap, bulletMaxGap);
            }
        }
        else
        {
            impactTimes[0] = startTime + Random.Range(meleeMinWindup, meleeMaxWindup) + meleeLungeTime;
        }

        float lastImpact = impactTimes[hitCount - 1];

        bool[] perfected = new bool[hitCount];
        bool[] heldAtImpact = new bool[hitCount];
        bool[] landed = new bool[hitCount];
        bool[] reflecting = new bool[hitCount];   // Bullets already flying back to the boss
        float lockoutUntil = 0f;

        Vector3 bossStartPosition = bossModel.position;
        Vector3 bossStartScale = bossModel.localScale;
        Vector3 target = player.position;

        GameObject[] bullets = isProjectile ? SpawnBullets(hitCount) : new GameObject[0];
        parryCue.SetActive(isProjectile && showTimingCue);

        while (Time.time <= lastImpact + window)
        {
            float now = Time.time;

            // A press counts as a perfect parry for a hit landing within the window.
            // A press with no hit nearby is a whiff and locks parrying for a moment, so mashing doesn't work.
            if (canPerfectParry && WasParryPressed() && now >= lockoutUntil)
            {
                int hit = FindHitInWindow(impactTimes, perfected, now, window);

                if (hit >= 0)
                {
                    perfected[hit] = true;
                }
                else
                {
                    lockoutUntil = now + whiffLockout;
                }
            }

            // When a hit lands, remember whether parry was being held (a block)
            for (int i = 0; i < hitCount; i++)
            {
                if (!landed[i] && now >= impactTimes[i])
                {
                    landed[i] = true;
                    heldAtImpact[i] = IsParryHeld();
                }
            }

            if (isProjectile)
            {
                // A parried bullet flies back right away, so each one is seen on its own
                for (int i = 0; i < hitCount; i++)
                {
                    if (perfected[i] && landed[i] && !reflecting[i])
                    {
                        reflecting[i] = true;
                        StartCoroutine(FlyBack(bullets[i], target, bossStartPosition));
                    }
                }

                UpdateBullets(bullets, impactTimes, landed, reflecting, bossStartPosition, target);
                UpdateRing(impactTimes, landed);
            }
            else
            {
                AnimateMelee(bossStartPosition, bossStartScale, target, impactTimes[0]);
            }

            yield return null;
        }

        bossModel.position = bossStartPosition;
        bossModel.localScale = bossStartScale;
        ResetBossColor();
        parryCue.SetActive(false);

        // Add up what happened to every hit
        AttackOutcome outcome = new AttackOutcome();
        float rewardScale = 1f + rewardBonusPercent / 100f;
        float blockReduction = isProjectile ? projectileBlockReduction : meleeBlockReduction;
        int perfects = 0;
        int blocks = 0;
        int hits = 0;

        for (int i = 0; i < hitCount; i++)
        {
            int share = GetDamageShare(damage, hitCount, i);

            if (perfected[i])
            {
                perfects++;

                if (isProjectile)
                {
                    outcome.BossDamage += Mathf.Max(1, Mathf.RoundToInt(share * reflectDamageMultiplier * rewardScale));
                    outcome.HealAmount += Mathf.RoundToInt(share * reflectHealPercent * rewardScale);
                }
                else
                {
                    outcome.BossDamage += Mathf.Max(1, Mathf.RoundToInt(share * counterDamageMultiplier * rewardScale));
                }
            }
            else if (canBlock && heldAtImpact[i])
            {
                blocks++;
                outcome.DamageTaken += Mathf.CeilToInt(share * (1f - blockReduction));
            }
            else
            {
                hits++;
                outcome.DamageTaken += share;
            }
        }

        if (perfects > 0)
        {
            if (isProjectile)
            {
                // Let the last reflected bullets finish flying back
                yield return new WaitForSeconds(reflectFlightTime);
            }
            else
            {
                yield return Knockback(bossStartPosition, target);
            }
        }

        ClearBullets();
        HideVisuals();

        outcome.Message = BuildResultMessage(isProjectile, hitCount, perfects, blocks, hits, outcome);
        IsAttacking = false;
        onFinished?.Invoke(outcome);
    }

    // Splits the damage as evenly as possible, e.g. 10 over 3 bullets = 4, 3, 3
    private int GetDamageShare(int totalDamage, int hitCount, int index)
    {
        int baseShare = totalDamage / hitCount;
        int remainder = totalDamage - baseShare * hitCount;
        return baseShare + (index < remainder ? 1 : 0);
    }

    // The closest hit to "now" that is inside the window and not parried yet (-1 if none)
    private int FindHitInWindow(float[] impactTimes, bool[] perfected, float now, float window)
    {
        int best = -1;
        float bestGap = window;

        for (int i = 0; i < impactTimes.Length; i++)
        {
            float gap = Mathf.Abs(now - impactTimes[i]);

            if (!perfected[i] && gap <= bestGap)
            {
                best = i;
                bestGap = gap;
            }
        }

        return best;
    }

    // Instantiate makes a copy of the template bullet for each shot
    private GameObject[] SpawnBullets(int count)
    {
        GameObject[] bullets = new GameObject[count];

        for (int i = 0; i < count; i++)
        {
            GameObject bullet = Instantiate(projectile);
            bullet.SetActive(false);
            activeBullets.Add(bullet);
            bullets[i] = bullet;
        }

        return bullets;
    }

    // Each bullet leaves the boss at its own time and disappears when it lands
    private void UpdateBullets(GameObject[] bullets, float[] impactTimes, bool[] landed, bool[] reflecting, Vector3 from, Vector3 to)
    {
        for (int i = 0; i < bullets.Length; i++)
        {
            // Reflected bullets are moved by FlyBack instead
            if (reflecting[i])
            {
                continue;
            }

            float launchTime = impactTimes[i] - bulletFlightTime;
            bool inFlight = !landed[i] && Time.time >= launchTime;

            bullets[i].SetActive(inFlight);

            if (inFlight)
            {
                float progress = Mathf.Clamp01((Time.time - launchTime) / bulletFlightTime);
                bullets[i].transform.position = Vector3.Lerp(from, to, progress);
            }
        }
    }

    // The ring always shows the timing of the next bullet that hasn't landed
    private void UpdateRing(float[] impactTimes, bool[] landed)
    {
        for (int i = 0; i < impactTimes.Length; i++)
        {
            if (!landed[i])
            {
                float launchTime = impactTimes[i] - bulletFlightTime;
                float progress = Mathf.Clamp01((Time.time - launchTime) / bulletFlightTime);
                shrinkingRing.localScale = Vector3.one * Mathf.Lerp(ringStartScale, 1f, progress);
                return;
            }
        }

        shrinkingRing.localScale = Vector3.one;
    }

    private string BuildResultMessage(bool isProjectile, int hitCount, int perfects, int blocks, int hits, AttackOutcome outcome)
    {
        List<string> parts = new List<string>();

        if (perfects > 0)
        {
            parts.Add(isProjectile ? $"{perfects} reflected" : "PERFECT PARRY");
        }

        if (blocks > 0)
        {
            parts.Add($"{blocks} blocked");
        }

        if (hits > 0)
        {
            parts.Add($"{hits} hit");
        }

        string attackName = isProjectile ? $"Volley of {hitCount}" : "Strike";
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

    private void AnimateMelee(Vector3 startPosition, Vector3 startScale, Vector3 target, float impactTime)
    {
        float lungeStartTime = impactTime - meleeLungeTime;

        if (Time.time < lungeStartTime)
        {
            float shake = 1f + meleeShakeAmount * Mathf.Sin(Time.time * 30f);
            bossModel.localScale = startScale * shake;
            bossModel.position = startPosition;

            bool flashOn = Mathf.Sin(Time.time * 20f) > 0f;
            SetBossColor(flashOn ? windupColor : bossOriginalColor);
        }
        else
        {
            bossModel.localScale = startScale;
            SetBossColor(windupColor);

            float lungeProgress = Mathf.Clamp01((Time.time - lungeStartTime) / meleeLungeTime);
            Vector3 lungeEnd = Vector3.Lerp(startPosition, target, meleeLungeDistance);
            bossModel.position = Vector3.Lerp(startPosition, lungeEnd, lungeProgress);
        }
    }

    // One parried bullet flies from the player back into the boss
    private IEnumerator FlyBack(GameObject bullet, Vector3 from, Vector3 to)
    {
        bullet.SetActive(true);
        float startTime = Time.time;

        while (Time.time < startTime + reflectFlightTime)
        {
            float progress = (Time.time - startTime) / reflectFlightTime;
            bullet.transform.position = Vector3.Lerp(from, to, progress);
            yield return null;
        }

        bullet.SetActive(false);
        yield return FlashBoss();
    }

    private IEnumerator Knockback(Vector3 bossStartPosition, Vector3 playerPosition)
    {
        Vector3 away = bossStartPosition - playerPosition;
        away.y = 0f;
        Vector3 pushedPosition = bossStartPosition + away.normalized * knockbackDistance;

        SetBossColor(hitFlashColor);
        float startTime = Time.time;

        while (Time.time < startTime + knockbackTime)
        {
            float t = (Time.time - startTime) / knockbackTime;
            float push = t < 0.5f ? t * 2f : (1f - t) * 2f;
            bossModel.position = Vector3.Lerp(bossStartPosition, pushedPosition, push);
            yield return null;
        }

        bossModel.position = bossStartPosition;
        ResetBossColor();
    }

    private IEnumerator FlashBoss()
    {
        SetBossColor(hitFlashColor);
        yield return new WaitForSeconds(0.15f);
        ResetBossColor();
    }

    // Destroy removes the bullet copies once they are no longer needed
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