using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class QuickMatchServerCallbacks : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private NetworkObject? _quickMatchClientInstance;

    [SerializeField]
    private int _roomIndex = -1;

    [SerializeField]
    private NetworkPrefabRef _playerControllerPrefab;

    private readonly Dictionary<PlayerRef, NetworkObject> _playerControllers = new();

    public NetworkObject? QuickMatchClientInstance => _quickMatchClientInstance;
    public int RoomIndex => _roomIndex;

    public void SetPlayerControllerPrefab(NetworkPrefabRef prefab)
    {
        _playerControllerPrefab = prefab;
    }

    public void Initialise(int roomIndex, NetworkObject? instance)
    {
        _roomIndex = roomIndex;
        _quickMatchClientInstance = instance;
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!_playerControllerPrefab.IsValid)
        {
            Debug.LogWarning("Player controller prefab has not been assigned for quick match rooms.");
            return;
        }

        NetworkObject? controllerObject = null;

        try
        {
            controllerObject = runner.Spawn(_playerControllerPrefab, Vector3.zero, Quaternion.identity, player);
            if (controllerObject != null)
                Debug.Log("Vào phòng thành công. đã spawn xong input điều khiển cho user");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Failed to spawn player controller for player {player}: {ex}");
            return;
        }

        if (controllerObject == null)
        {
            Debug.LogError($"❌ Runner returned a null controller object for player {player}.");
            return;
        }

        runner.SetPlayerObject(player, controllerObject);

        if (controllerObject.TryGetComponent(out PlayerNetworkController controller))
        {
            controller.AssignQuickMatchClient(_quickMatchClientInstance);
        }
        else
        {
            Debug.LogWarning("Spawned player controller is missing the PlayerNetworkController component.");
        }

        _playerControllers[player] = controllerObject;
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_playerControllers.TryGetValue(player, out var controllerObject))
        {
            if (controllerObject != null && controllerObject.IsValid)
            {
                runner.Despawn(controllerObject);
            }

            _playerControllers.Remove(player);
        }

        if (runner.TryGetPlayerObject(player, out _))
        {
            runner.SetPlayerObject(player, null);
        }
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        _quickMatchClientInstance = null;

        foreach (var controller in _playerControllers.Values)
        {
            if (controller != null && controller.IsValid)
            {
                runner.Despawn(controller);
            }
        }

        _playerControllers.Clear();
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.Log($"Disconnected from server. Reason: {reason}.");
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
    }

    //public void OnReflexStage2(NetworkRunner runner, Fusion.Sockets. callback)
    //{
    //}
}
