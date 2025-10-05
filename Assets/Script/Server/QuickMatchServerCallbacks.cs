using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class QuickMatchServerCallbacks : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private NetworkObject? _quickMatchClientInstance;

    [SerializeField]
    private int _roomIndex = -1;

    [SerializeField]
    private NetworkPrefabRef _playerControllerPrefab;

    [SerializeField]
    private string _roomName = string.Empty;

    private Transform? _roomRoot;
    private Scene _networkScene;

    private readonly Dictionary<PlayerRef, NetworkObject> _playerControllers = new();

    public NetworkObject? QuickMatchClientInstance => _quickMatchClientInstance;
    public int RoomIndex => _roomIndex;
    public string RoomName => _roomName;
    private Transform RoomRoot => _roomRoot != null ? _roomRoot : transform;
    private bool HasNetworkScene => _networkScene.IsValid() && _networkScene.isLoaded;

    public void SetPlayerControllerPrefab(NetworkPrefabRef prefab)
    {
        _playerControllerPrefab = prefab;
    }

    public void Initialise(int roomIndex, string roomName, NetworkObject? instance, Scene networkScene, Transform? roomRoot)
    {
        _roomIndex = roomIndex;
        _roomName = roomName ?? string.Empty;
        _quickMatchClientInstance = instance;
        _networkScene = networkScene;
        _roomRoot = roomRoot;
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!_playerControllerPrefab.IsValid)
        {
            Debug.LogWarning("Player controller prefab has not been assigned for quick match rooms.");
            return;
        }

        NetworkObject? controllerObject = null;

        var hasNetworkScene = HasNetworkScene;
        var previousActiveScene = SceneManager.GetActiveScene();
        var activeSceneChanged = false;

        try
        {
            if (hasNetworkScene && previousActiveScene != _networkScene)
            {
                SceneManager.SetActiveScene(_networkScene);
                activeSceneChanged = true;
            }

            controllerObject = runner.Spawn(_playerControllerPrefab, Vector3.zero, Quaternion.identity, player);
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ Failed to spawn player controller for player {player}: {ex}");
            return;
        }
        finally
        {
            if (activeSceneChanged)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
        }

        if (controllerObject == null)
        {
            Debug.LogError($"❌ Runner returned a null controller object for player {player}.");
            return;
        }

        var movedToNetworkScene = false;

        if (hasNetworkScene)
        {
            try
            {
                SceneManager.MoveGameObjectToScene(controllerObject.gameObject, _networkScene);
                movedToNetworkScene = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"⚠️ Unable to move controller for player {player} into network scene '{_networkScene.name}': {ex.Message}");
            }
        }

        if (!movedToNetworkScene)
        {
            DontDestroyOnLoad(controllerObject.gameObject);

            try
            {
                runner.MakeDontDestroyOnLoad(controllerObject.gameObject);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"⚠️ Unable to mark player controller as DontDestroyOnLoad via runner: {ex.Message}");
            }
        }

        runner.SetPlayerObject(player, controllerObject);

        var playerLabel = DerivePlayerLabel(runner, player);
        if (movedToNetworkScene)
        {
            var targetParent = _roomRoot != null ? _roomRoot : null;
           // controllerObject.transform.SetParent(targetParent, worldPositionStays: false);
        }
        else
        {
           // controllerObject.transform.SetParent(RoomRoot, worldPositionStays: false);
        }
        controllerObject.gameObject.name = $"{playerLabel}_Input";

        if (controllerObject.TryGetComponent(out PlayerNetworkController controller))
        {
            controller.AssignQuickMatchClient(_quickMatchClientInstance);
        }
        else
        {
            Debug.LogWarning("Spawned player controller is missing the PlayerNetworkController component.");
        }

        Debug.Log($"{player} Vào phòng thành công. đã spawn xong input điều khiển");
        _playerControllers[player] = controllerObject;
    }

    private string DerivePlayerLabel(NetworkRunner runner, PlayerRef player)
    {
        string? label = null;

        try
        {
            label = runner.GetPlayerUserId(player);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"⚠️ Unable to read userId for player {player}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(label) && runner.TryGetPlayerObject(player, out var playerObject) && playerObject != null)
        {
            label = playerObject.name;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            label = player.PlayerId.ToString();
        }

        return label;
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
        _networkScene = default;
        _roomRoot = null;

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
