using System.Collections;
using System.Collections.Generic;
using Thulla.Core;
using Thulla.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Thulla.Testing
{
    /// <summary>
    /// Drives every registered LocalBotHand: whenever the turn (replicated from
    /// BhabhiGameManager) belongs to a registered bot id, waits a short delay (so a human watching
    /// the host can actually see the move happen) and then plays a legal card for it directly on
    /// the server. This is test/debug-only scaffolding - it never touches the network layer itself,
    /// it just calls the same server-side entry point a real client's ServerRpc would call.
    /// </summary>
    public class BotDirector : MonoBehaviour
    {
        [SerializeField] private float moveDelaySeconds = 0.6f;

        private readonly Dictionary<ulong, LocalBotHand> _bots = new Dictionary<ulong, LocalBotHand>();
        private Coroutine _pendingMove;

        public void RegisterBot(ulong botId, LocalBotHand hand)
        {
            _bots[botId] = hand;
        }

        public void Clear()
        {
            _bots.Clear();
            if (_pendingMove != null) StopCoroutine(_pendingMove);
        }

        private void OnEnable()
        {
            StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (BhabhiGameManager.Instance != null)
            {
                BhabhiGameManager.Instance.CurrentTurnClientId.OnValueChanged -= HandleTurnChanged;
            }
        }

        private IEnumerator BindWhenReady()
        {
            while (BhabhiGameManager.Instance == null) yield return null;
            BhabhiGameManager.Instance.CurrentTurnClientId.OnValueChanged += HandleTurnChanged;
        }

        private void HandleTurnChanged(ulong previous, ulong current)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (!_bots.TryGetValue(current, out LocalBotHand bot)) return;

            if (_pendingMove != null) StopCoroutine(_pendingMove);
            _pendingMove = StartCoroutine(PlayAfterDelay(current, bot));
        }

        private IEnumerator PlayAfterDelay(ulong botId, LocalBotHand bot)
        {
            yield return new WaitForSeconds(moveDelaySeconds);

            BhabhiGameManager mgr = BhabhiGameManager.Instance;
            if (mgr == null || mgr.CurrentTurnClientId.Value != botId) yield break; // State moved on already.
            if (bot.CardCount == 0) yield break;

            NetworkCard card = ChooseCard(mgr, bot);
            mgr.ServerTryPlayCard(botId, card);
        }

        /// <summary>Minimal legal-move heuristic: follow the lead suit if possible, otherwise play the lowest card on hand.</summary>
        private static NetworkCard ChooseCard(BhabhiGameManager mgr, LocalBotHand bot)
        {
            IReadOnlyList<NetworkCard> cards = bot.Cards;

            if (mgr.HasLeadSuit)
            {
                foreach (NetworkCard card in cards)
                {
                    if (card.Suit == mgr.LeadSuit) return card;
                }
            }

            return cards[0];
        }
    }
}
