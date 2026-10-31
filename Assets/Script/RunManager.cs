using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.Cinemachine;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Manages everything shared by all minigames: character, chips, bets, rounds, curses, health, the final boss
public class RunManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text chipsText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text ownedUpgradesText;
    [SerializeField] private TMP_Text statsText;
    [SerializeField] private TMP_Text bossText;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button betDownButton;
    [SerializeField] private Button betUpButton;

    [Header("Upgrade Screen")]
    [SerializeField] private GameObject upgradePanel;
    [SerializeField] private Button[] upgradeButtons;
    [SerializeField] private List<Upgrade> upgradePool;

    [Header("Characters")]
    [SerializeField] private List<CharacterData> characters;
    [SerializeField] private GameObject characterPanel;
    [SerializeField] private Button[] characterButtons;
    [SerializeField] private Renderer playerRenderer;   // Recolored to match the chosen chibi

    [Header("Rarity")]
    [SerializeField] private int commonWeight = 70;
    [SerializeField] private int rareWeight = 25;
    [SerializeField] private int legendaryWeight = 5;
    [SerializeField] private Color commonColor = new Color(0.85f, 0.85f, 0.85f);
    [SerializeField] private Color rareColor = new Color(0.55f, 0.75f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.8f, 0.25f);

    [Header("Run Structure")]
    [SerializeField] private int finalRound = 9;
    [SerializeField] private List<int> curseRounds = new List<int> { 3, 6 };
    [SerializeField] private List<BossModifier> cursePool;

    [Header("Final Boss")]
    [SerializeField] private List<FinalBoss> finalBossPool;
    [SerializeField] private int revealBossFromRound = 7;
    [SerializeField] private int playerMaxHealth = 100;   // Base health before the character's bonus
    [SerializeField] private BossCombat bossCombat;
    [SerializeField] private CinemachineImpulseSource hitShake;   // Shakes the camera when the player gets hit
    [SerializeField] private int overtimeStartPlay = 10;          // Overtime begins on this play of the boss fight
    [SerializeField] private float overtimeRampPerPlay = 0.1f;

    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private int startingMaxBetLevel = 3;
    [SerializeField] private int playsPerRound = 12;
    [SerializeField] private int baseRoundGoal = 20;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;
    [SerializeField, Range(0f, 1f)] private float stackFalloff = 0.5f;
    [SerializeField] private float chipRollTime = 0.5f;   // Roughly how long the chip counter takes to catch up
    [SerializeField] private bool startRunOnLoad = false; // Off: the main menu's PLAY button starts the run
    public event Action RunStarted;
    public event Action StateChanged;
    public event Action RoundStarted;

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private TMP_Text restartButtonText;

    private CharacterData character;   // The chibi chosen for this run
    private int chips;
    private float displayedChips;   // The number shown on screen, rolling toward the real chips
    private int roundNumber;
    private int targetChips;
    private int playsLeft;
    private int playsUsedThisRound;
    private int betLevel = 1;
    private bool isGameOver;
    private bool isVictory;

    private BossModifier currentCurse;
    private bool lastRoundWasCurse;

    private FinalBoss finalBoss;
    private int playerHealth;
    private int bossHealth;
    private bool isBossAttacking;
    private int shieldsLeft;   // Boss attacks that will be blocked completely (Lucky Charm)
    public bool CanPlay => !isGameOver && !upgradePanel.activeSelf && !characterPanel.activeSelf && !isBossAttacking;
    public int PlaysLeft => playsLeft;
    public int RoundNumber => roundNumber;
    public int Chips => chips;
    public int BetLevel => betLevel;
    public int MaxBetLevel => startingMaxBetLevel + GetUpgradeTotal(UpgradeType.ExtraBetLevel);
    public int MaxHealth => playerMaxHealth + (character != null ? character.MaxHealthBonus : 0) + GetUpgradeTotal(UpgradeType.MaxHealth);
    public bool HasPlayedThisRound => playsUsedThisRound > 0;
    public bool IsCurseRound => currentCurse != null;
    public bool IsFinalRound => roundNumber == finalRound;
    public FinalBoss FinalBoss => finalBoss;
    public bool IsBossFight => IsFinalRound && finalBoss != null;
    public bool IsBossRevealed => roundNumber >= revealBossFromRound;
    public bool CanPerfectParry => character == null || character.CanPerfectParry;
    public bool CanBlock => character == null || character.CanBlock;

    // 1.0 at first, then 1.1, 1.2, 1.3... once the boss fight goes into overtime
    public float OvertimeMultiplier => IsBossFight && playsUsedThisRound >= overtimeStartPlay
        ? 1f + overtimeRampPerPlay * (playsUsedThisRound - overtimeStartPlay + 1)
        : 1f;
    public bool IsMinigameBusy { get; private set; }
    public bool IsGameOver => isGameOver;   // The run has ended (won or lost)

    private void Start()
    {
        restartButtonText = restartButton.GetComponentInChildren<TMP_Text>();

        restartButton.onClick.AddListener(StartNewRun);
        betDownButton.onClick.AddListener(() => ChangeBetLevel(-1));
        betUpButton.onClick.AddListener(() => ChangeBetLevel(1));

        if (startRunOnLoad)
        {
            StartNewRun();
        }
    }
    // Rolls the chip counter toward the real value, so wins and losses count up or down
    private void Update()
    {
        if (Mathf.Approximately(displayedChips, chips))
        {
            return;
        }

        float speed = Mathf.Max(Mathf.Abs(chips - displayedChips) / chipRollTime, 20f);
        displayedChips = Mathf.MoveTowards(displayedChips, chips, speed * Time.deltaTime);
        RefreshChipsText();
    }

    private void RefreshChipsText()
    {
        int shown = Mathf.RoundToInt(displayedChips);

        // Green while counting up, red while counting down, white when settled
        string color = shown < chips ? "#7BE495" : shown > chips ? "#FF6B6B" : "#FFFFFF";
        chipsText.text = $"Chips: <color={color}>{shown}</color>   Bet: {GetCurrentBet()} (x{betLevel})";
    }

    // Resets the run, then waits for the player to choose a chibi
    public void StartNewRun()
    {
        character = null;
        chips = startingChips;
        displayedChips = chips;
        ownedUpgrades.Clear();
        betLevel = 1;
        roundNumber = 0;
        targetChips = 0;
        playsLeft = 0;
        currentCurse = null;
        isGameOver = false;
        isVictory = false;
        IsMinigameBusy = false;
        lastRoundWasCurse = false;
        isBossAttacking = false;

        if (bossCombat != null)
        {
            bossCombat.CancelAttack();
        }

        finalBoss = finalBossPool.Count > 0 ? finalBossPool[Random.Range(0, finalBossPool.Count)] : null;

        upgradePanel.SetActive(false);
        restartButton.gameObject.SetActive(false);

        ShowCharacterChoices();
        messageText.text = "Choose your chibi!";
        UpdateUI();
    }

    private void ShowCharacterChoices()
    {
        for (int i = 0; i < characterButtons.Length; i++)
        {
            Button button = characterButtons[i];

            if (i >= characters.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            CharacterData choice = characters[i];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text = $"<b>{choice.DisplayName}</b>\n\n{choice.Description}";
            button.image.color = choice.Color;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ChooseCharacter(choice));
        }

        characterPanel.SetActive(true);
    }

    // The run really begins once a chibi is chosen
    private void ChooseCharacter(CharacterData chosen)
    {
        character = chosen;
        characterPanel.SetActive(false);

        ownedUpgrades.AddRange(chosen.StartingUpgrades);
        playerHealth = MaxHealth;

        if (playerRenderer != null)
        {
            playerRenderer.material.color = chosen.Color;
        }

        // Announce the new run first (so tables reset), then start round 1
        RunStarted?.Invoke();
        StartRound(1);
        UpdateUI();
    }

    public void SetMinigameBusy(bool busy)
    {
        IsMinigameBusy = busy;
        UpdateUI();
    }

    // A play with one result: positive = chips won, negative = chips lost
    public void FinishPlay(int chipChange, string message)
    {
        if (!CanPlay)
        {
            return;
        }

        if (IsBossFight)
        {
            HandleBossPlay(Mathf.Max(0, chipChange), Mathf.Max(0, -chipChange), message);
            return;
        }

        chips += chipChange;
        playsLeft--;
        playsUsedThisRound++;
        messageText.text = message;

        if (chipChange > 0)
        {
            AudioManager.Play(SoundId.Win);
        }
        else if (chipChange < 0)
        {
            AudioManager.Play(SoundId.Lose);
        }

        if (chips >= targetChips)
        {
            ClearRound();
        }
        else if (chips <= 0 || playsLeft <= 0)
        {
            GameOver();
        }

        UpdateUI();
    }

    // A play that can win and lose at the same time (for example split hands in Blackjack).
    // In normal rounds only the total counts. In the boss fight, wins hurt the boss first,
    // then losses make the boss attack.
    public void FinishMixedPlay(int chipsWon, int chipsLost, string message)
    {
        if (!CanPlay)
        {
            return;
        }

        if (IsBossFight)
        {
            HandleBossPlay(chipsWon, chipsLost, message);
            return;
        }

        FinishPlay(chipsWon - chipsLost, message);
    }

    private void HandleBossPlay(int chipsWon, int chipsLost, string message)
    {
        playsUsedThisRound++;

        // Overtime: the longer the fight, the more damage both sides deal
        float overtime = OvertimeMultiplier;

        if (overtime > 1f)
        {
            chipsWon = Mathf.RoundToInt(chipsWon * overtime);
            chipsLost = Mathf.RoundToInt(chipsLost * overtime);
            message += $"\nOvertime x{overtime:0.0}!";
        }

        // Wins hurt the boss first
        if (chipsWon > 0)
        {
            bossHealth = Mathf.Max(0, bossHealth - chipsWon);
            message += $"\nYou hit {finalBoss.DisplayName} for {chipsWon} damage!";
            AudioManager.Play(SoundId.BossHurt);

            // Vampire Chips: heal a share of the damage dealt
            int heal = Mathf.RoundToInt(chipsWon * GetUpgradeTotal(UpgradeType.BossLifesteal) / 100f);

            if (heal > 0)
            {
                playerHealth = Mathf.Min(MaxHealth, playerHealth + heal);
                message += $" You heal {heal} HP.";
            }

            // The boss is defeated before it can strike back
            if (bossHealth <= 0)
            {
                messageText.text = message;
                Victory();
                UpdateUI();
                return;
            }
        }

        // Then losses make the boss attack
        if (chipsLost > 0)
        {
            // Lucky Charm: a shield cancels the attack completely
            if (shieldsLeft > 0)
            {
                shieldsLeft--;
                messageText.text = message + $"\nYour shield blocks {finalBoss.DisplayName}'s attack!";
                AudioManager.Play(SoundId.Block);
                UpdateUI();
                return;
            }

            if (bossCombat != null)
            {
                string defenseHint = CanPerfectParry ? "RIGHT-CLICK to parry!"
                    : CanBlock ? "Hold RIGHT-CLICK to block!"
                    : "Brace yourself!";

                float windowBonus = GetUpgradeTotal(UpgradeType.PerfectWindowMs) / 1000f;
                int rewardBonus = GetUpgradeTotal(UpgradeType.ParryRewardPercent);

                isBossAttacking = true;
                messageText.text = message + $"\n{finalBoss.DisplayName} attacks! {defenseHint}";
                bossCombat.StartAttack(chipsLost, finalBoss, CanPerfectParry, CanBlock, windowBonus, rewardBonus, FinishBossAttack);
                UpdateUI();
                return;
            }

            playerHealth = Mathf.Max(0, playerHealth - chipsLost);
            message += $"\n{finalBoss.DisplayName} hits you for {chipsLost} damage!";
        }
        else if (chipsWon == 0)
        {
            message += "\nNo damage this time.";
        }

        messageText.text = message;
        CheckBossFightEnd();
        UpdateUI();
    }

    private void FinishBossAttack(AttackOutcome outcome)
    {
        isBossAttacking = false;

        playerHealth = Mathf.Clamp(playerHealth - outcome.DamageTaken + outcome.HealAmount, 0, MaxHealth);
        bossHealth = Mathf.Max(0, bossHealth - outcome.BossDamage);
        messageText.text = outcome.Message;
        // Bigger hits shake the screen harder
        if (outcome.DamageTaken > 0 && hitShake != null)
        {
            hitShake.GenerateImpulseWithForce(Mathf.Clamp(outcome.DamageTaken / 10f, 0.3f, 2f));
        }
        CheckBossFightEnd();
        UpdateUI();
    }

    private void CheckBossFightEnd()
    {
        if (bossHealth <= 0)
        {
            Victory();
        }
        else if (playerHealth <= 0)
        {
            GameOver();
        }
    }

    public void ShowMessage(string message)
    {
        messageText.text = message;
    }

    public void SpendPlays(int amount)
    {
        playsLeft = Mathf.Max(0, playsLeft - amount);
        UpdateUI();
    }

    public int GetCurrentBet()
    {
        return GetBetForLevel(betLevel);
    }

    private int GetBetForLevel(int level)
    {
        return (betAmount + GetUpgradeTotal(UpgradeType.BetIncrease)) * level;
    }

    private bool CanChangeBet()
    {
        return CanPlay && !IsMinigameBusy;
    }

    private void ChangeBetLevel(int change)
    {
        if (!CanChangeBet())
        {
            return;
        }

        int newLevel = Mathf.Clamp(betLevel + change, 1, MaxBetLevel);

        if (change > 0 && GetBetForLevel(newLevel) > chips)
        {
            return;
        }

        betLevel = newLevel;
        UpdateUI();
    }

    public int GetBossValue(BossEffect effect)
    {
        return currentCurse != null && currentCurse.Effect == effect ? currentCurse.Value : 0;
    }

    public int GetUpgradeTotal(UpgradeType type)
    {
        float total = 0f;
        List<Upgrade> counted = new List<Upgrade>();

        foreach (Upgrade upgrade in ownedUpgrades)
        {
            if (upgrade.Type != type || counted.Contains(upgrade))
            {
                continue;
            }

            counted.Add(upgrade);
            total += GetStackedValue(upgrade, CountOwned(upgrade));
        }

        return Mathf.RoundToInt(total);
    }

    private float GetStackedValue(Upgrade upgrade, int copies)
    {
        if (!upgrade.HasDiminishingStacks)
        {
            return upgrade.Value * copies;
        }

        float total = 0f;
        float copyValue = upgrade.Value;

        for (int i = 0; i < copies; i++)
        {
            total += copyValue;
            copyValue *= stackFalloff;
        }

        return total;
    }

    private void StartRound(int number)
    {
        roundNumber = number;
        currentCurse = PickCurseForRound(number);

        int plays = playsPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses) - GetBossValue(BossEffect.FewerPlays);
        playsLeft = Mathf.Max(1, plays);
        playsUsedThisRound = 0;

        int chipsToEarn = baseRoundGoal + goalIncreasePerRound * (number - 1);
        chipsToEarn = Mathf.RoundToInt(chipsToEarn * (100 + GetBossValue(BossEffect.GoalIncreasePercent)) / 100f);
        targetChips = chips + chipsToEarn;

        if (IsBossFight)
        {
            bossHealth = finalBoss.MaxHealth;
            shieldsLeft = GetUpgradeTotal(UpgradeType.BossShield);
        }
        else
        {
            // Health is only lost in the boss fight, so keep it full (this also applies Tough Skin)
            playerHealth = MaxHealth;
        }

        RoundStarted?.Invoke();
    }

    private BossModifier PickCurseForRound(int number)
    {
        if (!curseRounds.Contains(number) || number == finalRound || cursePool.Count == 0)
        {
            return null;
        }

        return cursePool[Random.Range(0, cursePool.Count)];
    }

    private void ClearRound()
    {
        if (IsFinalRound)
        {
            Victory();
            return;
        }

        lastRoundWasCurse = currentCurse != null;

        int interestPercent = GetUpgradeTotal(UpgradeType.RoundEndInterest);
        int interest = chips * interestPercent / 100;
        chips += interest;

        string clearText = lastRoundWasCurse
            ? $"\nCurse of {currentCurse.DisplayName} lifted! Pick a rare reward."
            : $"\nRound {roundNumber} cleared!";

        if (interest > 0)
        {
            clearText += $" +{interest} interest";
        }

        messageText.text += clearText;
        AudioManager.Play(SoundId.RoundClear);
        ShowUpgradeChoices();
    }

    private void Victory()
    {
        isGameOver = true;
        isVictory = true;
        AudioManager.Play(SoundId.Victory);

        if (finalBoss != null)
        {
            chips += finalBoss.Jackpot;
            messageText.text += $"\n<color=#FFD54F>YOU WIN! {finalBoss.DisplayName} is defeated! Jackpot +{finalBoss.Jackpot} chips!</color>";
        }
        else
        {
            messageText.text += $"\n<color=#FFD54F>YOU WIN! You finished the run with {chips} chips.</color>";
        }

        RecordBook.RecordRunEnd(roundNumber, chips, true);
        restartButton.gameObject.SetActive(true);
    }

    private void GameOver()
    {
        isGameOver = true;
        AudioManager.Play(SoundId.GameOver);

        messageText.text += playerHealth <= 0 && finalBoss != null
            ? $"\nYou were knocked out by {finalBoss.DisplayName}!"
            : $"\nGame over! You reached round {roundNumber} of {finalRound}.";

        RecordBook.RecordRunEnd(roundNumber, chips, false);
        restartButton.gameObject.SetActive(true);
    }

    private void ShowUpgradeChoices()
    {
        List<Upgrade> offered = PickRandomUpgrades(upgradeChoices);

        for (int i = 0; i < upgradeButtons.Length; i++)
        {
            Button button = upgradeButtons[i];

            if (i >= offered.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            Upgrade choice = offered[i];
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text =
                $"<b>{choice.DisplayName}</b>\n<size=70%>{choice.Rarity}</size>\n\n{choice.Description}{BuildStackHint(choice)}";
            button.image.color = GetRarityColor(choice.Rarity);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ChooseUpgrade(choice));
        }

        upgradePanel.SetActive(true);
    }

    private void ChooseUpgrade(Upgrade upgrade)
    {
        ownedUpgrades.Add(upgrade);
        upgradePanel.SetActive(false);

        StartRound(roundNumber + 1);

        messageText.text = $"Got {upgrade.DisplayName}!\n{messageText.text}";
        UpdateUI();
    }

    private List<Upgrade> PickRandomUpgrades(int count)
    {
        // Upgrades the chosen chibi can actually use (and one-time upgrades not owned yet)
        List<Upgrade> allowed = new List<Upgrade>();

        foreach (Upgrade upgrade in upgradePool)
        {
            bool usable = !upgrade.RequiresPerfectParry || CanPerfectParry;
            bool alreadyHave = upgrade.OnlyOnce && CountOwned(upgrade) > 0;

            if (usable && !alreadyHave)
            {
                allowed.Add(upgrade);
            }
        }

        // After a curse round, only Rare and Legendary
        List<Upgrade> candidates = new List<Upgrade>();

        foreach (Upgrade upgrade in allowed)
        {
            if (!lastRoundWasCurse || upgrade.Rarity != UpgradeRarity.Common)
            {
                candidates.Add(upgrade);
            }
        }

        if (candidates.Count == 0)
        {
            candidates.AddRange(allowed);
        }

        List<Upgrade> result = new List<Upgrade>();

        while (result.Count < count && candidates.Count > 0)
        {
            int index = PickWeightedIndex(candidates);
            result.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return result;
    }
    private int PickWeightedIndex(List<Upgrade> candidates)
    {
        int totalWeight = 0;

        foreach (Upgrade upgrade in candidates)
        {
            totalWeight += GetRarityWeight(upgrade.Rarity);
        }

        if (totalWeight <= 0)
        {
            return Random.Range(0, candidates.Count);
        }

        int roll = Random.Range(0, totalWeight);

        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= GetRarityWeight(candidates[i].Rarity);

            if (roll < 0)
            {
                return i;
            }
        }

        return candidates.Count - 1;
    }

    private int GetRarityWeight(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Rare => rareWeight,
            UpgradeRarity.Legendary => legendaryWeight,
            _ => commonWeight
        };
    }

    private Color GetRarityColor(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Rare => rareColor,
            UpgradeRarity.Legendary => legendaryColor,
            _ => commonColor
        };
    }

    private int CountOwned(Upgrade upgrade)
    {
        int count = 0;

        foreach (Upgrade owned in ownedUpgrades)
        {
            if (owned == upgrade)
            {
                count++;
            }
        }

        return count;
    }

    private string BuildOwnedUpgradesText()
    {
        if (ownedUpgrades.Count == 0)
        {
            return "Upgrades:\nNone yet";
        }

        string text = "Upgrades:";
        List<Upgrade> listed = new List<Upgrade>();

        foreach (Upgrade upgrade in ownedUpgrades)
        {
            if (listed.Contains(upgrade))
            {
                continue;
            }

            listed.Add(upgrade);
            int count = CountOwned(upgrade);
            text += count > 1 ? $"\n{upgrade.DisplayName} x{count}" : $"\n{upgrade.DisplayName}";
        }

        return text;
    }

    private string BuildStackHint(Upgrade upgrade)
    {
        int owned = CountOwned(upgrade);

        if (owned == 0)
        {
            return "";
        }

        float nextValue = GetStackedValue(upgrade, owned + 1) - GetStackedValue(upgrade, owned);
        return $"\n<size=70%>Owned x{owned}. Next copy: +{nextValue:0.#}{GetUnitSuffix(upgrade.Type)}</size>";
    }

    private string GetUnitSuffix(UpgradeType type)
    {
        return type switch
        {
            UpgradeType.WinPayoutPercent or UpgradeType.RoundEndInterest
                or UpgradeType.BossLifesteal or UpgradeType.ParryRewardPercent => "%",
            UpgradeType.ExtraGuesses => " plays",
            UpgradeType.ExtraBetLevel => " bet level",
            UpgradeType.MaxHealth => " HP",
            UpgradeType.BossShield => " shield",
            UpgradeType.PerfectWindowMs => " ms",
            _ => " chips"
        };
    }

    // Combat numbers for the Stats panel. Bonuses at 0 are hidden.
    private string BuildCombatStatsText()
    {
        string text = "";

        int bonusHealth = GetUpgradeTotal(UpgradeType.MaxHealth);
        if (bonusHealth > 0)
        {
            text += $"\nMax health: +{bonusHealth}";
        }

        int lifesteal = GetUpgradeTotal(UpgradeType.BossLifesteal);
        if (lifesteal > 0)
        {
            text += $"\nBoss fight lifesteal: {lifesteal}%";
        }

        int shields = IsBossFight ? shieldsLeft : GetUpgradeTotal(UpgradeType.BossShield);
        if (shields > 0)
        {
            text += $"\nShields: {shields}";
        }

        int parryReward = GetUpgradeTotal(UpgradeType.ParryRewardPercent);
        if (parryReward > 0)
        {
            text += $"\nParry rewards: +{parryReward}%";
        }

        int windowBonus = GetUpgradeTotal(UpgradeType.PerfectWindowMs);
        if (windowBonus > 0)
        {
            text += $"\nPerfect window: +{windowBonus} ms";
        }

        return text;
    }

    private int GetNextCurseRound()
    {
        int next = -1;

        foreach (int round in curseRounds)
        {
            if (round >= roundNumber && round < finalRound && (next < 0 || round < next))
            {
                next = round;
            }
        }

        return next;
    }

    private string BuildStatsText()
    {
        string text = "Stats:";

        if (character != null)
        {
            string parryType = CanPerfectParry ? "perfect + block" : CanBlock ? "block only" : "none";
            text += $"\nChibi: {character.DisplayName} (parry: {parryType})";
        }

        text += $"\nBet: {GetCurrentBet()} (x{betLevel} of x{MaxBetLevel})";
        text += $"\nPlays per round: {playsPerRound + GetUpgradeTotal(UpgradeType.ExtraGuesses)}";

        int winPercent = GetUpgradeTotal(UpgradeType.WinPayoutPercent);
        if (winPercent > 0)
        {
            text += $"\nWin bonus: +{winPercent}%";
        }

        int flatWinBonus = GetUpgradeTotal(UpgradeType.BonusChipsPerWin);
        if (flatWinBonus > 0)
        {
            text += $"\nExtra chips per win: +{flatWinBonus}";
        }

        int lossReduction = GetUpgradeTotal(UpgradeType.LossReduction);
        if (lossReduction > 0)
        {
            text += $"\nLoss reduction: -{lossReduction} chips";
        }

        int tieReward = GetUpgradeTotal(UpgradeType.TieReward);
        if (tieReward > 0)
        {
            text += $"\nHigh-Low tie reward: +{tieReward} chips";
        }

        int interest = GetUpgradeTotal(UpgradeType.RoundEndInterest);
        if (interest > 0)
        {
            text += $"\nRound interest: {interest}%";
        }

        if (GetUpgradeTotal(UpgradeType.BlackjackAfterSplit) > 0)
        {
            text += "\nBlackjack: 21 after a split pays as blackjack";
        }

        text += BuildCombatStatsText();

        if (IsBossFight)
        {
            text += "\n<color=#FFD54F>Final boss fight!</color>";
        }
        else if (IsCurseRound)
        {
            text += "\n<color=#FF6B6B>Curse round now!</color>";
        }
        else if (roundNumber > 0)
        {
            int nextCurse = GetNextCurseRound();

            if (nextCurse > 0 && cursePool.Count > 0)
            {
                text += $"\nNext curse: round {nextCurse}";
            }

            if (finalBoss != null && IsBossRevealed)
            {
                text += $"\nFinal boss: {finalBoss.DisplayName} ({FinalBoss.GetGameName(finalBoss.FavoriteGame)}) at round {finalRound}";
            }
            else
            {
                text += $"\nFinal boss: round {finalRound} (revealed at round {revealBossFromRound})";
            }
        }

        return text;
    }

    private void UpdateUI()
    {
        while (betLevel > 1 && GetCurrentBet() > chips)
        {
            betLevel--;
        }

        RefreshChipsText();
        healthText.text = $"HP: {playerHealth}/{MaxHealth}";

        if (IsBossFight)
        {
            roundText.text = $"Round {roundNumber}/{finalRound}  (FINAL BOSS)  |  Boss HP: {bossHealth}/{finalBoss.MaxHealth}";
        }
        else if (roundNumber > 0)
        {
            string roundTag = IsCurseRound ? "  (CURSE)" : "";
            roundText.text = $"Round {roundNumber}/{finalRound}{roundTag}  |  Goal: {targetChips}  |  Plays: {playsLeft}";
        }
        else
        {
            roundText.text = "Choosing a chibi...";
        }

        ownedUpgradesText.text = BuildOwnedUpgradesText();
        statsText.text = BuildStatsText();

        bossText.gameObject.SetActive(IsCurseRound || IsFinalRound);

        if (IsBossFight)
        {
            string overtimeLine = OvertimeMultiplier > 1f
                ? $"<color=#FF6B6B>OVERTIME x{OvertimeMultiplier:0.0}: all damage increased</color>"
                : $"Overtime starts on play {overtimeStartPlay}";

            bossText.text = $"<color=#FFD54F>FINAL BOSS: {finalBoss.DisplayName}</color>   HP {bossHealth}/{finalBoss.MaxHealth}\n"
                + $"<size=80%>Wins deal damage. Losses hurt you instead of costing chips.\n{overtimeLine}</size>";
        }
        else if (IsFinalRound)
        {
            bossText.text = "<color=#FFD54F>FINAL ROUND</color>\n<size=80%>Clear this round to win the run!</size>";
        }
        else if (IsCurseRound)
        {
            bossText.text = $"CURSE: {currentCurse.DisplayName}\n<size=80%>{currentCurse.Description}</size>";
        }

        restartButtonText.text = isVictory ? "PLAY AGAIN" : "RESTART";

        betDownButton.interactable = CanChangeBet() && betLevel > 1;
        betUpButton.interactable = CanChangeBet()
            && betLevel < MaxBetLevel
            && GetBetForLevel(betLevel + 1) <= chips;

        StateChanged?.Invoke();
    }
}