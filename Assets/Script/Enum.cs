public static class ApiConfig
{
    public static readonly string BaseUrl = "http://103.12.77.207/api";
    public static readonly string BaseUrlPhoton = "http://103.12.77.207";
    public static readonly string BaseUrlLocal = "http://localhost:5000/api";
}
public enum StatusPlayer
{
    Normal,
    ShootExam,
    StartPoint,
    HoldingBall,
    Power,
    WaitingDestroy,
    Destroy
}