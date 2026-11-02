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
        public float width;   // 0 = the normal width (Readable Sign Size). Narrower where the table is busy (Blackjack).
    }

    [SerializeField] private TMP_Text screenMessage;          // The message on the screen that every minigame writes to
    [SerializeField] private CanvasGroup screenMessageGroup;  // Used to hide the screen message while at a table
    [SerializeField] private TableSign[] tables;

    [Header("Readable Signs")]
    [SerializeField] private Vector2 readableSignSize = new Vector2(20f, 1.8f);   // Wide and tall, so long results stay big
    [SerializeField] private float minFontSize = 3f;                             // Long messages never shrink below this
    [SerializeField] private float maxFontSize = 5f;
    [SerializeField, Range(50, 100)] private int detailLinePercent = 80; // Lines after the first are a little smaller
    [SerializeField, Range(0f, 0.5f)] private float outline = 0.25f;

    private string lastMessage;
    private string lastFormatted = "";

    // Make every sign easy to read: bigger, never tiny, with a dark outline around the letters
    private void Awake()
    {
        foreach (TableSign table in tables)
        {
            if (table.sign == null)
            {
                continue;
            }

            float width = table.width > 0f ? table.width : readableSignSize.x;
            table.sign.rectTransform.sizeDelta = new Vector2(width, readableSignSize.y);
            table.sign.enableAutoSizing = true;
            table.sign.fontSizeMin = minFontSize;
            table.sign.fontSizeMax = maxFontSize;
            table.sign.fontStyle |= FontStyles.Bold;
            table.sign.outlineWidth = outline;
            table.sign.outlineColor = new Color32(20, 20, 30, 255);

            // The sign leans back; lift it so its lower lines don't sink into the felt
            Vector3 position = table.sign.transform.localPosition;
            float lean = table.sign.transform.localEulerAngles.x * Mathf.Deg2Rad;
            position.y = readableSignSize.y * 0.5f * Mathf.Cos(lean) + 0.05f;
            table.sign.transform.localPosition = position;
        }
    }

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
            if (screenMessage.text != lastMessage)
            {
                lastMessage = screenMessage.text;
                lastFormatted = FormatForSign(lastMessage);
            }

            activeTable.sign.text = lastFormatted;
        }

        // At a table: the message is on the felt. On the casino floor: on the screen.
        screenMessageGroup.alpha = activeTable != null ? 0f : 1f;
    }

    // The first line is the headline; the lines after it are details, printed a little smaller
    private string FormatForSign(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        int firstBreak = message.IndexOf('\n');

        if (firstBreak < 0)
        {
            return message;
        }

        return message.Substring(0, firstBreak) + $"\n<size={detailLinePercent}%>" + message.Substring(firstBreak + 1) + "</size>";
    }
}