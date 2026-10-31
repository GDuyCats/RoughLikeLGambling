using UnityEngine;

// Chooses which song plays: relaxed casino music, tense music for the final boss fight,
// and silence once the run is over (so the win or lose jingle can be heard).
public class MusicDirector : MonoBehaviour
{
    [SerializeField] private RunManager run;
    [SerializeField] private AudioClip casinoMusic;
    [SerializeField] private AudioClip bossMusic;

    private void Update()
    {
        AudioClip wanted;

        if (run.IsGameOver)
        {
            wanted = null;
        }
        else if (run.IsBossFight)
        {
            wanted = bossMusic;
        }
        else
        {
            wanted = casinoMusic;
        }

        AudioManager.PlayMusic(wanted);
    }
}