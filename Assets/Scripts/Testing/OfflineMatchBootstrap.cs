using System.Collections;
using Thulla.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Thulla.Testing
{
    /// <summary>
    /// One-button "offline mode" for solo testing: starts a local Host (loopback, no external
    /// connection required) and fills the remaining seats with LocalBotHand opponents driven by
    /// BotDirector, so a single person on a single machine can play through a full match -
    /// dealing, suit enforcement, Thulla pickups, Safe exits, and Bhabhi detection - without a
    /// second device or a build.
    /// </summary>
    public class OfflineMatchBootstrap : MonoBehaviour
    {
        private const ulong BotIdRangeStart = 90000;

        [SerializeField] private int botCount = 3;
        [SerializeField] private BotDirector botDirector;

        public void StartOfflineTest()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[OfflineMatchBootstrap] No NetworkManager in the scene.");
                return;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[OfflineMatchBootstrap] A network session is already running.");
                return;
            }

            botDirector.Clear();
            NetworkManager.Singleton.StartHost();
            StartCoroutine(SetupBotsAndStartMatch());
        }

        private IEnumerator SetupBotsAndStartMatch()
        {
            // Wait for the scene-placed BhabhiGameManager to finish its server spawn.
            while (BhabhiGameManager.Instance == null) yield return null;

            // Wait for the host's own PlayerHandController to finish registering. Its spawn is
            // driven by NetworkManager's connection-approval pipeline and is not guaranteed to
            // land within a fixed number of frames after StartHost() returns - polling the actual
            // condition (rather than assuming "one extra frame is enough") avoids a race where
            // bots get dealt in and the match starts before the host has a seat at the table.
            ulong hostClientId = NetworkManager.Singleton.LocalClientId;
            while (!BhabhiGameManager.Instance.IsClientRegistered(hostClientId)) yield return null;

            for (int i = 0; i < botCount; i++)
            {
                ulong botId = BotIdRangeStart + (ulong)i;
                var bot = new LocalBotHand();
                BhabhiGameManager.Instance.ServerRegisterPlayer(botId, bot);
                botDirector.RegisterBot(botId, bot);
            }

            BhabhiGameManager.Instance.ServerStartMatch();
        }
    }
}
