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
    public static event Action<SessionInfo>? OnMatchReady;
    public static event Action? OnQueueCancelled;
    public static event Action<SessionInfo>? OnMatchStarting;

    private static QuickMatchClient? _local;
    private static readonly Dictionary<PlayerRef, PendingMatch> _pendingMatches = new();

    private QuickMatchState _state = QuickMatchState.Idle;
    private SessionInfo _pendingSession;

    private class PendingMatch
    {
        public SessionInfo SessionInfo;
        public List<PlayerRef> Players = new();
        public HashSet<PlayerRef> Confirmed = new();
    }

    public QuickMatchState State => _state;

    public override void Spawned()
    {
        base.Spawned();
        if (Object.HasInputAuthority)
        {
            _local = this;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);
        if (_local == this)
        {
            _local = null;
        }
    }

    public static void RequestQuickMatch()
    {
        if (_local == null)
        {
            Debug.LogWarning("QuickMatchClient chưa được spawn cho local player.");
            return;
        }

        if (_local._state != QuickMatchState.Idle)
        {
            Debug.LogWarning("Đang trong quá trình tìm trận hoặc đã được ghép trận.");
            return;
        }

        _local.SetState(QuickMatchState.Searching);
        OnSearching?.Invoke();
        _local.RPC_RequestQuickMatch();
    }

    public static void ConfirmReady(bool ready)
    {
        if (_local == null)
        {
            Debug.LogWarning("QuickMatchClient chưa được spawn cho local player.");
            return;
        }

        _local.RPC_ConfirmReady(ready);
        if (!ready)
        {
            _local.SetState(QuickMatchState.Idle);
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestQuickMatch(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        if (RoomPoolManager.Instance == null)
        {
            Debug.LogWarning("RoomPoolManager chưa sẵn sàng để xử lý Quick Match.");
            return;
        }

        if (_pendingMatches.ContainsKey(info.Source))
        {
            Debug.LogWarning($"Player {info.Source} đã có trận đang chờ xác nhận.");
            return;
        }

        if (!RoomPoolManager.Instance.TryEnqueueQuickMatchPlayer(info.Source, out var sessionInfo, out var players) || players.Count == 0)
        {
            return;
        }

        CreatePendingMatch(sessionInfo, players);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_NotifyMatchReady(SessionInfo info, RpcInfo rpcInfo = default)
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        _pendingSession = info;
        SetState(QuickMatchState.MatchReady);
        OnMatchReady?.Invoke(info);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ConfirmReady(bool ready, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        if (!_pendingMatches.TryGetValue(info.Source, out var pendingMatch))
        {
            return;
        }

        if (!ready)
        {
            CancelPendingMatch(pendingMatch, info.Source);
            return;
        }

        if (!pendingMatch.Confirmed.Add(info.Source))
        {
            return;
        }

        if (pendingMatch.Confirmed.Count < pendingMatch.Players.Count)
        {
            return;
        }

        foreach (var player in pendingMatch.Players)
        {
            _pendingMatches.Remove(player);
        }

        foreach (var player in pendingMatch.Players)
        {
            if (Runner.TryGetPlayerObject(player, out var playerObject) && playerObject.TryGetComponent(out QuickMatchClient client))
            {
                client.RPC_StartMatch(pendingMatch.SessionInfo);
            }
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_StartMatch(SessionInfo info)
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        _pendingSession = info;
        SetState(QuickMatchState.EnteringMatch);
        OnMatchStarting?.Invoke(info);

        var args = new StartGameArgs
        {
            GameMode = GameMode.Client,
           // SessionName = info
        };

        Runner.StartGame(args);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_QueueCancelled()
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        SetState(QuickMatchState.Searching);
        OnQueueCancelled?.Invoke();
        OnSearching?.Invoke();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_ExitQueue()
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        SetState(QuickMatchState.Idle);
        OnQueueCancelled?.Invoke();
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
            if (Runner.TryGetPlayerObject(player, out var playerObject) && playerObject.TryGetComponent(out QuickMatchClient client))
            {
                if (player == declinedPlayer)
                {
                    client.RPC_ExitQueue();
                }
                else
                {
                    client.RPC_QueueCancelled();
                }
            }
        }
    }

    private void SetState(QuickMatchState newState)
    {
        _state = newState;
    }

    private void CreatePendingMatch(SessionInfo sessionInfo, List<PlayerRef> players)
    {
        if (!sessionInfo.IsValid)
        {
            Debug.LogWarning("SessionInfo không hợp lệ cho Quick Match.");
        }

        var pendingMatch = new PendingMatch
        {
            SessionInfo = sessionInfo,
            Players = new List<PlayerRef>(players)
        };

        foreach (var player in players)
        {
            _pendingMatches[player] = pendingMatch;
            if (Runner.TryGetPlayerObject(player, out var playerObject) && playerObject.TryGetComponent(out QuickMatchClient client))
            {
                client.RPC_NotifyMatchReady(sessionInfo);
            }
        }
    }
}
