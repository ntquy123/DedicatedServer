using UnityEngine;

[CreateAssetMenu(menuName = "Server/Server Config", fileName = "ServerConfig")]
public class ServerConfig : ScriptableObject
{
    [SerializeField]
    private string _publicIpAddress = string.Empty;

    public string PublicIpAddress => _publicIpAddress;
}
