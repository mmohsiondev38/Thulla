using System;
using System.Collections.Generic;
using Thulla.Core;
using Unity.Netcode;
using UnityEngine;

namespace Thulla.Gameplay
{
    public enum GamePhase : byte
    {
        WaitingForPlayers = 0,
        InProgress = 1,
        GameOver = 2
    }

    /// <summary>
    /// Server-authoritative match/turn/trick manager. Every rule in the design - suit enforcement,
    /// Thulla pickup, clean-round clearing, safe/Bhabhi detection - is decided here and only here.
    /// Clients only ever *request* a play (via PlayerHandController's ServerRpc) and *observe* the
    /// resulting replicated state and announcements; they can never mutate game state directly.
    /// </summary>
    public class BhabhiGameManager : NetworkBehaviour
    {
        public static BhabhiGameManager Instance { get; private set; }

        private const ulong NoClient = ulong.MaxValue;
        private const int NoLeadSuit = -1;

        [SerializeField] private int minPlayersToStart = 2;

        // ----- Replicated state everyone (or, for hands, only the owner) can see -----
        public readonly NetworkVariable<GamePhase> Phase = new NetworkVariable<GamePhase>(GamePhase.WaitingForPlayers);
        public readonly NetworkVariable<ulong> CurrentTurnClientId = new NetworkVariable<ulong>(NoClient);
        public readonly NetworkVariable<int> LeadSuitRaw = new NetworkVariable<int>(NoLeadSuit);
        public readonly NetworkVariable<ulong> BhabhiClientId = new NetworkVariable<ulong>(NoClient);
        public readonly NetworkList<NetworkCard> TableCards = new NetworkList<NetworkCard>();
        public readonly NetworkList<ulong> SafeClientIds = new NetworkList<ulong>();

        public bool HasLeadSuit => LeadSuitRaw.Value != NoLeadSuit;
        public Suit LeadSuit => (Suit)LeadSuitRaw.Value;

        // ----- Server-only bookkeeping -----
        private readonly List<ulong> _turnOrder = new List<ulong>();
        private readonly Dictionary<ulong, IHandController> _hands = new Dictionary<ulong, IHandController>();
        private readonly List<TrickPlay> _currentTrick = new List<TrickPlay>();
        private ulong _trickLeaderClientId;

        private struct TrickPlay
        {
            public ulong ClientId;
            public NetworkCard Card;
        }

        // ----- Client-side announcement events, raised by ClientRpcs below -----
        public event Action<ulong> OnThullaAnnounced;
        public event Action<ulong> OnRoundClearedAnnounced;
        public event Action<ulong> OnPlayerSafeAnnounced;
        public event Action<ulong> OnBhabhiAnnounced;
        public event Action<string> OnPlayRejected;

        private void Awake()
        {
            Instance = this;
        }

        public bool IsPlayerSafe(ulong clientId)
        {
            for (int i = 0; i < SafeClientIds.Count; i++)
            {
                if (SafeClientIds[i] == clientId) return true;
            }
            return false;
        }

        /// <summary>
        /// True once clientId's PlayerHandController.OnNetworkSpawn has registered with this
        /// manager. A freshly connected player's registration lands a frame or two after
        /// NetworkManager reports them connected (spawn -> OnNetworkSpawn is not instantaneous),
        /// so callers that need "everyone is really ready" (e.g. OfflineMatchBootstrap before it
        /// deals bots in) must poll this rather than assuming one frame is enough.
        /// </summary>
        public bool IsClientRegistered(ulong clientId)
        {
            return _hands.ContainsKey(clientId);
        }

        // ------------------------------------------------------------------
        // Registration (called by PlayerHandController on the server)
        // ------------------------------------------------------------------

        // No IsServer guard here: the caller (PlayerHandController.OnNetworkSpawn) already checks
        // its own IsServer before calling in. Checking *this* NetworkBehaviour's IsServer instead
        // is wrong - it depends on BhabhiGameManager's own NetworkObject having finished spawning,
        // which is not guaranteed to happen before a player's own NetworkObject spawns and this
        // runs. Re-checking here silently drops the registration in that race.
        public void ServerRegisterPlayer(ulong clientId, IHandController hand)
        {
            _hands[clientId] = hand;
            if (!_turnOrder.Contains(clientId))
            {
                _turnOrder.Add(clientId); // Connection order == clockwise seating order.
            }
        }

        // Same reasoning as ServerRegisterPlayer above: no self IsServer guard, the caller already
        // gated on its own IsServer, and NetworkList writes here are safe even if this
        // NetworkObject's own spawn is still settling.
        public void ServerUnregisterPlayer(ulong clientId)
        {
            _hands.Remove(clientId);
            bool wasCurrentTurn = CurrentTurnClientId.Value == clientId;
            _turnOrder.Remove(clientId);
            SafeClientIds.Remove(clientId);

            if (Phase.Value == GamePhase.InProgress)
            {
                if (wasCurrentTurn) AdvanceTurnToNextActive(clientId);
                CheckForBhabhiWin();
            }
        }

        // ------------------------------------------------------------------
        // Match start
        // ------------------------------------------------------------------

        /// <summary>Call from host UI (e.g. a "Start Match" button visible only to the server/host).</summary>
        public void ServerStartMatch()
        {
            if (!IsServer) return;
            if (Phase.Value == GamePhase.InProgress) return;

            if (_turnOrder.Count < minPlayersToStart)
            {
                Debug.LogWarning($"[BhabhiGameManager] Need at least {minPlayersToStart} players to start.");
                return;
            }

            int deckCount = DeckManager.GetRequiredDeckCount(_turnOrder.Count);
            List<NetworkCard> deck = DeckManager.BuildShuffledDeck(deckCount);
            Dictionary<ulong, List<NetworkCard>> dealt = DeckManager.DealEvenly(deck, _turnOrder);

            foreach (var kvp in dealt)
            {
                _hands[kvp.Key].ServerSetHand(kvp.Value);
            }

            TableCards.Clear();
            SafeClientIds.Clear();
            _currentTrick.Clear();
            LeadSuitRaw.Value = NoLeadSuit;
            BhabhiClientId.Value = NoClient;

            ulong leader = DeckManager.TryFindAceOfSpadesHolder(dealt, out ulong holder)
                ? holder
                : _turnOrder[0]; // Should never happen with a complete deck, but keeps the match startable.

            CurrentTurnClientId.Value = leader;
            Phase.Value = GamePhase.InProgress;
        }

        // ------------------------------------------------------------------
        // Play validation + resolution (the core rules engine)
        // ------------------------------------------------------------------

        /// <summary>Required validation entry point: true if clientId may legally play playedCard right now.</summary>
        public bool IsValidPlay(ulong clientId, NetworkCard playedCard)
        {
            if (Phase.Value != GamePhase.InProgress) return false;
            if (CurrentTurnClientId.Value != clientId) return false;
            if (!_hands.TryGetValue(clientId, out IHandController hand)) return false;
            if (!hand.ServerHasCard(playedCard)) return false;

            if (_currentTrick.Count == 0)
            {
                return true; // Leading the trick: any card sets the lead suit.
            }

            if (playedCard.Suit == LeadSuit)
            {
                return true; // Following suit is always legal.
            }

            // Off-suit is only legal (a Thulla) if the player holds no card of the lead suit at all.
            return !hand.ServerHasSuit(LeadSuit);
        }

        public void ServerTryPlayCard(ulong clientId, NetworkCard card)
        {
            if (!IsServer) return;

            if (!IsValidPlay(clientId, card))
            {
                NotifyRejectedClientRpc("Invalid play - you must follow the lead suit if you can.", BuildTargetParams(clientId));
                return;
            }

            IHandController hand = _hands[clientId];
            bool isLeadingTrick = _currentTrick.Count == 0;
            bool wasThulla = !isLeadingTrick && card.Suit != LeadSuit;

            hand.ServerRemoveCard(card);

            if (isLeadingTrick)
            {
                LeadSuitRaw.Value = (int)card.Suit;
                _trickLeaderClientId = clientId;
            }

            TableCards.Add(card);
            _currentTrick.Add(new TrickPlay { ClientId = clientId, Card = card });

            CheckAndMarkSafe(clientId);

            if (wasThulla)
            {
                ResolveThulla();
                return;
            }

            ulong nextActive = FindNextActiveClientId(clientId);
            bool trickComplete = nextActive == _trickLeaderClientId || nextActive == NoClient;

            if (trickComplete)
            {
                ResolveCleanRound();
                return;
            }

            CurrentTurnClientId.Value = nextActive;
            CheckForBhabhiWin();
        }

        /// <summary>Thulla: round stops immediately, highest lead-suit card owner picks up everything and leads next.</summary>
        private void ResolveThulla()
        {
            ulong pickerClientId = FindHighestLeadSuitPlayer();

            var pickedUp = new List<NetworkCard>(TableCards.Count);
            for (int i = 0; i < TableCards.Count; i++) pickedUp.Add(TableCards[i]);

            _hands[pickerClientId].ServerAddCards(pickedUp);

            // A player who had just gone Safe on the lead play of this trick re-enters the game if a
            // later Thulla forces them to pick the pile back up - matches real Bhabhi risk/reward.
            SafeClientIds.Remove(pickerClientId);

            ClearTrickState();
            CurrentTurnClientId.Value = pickerClientId;

            AnnounceThullaClientRpc(pickerClientId);
        }

        /// <summary>Clean round: every active player followed suit, so the table is discarded and the highest lead-suit card owner leads next.</summary>
        private void ResolveCleanRound()
        {
            ulong winnerClientId = FindHighestLeadSuitPlayer();

            ClearTrickState();

            ulong nextLeader = IsPlayerSafe(winnerClientId)
                ? FindNextActiveClientId(winnerClientId)
                : winnerClientId;

            CurrentTurnClientId.Value = nextLeader;

            AnnounceRoundClearClientRpc(winnerClientId);
            CheckForBhabhiWin();
        }

        private ulong FindHighestLeadSuitPlayer()
        {
            ulong best = _currentTrick[0].ClientId;
            int bestRank = -1;

            foreach (TrickPlay play in _currentTrick)
            {
                if (play.Card.Suit != LeadSuit) continue;

                int rank = (int)play.Card.Rank;
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = play.ClientId;
                }
            }

            return best;
        }

        private void ClearTrickState()
        {
            TableCards.Clear();
            _currentTrick.Clear();
            LeadSuitRaw.Value = NoLeadSuit;
        }

        private void CheckAndMarkSafe(ulong clientId)
        {
            if (!_hands.TryGetValue(clientId, out IHandController hand)) return;
            if (hand.CardCount > 0) return;
            if (IsPlayerSafe(clientId)) return;

            SafeClientIds.Add(clientId);
            AnnouncePlayerSafeClientRpc(clientId);
        }

        private void AdvanceTurnToNextActive(ulong fromClientId)
        {
            CurrentTurnClientId.Value = FindNextActiveClientId(fromClientId);
            CheckForBhabhiWin();
        }

        /// <summary>Walks the clockwise seating order from fromClientId, skipping anyone already Safe.</summary>
        private ulong FindNextActiveClientId(ulong fromClientId)
        {
            if (_turnOrder.Count == 0) return NoClient;

            int startIndex = _turnOrder.IndexOf(fromClientId);
            if (startIndex < 0) startIndex = 0;

            for (int step = 1; step <= _turnOrder.Count; step++)
            {
                int index = (startIndex + step) % _turnOrder.Count;
                ulong candidate = _turnOrder[index];
                if (!IsPlayerSafe(candidate)) return candidate;
            }

            return NoClient; // Nobody left active - match should already be flagged GameOver.
        }

        private void CheckForBhabhiWin()
        {
            if (Phase.Value != GamePhase.InProgress) return;

            var activePlayers = new List<ulong>();
            foreach (ulong clientId in _turnOrder)
            {
                if (!IsPlayerSafe(clientId)) activePlayers.Add(clientId);
            }

            if (activePlayers.Count == 1)
            {
                ulong bhabhi = activePlayers[0];
                BhabhiClientId.Value = bhabhi;
                Phase.Value = GamePhase.GameOver;
                AnnounceBhabhiClientRpc(bhabhi);
            }
        }

        private static ClientRpcParams BuildTargetParams(ulong clientId)
        {
            return new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
        }

        // ------------------------------------------------------------------
        // ClientRpcs: fire-and-forget announcements consumed by MobileUIController.
        // ------------------------------------------------------------------

        [ClientRpc]
        private void AnnounceThullaClientRpc(ulong pickerClientId) => OnThullaAnnounced?.Invoke(pickerClientId);

        [ClientRpc]
        private void AnnounceRoundClearClientRpc(ulong winnerClientId) => OnRoundClearedAnnounced?.Invoke(winnerClientId);

        [ClientRpc]
        private void AnnouncePlayerSafeClientRpc(ulong clientId) => OnPlayerSafeAnnounced?.Invoke(clientId);

        [ClientRpc]
        private void AnnounceBhabhiClientRpc(ulong clientId) => OnBhabhiAnnounced?.Invoke(clientId);

        [ClientRpc]
        private void NotifyRejectedClientRpc(string reason, ClientRpcParams rpcParams = default) => OnPlayRejected?.Invoke(reason);
    }
}
