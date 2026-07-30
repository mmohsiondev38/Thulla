using System.Collections.Generic;
using Thulla.Core;
using Thulla.UI;
using Unity.Netcode;
using UnityEngine;

namespace Thulla.Gameplay
{
    /// <summary>
    /// Owns one player's hand. The NetworkList itself is server-writable / owner-readable only
    /// (see Awake) so opponents' hands are never replicated to clients that shouldn't see them -
    /// only the server and the owning client ever hold the real card values. Every mutation method
    /// below is server-authoritative and is only ever called by BhabhiGameManager on the server.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerHandController : NetworkBehaviour, IHandController
    {
        [Header("Prefab-local Assets")]
        [SerializeField] private CardView cardViewPrefab;
        [SerializeField] private CardSpriteLibrary spriteLibrary;

        // Not serialized: a prefab asset cannot reference a scene object. The local owner resolves
        // its hand container from the shared MobileUIController singleton once it spawns instead.
        private RectTransform handContainer;

        private NetworkList<NetworkCard> _hand;
        private readonly List<CardView> _spawnedViews = new List<CardView>();

        public int CardCount => _hand.Count;
        public RectTransform HandContainer => handContainer;

        private void Awake()
        {
            // Owner-only read permission: opponents never receive this list's contents over the wire.
            // Write permission stays at the default (Server) - clients can only *request* changes via RPC.
            _hand = new NetworkList<NetworkCard>(
                readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            _hand.OnListChanged += HandleHandChanged;

            if (IsServer)
            {
                BhabhiGameManager.Instance.ServerRegisterPlayer(OwnerClientId, this);
            }

            if (IsOwner)
            {
                handContainer = MobileUIController.Instance.HandArea;
                MobileUIController.Instance.RegisterLocalHand(this);
                RebuildAllCardViews();
            }
        }

        public override void OnNetworkDespawn()
        {
            _hand.OnListChanged -= HandleHandChanged;

            if (IsServer && BhabhiGameManager.Instance != null)
            {
                BhabhiGameManager.Instance.ServerUnregisterPlayer(OwnerClientId);
            }
        }

        private void HandleHandChanged(NetworkListEvent<NetworkCard> changeEvent)
        {
            if (!IsOwner) return;
            RebuildAllCardViews();
        }

        // ------------------------------------------------------------------
        // Server-authoritative hand mutation. Never called directly by clients.
        // ------------------------------------------------------------------

        public void ServerSetHand(List<NetworkCard> cards)
        {
            if (!IsServer) return;

            _hand.Clear();
            foreach (NetworkCard card in cards) _hand.Add(card);
            ServerSort();
        }

        public void ServerAddCards(IEnumerable<NetworkCard> cards)
        {
            if (!IsServer) return;

            foreach (NetworkCard card in cards) _hand.Add(card);
            ServerSort();
        }

        public bool ServerRemoveCard(NetworkCard card)
        {
            if (!IsServer) return false;

            for (int i = 0; i < _hand.Count; i++)
            {
                if (CardRules.IsSameCard(_hand[i], card))
                {
                    _hand.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public bool ServerHasCard(NetworkCard card)
        {
            for (int i = 0; i < _hand.Count; i++)
            {
                if (CardRules.IsSameCard(_hand[i], card)) return true;
            }

            return false;
        }

        public bool ServerHasSuit(Suit suit)
        {
            for (int i = 0; i < _hand.Count; i++)
            {
                if (_hand[i].Suit == suit) return true;
            }

            return false;
        }

        private void ServerSort()
        {
            var sorted = new List<NetworkCard>(_hand.Count);
            for (int i = 0; i < _hand.Count; i++) sorted.Add(_hand[i]);
            sorted.Sort(NetworkCardComparer.Instance);

            for (int i = 0; i < sorted.Count; i++) _hand[i] = sorted[i];
        }

        // ------------------------------------------------------------------
        // Client-side presentation
        // ------------------------------------------------------------------

        private void RebuildAllCardViews()
        {
            foreach (CardView view in _spawnedViews)
            {
                if (view != null) Destroy(view.gameObject);
            }
            _spawnedViews.Clear();

            for (int i = 0; i < _hand.Count; i++)
            {
                NetworkCard card = _hand[i];
                CardView view = Instantiate(cardViewPrefab, handContainer);
                view.Initialize(this, card, spriteLibrary);
                _spawnedViews.Add(view);
            }

            MobileUIController.Instance.ArrangeHandCurve(handContainer);
        }

        /// <summary>Called by a CardView when the local player drags it onto the table drop zone.</summary>
        public void RequestPlayCard(NetworkCard card)
        {
            if (!IsOwner) return;
            RequestPlayCardRpc(card);
        }

        [Rpc(SendTo.Server, RequireOwnership = true)]
        private void RequestPlayCardRpc(NetworkCard card, RpcParams rpcParams = default)
        {
            // Server authority: the client only *requests* a play; BhabhiGameManager validates and
            // applies it. SenderClientId is supplied by Netcode itself and cannot be spoofed by the caller.
            BhabhiGameManager.Instance.ServerTryPlayCard(rpcParams.Receive.SenderClientId, card);
        }
    }
}
