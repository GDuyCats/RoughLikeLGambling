using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// A giant dealer sitting behind a table. Each table's dealer is also one of the final bosses.
// Most of the time they just sit there, breathing and blinking. Once the run's boss is revealed,
// that dealer's eyes glow red. A thrown chip on the head makes a dealer flinch and grumble.
// Poke them too often and they slam a hand down on the chibi: just for fun, it never costs health.
public class TableDealer : MonoBehaviour
{
    [SerializeField] private FinalBoss boss;            // Which boss this dealer is
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Renderer headRenderer;
    [SerializeField] private Renderer[] eyeRenderers;
    [SerializeField] private TMP_Text speech;           // Floating text for grumbles
    [SerializeField, TextArea] private string[] grumbles = { "Hey! Watch it!" };

    [Header("Idle")]
    [SerializeField] private float breatheHeight = 0.15f;
    [SerializeField] private float breatheSpeed = 1.5f;
    [SerializeField] private float minBlinkGap = 2f;
    [SerializeField] private float maxBlinkGap = 5f;

    [Header("Eyes")]
    [SerializeField] private Color calmEyeColor = new Color(0.9f, 0.9f, 1f);
    [SerializeField] private Color bossEyeColor = new Color(1f, 0.1f, 0.05f);
    [SerializeField] private float eyeGlow = 3f;
    [SerializeField] private float rageEyeGlow = 2.5f;     // Eyes glow this many times brighter when the boss is furious

    [Header("Chip Hits")]
    [SerializeField] private float grumbleCooldown = 2f;   // At most one grumble this often
    [SerializeField] private float speechTime = 2f;

    [Header("Annoyance")]
    [SerializeField] private int warnAt = 2;               // This many hits in a row: a warning
    [SerializeField] private int slapAt = 3;               // This many: the dealer slams a hand down
    [SerializeField] private float forgiveTime = 10f;      // One step of annoyance fades after this long without a hit

    [Header("Playful Slap")]
    [SerializeField] private float minSlapWindup = 0.25f;  // The hand rises for a random time in this range, then drops:
    [SerializeField] private float maxSlapWindup = 0.6f;   // short and unpredictable, so the slap comes as a surprise
    [SerializeField] private float slapDrop = 0.08f;       // How fast the hand comes down
    [SerializeField] private float hoverHeight = 6f;
    [SerializeField] private float blockPush = 7f;         // How far a blocked slap shoves the chibi
    [SerializeField] private float whiffLockout = 0.25f;

    private RunManager run;
    private PlayerController player;
    private BossCombat bossCombat;

    private Vector3 headRest;
    private Quaternion headRestRotation;
    private Vector3[] eyeScales;
    private float nextBlinkTime;
    private float nextGrumbleTime;
    private Coroutine flinchRoutine;
    private Coroutine speechRoutine;

    private bool isMarked;          // This dealer is the run's revealed boss
    private bool isEnraged;         // The boss is below half health: its eyes blaze
    private int annoyance;
    private float nextCalmTime;
    private bool isSlapping;

    public FinalBoss Boss => boss;
    public Transform Head => head;
    public Transform LeftHand => leftHand;
    public Transform RightHand => rightHand;
    public Renderer HeadRenderer => headRenderer;

    private void Awake()
    {
        run = FindFirstObjectByType<RunManager>();
        player = FindFirstObjectByType<PlayerController>();
        bossCombat = FindFirstObjectByType<BossCombat>();

        headRest = head.localPosition;
        headRestRotation = head.localRotation;

        eyeScales = new Vector3[eyeRenderers.Length];

        for (int i = 0; i < eyeRenderers.Length; i++)
        {
            eyeScales[i] = eyeRenderers[i].transform.localScale;
        }

        SetEyeColor(calmEyeColor);

        if (speech != null)
        {
            speech.gameObject.SetActive(false);
        }

        nextBlinkTime = Time.time + Random.Range(minBlinkGap, maxBlinkGap);
    }

    private void Update()
    {
        // Breathing: the head bobs gently up and down
        head.localPosition = headRest + Vector3.up * Mathf.Sin(Time.time * breatheSpeed) * breatheHeight;

        if (Time.time >= nextBlinkTime && flinchRoutine == null)
        {
            nextBlinkTime = Time.time + Random.Range(minBlinkGap, maxBlinkGap);
            StartCoroutine(BlinkRoutine());
        }

        // Left alone, the dealer slowly calms down
        if (annoyance > 0 && !isSlapping && Time.time >= nextCalmTime)
        {
            annoyance--;
            nextCalmTime = Time.time + forgiveTime;
        }
    }

    // true: this dealer is the run's revealed boss, so the eyes glow red. false: calm eyes again.
    public void SetMarked(bool marked)
    {
        isMarked = marked;
        SetEyeColor(marked ? bossEyeColor : calmEyeColor);
    }

    // The boss is furious (low health): blazing eyes, and it shouts its line once
    public void SetEnraged(bool enraged, string line)
    {
        isEnraged = enraged;
        SetEyeColor(isMarked || enraged ? bossEyeColor : calmEyeColor);

        if (enraged && !string.IsNullOrEmpty(line))
        {
            Say(line);
        }
    }

    // A thrown chip hit this dealer on the head
    public void OnChipHit()
    {
        AudioManager.Play(SoundId.DealerBonk);

        if (flinchRoutine != null)
        {
            StopCoroutine(flinchRoutine);
        }

        flinchRoutine = StartCoroutine(FlinchRoutine());

        if (GetAngrier())
        {
            return;
        }

        if (grumbles.Length > 0 && Time.time >= nextGrumbleTime)
        {
            nextGrumbleTime = Time.time + grumbleCooldown;
            Say(Pick(grumbles));
        }
    }

    // The chibi took a chip from this dealer's tray: that counts as a poke too
    public void OnChipStolen()
    {
        if (GetAngrier())
        {
            return;
        }

        if (Time.time >= nextGrumbleTime)
        {
            nextGrumbleTime = Time.time + grumbleCooldown;
            Say(Pick(TheftLines()));
        }
    }

    // One step angrier: a warning, then the slap. Returns true if the dealer already said something.
    // In the boss fight the boss is already fighting for real, and a slap already on its way counts once.
    private bool GetAngrier()
    {
        if (run == null || run.IsBossFight || isSlapping)
        {
            return false;
        }

        annoyance++;
        nextCalmTime = Time.time + forgiveTime;

        if (annoyance >= slapAt)
        {
            Say(Pick(SlapLines()));
            StartCoroutine(SlapRoutine());
            return true;
        }

        if (annoyance >= warnAt)
        {
            Say(Pick(WarningLines()));
            StartCoroutine(EyeFlashRoutine());
            return true;
        }

        return false;
    }

    // ---------- The playful slap ----------

    // A sudden slam: the hand jumps up over the chibi and drops after a short, random moment. No warning shadow.
    // Perfect parry bounces it away, block shoves the chibi back, otherwise the chibi gets squashed.
    // No health is ever lost.
    private IEnumerator SlapRoutine()
    {
        isSlapping = true;

        Transform hand = ClosestHand();
        Vector3 handRest = hand.localPosition;

        float baseWindow = bossCombat != null ? bossCombat.PerfectWindow : 0.08f;
        float window = baseWindow + run.GetUpgradeTotal(UpgradeType.PerfectWindowMs) / 1000f;
        bool canPerfect = run.CanPerfectParry;
        bool canBlock = run.CanBlock;

        float windup = Random.Range(minSlapWindup, maxSlapWindup);
        float impactTime = Time.time + windup + slapDrop;
        float dropStart = impactTime - slapDrop;
        float lockoutUntil = 0f;
        bool dropping = false;
        Vector3 dropFrom = hand.position;

        bool perfected = false;
        bool freezeShown = false;
        bool landed = false;
        bool held = false;
        bool cancelled = false;
        Vector3 target = FlatPlayerPosition();

        // Run until a moment after impact, so a slightly late press can still be perfect
        while (Time.time < impactTime + window)
        {
            float now = Time.time;

            // The chibi left the table: forget it
            if (!IsPlayerNearby())
            {
                cancelled = true;
                break;
            }

            if (canPerfect && !perfected && !ParryFreeze.IsFrozen && WasParryPressed() && now >= lockoutUntil)
            {
                if (Mathf.Abs(now - impactTime) <= window)
                {
                    perfected = true;
                    AudioManager.Play(SoundId.PerfectParry);
                }
                else
                {
                    lockoutUntil = now + whiffLockout;
                }
            }

            // The slap follows the chibi until it lands: it always hits
            if (!landed)
            {
                target = FlatPlayerPosition();
            }

            Vector3 hover = target + Vector3.up * hoverHeight;

            if (now < dropStart)
            {
                // Snap up fast over the chibi
                hand.position = Vector3.Lerp(hand.position, hover, 20f * Time.deltaTime);
            }
            else
            {
                // Smash down from wherever the hand got to
                if (!dropping)
                {
                    dropping = true;
                    dropFrom = hand.position;
                }

                float progress = Mathf.Clamp01((now - dropStart) / slapDrop);
                hand.position = Vector3.Lerp(dropFrom, target + Vector3.up * 0.5f, progress);
            }

            if (!landed && now >= impactTime)
            {
                landed = true;
                held = IsParryHeld();
            }

            // A perfect parry: everything freezes at the moment of contact, and the chibi shouts a line
            if (perfected && !freezeShown && now >= impactTime)
            {
                freezeShown = true;
                ParryFreeze.PlayerParried(player.transform, Vector3.Lerp(hand.position, player.transform.position, 0.5f), true);
            }

            yield return null;
        }

        if (!cancelled)
        {
            if (perfected)
            {
                // The hand bounces off, and the dealer is grudgingly impressed
                Say(Pick(PraiseLines()));
                yield return MoveHand(hand, hand.position, hand.position + Vector3.up * hoverHeight, 0.15f);
            }
            else if (canBlock && held)
            {
                AudioManager.Play(SoundId.Block);
                Vector3 away = player.transform.position - transform.position;
                away.y = 0f;
                player.Knockback(away.normalized * blockPush);
            }
            else
            {
                AudioManager.Play(SoundId.BossSlam);
                player.Squash();
            }

            yield return new WaitForSeconds(0.3f);
        }

        // Back to resting on the table
        Vector3 restWorld = hand.parent.TransformPoint(handRest);
        yield return MoveHand(hand, hand.position, restWorld, 0.4f);
        hand.localPosition = handRest;

        annoyance = 0;
        isSlapping = false;
    }

    private IEnumerator MoveHand(Transform hand, Vector3 from, Vector3 to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            hand.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        hand.position = to;
    }

    private Transform ClosestHand()
    {
        Vector3 chibi = player.transform.position;
        return Vector3.Distance(leftHand.position, chibi) <= Vector3.Distance(rightHand.position, chibi) ? leftHand : rightHand;
    }

    private Vector3 FlatPlayerPosition()
    {
        return new Vector3(player.transform.position.x, 0f, player.transform.position.z);
    }

    // Is the chibi still on this dealer's table (not back on the casino floor)?
    private bool IsPlayerNearby()
    {
        Vector3 offset = player.transform.position - transform.position;
        offset.y = 0f;
        return offset.magnitude < 40f;
    }

    private bool WasParryPressed()
    {
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    }

    private bool IsParryHeld()
    {
        return Mouse.current != null && Mouse.current.rightButton.isPressed;
    }

    // ---------- Lines ----------

    private string[] TheftLines()
    {
        return GameOf() switch
        {
            MinigameType.HighLow => new[] { "I saw your hand move before it did.", "Those chips are fated to stay with me." },
            MinigameType.Dice => new[] { "Thief! That is the royal treasury!", "Hands off the crown's chips!" },
            _ => new[] { "Hey. That's the house's money.", "Put it back, kid." }
        };
    }

    private string[] WarningLines()
    {
        return GameOf() switch
        {
            MinigameType.HighLow => new[] { "I have seen how this ends for you.", "One more, and fate turns cruel." },
            MinigameType.Dice => new[] { "Last warning, peasant!", "The King's patience is NOT endless!" },
            _ => new[] { "One more and you're a pancake.", "Strike two, kid." }
        };
    }

    private string[] SlapLines()
    {
        return GameOf() switch
        {
            MinigameType.HighLow => new[] { "As foretold!", "The prophecy is fulfilled!" },
            MinigameType.Dice => new[] { "ENOUGH!", "Kneel before your King!" },
            _ => new[] { "That's it!", "House rules, pal!" }
        };
    }

    private string[] PraiseLines()
    {
        return GameOf() switch
        {
            MinigameType.HighLow => new[] { "...I did not see that coming.", "Interesting. Very interesting." },
            MinigameType.Dice => new[] { "Impossible! Nobody blocks the King!", "Hmph. Lucky." },
            _ => new[] { "...Not bad, kid.", "Okay, okay. You got me." }
        };
    }

    // What this dealer shouts when, as the boss, it parries the chibi's counter
    public string PickBossParryLine()
    {
        return Pick(BossParryLines());
    }

    private string[] BossParryLines()
    {
        return GameOf() switch
        {
            MinigameType.HighLow => new[] { "Foreseen.", "I saw that three moves ago.", "Fate says no." },
            MinigameType.Dice => new[] { "The King blocks!", "Denied, peasant!", "You dare?!" },
            _ => new[] { "Nice try, kid.", "I've got an ace for that.", "House always wins." }
        };
    }

    private MinigameType GameOf()
    {
        return boss != null ? boss.FavoriteGame : MinigameType.Blackjack;
    }

    private static string Pick(string[] lines)
    {
        return lines[Random.Range(0, lines.Length)];
    }

    // ---------- Face ----------

    private void Say(string line)
    {
        if (speech == null)
        {
            return;
        }

        if (speechRoutine != null)
        {
            StopCoroutine(speechRoutine);
        }

        speechRoutine = StartCoroutine(SpeechRoutine(line));
    }

    // Eyes shut, head jerks back, then an angry little shake
    private IEnumerator FlinchRoutine()
    {
        SetEyesOpen(false);

        const float jerkTime = 0.08f;
        const float shakeTime = 0.45f;

        for (float t = 0f; t < jerkTime; t += Time.deltaTime)
        {
            head.localRotation = headRestRotation * Quaternion.Euler(-15f * (t / jerkTime), 0f, 0f);
            yield return null;
        }

        for (float t = 0f; t < shakeTime; t += Time.deltaTime)
        {
            float fade = 1f - t / shakeTime;
            float shake = Mathf.Sin(t * 45f) * 8f * fade;
            head.localRotation = headRestRotation * Quaternion.Euler(-15f * fade, shake, 0f);
            yield return null;
        }

        head.localRotation = headRestRotation;
        SetEyesOpen(true);
        flinchRoutine = null;
    }

    // A quick red flash of the eyes: the warning
    private IEnumerator EyeFlashRoutine()
    {
        SetEyeColor(bossEyeColor);
        yield return new WaitForSeconds(0.4f);
        SetEyeColor(isMarked ? bossEyeColor : calmEyeColor);
    }

    private IEnumerator BlinkRoutine()
    {
        SetEyesOpen(false);
        yield return new WaitForSeconds(0.12f);

        // A flinch keeps the eyes shut on its own
        if (flinchRoutine == null)
        {
            SetEyesOpen(true);
        }
    }

    private IEnumerator SpeechRoutine(string line)
    {
        speech.text = line;
        speech.gameObject.SetActive(true);
        yield return new WaitForSeconds(speechTime);
        speech.gameObject.SetActive(false);
        speechRoutine = null;
    }

    // Closed eyes are just the eyes squashed flat
    private void SetEyesOpen(bool open)
    {
        for (int i = 0; i < eyeRenderers.Length; i++)
        {
            Vector3 scale = eyeScales[i];
            eyeRenderers[i].transform.localScale = open ? scale : new Vector3(scale.x, scale.y * 0.1f, scale.z);
        }
    }

    private void SetEyeColor(Color color)
    {
        foreach (Renderer eye in eyeRenderers)
        {
            Material material = eye.material;
            material.color = color;

            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * eyeGlow * (isEnraged ? rageEyeGlow : 1f));
            }
        }
    }
}
