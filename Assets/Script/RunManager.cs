using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Manages everything shared by all minigames: chips, bets, rounds, curses, health, the final boss
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
    [SerializeField] private int revealBossFromRound = 7;   // From this round, the player knows who the final boss is
    [SerializeField] private int playerMaxHealth = 100;
    [SerializeField] private BossCombat bossCombat;
    [Header("Rules")]
    [SerializeField] private int startingChips = 100;
    [SerializeField] private int betAmount = 10;
    [SerializeField] private int startingMaxBetLevel = 3;
    [SerializeField] private int playsPerRound = 12;
    [SerializeField] private int baseRoundGoal = 20;
    [SerializeField] private int goalIncreasePerRound = 10;
    [SerializeField] private int upgradeChoices = 3;
    [SerializeField, Range(0f, 1f)] private float stackFalloff = 0.5f;

    public event Action RunStarted;
    public event Action StateChanged;
    public event Action RoundStarted;

    private readonly List<Upgrade> ownedUpgrades = new List<Upgrade>();

    private TMP_Text restartButtonText;

    private int chips;
    private int roundNumber;
    private int targetChips;
    private int playsLeft;
    private int playsUsedThisRound;
    private int betLevel = 1;
    private bool isGameOver;
    private bool isVictory;

    private BossModifier currentCurse;
    private bool lastRoundWasCurse;

    private FinalBoss finalBoss;   // Chosen at the start of each run
    private int playerHealth;
    private int bossHealth;
    private bool isBossAttacking;
    public bool CanPlay => !isGameOver && !upgradePanel.activeSelf && !isBossAttacking;
    public int PlaysLeft => playsLeft;
    public int RoundNumber => roundNumber;
    public int Chips => chips;
    public int BetLevel => betLevel;
    public int MaxBetLevel => startingMaxBetLevel + GetUpgradeTotal(UpgradeType.ExtraBetLevel);
    public bool HasPlayedThisRound => playsUsedThisRound > 0;
    public bool IsCurseRound => currentCurse != null;
    public bool IsFinalRound => roundNumber == finalRound;
    public FinalBoss FinalBoss => finalBoss;
    public bool IsBossFight => IsFinalRound && finalBoss != null;
    public bool IsBossRevealed => roundNumber >= revealBossFromRound;

    public bool IsMinigameBusy { get; private set; }

    private void Start()
    {
        restartButtonText = restartButton.GetComponentInChildren<TMP_Text>();

        restartButton.onClick.AddListener(StartNewRun);
        betDownButton.onClick.AddListener(() => ChangeBetLevel(-1));
        betUpButton.onClick.AddListener(() => ChangeBetLevel(1));

        StartNewRun();
    }

    public void StartNewRun()
    {
        chips = startingChips;
        playerHealth = playerMaxHealth;
        ownedUpgrades.Clear();
        betLevel = 1;
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
        RunStarted?.Invoke();
        StartRound(1);
        // RunStarted?.Invoke();
        UpdateUI();
    }

    public void SetMinigameBusy(bool busy)
    {
        IsMinigameBusy = busy;
        UpdateUI();
    }

    public void FinishPlay(int chipChange, string message)
    {
        if (!CanPlay)
        {
            return;
        }

        if (IsBossFight)
        {
            HandleBossPlay(chipChange, message);
            return;
        }

        chips += chipChange;
        playsLeft--;
        playsUsedThisRound++;
        messageText.text = message;

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

    // In the boss fight, wins damage the boss and losses damage the player instead of costing chips
    // In the boss fight, wins damage the boss and losses make the boss attack
    private void HandleBossPlay(int chipChange, string message)
    {
        playsUsedThisRound++;

        // A lost hand: the boss attacks, and the player can try to parry
        if (chipChange < 0 && bossCombat != null)
        {
            isBossAttacking = true;
            messageText.text = message + $"\n{finalBoss.DisplayName} attacks! RIGHT-CLICK to parry!";
            bossCombat.StartAttack(-chipChange, FinishBossAttack);
            UpdateUI();
            return;
        }

        if (chipChange > 0)
        {
            bossHealth = Mathf.Max(0, bossHealth - chipChange);
            message += $"\nYou hit {finalBoss.DisplayName} for {chipChange} damage!";
        }
        else if (chipChange < 0)
        {
            // Only used if no BossCombat is assigned
            int damage = -chipChange;
            playerHealth = Mathf.Max(0, playerHealth - damage);
            message += $"\n{finalBoss.DisplayName} hits you for {damage} damage!";
        }
        else
        {
            message += "\nNo damage this time.";
        }

        messageText.text = message;
        CheckBossFightEnd();
        UpdateUI();
    }

    // Called by BossCombat when the boss's attack is over
    private void FinishBossAttack(AttackOutcome outcome)
    {
        isBossAttacking = false;

        // Healing can never go above max health
        playerHealth = Mathf.Clamp(playerHealth - outcome.DamageTaken + outcome.HealAmount, 0, playerMaxHealth);
        bossHealth = Mathf.Max(0, bossHealth - outcome.BossDamage);
        messageText.text = outcome.Message;

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
        ShowUpgradeChoices();
    }

    private void Victory()
    {
        isGameOver = true;
        isVictory = true;

        if (finalBoss != null)
        {
            chips += finalBoss.Jackpot;
            messageText.text += $"\n<color=#FFD54F>YOU WIN! {finalBoss.DisplayName} is defeated! Jackpot +{finalBoss.Jackpot} chips!</color>";
        }
        else
        {
            messageText.text += $"\n<color=#FFD54F>YOU WIN! You finished the run with {chips} chips.</color>";
        }

        restartButton.gameObject.SetActive(true);
    }

    private void GameOver()
    {
        isGameOver = true;

        messageText.text += playerHealth <= 0
            ? $"\nYou were knocked out by {finalBoss.DisplayName}!"
            : $"\nGame over! You reached round {roundNumber} of {finalRound}.";

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
        List<Upgrade> candidates = new List<Upgrade>();

        foreach (Upgrade upgrade in upgradePool)
        {
            if (!lastRoundWasCurse || upgrade.Rarity != UpgradeRarity.Common)
            {
                candidates.Add(upgrade);
            }
        }

        if (candidates.Count == 0)
        {
            candidates.AddRange(upgradePool);
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
            UpgradeType.WinPayoutPercent or UpgradeType.RoundEndInterest => "%",
            UpgradeType.ExtraGuesses => " plays",
            UpgradeType.ExtraBetLevel => " bet level",
            _ => " chips"
        };
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

        if (IsBossFight)
        {
            text += "\n<color=#FFD54F>Final boss fight!</color>";
        }
        else if (IsCurseRound)
        {
            text += "\n<color=#FF6B6B>Curse round now!</color>";
        }
        else
        {
            int nextCurse = GetNextCurseRound();

            if (nextCurse > 0 && cursePool.Count > 0)
            {
                text += $"\nNext curse: round {nextCurse}";
            }

            // Reveal the final boss and its favorite game a few rounds early
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

        chipsText.text = $"Chips: {chips}   Bet: {GetCurrentBet()} (x{betLevel})";
        healthText.text = $"HP: {playerHealth}/{playerMaxHealth}";

        if (IsBossFight)
        {
            roundText.text = $"Round {roundNumber}/{finalRound}  (FINAL BOSS)  |  Boss HP: {bossHealth}/{finalBoss.MaxHealth}";
        }
        else
        {
            string roundTag = IsCurseRound ? "  (CURSE)" : "";
            roundText.text = $"Round {roundNumber}/{finalRound}{roundTag}  |  Goal: {targetChips}  |  Plays: {playsLeft}";
        }

        ownedUpgradesText.text = BuildOwnedUpgradesText();
        statsText.text = BuildStatsText();

        bossText.gameObject.SetActive(IsCurseRound || IsFinalRound);

        if (IsBossFight)
        {
            bossText.text = $"<color=#FFD54F>FINAL BOSS: {finalBoss.DisplayName}</color>   HP {bossHealth}/{finalBoss.MaxHealth}\n"
                + "<size=80%>Wins deal damage. Losses hurt you instead of costing chips.</size>";
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