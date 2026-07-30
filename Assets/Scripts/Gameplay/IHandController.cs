using System.Collections.Generic;
using Thulla.Core;

namespace Thulla.Gameplay
{
    /// <summary>
    /// Everything BhabhiGameManager needs from "a player's hand", abstracted away from how that
    /// hand is actually stored. PlayerHandController implements this over a replicated
    /// NetworkList for real connected clients; LocalBotHand implements it over a plain in-memory
    /// list for offline bot opponents that have no NetworkObject/connection at all. The rules
    /// engine in BhabhiGameManager is written against this interface only, so it cannot tell
    /// (and does not need to know) which kind of hand it is talking to.
    /// </summary>
    public interface IHandController
    {
        int CardCount { get; }

        void ServerSetHand(List<NetworkCard> cards);
        void ServerAddCards(IEnumerable<NetworkCard> cards);
        bool ServerRemoveCard(NetworkCard card);
        bool ServerHasCard(NetworkCard card);
        bool ServerHasSuit(Suit suit);
    }
}
