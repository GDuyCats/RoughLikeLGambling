using System;
using TMPro;
using UnityEngine;

// Shows the game's message on the table the chibi is playing at, printed on the felt.
// Away from the tables (on the casino floor) the message stays on the screen as before.
// The minigames keep writing to the screen message; this script just copies it onto the table.
public class TableMessageMirror : MonoBehaviour
{
    // One table: its minigame panel (open while playing there) and the text printed on its felt
    [Serializable]
    private class TableSign
    {
        public GameObject minigamePanel;
        public TMP_Text sign;
    }

    [SerializeField] private TMP_Text screenMessage;          // The message on the screen that every minigame writes to
    [SerializeField] private CanvasGroup screenMessageGroup;  // Used to hide the screen message while at a table
    [SerializeField] private TableSign[] tables;

    // LateUpdate runs after every Update, so the minigames have already written this frame's message
    private void LateUpdate()
    {
        TableSign activeTable = null;

        foreach (TableSign table in tables)
        {
            bool isOpen = table.minigamePanel.activeInHierarchy;

            if (table.sign.gameObject.activeSelf != isOpen)
            {
                table.sign.gameObject.SetActive(isOpen);
            }

            if (isOpen)
            {
                activeTable = table;
            }
        }

        if (activeTable != null)
        {
            activeTable.sign.text = screenMessage.text;
        }

        // At a table: the message is on the felt. On the casino floor: on the screen.
        screenMessageGroup.alpha = activeTable != null ? 0f : 1f;
    }
}