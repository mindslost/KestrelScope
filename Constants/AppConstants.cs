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

    public static class DatabaseManagement
    {
        public const string DefaultBackupDirectory = "backups";
        public const int DefaultRetentionMetricsDays = 14;
        public const int DefaultRetentionTracesDays = 7;
        public const int DefaultRetentionLogsDays = 14;
        public const int DefaultRetentionAlertsDays = 90;
        public const int DefaultRetentionAuditLogsDays = 180;
        public const long DefaultStorageWarningThresholdMb = 5120; // 5 GB default warning threshold
        public const int DefaultPruneBatchSize = 5000;
        public const bool DefaultAutoPruneEnabled = true;
        public const int DefaultAutoPruneHourUtc = 2;
        public const bool DefaultAutoBackupEnabled = false;
        public const int DefaultAutoBackupHourUtc = 3;
        public const int DefaultBackupRetentionCount = 7;
        public const string RestoreConfirmationKeyword = "CONFIRM_RESTORE";
    }

    public static class AuditActions
    {
        public const string Prune = "PRUNE";
        public const string BackupCreate = "BACKUP_CREATE";
        public const string BackupDelete = "BACKUP_DELETE";
        public const string BackupRestore = "RESTORE";
        public const string RetentionUpdate = "RETENTION_UPDATE";
        public const string Vacuum = "VACUUM";
        public const string Checkpoint = "CHECKPOINT";
        public const string IntegrityCheck = "INTEGRITY_CHECK";
    }
}

