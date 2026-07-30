using System;
using System.Collections.Generic;

namespace Thulla.Core
{
    /// <summary>
    /// Pure server-side deck construction/dealing logic. Deliberately holds no Unity or
    /// NetworkBehaviour state so it can be unit tested in isolation - BhabhiGameManager is the
    /// only intended caller and must only invoke it on the server.
    /// </summary>
    public static class DeckManager
    {
        public const int SingleDeckPlayerThreshold = 6;
        private const int CardsPerDeck = 52;

        /// <summary>1 deck for <= 6 players, 2 decks (104 cards) for 7+.</summary>
        public static int GetRequiredDeckCount(int connectedPlayerCount)
        {
            return connectedPlayerCount > SingleDeckPlayerThreshold ? 2 : 1;
        }

        /// <summary>Builds 1 or 2 full 52-card decks (tagged by DeckId) and Fisher-Yates shuffles them together.</summary>
        public static List<NetworkCard> BuildShuffledDeck(int deckCount, Random rng = null)
        {
            rng ??= new Random();
            var deck = new List<NetworkCard>(CardsPerDeck * deckCount);

            for (byte deckId = 0; deckId < deckCount; deckId++)
            {
                foreach (Suit suit in (Suit[])Enum.GetValues(typeof(Suit)))
                {
                    foreach (Rank rank in (Rank[])Enum.GetValues(typeof(Rank)))
                    {
                        deck.Add(new NetworkCard(suit, rank, deckId));
                    }
                }
            }

            Shuffle(deck, rng);
            return deck;
        }

        /// <summary>Fisher-Yates shuffle, in place.</summary>
        public static void Shuffle(IList<NetworkCard> deck, Random rng)
        {
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
        }

        /// <summary>
        /// Deals the shuffled deck round-robin across active players (in clockwise seating order)
        /// so hand sizes differ by at most one card.
        /// </summary>
        public static Dictionary<ulong, List<NetworkCard>> DealEvenly(List<NetworkCard> shuffledDeck, IReadOnlyList<ulong> orderedClientIds)
        {
            var hands = new Dictionary<ulong, List<NetworkCard>>(orderedClientIds.Count);
            foreach (ulong clientId in orderedClientIds)
            {
                hands[clientId] = new List<NetworkCard>();
            }

            for (int i = 0; i < shuffledDeck.Count; i++)
            {
                ulong clientId = orderedClientIds[i % orderedClientIds.Count];
                hands[clientId].Add(shuffledDeck[i]);
            }

            return hands;
        }

        /// <summary>
        /// Finds whoever holds the Ace of Spades from Deck 1 (DeckId 0) - that player leads the
        /// very first trick of the match.
        /// </summary>
        public static bool TryFindAceOfSpadesHolder(Dictionary<ulong, List<NetworkCard>> hands, out ulong holderClientId)
        {
            foreach (var kvp in hands)
            {
                foreach (NetworkCard card in kvp.Value)
                {
                    if (card.Suit == Suit.Spades && card.Rank == Rank.Ace && card.DeckId == 0)
                    {
                        holderClientId = kvp.Key;
                        return true;
                    }
                }
            }

            holderClientId = default;
            return false;
        }
    }
}
