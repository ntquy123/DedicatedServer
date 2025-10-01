using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;

public class RoomPoolManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private int _targetEmptyRooms = 3;

    [SerializeField]
    private int _maxPlayersPerRoom = 3;

    [SerializeField]
    private float _idleShutdownSeconds = 180f;

    private readonly Dictionary<NetworkRunner, RoomEntry> _rooms = new();
    private readonly HashSet<NetworkRunner> _shutdownInProgress = new();

    [SerializeField]
    private int _maxConcurrentPlayers = 18;

    private int _currentOnlinePlayers;

    private AppSettings _customPhotonSettings = null!;
    private ushort _basePort;
    private string _roomPrefix = "Room";
    private bool _initialised;
    private bool _isCreatingRooms;
    private int _nextPortOffset;

    private Coroutine? _topUpRoutine;

    private class RoomEntry
    {
        public NetworkRunner Runner = null!;
        public string Name = string.Empty;
        public int PlayerCount;
        public DateTime LastEmptyUtc;
        public ushort Port;
    }

    public IEnumerator InitialisePool(AppSettings photonSettings, ushort basePort, string roomPrefix)
    {
        if (photonSettings == null) throw new ArgumentNullException(nameof(photonSettings));

        _customPhotonSettings = photonSettings;
        _basePort = basePort;
        _roomPrefix = string.IsNullOrWhiteSpace(roomPrefix) ? "Room" : roomPrefix;
        _rooms.Clear();
        _shutdownInProgress.Clear();
        _currentOnlinePlayers = 0;
        _nextPortOffset = 0;

        Debug.Log($"🏁 RoomPoolManager.InitialisePool() with basePort={_basePort}, targetEmptyRooms={_targetEmptyRooms}, idleShutdown={_idleShutdownSeconds}s");

        yield return EnsureMinimumEmptyRoomsCoroutine(forceLog: true);

        _initialised = true;
        LogPoolStatus("Initial pool ready");
    }

    private void Update()
    {
        if (!_initialised)
        {
            return;
        }

        var emptyRooms = new List<NetworkRunner>();

        foreach (var kvp in _rooms)
        {
            var entry = kvp.Value;

            if (entry.PlayerCount == 0)
            {
                var idleSeconds = (DateTime.UtcNow - entry.LastEmptyUtc).TotalSeconds;
                if (_idleShutdownSeconds > 0 && idleSeconds >= _idleShutdownSeconds)
                {
                    emptyRooms.Add(entry.Runner);
                }
            }
        }

        foreach (var runner in emptyRooms)
        {
            if (_shutdownInProgress.Add(runner))
            {
                StartCoroutine(ShutdownRoomCoroutine(runner));
            }
        }
    }

    private IEnumerator EnsureMinimumEmptyRoomsCoroutine(bool forceLog = false)
    {
        while (_isCreatingRooms)
        {
            yield return null;
        }

        _isCreatingRooms = true;

        try
        {
            while (CountEmptyRooms() < _targetEmptyRooms)
            {
                yield return CreateRoomCoroutine();
            }

            if (forceLog)
            {
                LogPoolStatus("Ensured minimum empty rooms");
            }
        }
        finally
        {
            _isCreatingRooms = false;
        }
    }

    private IEnumerator CreateRoomCoroutine()
    {
        var roomName = GenerateRoomName();
        var port = (ushort)(_basePort + _nextPortOffset);
        _nextPortOffset++;

        Debug.Log($"➕ Creating room '{roomName}' on port {port}");

        var go = new GameObject($"Runner_{roomName}");
        DontDestroyOnLoad(go);

        var runner = go.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;

        var sceneManager = go.AddComponent<NetworkSceneManagerDefault>();

        runner.AddCallbacks(this);

        var args = new StartGameArgs
        {
            GameMode = GameMode.Server,
            SessionName = roomName,
            Address = NetAddress.CreateFromIpPort("0.0.0.0", port),
            SceneManager = sceneManager,
            PlayerCount = _maxPlayersPerRoom,
            CustomPhotonAppSettings = _customPhotonSettings
        };

        var startTask = runner.StartGame(args);

        while (!startTask.IsCompleted)
        {
            yield return null;
        }

        var result = startTask.Result;

        if (result.Ok)
        {
            var entry = new RoomEntry
            {
                Runner = runner,
                Name = roomName,
                PlayerCount = 0,
                LastEmptyUtc = DateTime.UtcNow,
                Port = port
            };

            _rooms[runner] = entry;

            Debug.Log($"✅ Room '{roomName}' started on port {port}");
        }
        else
        {
            Debug.LogError($"❌ Failed to start room '{roomName}': {result.ShutdownReason}");
            runner.RemoveCallbacks(this);
            Destroy(go);
        }
    }

    private IEnumerator ShutdownRoomCoroutine(NetworkRunner runner)
    {
        if (!_rooms.TryGetValue(runner, out var entry))
        {
            yield break;
        }

        Debug.Log($"♻️ Shutting down idle room '{entry.Name}' on port {entry.Port}");

        var shutdownTask = runner.Shutdown();

        while (!shutdownTask.IsCompleted)
        {
            yield return null;
        }

        runner.RemoveCallbacks(this);
        AdjustOnlinePlayerCount(-entry.PlayerCount);

        _rooms.Remove(runner);
        _shutdownInProgress.Remove(runner);
        Destroy(runner.gameObject);

        LogPoolStatus($"Room '{entry.Name}' shut down");

        if (_topUpRoutine == null)
        {
            _topUpRoutine = StartCoroutine(TopUpRoomsCoroutine());
        }
    }

    private IEnumerator TopUpRoomsCoroutine()
    {
        try
        {
            yield return EnsureMinimumEmptyRoomsCoroutine();
        }
        finally
        {
            _topUpRoutine = null;
        }
    }

    private int CountEmptyRooms()
    {
        return _rooms.Values.Count(r => r.PlayerCount == 0);
    }

    private string GenerateRoomName()
    {
        var guid = Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"{_roomPrefix}_{guid}";
    }

    private void LogPoolStatus(string context)
    {
        var status = _rooms.Count == 0
            ? "(none)"
            : string.Join(", ", _rooms.Values.Select(r => $"{r.Name}[players={r.PlayerCount},port={r.Port}]").ToArray());
        Debug.Log($"📊 {context} | Rooms: {status}");
    }

    private void OnDestroy()
    {
        foreach (var runner in _rooms.Keys.ToList())
        {
            runner.RemoveCallbacks(this);
            _shutdownInProgress.Remove(runner);
            runner.Shutdown();
            Destroy(runner.gameObject);
        }

        _rooms.Clear();
        _shutdownInProgress.Clear();
        _currentOnlinePlayers = 0;
    }

    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (_rooms.TryGetValue(runner, out var entry))
        {
            UpdateRoomPlayerCount(entry, runner.ActivePlayers.Count());
            Debug.Log($"👥 Player joined room '{entry.Name}'. Count={entry.PlayerCount}");
            if (entry.PlayerCount >= _maxPlayersPerRoom)
            {
                Debug.Log($"🚪 Room '{entry.Name}' is full. Triggering pool top-up.");
                if (_topUpRoutine == null)
                {
                    _topUpRoutine = StartCoroutine(TopUpRoomsCoroutine());
                }
            }
            LogPoolStatus($"Player joined {entry.Name}");
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_rooms.TryGetValue(runner, out var entry))
        {
            UpdateRoomPlayerCount(entry, Math.Max(0, runner.ActivePlayers.Count()));
            if (entry.PlayerCount == 0)
            {
                entry.LastEmptyUtc = DateTime.UtcNow;
            }

            Debug.Log($"👤 Player left room '{entry.Name}'. Count={entry.PlayerCount}");
            LogPoolStatus($"Player left {entry.Name}");

            if (_topUpRoutine == null && CountEmptyRooms() < _targetEmptyRooms)
            {
                _topUpRoutine = StartCoroutine(TopUpRoomsCoroutine());
            }
        }
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        if (_rooms.Remove(runner, out var entry))
        {
            AdjustOnlinePlayerCount(-entry.PlayerCount);
            Debug.LogWarning($"⚠️ Runner for room '{entry.Name}' shutdown due to {shutdownReason}");
            Destroy(runner.gameObject);
            _shutdownInProgress.Remove(runner);
            if (_topUpRoutine == null)
            {
                _topUpRoutine = StartCoroutine(TopUpRoomsCoroutine());
            }
            LogPoolStatus($"Runner shutdown {entry.Name}");
        }
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
    }

    public void OnDisconnectedFromServer(NetworkRunner runner)
    {
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (_currentOnlinePlayers >= _maxConcurrentPlayers)
        {
            Debug.LogError("quá tải server");
            request.Refuse(NetConnectFailedReason.ServerFull);
            return;
        }

        request.Accept();
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

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data)
    {
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
    }

    public void OnResourceLoadFailed(NetworkRunner runner, object resourceKey, NetworkObject obj)
    {
    }

    public void OnResourceLoadSuccess(NetworkRunner runner, object resourceKey, NetworkObject obj)
    {
    }

    public void OnRpcMessageReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data)
    {
    }
    #endregion

    private void UpdateRoomPlayerCount(RoomEntry entry, int newCount)
    {
        var previous = entry.PlayerCount;
        entry.PlayerCount = newCount;

        var delta = newCount - previous;
        if (delta != 0)
        {
            AdjustOnlinePlayerCount(delta);
        }
    }

    private void AdjustOnlinePlayerCount(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _currentOnlinePlayers = Mathf.Max(0, _currentOnlinePlayers + delta);
        Debug.Log($"🌐 Total online players: {_currentOnlinePlayers}");
    }
}
