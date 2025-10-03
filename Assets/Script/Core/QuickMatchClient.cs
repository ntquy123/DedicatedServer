using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class QuickMatchClient : NetworkBehaviour
{
    public enum QuickMatchState
    {
        Idle,
        Searching,
        MatchReady,
        EnteringMatch
    }

    public static event Action? OnSearching;
    [Serializable]
    public struct QuickMatchTicket : INetworkStruct
    {
        public NetworkString<_64> SessionName;

        public QuickMatchTicket(NetworkString<_64> sessionName)
        {
            SessionName = sessionName;
        }

        public QuickMatchTicket(SessionInfo sessionInfo)
        {
            SessionName = sessionInfo.Name;
        }

        public bool IsValid => SessionName != null;

        public override string ToString() => SessionName.ToString();
    }

    public static event Action<QuickMatchTicket>? OnMatchReady;
    public static event Action? OnQueueCancelled;
    public static event Action<QuickMatchTicket>? OnMatchStarting;
    public static event Action? OnExitedQueue;
    public static event Action<PlayerRef, int, int>? OnPlayerReadyStatusChanged;

    private static readonly Dictionary<PlayerRef, PendingMatch> _pendingMatches = new();

    private class PendingMatch
    {
        public SessionInfo SessionInfo = default;
        public List<PlayerRef> Players = new();
        public HashSet<PlayerRef> Confirmed = new();
    }

    public static bool RequestQuickMatch()
    {
        return PlayerNetworkController.RequestQuickMatch();
    }

    public static void ConfirmReady(bool ready)
    {
        PlayerNetworkController.ConfirmReady(ready);
    }

    public static QuickMatchState LocalState => PlayerNetworkController.Local?.State ?? QuickMatchState.Idle;

    public QuickMatchTicket PendingSession => PlayerNetworkController.LocalPendingSession;

    internal static void RaiseSearching()
    {
        OnSearching?.Invoke();
    }

    internal static void RaiseMatchReady(QuickMatchTicket ticket)
    {
        OnMatchReady?.Invoke(ticket);
    }

    internal static void RaiseQueueCancelled()
    {
        OnQueueCancelled?.Invoke();
    }

    internal static void RaiseMatchStarting(QuickMatchTicket ticket)
    {
        OnMatchStarting?.Invoke(ticket);
    }

    internal static void RaiseExitedQueue()
    {
        OnExitedQueue?.Invoke();
    }

    internal static void RaisePlayerReadyStatus(PlayerRef player, int readyCount, int totalPlayers)
    {
        OnPlayerReadyStatusChanged?.Invoke(player, readyCount, totalPlayers);
    }

    internal void HandlePlayerRequestQuickMatch(PlayerRef player)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        if (RoomPoolManager.Instance == null)
        {
            Debug.LogWarning("RoomPoolManager is not ready to process quick matches.");
            return;
        }

        if (_pendingMatches.ContainsKey(player))
        {
            Debug.LogWarning($"Player {player} already has a pending match.");
            return;
        }

        if (!RoomPoolManager.Instance.TryEnqueueQuickMatchPlayer(player, out var sessionInfo, out var players) || players.Count == 0)
        {
            return;
        }

        CreatePendingMatch(sessionInfo, players);
    }

    internal void HandlePlayerConfirmReady(PlayerRef player, bool ready)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        if (!_pendingMatches.TryGetValue(player, out var pendingMatch))
        {
            return;
        }

        if (!ready)
        {
            CancelPendingMatch(pendingMatch, player);
            return;
        }

        if (!pendingMatch.Confirmed.Add(player))
        {
            return;
        }

        Debug.Log($"🟢 Player {player} confirmed ready ({pendingMatch.Confirmed.Count}/{pendingMatch.Players.Count}).");

        BroadcastReadyStatus(pendingMatch, player);

        if (pendingMatch.Confirmed.Count < pendingMatch.Players.Count)
        {
            return;
        }

        //if (GameManagerNetWork.Instance != null && GameManagerNetWork.Instance.serverRPC != null)
        //{
        //    GameManagerNetWork.Instance.serverRPC.RpcReady_CLIENT();
        //}
        //else
        //{
        //    Debug.LogWarning("⚠️ Unable to notify serverRPC that all clients are ready.");
        //}

        foreach (var playeritem in pendingMatch.Players)
        {
            _pendingMatches.Remove(playeritem);
        }

        foreach (var playeritem in pendingMatch.Players)
        {
            if (TryGetController(playeritem, out var controller))
            {
                controller.RPC_StartMatch(new QuickMatchTicket(pendingMatch.SessionInfo));
            }
        }
    }

    private void BroadcastReadyStatus(PendingMatch pendingMatch, PlayerRef readyPlayer)
    {
        foreach (var player in pendingMatch.Players)
        {
            if (TryGetController(player, out var controller))
            {
                controller.RPC_PlayerReadyStatus(readyPlayer, pendingMatch.Confirmed.Count, pendingMatch.Players.Count);
            }
        }
    }

    private void CancelPendingMatch(PendingMatch pendingMatch, PlayerRef declinedPlayer)
    {
        if (RoomPoolManager.Instance != null)
        {
            RoomPoolManager.Instance.ReleaseQuickMatchReservation(pendingMatch.SessionInfo);
        }

        foreach (var player in pendingMatch.Players)
        {
            _pendingMatches.Remove(player);
        }

        if (RoomPoolManager.Instance != null)
        {
            var playersToRequeue = new List<PlayerRef>();
            foreach (var player in pendingMatch.Players)
            {
                if (player != declinedPlayer)
                {
                    playersToRequeue.Add(player);
                }
            }

            if (playersToRequeue.Count > 0)
            {
                RoomPoolManager.Instance.RequeueQuickMatchPlayers(playersToRequeue);

                if (RoomPoolManager.Instance.TryAllocateQuickMatchGroup(out var sessionInfo, out var players) && players.Count > 0)
                {
                    CreatePendingMatch(sessionInfo, players);
                }
            }
        }

        foreach (var player in pendingMatch.Players)
        {
            if (TryGetController(player, out var controller))
            {
                if (player == declinedPlayer)
                {
                    controller.RPC_ExitQueue();
                }
                else
                {
                    controller.RPC_QueueCancelled();
                }
            }
        }
    }

    private void CreatePendingMatch(SessionInfo sessionInfo, List<PlayerRef> players)
    {
        if (!sessionInfo.IsValid)
        {
            Debug.LogWarning("Invalid SessionInfo provided for quick match.");
        }

        var pendingMatch = new PendingMatch
        {
            SessionInfo = sessionInfo,
            Players = new List<PlayerRef>(players)
        };

        foreach (var player in players)
        {
            _pendingMatches[player] = pendingMatch;
            if (TryGetController(player, out var controller))
            {
                controller.RPC_NotifyMatchReady(new QuickMatchTicket(sessionInfo));
            }
        }
    }

    private bool TryGetController(PlayerRef player, out PlayerNetworkController controller)
    {
        controller = default!;
        if (Runner != null && Runner.TryGetPlayerObject(player, out var playerObject) && playerObject != null)
        {
            return playerObject.TryGetComponent(out controller);
        }

        return false;
    }
}
