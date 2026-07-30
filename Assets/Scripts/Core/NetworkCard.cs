using System;
using Unity.Netcode;

namespace Thulla.Core
{
    /// <summary>
    /// Wire format for a single card. DeckId distinguishes duplicate cards when two decks are in
    /// play (7+ players) so, e.g., the two Ace of Spades from a double deck never collapse into
    /// "the same" card during equality checks or hand lookups.
    /// </summary>
    public struct NetworkCard : INetworkSerializable, IEquatable<NetworkCard>
    {
        public Suit Suit;
        public Rank Rank;
        public byte DeckId;

        // Sentinel DeckId marking "no card" (e.g. a default/uninitialized value).
        private const byte InvalidDeckId = 255;

        public static readonly NetworkCard Invalid = new NetworkCard
        {
            Suit = Suit.Clubs,
            Rank = Rank.Two,
            DeckId = InvalidDeckId
        };

        public bool IsValid => DeckId != InvalidDeckId;

        public NetworkCard(Suit suit, Rank rank, byte deckId)
        {
            Suit = suit;
            Rank = rank;
            DeckId = deckId;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Suit);
            serializer.SerializeValue(ref Rank);
            serializer.SerializeValue(ref DeckId);
        }

        public bool Equals(NetworkCard other)
        {
            return Suit == other.Suit && Rank == other.Rank && DeckId == other.DeckId;
        }

        public override bool Equals(object obj) => obj is NetworkCard other && Equals(other);

        public override int GetHashCode() => (int)Suit | ((int)Rank << 4) | (DeckId << 12);

        public override string ToString() => $"{CardRules.DisplayName(this)} (Deck {DeckId})";
    }
}
