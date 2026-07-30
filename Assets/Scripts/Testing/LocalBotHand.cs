using System.Collections.Generic;
using Thulla.Core;
using Thulla.Gameplay;

namespace Thulla.Testing
{
    /// <summary>
    /// An offline bot's hand: a plain in-memory IHandController with no NetworkObject, no
    /// NetworkList, and no real Netcode client behind it at all. It exists purely on the host so a
    /// solo developer can test a full match (dealing, Thulla, Safe/Bhabhi) without any second
    /// device or build. BhabhiGameManager treats it identically to a real player's
    /// PlayerHandController because both implement IHandController.
    /// </summary>
    public sealed class LocalBotHand : IHandController
    {
        private readonly List<NetworkCard> _cards = new List<NetworkCard>();

        public int CardCount => _cards.Count;

        /// <summary>Read-only view used by BotDirector to pick a card; never touched by the network layer.</summary>
        public IReadOnlyList<NetworkCard> Cards => _cards;

        public void ServerSetHand(List<NetworkCard> cards)
        {
            _cards.Clear();
            _cards.AddRange(cards);
            _cards.Sort(NetworkCardComparer.Instance);
        }

        public void ServerAddCards(IEnumerable<NetworkCard> cards)
        {
            _cards.AddRange(cards);
            _cards.Sort(NetworkCardComparer.Instance);
        }

        public bool ServerRemoveCard(NetworkCard card)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (CardRules.IsSameCard(_cards[i], card))
                {
                    _cards.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool ServerHasCard(NetworkCard card)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (CardRules.IsSameCard(_cards[i], card)) return true;
            }
            return false;
        }

        public bool ServerHasSuit(Suit suit)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                if (_cards[i].Suit == suit) return true;
            }
            return false;
        }
    }
}
