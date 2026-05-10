using System;
using System.Collections;
using System.Collections.Generic;
using CubeFly.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CubeFly.HangarSelect
{
    // Builds the hangar slot picker UI on Awake: title + 3 slot cards
    // (each empty or filled) + a Cancel button. Reads save metadata
    // from SaveManager and routes the player into BuildScene with the
    // chosen slot armed in GameData. The persistent UICanvas is
    // hidden while this scene is active (UIManager.OnSceneStateChanged).
    public class HangarSelectController : MonoBehaviour
    {
        [Header("Registries (needed when continuing a saved construct)")]
        [SerializeField] ShapeRegistry shapeRegistry;
        [SerializeField] MaterialRegistry materialRegistry;

        [Header("Card sizing")]
        [SerializeField] Vector2 cardSize = new Vector2(420f, 300f);
        [SerializeField] float cardSpacing = 40f;
        [SerializeField] float cardYOffset = 0f;

        [Header("Inline-confirm delete")]
        [SerializeField] float deleteConfirmTimeoutSeconds = 5f;

        const string TAG = "HangarSelect";
        const string MainMenuSceneName = "MainMenu";
        const string BuildSceneName = "BuildScene";

        // Per-card state. Built once in Awake; rebuilt cosmetically
        // when a slot's data changes (delete).
        class SlotCard
        {
            public int Slot;
            public RectTransform Root;
            public Text TitleLabel;
            public Text BodyLabel;          // shows stats / empty hint
            public Button PrimaryButton;    // "Continue" or "Start new"
            public Text PrimaryLabel;
            public Button DeleteButton;     // null when slot is empty
            public Text DeleteLabel;        // "Delete" / "Yes, delete"
            public Button DeleteCancelButton; // shown only during confirm
            public Text DeleteCancelLabel;
            public bool IsEmpty;
            public Coroutine ConfirmTimeoutRoutine;
            public bool DeleteConfirming;
        }

        readonly List<SlotCard> _cards = new List<SlotCard>(SaveManager.SlotCount);

        void Awake()
        {
            UIStyle.EnsureEventSystem();
            BuildUI();
            RefreshAllCards();
            Debug.unityLogger.Log(TAG, "Hangar slot picker initialised.");
        }

        void Update()
        {
            // Keyboard shortcuts: 1/2/3 trigger the active card's
            // primary action; Esc cancels. Shift is reserved (no
            // shape/material context here).
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            Key[] digitKeys = { Key.Digit1, Key.Digit2, Key.Digit3 };
            int max = Mathf.Min(_cards.Count, digitKeys.Length);
            for (int i = 0; i < max; i++)
            {
                if (kb[digitKeys[i]].wasPressedThisFrame)
                {
                    ActivateSlot(i);
                    break;
                }
            }
            // PauseMenu doesn't open in this scene, but if it ever
            // did (a future feature might add a pause-on-loading
            // screen), defer ESC to it on its toggle frame.
            if (kb.escapeKey.wasPressedThisFrame
                && (PauseMenu.Instance == null || !PauseMenu.Instance.EscConsumedThisFrame))
                OnCancel();
        }

        // ---------- UI construction ----------

        void BuildUI()
        {
            Canvas canvas = UIStyle.BuildScreenSpaceCanvas("HangarSelectCanvas", sortingOrder: 200);
            RectTransform root = (RectTransform)canvas.transform;

            // Title — centred above the cards.
            Text title = UIStyle.BuildLabel(root, "Choose a Slot", fontSize: 72, style: FontStyle.Bold);
            RectTransform trt = (RectTransform)title.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(900f, 120f);
            trt.anchoredPosition = new Vector2(0f, cardSize.y / 2f + 100f + cardYOffset);

            // Three cards stacked horizontally.
            float totalWidth = SaveManager.SlotCount * cardSize.x
                + Mathf.Max(0, SaveManager.SlotCount - 1) * cardSpacing;
            float startX = -totalWidth / 2f + cardSize.x / 2f;
            for (int i = 0; i < SaveManager.SlotCount; i++)
            {
                SlotCard card = BuildCard(root, i);
                RectTransform crt = card.Root;
                crt.anchoredPosition = new Vector2(startX + i * (cardSize.x + cardSpacing), cardYOffset);
                _cards.Add(card);
            }

            // Cancel button — bottom-centre.
            (Button cancelButton, Text _) = UIStyle.BuildLabeledButton(
                root, "Cancel", new Vector2(220f, 64f), fontSize: 28);
            RectTransform crtc = (RectTransform)cancelButton.transform;
            crtc.anchorMin = crtc.anchorMax = crtc.pivot = new Vector2(0.5f, 0.5f);
            crtc.anchoredPosition = new Vector2(0f, -(cardSize.y / 2f + 80f) + cardYOffset);
            cancelButton.onClick.AddListener(OnCancel);
        }

        SlotCard BuildCard(RectTransform parent, int slot)
        {
            SlotCard card = new SlotCard { Slot = slot };

            // Card root — a tinted Image so the card outline reads cleanly.
            GameObject rootGO = new GameObject($"SlotCard{slot}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rootGO.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)rootGO.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = cardSize;
            Image bg = rootGO.GetComponent<Image>();
            bg.color = new Color(0.10f, 0.10f, 0.14f, 0.92f);
            bg.raycastTarget = false; // clicks pass through to inner buttons
            card.Root = rt;

            // Title (top of card).
            Text title = UIStyle.BuildLabel(rt, $"Slot {slot + 1}", fontSize: 32, style: FontStyle.Bold);
            RectTransform titleRT = (RectTransform)title.transform;
            titleRT.anchorMin = titleRT.anchorMax = titleRT.pivot = new Vector2(0.5f, 1f);
            titleRT.sizeDelta = new Vector2(cardSize.x - 32f, 48f);
            titleRT.anchoredPosition = new Vector2(0f, -16f);
            card.TitleLabel = title;

            // Body label — multi-line stats / empty hint.
            Text body = UIStyle.BuildLabel(rt, string.Empty, fontSize: 22);
            body.alignment = TextAnchor.UpperCenter;
            RectTransform bodyRT = (RectTransform)body.transform;
            bodyRT.anchorMin = bodyRT.anchorMax = bodyRT.pivot = new Vector2(0.5f, 1f);
            bodyRT.sizeDelta = new Vector2(cardSize.x - 32f, 130f);
            bodyRT.anchoredPosition = new Vector2(0f, -78f);
            card.BodyLabel = body;

            // Primary button — "Start new" / "Continue".
            (Button primary, Text primaryLabel) = UIStyle.BuildLabeledButton(
                rt, "—", new Vector2(cardSize.x - 80f, 56f), fontSize: 26);
            RectTransform prt = (RectTransform)primary.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
            prt.anchoredPosition = new Vector2(0f, 84f);
            primary.onClick.AddListener(() => ActivateSlot(slot));
            card.PrimaryButton = primary;
            card.PrimaryLabel = primaryLabel;

            // Delete button (initially hidden; revealed when slot has data).
            (Button del, Text delLabel) = UIStyle.BuildLabeledButton(
                rt, "Delete", new Vector2((cardSize.x - 80f) / 2f - 4f, 44f), fontSize: 20);
            RectTransform delRT = (RectTransform)del.transform;
            delRT.anchorMin = delRT.anchorMax = delRT.pivot = new Vector2(0.5f, 0f);
            delRT.anchoredPosition = new Vector2(-(cardSize.x - 80f) / 4f - 2f, 24f);
            del.onClick.AddListener(() => OnDeletePressed(slot));
            del.gameObject.SetActive(false);
            card.DeleteButton = del;
            card.DeleteLabel = delLabel;

            // Inline-confirm cancel button — shown only during the confirm window.
            (Button delCancel, Text delCancelLabel) = UIStyle.BuildLabeledButton(
                rt, "Cancel", new Vector2((cardSize.x - 80f) / 2f - 4f, 44f), fontSize: 20);
            RectTransform delCancelRT = (RectTransform)delCancel.transform;
            delCancelRT.anchorMin = delCancelRT.anchorMax = delCancelRT.pivot = new Vector2(0.5f, 0f);
            delCancelRT.anchoredPosition = new Vector2((cardSize.x - 80f) / 4f + 2f, 24f);
            delCancel.onClick.AddListener(() => OnDeleteCancel(slot));
            delCancel.gameObject.SetActive(false);
            card.DeleteCancelButton = delCancel;
            card.DeleteCancelLabel = delCancelLabel;

            return card;
        }

        // ---------- Card rendering ----------

        void RefreshAllCards()
        {
            SaveSlotInfo[] infos = SaveManager.ReadAllSlotMetadata();
            for (int i = 0; i < _cards.Count && i < infos.Length; i++)
                ApplySlotInfo(_cards[i], infos[i]);
        }

        void RefreshCard(int slot)
        {
            if (slot < 0 || slot >= _cards.Count) return;
            SaveSlotInfo info = SaveManager.TryLoad(slot, out ConstructSave save)
                ? SaveSlotInfo.From(slot, save)
                : SaveSlotInfo.Empty(slot);
            ApplySlotInfo(_cards[slot], info);
        }

        void ApplySlotInfo(SlotCard card, SaveSlotInfo info)
        {
            CancelDeleteConfirm(card);

            card.IsEmpty = info.IsEmpty;
            card.TitleLabel.text = info.Name;

            if (info.IsEmpty)
            {
                card.BodyLabel.text = "<empty>";
                card.PrimaryLabel.text = "Start new construct";
                card.DeleteButton.gameObject.SetActive(false);
            }
            else
            {
                string when = info.ModifiedUtc == default
                    ? "Last edited unknown"
                    : $"Last edited {FormatRelative(info.ModifiedUtc)}";
                card.BodyLabel.text =
                    $"{info.CubeCount} cube{(info.CubeCount == 1 ? string.Empty : "s")}  ·  Mass {info.TotalMass:F1}\n" +
                    $"HP {info.TotalHealthPoints:F0}\n" +
                    $"{when}";
                card.PrimaryLabel.text = "Continue";
                card.DeleteButton.gameObject.SetActive(true);
            }
        }

        // ---------- Slot actions ----------

        void ActivateSlot(int slot)
        {
            if (slot < 0 || slot >= _cards.Count) return;
            SlotCard card = _cards[slot];

            // Activating a card aborts an in-progress delete confirm.
            CancelDeleteConfirm(card);

            GameData.SetActiveSlot(slot);

            if (card.IsEmpty)
            {
                GameData.Clear();
                Debug.unityLogger.Log(TAG, $"Slot {slot}: starting fresh construct.");
            }
            else
            {
                if (SaveManager.TryLoad(slot, out ConstructSave save))
                {
                    GameData.LoadFromSave(save, shapeRegistry, materialRegistry);
                    Debug.unityLogger.Log(TAG,
                        $"Slot {slot}: loaded '{save.slotName}' with {save.placements?.Length ?? 0} placement(s).");
                }
                else
                {
                    // The card thought it was filled but the file is now
                    // missing / unreadable. Recover by treating as fresh.
                    Debug.unityLogger.LogWarning(TAG,
                        $"Slot {slot}: load failed at activation, starting fresh.");
                    GameData.Clear();
                }
            }

            SceneManager.LoadScene(BuildSceneName);
        }

        void OnCancel()
        {
            Debug.unityLogger.Log(TAG, "Cancelled — returning to MainMenu.");
            SceneManager.LoadScene(MainMenuSceneName);
        }

        // ---------- Inline-confirm delete ----------

        void OnDeletePressed(int slot)
        {
            if (slot < 0 || slot >= _cards.Count) return;
            SlotCard card = _cards[slot];

            if (!card.DeleteConfirming)
            {
                EnterDeleteConfirm(card);
                return;
            }
            // Second click on the (now-labelled "Yes, delete") button: commit.
            CommitDelete(slot);
        }

        void EnterDeleteConfirm(SlotCard card)
        {
            card.DeleteConfirming = true;
            card.DeleteLabel.text = "Yes, delete";
            card.DeleteCancelButton.gameObject.SetActive(true);
            // Auto-cancel after the timeout to avoid a stuck confirm state.
            if (card.ConfirmTimeoutRoutine != null) StopCoroutine(card.ConfirmTimeoutRoutine);
            card.ConfirmTimeoutRoutine = StartCoroutine(DeleteConfirmTimeout(card));
        }

        IEnumerator DeleteConfirmTimeout(SlotCard card)
        {
            yield return new WaitForSeconds(deleteConfirmTimeoutSeconds);
            CancelDeleteConfirm(card);
        }

        void CancelDeleteConfirm(SlotCard card)
        {
            if (card.ConfirmTimeoutRoutine != null)
            {
                StopCoroutine(card.ConfirmTimeoutRoutine);
                card.ConfirmTimeoutRoutine = null;
            }
            card.DeleteConfirming = false;
            card.DeleteLabel.text = "Delete";
            card.DeleteCancelButton.gameObject.SetActive(false);
        }

        void OnDeleteCancel(int slot)
        {
            if (slot < 0 || slot >= _cards.Count) return;
            CancelDeleteConfirm(_cards[slot]);
        }

        void CommitDelete(int slot)
        {
            SaveManager.Delete(slot);
            // Refresh from disk — the card flips back to its empty state.
            RefreshCard(slot);
            Debug.unityLogger.Log(TAG, $"Slot {slot}: deleted.");
        }

        // ---------- Helpers ----------

        static string FormatRelative(DateTime utc)
        {
            TimeSpan diff = DateTime.UtcNow - utc;
            if (diff.TotalSeconds < 60)  return "just now";
            if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes} min ago";
            if (diff.TotalHours < 24)    return $"{(int)diff.TotalHours} h ago";
            if (diff.TotalDays < 7)      return $"{(int)diff.TotalDays} d ago";
            return utc.ToLocalTime().ToString("yyyy-MM-dd");
        }
    }
}
