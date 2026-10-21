using System;
using System.Collections;
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

// Plays the final boss's attacks and checks the player's parry (right mouse button).
// Like Street Fighter 6: press at the moment of impact for a perfect parry, hold to block.
public class BossCombat : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private Transform bossModel;
    [SerializeField] private Renderer bossRenderer;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject projectile;
    [SerializeField] private GameObject parryCue;
    [SerializeField] private RectTransform shrinkingRing;

    [Header("Attacks")]
    [SerializeField, Range(0f, 1f)] private float projectileChance = 0.5f;
    [SerializeField] private float projectileMinTime = 0.7f;
    [SerializeField] private float projectileMaxTime = 1.1f;
    [SerializeField] private float meleeMinWindup = 0.5f;
    [SerializeField] private float meleeMaxWindup = 1.2f;
    [SerializeField] private float meleeLungeTime = 0.2f;
    [SerializeField, Range(0f, 1f)] private float meleeLungeDistance = 0.6f;
    [SerializeField, Range(0f, 0.5f)] private float meleeShakeAmount = 0.15f;
    [SerializeField] private Color windupColor = Color.white;

    [Header("Parry")]
    [SerializeField] private float perfectWindow = 0.08f;   // Pressing parry within this time of impact: perfect parry
    [SerializeField] private float ringStartScale = 3f;
    [SerializeField] private bool showTimingCue = true;

    [Header("Perfect Parry Rewards")]
    [SerializeField, Range(0f, 3f)] private float reflectDamageMultiplier = 1f;
    [SerializeField, Range(0f, 1f)] private float reflectHealPercent = 0.5f;
    [SerializeField, Range(0f, 3f)] private float counterDamageMultiplier = 1.5f;
    [SerializeField] private float reflectFlightTime = 0.3f;
    [SerializeField] private float knockbackDistance = 0.8f;
    [SerializeField] private float knockbackTime = 0.25f;
    [SerializeField] private Color hitFlashColor = Color.yellow;

    private Coroutine attackRoutine;
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

    // What the player can do depends on the chosen chibi
    public void StartAttack(int damage, bool canPerfectParry, bool canBlock, Action<AttackOutcome> onFinished)
    {
        CancelAttack();
        attackRoutine = StartCoroutine(AttackRoutine(damage, canPerfectParry, canBlock, onFinished));
    }

    public void CancelAttack()
    {
        StopAllCoroutines();
        attackRoutine = null;
        IsAttacking = false;
        ResetBossColor();
        HideVisuals();
    }

    private IEnumerator AttackRoutine(int damage, bool canPerfectParry, bool canBlock, Action<AttackOutcome> onFinished)
    {
        IsAttacking = true;

        bool isProjectile = Random.value < projectileChance;
        float timeToImpact = isProjectile
            ? Random.Range(projectileMinTime, projectileMaxTime)
            : Random.Range(meleeMinWindup, meleeMaxWindup) + meleeLungeTime;

        float startTime = Time.time;
        float impactTime = startTime + timeToImpact;
        float endTime = impactTime + perfectWindow;   // Keep listening a moment after impact for late perfect parries

        Vector3 bossStartPosition = bossModel.position;
        Vector3 bossStartScale = bossModel.localScale;
        Vector3 target = player.position;

        float? firstPressTime = null;   // Only the first press can be a perfect parry, so mashing doesn't work
        bool heldAtImpact = false;      // Was the parry button held down when the attack landed?
        bool impactChecked = false;

        parryCue.SetActive(isProjectile && showTimingCue);
        projectile.SetActive(isProjectile);

        while (Time.time < endTime)
        {
            float progress = Mathf.Clamp01((Time.time - startTime) / timeToImpact);

            if (isProjectile)
            {
                projectile.transform.position = Vector3.Lerp(bossStartPosition, target, progress);
                shrinkingRing.localScale = Vector3.one * Mathf.Lerp(ringStartScale, 1f, progress);
            }
            else
            {
                AnimateMelee(bossStartPosition, bossStartScale, target, impactTime);
            }

            if (firstPressTime == null && WasParryPressed())
            {
                firstPressTime = Time.time;
            }

            // At the moment of impact, check if the player is holding parry to block
            if (!impactChecked && Time.time >= impactTime)
            {
                impactChecked = true;
                heldAtImpact = IsParryHeld();
            }

            yield return null;
        }

        bossModel.position = bossStartPosition;
        bossModel.localScale = bossStartScale;
        ResetBossColor();
        parryCue.SetActive(false);

        bool isPerfect = canPerfectParry && firstPressTime.HasValue && Mathf.Abs(firstPressTime.Value - impactTime) <= perfectWindow;
        string attackName = isProjectile ? "projectile" : "strike";
        AttackOutcome outcome = new AttackOutcome();

        if (isPerfect && isProjectile)
        {
            yield return ReflectProjectile(target, bossStartPosition);

            outcome.BossDamage = Mathf.Max(1, Mathf.RoundToInt(damage * reflectDamageMultiplier));
            outcome.HealAmount = Mathf.RoundToInt(damage * reflectHealPercent);
            outcome.Message = $"PERFECT PARRY! Reflected for {outcome.BossDamage} damage and healed {outcome.HealAmount} HP!";
        }
        else if (isPerfect)
        {
            yield return Knockback(bossStartPosition, target);

            outcome.BossDamage = Mathf.Max(1, Mathf.RoundToInt(damage * counterDamageMultiplier));
            outcome.Message = $"PERFECT PARRY! Your counterattack deals {outcome.BossDamage} damage!";
        }
        else if (canBlock && heldAtImpact)
        {
            outcome.DamageTaken = Mathf.CeilToInt(damage / 2f);
            outcome.Message = $"Blocked! The {attackName} deals {outcome.DamageTaken} damage.";
        }
        else
        {
            outcome.DamageTaken = damage;
            outcome.Message = $"The {attackName} hits you for {damage} damage!";
        }

        HideVisuals();
        IsAttacking = false;
        attackRoutine = null;
        onFinished?.Invoke(outcome);
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

    private IEnumerator ReflectProjectile(Vector3 from, Vector3 to)
    {
        projectile.SetActive(true);
        float startTime = Time.time;

        while (Time.time < startTime + reflectFlightTime)
        {
            float progress = (Time.time - startTime) / reflectFlightTime;
            projectile.transform.position = Vector3.Lerp(from, to, progress);
            yield return null;
        }

        projectile.SetActive(false);
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

    // True only on the frame the button goes down
    private bool WasParryPressed()
    {
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    }

    // True for as long as the button is held
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