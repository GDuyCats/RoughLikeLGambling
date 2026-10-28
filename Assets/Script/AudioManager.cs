using System;
using System.Collections.Generic;
using UnityEngine;
// Both System and UnityEngine have a "Random", so we tell C# to use Unity's one
using Random = UnityEngine.Random;

// Every sound the game can play.
// IMPORTANT: only add new sounds at the END of this list (Unity saves them as numbers).
public enum SoundId
{
    ButtonPress,
    CardDeal,
    CardFlip,
    DiceThrow,
    DiceHit,
    DealerKick,
    ChipHit,
    ChipPickup,
    ChipThrow,
    Win,
    Lose,
    Blackjack,
    RoundClear,
    GameOver,
    Victory,
    Jump,
    Smash,
    Squash,
    Dizzy,
    Knockback,
    TableHop,
    BossWindup,
    BossSlam,
    BossSweep,
    BossFlick,
    PerfectParry,
    Block,
    PlayerHit,
    BossParry,
    BossHurt
}

// Plays sound effects from anywhere with one line: AudioManager.Play(SoundId.CardFlip);
// Each sound can have several versions; one is picked at random with a slightly different pitch,
// so sounds heard hundreds of times don't get boring.
public class AudioManager : MonoBehaviour
{
    // One sound: its versions and how it should be played
    [Serializable]
    private class SoundSet
    {
        public SoundId id;
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 0.5f)] public float pitchVariation = 0.08f;   // 0.08 = up to 8% higher or lower
        public float minInterval = 0.05f;                         // Stops the same sound stacking up when many things hit at once
    }

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float effectsVolume = 1f;

    [Header("Sounds")]
    [SerializeField] private int voices = 16;   // How many sounds can play at the same time
    [SerializeField] private List<SoundSet> sounds = new List<SoundSet>();

    private readonly Dictionary<SoundId, SoundSet> lookup = new Dictionary<SoundId, SoundSet>();
    private readonly Dictionary<SoundId, float> lastPlayed = new Dictionary<SoundId, float>();
    private AudioSource[] sources;
    private int nextSource;

    // The one AudioManager in the scene, so any script can reach it
    public static AudioManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        foreach (SoundSet set in sounds)
        {
            lookup[set.id] = set;
        }

        // A few speakers that take turns, so several sounds can overlap
        sources = new AudioSource[voices];

        for (int i = 0; i < voices; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // Plain stereo sound, not placed in 3D
            sources[i] = source;
        }
    }

    // volumeScale lets a caller play a sound softer, for example a gentle bump
    public static void Play(SoundId id, float volumeScale = 1f)
    {
        if (Instance != null)
        {
            Instance.PlaySound(id, volumeScale);
        }
    }

    private void PlaySound(SoundId id, float volumeScale)
    {
        if (!lookup.TryGetValue(id, out SoundSet set) || set.clips == null || set.clips.Length == 0)
        {
            return;
        }

        if (lastPlayed.TryGetValue(id, out float lastTime) && Time.time - lastTime < set.minInterval)
        {
            return;
        }

        AudioClip clip = set.clips[Random.Range(0, set.clips.Length)];

        if (clip == null)
        {
            return;
        }

        lastPlayed[id] = Time.time;

        AudioSource source = sources[nextSource];
        nextSource = (nextSource + 1) % sources.Length;

        source.pitch = 1f + Random.Range(-set.pitchVariation, set.pitchVariation);
        source.PlayOneShot(clip, set.volume * volumeScale * effectsVolume * masterVolume);
    }
}