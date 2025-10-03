using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;

public class RoomPoolManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static RoomPoolManager? Instance { get; private set; }

    [SerializeField]
    private int _targetEmptyRooms = 3;

    [SerializeField]
    private int _maxPlayersPerRoom = 3;

    [SerializeField]
    private float _idleShutdownSeconds = 180f;

    [SerializeField]
    private ServerConfig? _serverConfig;

    private readonly Dictionary<NetworkRunner, RoomEntry> _rooms = new();
    private readonly HashSet<NetworkRunner> _shutdownInProgress = new();
    private readonly Queue<PlayerRef> _quickMatchQueue = new();
    private readonly HashSet<PlayerRef> _queuedQuickMatchPlayers = new();
    private readonly Dictionary<NetworkRunner, QuickMatchServerCallbacks> _quickMatchServerCallbacks = new();

    public readonly struct RoomPoolStatistics
    {
        public RoomPoolStatistics(string photonRegion, int totalOnlinePlayers, int totalRooms, int occupiedRooms, int fullRooms)
        {
            PhotonRegion = photonRegion;
            TotalOnlinePlayers = totalOnlinePlayers;
            TotalRooms = totalRooms;
            OccupiedRooms = occupiedRooms;
            FullRooms = fullRooms;
        }

        public string PhotonRegion { get; }
        public int TotalOnlinePlayers { get; }
        public int TotalRooms { get; }
        public int OccupiedRooms { get; }
        public int FullRooms { get; }
    }

    [SerializeField]
    private int _maxConcurrentPlayers = 18;

    private int _currentOnlinePlayers;

    private AppSettings _customPhotonSettings = null!;
    private ushort _basePort;
    private string _roomPrefix = "Room";
    private bool _initialised;
    private bool _isCreatingRooms;
    private int _nextPortOffset;
    private int _nextRoomIndex;
    private string _resolvedPublicIpAddress = "0.0.0.0";

    private Coroutine? _topUpRoutine;

    [SerializeField]
    private NetworkObject? _quickMatchClientPrefab;

    [SerializeField]
    private NetworkPrefabRef _quickMatchPlayerControllerPrefab;

    private class RoomEntry
    {
        public int Index { get; set; }
        public NetworkRunner Runner = null!;
        public string Name = string.Empty;
        public int PlayerCount;
        public DateTime LastEmptyUtc;
        public ushort Port;
        public bool IsReserved;
        public NetworkObject? QuickMatchClientInstance;
    }

    public void SetQuickMatchClientPrefab(NetworkObject? prefab)
    {
        _quickMatchClientPrefab = prefab;
    }

    public void SetQuickMatchPlayerControllerPrefab(NetworkPrefabRef prefab)
    {
        _quickMatchPlayerControllerPrefab = prefab;

        foreach (var callback in _quickMatchServerCallbacks.Values)
        {
            if (callback != null)
            {
                callback.SetPlayerControllerPrefab(prefab);
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("⚠️ Multiple RoomPoolManager instances detected. Replacing the previous instance.");
        }

        Instance = this;
    }

    public RoomPoolStatistics GetStatisticsSnapshot()
    {
        var photonRegion = _customPhotonSettings?.FixedRegion ?? string.Empty;
        var totalOnlinePlayers = Volatile.Read(ref _currentOnlinePlayers);

        int totalRooms;
        int occupiedRooms;
        int fullRooms;

        lock (_rooms)
        {
            totalRooms = _rooms.Count;
            occupiedRooms = 0;
            fullRooms = 0;

            foreach (var entry in _rooms.Values)
            {
                if (entry.PlayerCount > 0)
                {
                    occupiedRooms++;
                }

                if (entry.PlayerCount >= _maxPlayersPerRoom)
                {
                    fullRooms++;
                }
            }
        }

        return new RoomPoolStatistics(photonRegion, totalOnlinePlayers, totalRooms, occupiedRooms, fullRooms);
    }

    public IEnumerator InitialisePool(AppSettings photonSettings, ushort basePort, string roomPrefix)
    {
        if (photonSettings == null) throw new ArgumentNullException(nameof(photonSettings));

        ResolvePublicIpAddress();

        _customPhotonSettings = photonSettings;
        _basePort = basePort;
        _roomPrefix = string.IsNullOrWhiteSpace(roomPrefix) ? "Room" : roomPrefix;
        _rooms.Clear();
        _shutdownInProgress.Clear();
        _currentOnlinePlayers = 0;
        _nextPortOffset = 0;
        _nextRoomIndex = 0;

        Debug.Log($"🏁 RoomPoolManager.InitialisePool() with basePort={_basePort}, targetEmptyRooms={_targetEmptyRooms}, idleShutdown={_idleShutdownSeconds}s");

        yield return EnsureMinimumEmptyRoomsCoroutine(forceLog: true);

        _initialised = true;
        LogPoolStatus("Initial pool ready");
    }

    private void ResolvePublicIpAddress()
    {
        if (_serverConfig == null)
        {
            Debug.LogError("Server config asset is not assigned to RoomPoolManager. Defaulting public IP to 0.0.0.0.");
            _resolvedPublicIpAddress = "0.0.0.0";
            return;
        }

        var configuredIp = _serverConfig.PublicIpAddress;

        if (string.IsNullOrWhiteSpace(configuredIp))
        {
            Debug.LogWarning("Public IP address is empty in the assigned ServerConfig. Defaulting to 0.0.0.0.");
            _resolvedPublicIpAddress = "0.0.0.0";
            return;
        }

        configuredIp = configuredIp.Trim();

        if (!IPAddress.TryParse(configuredIp, out _))
        {
            Debug.LogError($"Invalid public IP address '{configuredIp}' configured in ServerConfig. Defaulting to 0.0.0.0.");
            _resolvedPublicIpAddress = "0.0.0.0";
            return;
        }

        _resolvedPublicIpAddress = configuredIp;
    }

    private void Update()
    {
        if (!_initialised)
        {
            return;
        }

        var roomCount = _rooms.Count;
        var anyRoomHasPlayers = false;

        foreach (var entry in _rooms.Values)
        {
            if (entry.PlayerCount > 0)
            {
                anyRoomHasPlayers = true;
                break;
            }
        }

        if (roomCount <= _targetEmptyRooms || anyRoomHasPlayers)
        {
            return;
        }

        var emptyRooms = new List<NetworkRunner>();
        var utcNow = DateTime.UtcNow;

        foreach (var kvp in _rooms)
        {
            var entry = kvp.Value;

            if (entry.PlayerCount == 0)
            {
                var idleSeconds = (utcNow - entry.LastEmptyUtc).TotalSeconds;
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

        QuickMatchServerCallbacks? quickMatchCallbacks = null;
        if (_quickMatchClientPrefab != null)
        {
            quickMatchCallbacks = go.AddComponent<QuickMatchServerCallbacks>();
            quickMatchCallbacks.SetPlayerControllerPrefab(_quickMatchPlayerControllerPrefab);
            runner.AddCallbacks(quickMatchCallbacks);
        }
        else
        {
            Debug.LogWarning("Quick match client prefab has not been assigned for server runners.");
        }

        var args = new StartGameArgs
        {
            GameMode = GameMode.Server,
            SessionName = roomName,
            CustomLobbyName = "DefaultLobby",
            // Server lắng nghe trên tất cả các interface nội bộ
            Address = NetAddress.CreateFromIpPort("0.0.0.0", port),
            // Server báo cáo địa chỉ công cộng cho Client kết nối trên VPS
            //CustomPublicAddress = NetAddress.CreateFromIpPort(_resolvedPublicIpAddress, port),
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
            var roomIndex = _nextRoomIndex++;
            NetworkObject? quickMatchInstance = null;

            if (_quickMatchClientPrefab != null)
            {
                try
                {
                    quickMatchInstance = runner.Spawn(_quickMatchClientPrefab, Vector3.zero, Quaternion.identity);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"❌ Failed to spawn quick match client instance for room '{roomName}': {ex}");
                }
            }

            var entry = new RoomEntry
            {
                Index = roomIndex,
                Runner = runner,
                Name = roomName,
                PlayerCount = 0,
                LastEmptyUtc = DateTime.UtcNow,
                Port = port,
                QuickMatchClientInstance = quickMatchInstance
            };

            if (quickMatchInstance != null)
            {
                quickMatchInstance.gameObject.name = $"Room_{entry.Index}_{roomName}";
                quickMatchInstance.transform.SetParent(go.transform, false);
            }

            _rooms[runner] = entry;
            if (quickMatchCallbacks != null)
            {
                quickMatchCallbacks.Initialise(entry.Index, entry.Name, quickMatchInstance);
                _quickMatchServerCallbacks[runner] = quickMatchCallbacks;
            }

            Debug.Log($"✅ Room '{roomName}' started on port {port}");
        }
        else
        {
            Debug.LogError($"❌ Failed to start room '{roomName}': {result.ShutdownReason}");
            runner.RemoveCallbacks(this);
            if (quickMatchCallbacks != null)
            {
                runner.RemoveCallbacks(quickMatchCallbacks);
                Destroy(quickMatchCallbacks);
            }
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

        if (entry.QuickMatchClientInstance != null && entry.QuickMatchClientInstance.IsValid)
        {
            runner.Despawn(entry.QuickMatchClientInstance);
            entry.QuickMatchClientInstance = null;
        }

        var shutdownTask = runner.Shutdown();

        while (!shutdownTask.IsCompleted)
        {
            yield return null;
        }

        RoomEntry? cleanupEntry = null;

        if (runner && _rooms.TryGetValue(runner, out var currentEntry))
        {
            runner.RemoveCallbacks(this);
            if (_quickMatchServerCallbacks.TryGetValue(runner, out var quickMatchCallbacks))
            {
                runner.RemoveCallbacks(quickMatchCallbacks);
                _quickMatchServerCallbacks.Remove(runner);
                if (quickMatchCallbacks)
                {
                    Destroy(quickMatchCallbacks);
                }
            }
            AdjustOnlinePlayerCount(-currentEntry.PlayerCount);

            _rooms.Remove(runner);
            cleanupEntry = currentEntry;
            Destroy(runner.gameObject);
        }

        _shutdownInProgress.Remove(runner);

        if (cleanupEntry != null)
        {
            LogPoolStatus($"Room '{cleanupEntry.Name}' shut down");
        }

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
        if (Instance == this)
        {
            Instance = null;
        }

        foreach (var runner in _rooms.Keys.ToList())
        {
            if (_rooms.TryGetValue(runner, out var entry) && entry.QuickMatchClientInstance != null && entry.QuickMatchClientInstance.IsValid)
            {
                runner.Despawn(entry.QuickMatchClientInstance);
                entry.QuickMatchClientInstance = null;
            }

            runner.RemoveCallbacks(this);
            if (_quickMatchServerCallbacks.TryGetValue(runner, out var quickMatchCallbacks))
            {
                runner.RemoveCallbacks(quickMatchCallbacks);
                _quickMatchServerCallbacks.Remove(runner);
                if (quickMatchCallbacks)
                {
                    Destroy(quickMatchCallbacks);
                }
            }
            _shutdownInProgress.Remove(runner);
            runner.Shutdown();
            Destroy(runner.gameObject);
        }

        _rooms.Clear();
        _shutdownInProgress.Clear();
        _currentOnlinePlayers = 0;
    }

    internal bool TryEnqueueQuickMatchPlayer(PlayerRef player, out SessionInfo sessionInfo, out List<PlayerRef> players)
    {
        sessionInfo = default;
        players = new List<PlayerRef>();

        if (!_initialised)
        {
            return false;
        }

        if (!_queuedQuickMatchPlayers.Add(player))
        {
            return false;
        }

        _quickMatchQueue.Enqueue(player);

        if (_topUpRoutine == null && CountEmptyRooms() == 0)
        {
            _topUpRoutine = StartCoroutine(TopUpRoomsCoroutine());
        }

        return TryDequeueQuickMatchGroup(out sessionInfo, out players);
    }

    internal void RequeueQuickMatchPlayers(IEnumerable<PlayerRef> players)
    {
        foreach (var player in players)
        {
            if (_queuedQuickMatchPlayers.Add(player))
            {
                _quickMatchQueue.Enqueue(player);
            }
        }
    }

    internal bool TryAllocateQuickMatchGroup(out SessionInfo sessionInfo, out List<PlayerRef> players)
    {
        return TryDequeueQuickMatchGroup(out sessionInfo, out players);
    }

    private bool TryDequeueQuickMatchGroup(out SessionInfo sessionInfo, out List<PlayerRef> players)
    {
        sessionInfo = default;
        players = new List<PlayerRef>();

        if (_quickMatchQueue.Count < _maxPlayersPerRoom)
        {
            return false;
        }

        var availableRoom = _rooms.Values.FirstOrDefault(r => r.PlayerCount == 0 && !r.IsReserved);
        if (availableRoom == null)
        {
            return false;
        }

        sessionInfo = availableRoom.Runner.SessionInfo;
        availableRoom.IsReserved = true;

        for (int i = 0; i < _maxPlayersPerRoom && _quickMatchQueue.Count > 0; i++)
        {
            var queuedPlayer = _quickMatchQueue.Dequeue();
            _queuedQuickMatchPlayers.Remove(queuedPlayer);
            players.Add(queuedPlayer);
        }

        return players.Count == _maxPlayersPerRoom;
    }

    internal void ReleaseQuickMatchReservation(SessionInfo sessionInfo)
    {
        if (!sessionInfo.IsValid)
        {
            return;
        }

        foreach (var entry in _rooms.Values)
        {
            if (entry.Runner.SessionInfo.Name == sessionInfo.Name)
            {
                entry.IsReserved = false;
                return;
            }
        }
    }

    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (_rooms.TryGetValue(runner, out var entry))
        {
            entry.IsReserved = false;
            UpdateRoomPlayerCount(entry, runner.ActivePlayers.Count());
            Debug.Log($"👥 Player joined room '{entry.Name}'. Count={entry.PlayerCount}");
            if (entry.PlayerCount >= _maxPlayersPerRoom)
            {
                Debug.Log($"🚪 Room '{entry.Name}' is full.");
            }

            var emptyRoomCount = CountEmptyRooms();
            if (_topUpRoutine == null && emptyRoomCount < _targetEmptyRooms)
            {
                _topUpRoutine = StartCoroutine(TopUpRoomsCoroutine());
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
        if (_shutdownInProgress.Contains(runner))
        {
            return;
        }

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

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (_currentOnlinePlayers >= _maxConcurrentPlayers)
        {
            Debug.LogError("quá tải server");
            request.Refuse();
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

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
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
