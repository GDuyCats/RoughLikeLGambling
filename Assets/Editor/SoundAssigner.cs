using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Used by Tools > Rearrange Tables. Puts the new sound files into the AudioManager's sound list,
// found by part of their file name anywhere in the project:
// - taking a playing card: every card dealt (Card Deal),
// - placing poker chips: chips going out or coming in, and R stacking the pile (Chips Move),
// - shuffling a deck of cards: the shoe being shuffled (Shuffle),
// - dice: the dice being thrown (Dice Throw).
// Safe to run again: each sound is set to the same file.
public static class SoundAssigner
{
    private class NewSound
    {
        public SoundId Id;
        public string FileName;
        public float Volume;
        public float PitchVariation;
        public float MinInterval;
    }

    private static readonly NewSound[] Sounds =
    {
        new NewSound { Id = SoundId.CardDeal, FileName = "taking-playing-card", Volume = 0.8f, PitchVariation = 0.08f, MinInterval = 0.08f },
        new NewSound { Id = SoundId.ChipsMove, FileName = "placing-poker-chips", Volume = 0.8f, PitchVariation = 0.06f, MinInterval = 0.2f },
        new NewSound { Id = SoundId.Shuffle, FileName = "shuffling-deck-of-cards", Volume = 1f, PitchVariation = 0f, MinInterval = 1.2f },
        new NewSound { Id = SoundId.DiceThrow, FileName = "dice-142528", Volume = 1f, PitchVariation = 0.05f, MinInterval = 0.1f }
    };

    public static void Assign(List<string> report)
    {
        AudioManager audio = Object.FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);

        if (audio == null)
        {
            report.Add("Sounds: no AudioManager in the scene, so the new sounds were not added.");
            return;
        }

        SerializedObject fields = new SerializedObject(audio);
        SerializedProperty list = fields.FindProperty("sounds");
        List<string> done = new List<string>();
        List<string> missing = new List<string>();

        foreach (NewSound sound in Sounds)
        {
            AudioClip clip = FindClip(sound.FileName);

            if (clip == null)
            {
                missing.Add(sound.FileName);
                continue;
            }

            SerializedProperty entry = FindEntry(list, sound.Id);

            if (entry == null)
            {
                list.arraySize++;
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("id").enumValueIndex = (int)sound.Id;
            }

            SerializedProperty clips = entry.FindPropertyRelative("clips");
            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = clip;
            entry.FindPropertyRelative("volume").floatValue = sound.Volume;
            entry.FindPropertyRelative("pitchVariation").floatValue = sound.PitchVariation;
            entry.FindPropertyRelative("minInterval").floatValue = sound.MinInterval;
            done.Add($"{sound.Id} = {clip.name}");
        }

        fields.ApplyModifiedProperties();

        if (done.Count > 0)
        {
            report.Add("Sounds: " + string.Join(", ", done) + ".");
        }

        if (missing.Count > 0)
        {
            report.Add("Sounds not found (check the file names): " + string.Join(", ", missing) + ".");
        }
    }

    private static SerializedProperty FindEntry(SerializedProperty list, SoundId id)
    {
        for (int i = 0; i < list.arraySize; i++)
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(i);

            if (entry.FindPropertyRelative("id").enumValueIndex == (int)id)
            {
                return entry;
            }
        }

        return null;
    }

    private static AudioClip FindClip(string namePart)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.ToLowerInvariant().Contains(namePart))
            {
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }

        return null;
    }
}
