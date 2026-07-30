using Thulla.Core;
using Thulla.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Thulla.Gameplay
{
    /// <summary>
    /// Visual + input for a single card in a player's hand. Purely client-side presentation - it
    /// never mutates game state directly, it only asks PlayerHandController to request a play,
    /// which the server then validates and applies (or rejects).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(Image))]
    public class CardView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image cardImage;
        [SerializeField] private Text rankSuitLabel;
        [SerializeField] private float selectedLiftPixels = 40f;

        private PlayerHandController _owner;
        private NetworkCard _card;
        private RectTransform _rect;
        private Canvas _rootCanvas;

        private Vector2 _restingAnchoredPosition;
        private Quaternion _restingRotation;
        private bool _isSelected;
        private bool _isDragging;

        public NetworkCard Card => _card;
        public PlayerHandController Owner => _owner;

        public void Initialize(PlayerHandController owner, NetworkCard card, CardSpriteLibrary spriteLibrary)
        {
            _owner = owner;
            _card = card;
            _rect = (RectTransform)transform;
            _rootCanvas = GetComponentInParent<Canvas>();

            if (cardImage == null) cardImage = GetComponent<Image>();
            if (spriteLibrary != null) cardImage.sprite = spriteLibrary.GetSprite(card);

            // Text fallback so cards stay identifiable before real 2D art is assigned in
            // CardSpriteLibrary (and useful as a corner index even once art is in place).
            if (rankSuitLabel != null) rankSuitLabel.text = BuildAbbreviation(card);
        }

        private static string BuildAbbreviation(NetworkCard card)
        {
            string rank = card.Rank switch
            {
                Rank.Two => "2",
                Rank.Three => "3",
                Rank.Four => "4",
                Rank.Five => "5",
                Rank.Six => "6",
                Rank.Seven => "7",
                Rank.Eight => "8",
                Rank.Nine => "9",
                Rank.Ten => "10",
                Rank.Jack => "J",
                Rank.Queen => "Q",
                Rank.King => "K",
                Rank.Ace => "A",
                _ => "?"
            };

            string suit = card.Suit switch
            {
                Suit.Clubs => "C",
                Suit.Diamonds => "D",
                Suit.Hearts => "H",
                Suit.Spades => "S",
                _ => "?"
            };

            return $"{rank}{suit}";
        }

        /// <summary>Called by MobileUIController after it lays this card out along the hand's curve.</summary>
        public void SetRestingTransform(Vector2 anchoredPosition, Quaternion rotation)
        {
            _restingAnchoredPosition = anchoredPosition;
            _restingRotation = rotation;

            if (_isDragging) return;

            _rect.anchoredPosition = _isSelected ? anchoredPosition + new Vector2(0f, selectedLiftPixels) : anchoredPosition;
            _rect.rotation = rotation;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_isDragging) return;

            _isSelected = !_isSelected;
            _rect.anchoredPosition = _isSelected
                ? _restingAnchoredPosition + new Vector2(0f, selectedLiftPixels)
                : _restingAnchoredPosition;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _isDragging = true;
            _rect.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_rootCanvas == null) return;
            if (!(_rect.parent is RectTransform parentRect)) return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);
            _rect.anchoredPosition = localPoint;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;

            RectTransform tableZone = MobileUIController.Instance != null ? MobileUIController.Instance.TableDropZone : null;
            bool droppedOnTable = tableZone != null && RectTransformUtility.RectangleContainsScreenPoint(
                tableZone, eventData.position, eventData.pressEventCamera);

            if (droppedOnTable)
            {
                _isSelected = false;
                _owner.RequestPlayCard(_card);
                // If the server accepts the play, the hand's NetworkList changes and
                // PlayerHandController rebuilds all card views (removing this one). If the server
                // rejects it, the hand is untouched and this card simply snaps back below.
            }

            _rect.anchoredPosition = _isSelected
                ? _restingAnchoredPosition + new Vector2(0f, selectedLiftPixels)
                : _restingAnchoredPosition;
            _rect.rotation = _restingRotation;
        }
    }
}
