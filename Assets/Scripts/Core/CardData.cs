using System.Collections.Generic;
using UnityEngine;

namespace Thulla.Core
{
    /// <summary>Card suit. Declaration order also defines sort priority (Clubs low -> Spades high).</summary>
    public enum Suit : byte
    {
        Clubs = 0,
        Diamonds = 1,
        Hearts = 2,
        Spades = 3
    }

    /// <summary>Card rank. Numeric value doubles as trick-strength (Ace highest).</summary>
    public enum Rank : byte
    {
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14
    }

    /// <summary>
    /// Pure helper logic shared by server rules and client presentation. Has no Unity/network
    /// dependencies so it is trivially unit-testable.
    /// </summary>
    public static class CardRules
    {
        public static bool IsSameCard(NetworkCard a, NetworkCard b)
        {
            return a.Suit == b.Suit && a.Rank == b.Rank && a.DeckId == b.DeckId;
        }

        public static int CompareForSort(NetworkCard a, NetworkCard b)
        {
            int suitCompare = ((int)a.Suit).CompareTo((int)b.Suit);
            if (suitCompare != 0) return suitCompare;
            return ((int)a.Rank).CompareTo((int)b.Rank);
        }

        public static string DisplayName(NetworkCard card)
        {
            return $"{card.Rank} of {card.Suit}";
        }
    }

    /// <summary>Sorts hands by suit then rank so mobile fan layouts stay stable and readable.</summary>
    public sealed class NetworkCardComparer : IComparer<NetworkCard>
    {
        public static readonly NetworkCardComparer Instance = new NetworkCardComparer();

        public int Compare(NetworkCard a, NetworkCard b) => CardRules.CompareForSort(a, b);
    }

    /// <summary>
    /// Designer-authored sprite lookup for the 52 unique cards plus a shared card back.
    /// Kept separate from gameplay/network code so art can be swapped without touching rules.
    /// </summary>
    [CreateAssetMenu(fileName = "CardSpriteLibrary", menuName = "Thulla/Card Sprite Library")]
    public sealed class CardSpriteLibrary : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public Suit Suit;
            public Rank Rank;
            public Sprite Sprite;
        }

        [SerializeField] private Sprite cardBackSprite;
        [SerializeField] private Entry[] entries = new Entry[0];

        private Dictionary<int, Sprite> _lookup;

        public Sprite CardBack => cardBackSprite;

        public Sprite GetSprite(NetworkCard card)
        {
            BuildLookupIfNeeded();
            int key = MakeKey(card.Suit, card.Rank);
            return _lookup.TryGetValue(key, out Sprite sprite) ? sprite : cardBackSprite;
        }

        private void BuildLookupIfNeeded()
        {
            if (_lookup != null) return;

            _lookup = new Dictionary<int, Sprite>(entries.Length);
            foreach (Entry entry in entries)
            {
                _lookup[MakeKey(entry.Suit, entry.Rank)] = entry.Sprite;
            }
        }

        private static int MakeKey(Suit suit, Rank rank) => ((int)suit << 8) | (int)rank;
    }
}
