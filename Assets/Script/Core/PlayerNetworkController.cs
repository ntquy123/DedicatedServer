using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class PlayerNetworkController : NetworkBehaviour
{
    private QuickMatchClient.QuickMatchTicket _pendingSession;
    private QuickMatchClient.QuickMatchState _state = QuickMatchClient.QuickMatchState.Idle;
    private QuickMatchClient? _quickMatchClient;

    public static PlayerNetworkController? Local { get; private set; }

    [Networked, OnChangedRender(nameof(OnQuickMatchClientChanged))]
    private NetworkId QuickMatchClientId { get; set; }

    public QuickMatchClient.QuickMatchState State => _state;

    internal static QuickMatchClient.QuickMatchTicket LocalPendingSession => Local?._pendingSession ?? default;

    public override void Spawned()
    {
        base.Spawned();

        if (Object.HasInputAuthority)
        {
            Local = this;
        }

        ResolveQuickMatchClient();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        if (Local == this)
        {
            Local = null;
        }

        _quickMatchClient = null;
    }

    public void AssignQuickMatchClient(NetworkObject? quickMatchClient)
    {
        if (Object == null || !Object.HasStateAuthority)
        {
            Debug.LogWarning("AssignQuickMatchClient can only be invoked on the state authority instance.");
            return;
        }

        QuickMatchClientId = quickMatchClient != null ? quickMatchClient.Id : default;
        ResolveQuickMatchClient();
    }

    public static bool RequestQuickMatch()
    {
        if (Local == null)
        {
            Debug.LogWarning("PlayerNetworkController has not been spawned for the local player.");
            return false;
        }

        return Local.RequestQuickMatchInternal();
    }

    public static void ConfirmReady(bool ready)
    {
        if (Local == null)
        {
            Debug.LogWarning("PlayerNetworkController has not been spawned for the local player.");
            return;
        }

        Local.ConfirmReadyInternal(ready);
    }

    public bool CmdRequestQuickMatch()
    {
        if (!Object.HasInputAuthority)
        {
            Debug.LogWarning("Quick match requests can only be issued by the input authority instance.");
            return false;
        }

        return RequestQuickMatchInternal();
    }

    public void CmdConfirmReady(bool ready)
    {
        if (!Object.HasInputAuthority)
        {
            Debug.LogWarning("Ready confirmations can only be issued by the input authority instance.");
            return;
        }

        ConfirmReadyInternal(ready);
    }

    private bool RequestQuickMatchInternal()
    {
        if (_state != QuickMatchClient.QuickMatchState.Idle)
        {
            Debug.LogWarning("Quick match cannot be requested while another request is active.");
            return false;
        }

        if (ResolveQuickMatchClient() == null)
        {
            Debug.LogWarning("Quick match service is not available in the current room.");
            return false;
        }

        SetState(QuickMatchClient.QuickMatchState.Searching);
        QuickMatchClient.RaiseSearching();
        RPC_RequestQuickMatch();
        return true;
    }

    private void ConfirmReadyInternal(bool ready)
    {
        if (!ready)
        {
            SetState(QuickMatchClient.QuickMatchState.Idle);
        }

        RPC_ConfirmReady(ready);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestQuickMatch(RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        var service = ResolveQuickMatchClient();
        if (service == null)
        {
            Debug.LogWarning("Quick match client instance is not available on the server.");
            return;
        }

        service.HandlePlayerRequestQuickMatch(info.Source);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ConfirmReady(bool ready, RpcInfo info = default)
    {
        if (!Object.HasStateAuthority)
        {
            return;
        }

        var service = ResolveQuickMatchClient();
        if (service == null)
        {
            Debug.LogWarning("Quick match client instance is not available on the server.");
            return;
        }

        service.HandlePlayerConfirmReady(info.Source, ready);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_NotifyMatchReady(QuickMatchClient.QuickMatchTicket ticket)
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        _pendingSession = ticket;
        SetState(QuickMatchClient.QuickMatchState.MatchReady);
        QuickMatchClient.RaiseMatchReady(ticket);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_StartMatch(QuickMatchClient.QuickMatchTicket ticket)
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        _pendingSession = ticket;
        SetState(QuickMatchClient.QuickMatchState.EnteringMatch);
        QuickMatchClient.RaiseMatchStarting(ticket);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_QueueCancelled()
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        SetState(QuickMatchClient.QuickMatchState.Searching);
        QuickMatchClient.RaiseQueueCancelled();
        QuickMatchClient.RaiseSearching();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_ExitQueue()
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        SetState(QuickMatchClient.QuickMatchState.Idle);
        QuickMatchClient.RaiseQueueCancelled();
        QuickMatchClient.RaiseExitedQueue();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    public void RPC_PlayerReadyStatus(PlayerRef readyPlayer, int readyCount, int totalPlayers)
    {
        if (!Object.HasInputAuthority)
        {
            return;
        }

        QuickMatchClient.RaisePlayerReadyStatus(readyPlayer, readyCount, totalPlayers);
    }

    private void SetState(QuickMatchClient.QuickMatchState newState)
    {
        _state = newState;
    }

    private QuickMatchClient? ResolveQuickMatchClient()
    {
        if (_quickMatchClient != null)
        {
            return _quickMatchClient;
        }

        if (Runner != null && Runner.TryFindObject(QuickMatchClientId, out var networkObject))
        {
            _quickMatchClient = networkObject.GetComponent<QuickMatchClient>();
        }

        return _quickMatchClient;
    }

    private void OnQuickMatchClientChanged()
    {
        _quickMatchClient = null;
        ResolveQuickMatchClient();
    }
}
