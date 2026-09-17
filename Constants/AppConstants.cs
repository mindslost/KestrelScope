namespace KestrelScope.Constants;

public static class AppConstants
{
    public static class Database
    {
        public const string DefaultConnectionString = "Data Source=observability.db;";
        public const string WalPragmaSettings = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        public const int BusyTimeoutMs = 5000;
    }

    public static class Auth
    {
        public const string CookieScheme = "CookieAuth";
        public const string CookieName = "ObsSession";
        public const string UserIdClaim = "UserId";
        public const int CookieExpirationDays = 7;
        public const string LoginPath = "/login.html";
    }

    public static class UserRoles
    {
        public const string Admin = "admin";
        public const string Standard = "standard";
        public const string AdminNormalized = "Admin";
        public const string StandardNormalized = "Standard";

        public static string Normalize(string? role) =>
            string.Equals(role, Admin, System.StringComparison.OrdinalIgnoreCase)
                ? AdminNormalized
                : StandardNormalized;

        public static string ToDbValue(string? role) =>
            string.Equals(role, Admin, System.StringComparison.OrdinalIgnoreCase)
                ? Admin
                : Standard;
    }

    public static class QueryDefaults
    {
        public const int DefaultWindowMinutes = 60;
        public const int DefaultLimit = 100;
        public const int MaxLimit = 500;
        public const int DefaultMetricsRecentMinutes = 15;
    }

    public static class Alerts
    {
        public const string StateFiring = "FIRING";
        public const string StateResolved = "RESOLVED";
        public const int DefaultEvaluationIntervalSeconds = 60;
        public const int InitialDelaySeconds = 2;
        public const int WebhookTimeoutSeconds = 5;
    }
}

