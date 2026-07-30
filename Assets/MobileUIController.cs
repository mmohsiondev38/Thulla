using System.Collections;
using Thulla.Core;
using Thulla.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Thulla.UI
{
    /// <summary>
    /// Pure client-side presentation: curved/scrollable hand layout for small mobile viewports,
    /// plus turn indicator, lead suit, and announcement banners driven by BhabhiGameManager's
    /// replicated NetworkVariables and ClientRpc events. Contains no gameplay rules whatsoever -
    /// it only ever reads state and shows it.
    /// </summary>
    public class MobileUIController : MonoBehaviour
    {
        public static MobileUIController Instance { get; private set; }

        [Header("Table")]
        [SerializeField] private RectTransform tableDropZone;

        [Header("Local Player Hand")]
        [SerializeField] private RectTransform handArea;

        [Header("Turn / Lead Suit HUD")]
        [SerializeField] private Text turnIndicatorText;
        [SerializeField] private Text leadSuitText;

        [Header("Announcement Banner")]
        [SerializeField] private CanvasGroup announcementGroup;
        [SerializeField] private Text announcementText;
        [SerializeField] private float announcementVisibleSeconds = 2f;
        [SerializeField] private float announcementFadeSeconds = 0.35f;

        [Header("Hand Curve Layout")]
        [SerializeField] private float cardSpacing = 90f;
        [SerializeField] private float maxFanAngle = 30f;
        [SerializeField] private float arcHeight = 40f;
        [SerializeField] private ScrollRect handScrollRect;

        public RectTransform TableDropZone => tableDropZone;
        public RectTransform HandArea => handArea;

        private Coroutine _announcementRoutine;
        private ulong _localClientId;
        private PlayerHandController _localHand;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void Awake()
        {
            Instance = this;
            if (announcementGroup != null) announcementGroup.alpha = 0f;
        }

        private void OnEnable()
        {
            StartCoroutine(BindWhenGameManagerReady());
        }

        private void OnDisable()
        {
            if (BhabhiGameManager.Instance == null) return;

            BhabhiGameManager mgr = BhabhiGameManager.Instance;
            mgr.CurrentTurnClientId.OnValueChanged -= HandleTurnChanged;
            mgr.LeadSuitRaw.OnValueChanged -= HandleLeadSuitChanged;
            mgr.OnThullaAnnounced -= HandleThulla;
            mgr.OnRoundClearedAnnounced -= HandleRoundCleared;
            mgr.OnPlayerSafeAnnounced -= HandlePlayerSafe;
            mgr.OnBhabhiAnnounced -= HandleBhabhi;
            mgr.OnPlayRejected -= HandleRejected;
        }

        /// <summary>Mobile screens can rotate/resize mid-match; re-run the curve layout when that happens.</summary>
        private void Update()
        {
            if (_localHand == null) return;
            if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight) return;

            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            ArrangeHandCurve(_localHand.HandContainer);
        }

        private IEnumerator BindWhenGameManagerReady()
        {
            while (BhabhiGameManager.Instance == null) yield return null;

            _localClientId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

            BhabhiGameManager mgr = BhabhiGameManager.Instance;
            mgr.CurrentTurnClientId.OnValueChanged += HandleTurnChanged;
            mgr.LeadSuitRaw.OnValueChanged += HandleLeadSuitChanged;
            mgr.OnThullaAnnounced += HandleThulla;
            mgr.OnRoundClearedAnnounced += HandleRoundCleared;
            mgr.OnPlayerSafeAnnounced += HandlePlayerSafe;
            mgr.OnBhabhiAnnounced += HandleBhabhi;
            mgr.OnPlayRejected += HandleRejected;

            RefreshTurnIndicator(mgr.CurrentTurnClientId.Value);
            RefreshLeadSuit(mgr.LeadSuitRaw.Value);
        }

        public void RegisterLocalHand(PlayerHandController localHand)
        {
            _localHand = localHand;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }

        // ----- HUD state -----

        private void HandleTurnChanged(ulong previous, ulong current) => RefreshTurnIndicator(current);

        private void RefreshTurnIndicator(ulong currentTurnClientId)
        {
            if (turnIndicatorText == null) return;
            turnIndicatorText.text = currentTurnClientId == _localClientId
                ? "Your Turn!"
                : $"Waiting for Player {currentTurnClientId}...";
        }

        private void HandleLeadSuitChanged(int previous, int current) => RefreshLeadSuit(current);

        private void RefreshLeadSuit(int leadSuitRaw)
        {
            if (leadSuitText == null) return;
            leadSuitText.text = leadSuitRaw < 0 ? "Lead Suit: -" : $"Lead Suit: {(Suit)leadSuitRaw}";
        }

        // ----- Announcements -----

        private void HandleThulla(ulong pickerClientId) => ShowAnnouncement($"Thulla! Player {pickerClientId} picks up the table.");
        private void HandleRoundCleared(ulong winnerClientId) => ShowAnnouncement($"Round clear - Player {winnerClientId} leads next.");
        private void HandlePlayerSafe(ulong clientId) => ShowAnnouncement($"Player {clientId} is Safe!");
        private void HandleBhabhi(ulong clientId) => ShowAnnouncement($"Bhabhi Identified! Player {clientId} loses.");
        private void HandleRejected(string reason) => ShowAnnouncement(reason);

        private void ShowAnnouncement(string message)
        {
            if (announcementGroup == null || announcementText == null) return;

            if (_announcementRoutine != null) StopCoroutine(_announcementRoutine);
            _announcementRoutine = StartCoroutine(AnnouncementRoutine(message));
        }

        private IEnumerator AnnouncementRoutine(string message)
        {
            announcementText.text = message;

            yield return Fade(announcementGroup, 0f, 1f, announcementFadeSeconds);
            yield return new WaitForSeconds(announcementVisibleSeconds);
            yield return Fade(announcementGroup, 1f, 0f, announcementFadeSeconds);
        }

        private static IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                group.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }
            group.alpha = to;
        }

        // ----- Hand layout -----

        /// <summary>
        /// Lays hand cards out along a shallow arc when they fit, or switches the hand to a
        /// horizontally scrollable strip when a big two-deck hand would overflow a phone screen.
        /// </summary>
        public void ArrangeHandCurve(RectTransform handContainer)
        {
            int count = handContainer.childCount;
            if (count == 0) return;

            float viewportWidth = handScrollRect != null
                ? handScrollRect.viewport.rect.width
                : handContainer.rect.width;

            float totalWidth = (count - 1) * cardSpacing;
            bool needsScroll = handScrollRect != null && totalWidth > viewportWidth;

            if (handScrollRect != null) handScrollRect.horizontal = needsScroll;

            float contentWidth = Mathf.Max(viewportWidth, totalWidth + cardSpacing);
            handContainer.sizeDelta = new Vector2(needsScroll ? contentWidth : viewportWidth, handContainer.sizeDelta.y);

            float angleStep = count > 1 ? Mathf.Min(maxFanAngle * 2f / (count - 1), maxFanAngle) : 0f;
            float startAngle = -angleStep * (count - 1) / 2f;

            for (int i = 0; i < count; i++)
            {
                CardView view = handContainer.GetChild(i).GetComponent<CardView>();
                if (view == null) continue;

                float x = (i - (count - 1) / 2f) * cardSpacing;
                float normalized = count > 1 ? i / (float)(count - 1) - 0.5f : 0f;
                float y = -Mathf.Abs(normalized) * arcHeight * 2f;
                float angle = startAngle + angleStep * i;

                view.SetRestingTransform(new Vector2(x, y), Quaternion.Euler(0f, 0f, -angle));
            }
        }
    }
}
