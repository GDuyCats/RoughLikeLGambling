using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Plays the final boss's attacks and checks if the player parries them with right click
public class BossCombat : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private Transform bossModel;
    [SerializeField] private Transform player;
    [SerializeField] private GameObject projectile;          // A small sphere, hidden when not in use
    [SerializeField] private GameObject parryCue;            // UI: a target circle and a shrinking ring
    [SerializeField] private RectTransform shrinkingRing;

    [Header("Attacks")]
    [SerializeField, Range(0f, 1f)] private float projectileChance = 0.5f;   // The rest are melee strikes
    [SerializeField] private float projectileMinTime = 0.7f;                 // Projectile flight time (seconds)
    [SerializeField] private float projectileMaxTime = 1.1f;
    [SerializeField] private float meleeMinWindup = 0.5f;                    // Random wind-up makes strikes harder to read
    [SerializeField] private float meleeMaxWindup = 1.2f;
    [SerializeField] private float meleeLungeTime = 0.2f;
    [SerializeField, Range(0f, 1f)] private float meleeLungeDistance = 0.6f; // How far the boss lunges toward the player

    [Header("Parry Windows (seconds)")]
    [SerializeField] private float perfectWindow = 0.08f;   // Within this time of impact: perfect parry
    [SerializeField] private float normalWindow = 0.3f;     // Earlier than this: too early
    [SerializeField] private float ringStartScale = 3f;

    private Coroutine attackRoutine;

    public bool IsAttacking { get; private set; }

    private void Awake()
    {
        HideVisuals();
    }

    // onFinished receives the damage the player actually takes and a result message
    public void StartAttack(int damage, Action<int, string> onFinished)
    {
        CancelAttack();
        attackRoutine = StartCoroutine(AttackRoutine(damage, onFinished));
    }

    public void CancelAttack()
    {
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }

        IsAttacking = false;
        HideVisuals();
    }

    private IEnumerator AttackRoutine(int damage, Action<int, string> onFinished)
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

        float? parryTime = null;   // When the player pressed parry (null = not yet)
        bool pressedTooEarly = false;

        // Only projectiles show the timing ring. Melee strikes must be read from the boss's movement.
        parryCue.SetActive(isProjectile);
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

            if (parryTime == null && !pressedTooEarly && WasParryPressed())
            {
                // Pressing way too early locks you out of parrying this attack
                if (Time.time - impactTime < -normalWindow)
                {
                    pressedTooEarly = true;
                }
                else
                {
                    parryTime = Time.time;
                }
            }

            // Wait for the next frame, then continue the loop
            yield return null;
        }

        bossModel.position = bossStartPosition;
        bossModel.localScale = bossStartScale;
        HideVisuals();

        string attackName = isProjectile ? "projectile" : "strike";
        int damageTaken;
        string result;

        if (parryTime.HasValue && Mathf.Abs(parryTime.Value - impactTime) <= perfectWindow)
        {
            damageTaken = 0;
            result = $"PERFECT PARRY! You stopped the {attackName}.";
        }
        else if (parryTime.HasValue)
        {
            damageTaken = Mathf.CeilToInt(damage / 2f);
            result = $"Blocked! The {attackName} deals {damageTaken} damage.";
        }
        else if (pressedTooEarly)
        {
            damageTaken = damage;
            result = $"Too early! The {attackName} hits you for {damage} damage.";
        }
        else
        {
            damageTaken = damage;
            result = $"The {attackName} hits you for {damage} damage!";
        }

        IsAttacking = false;
        attackRoutine = null;
        onFinished?.Invoke(damageTaken, result);
    }

    // Wind-up: the boss shakes. Then it lunges toward the player right before impact.
    private void AnimateMelee(Vector3 startPosition, Vector3 startScale, Vector3 target, float impactTime)
    {
        float lungeStartTime = impactTime - meleeLungeTime;

        if (Time.time < lungeStartTime)
        {
            float shake = 1f + 0.08f * Mathf.Sin(Time.time * 30f);
            bossModel.localScale = startScale * shake;
            bossModel.position = startPosition;
        }
        else
        {
            bossModel.localScale = startScale;
            float lungeProgress = Mathf.Clamp01((Time.time - lungeStartTime) / meleeLungeTime);
            Vector3 lungeEnd = Vector3.Lerp(startPosition, target, meleeLungeDistance);
            bossModel.position = Vector3.Lerp(startPosition, lungeEnd, lungeProgress);
        }
    }

    private bool WasParryPressed()
    {
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
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