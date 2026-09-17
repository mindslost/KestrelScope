namespace KestrelScope.Constants;

public static class TelemetryConstants
{
    public static class MetricNames
    {
        public const string HttpServerRequestDuration = "http.server.request.duration";
        public const string OrdersCreatedCount = "orders.created.count";
        public const string OrdersFailedCount = "orders.failed.count";
        public const string ProcessMemoryUsage = "process.memory.usage";
    }

    public static class ServiceNames
    {
        public const string DefaultOrderService = "order-service";
        public const string UnknownService = "unknown-service";
        public const string OrderProcessorService = "order-processor-service";
        public const string WebTierServices = "Web-Tier-Services";
        public const string ECommerceServices = "ECommerce-Services";
        public const string InventoryServices = "Inventory-Services";
        public const string AddressServices = "Address-Services";
        public const string OrderProcessingServices = "Order-Processing-Services";
        public const string CustomerSurveyServices = "Customer-Survey-Services";
    }

    public static class BackendNames
    {
        public const string InventoryMySql = "INVENTORY-MySQL";
        public const string OracleDbProduction = "Oracle DB Production";
        public const string MongoDbTest = "MongoDB testdb";
        public const string AppDyMySql = "APPDY-MySQL DB";

        public const string ActiveMqOrderQueue = "ActiveMQ-OrderQueue";
        public const string ActiveMqFulfillmentQueue = "ActiveMQ-FulfillmentQueue";
        public const string ActiveMqCustomerQueue = "ActiveMQ-CustomerQueue";
    }

    public static class TopologyConstants
    {
        public const string TypeService = "service";
        public const string TypeDatabase = "database";
        public const string TypeQueue = "queue";

        public const string StatusNormal = "normal";
        public const string StatusWarning = "warning";
        public const string StatusCritical = "critical";

        public const string ProtocolHttp = "HTTP";
        public const string ProtocolJdbc = "JDBC";
        public const string ProtocolJms = "JMS";
        public const string ProtocolMongo = "MongoDB";

        public const string TechJava = "Java";
        public const string TechNode = "Node.js";
        public const string TechDotNet = ".NET";
        public const string TechMySql = "MySQL";
        public const string TechOracle = "Oracle";
        public const string TechMongo = "MongoDB";
        public const string TechActiveMq = "ActiveMQ";
    }

    public static class SpanNames
    {
        public const string CreateOrder = "POST /api/orders";
        public const string ValidateStock = "InventoryService.ValidateStock";
        public const string ChargeCard = "PaymentGateway.ChargeCard";
        public const string SaveOrder = "OrderRepository.SaveOrder";
        public const string FindOrderById = "OrderRepository.FindById";
        public const string FailOrder = "POST /api/orders/fail";
        public const string UnnamedSpan = "unnamed_span";
        public const string UnnamedMetric = "unnamed_metric";
    }

    public static class SpanStatusNames
    {
        public const string Unset = "Unset";
        public const string Ok = "Ok";
        public const string Error = "Error";
    }

    public static class SeverityText
    {
        public const string Trace = "TRACE";
        public const string Debug = "DEBUG";
        public const string Info = "INFO";
        public const string Warn = "WARN";
        public const string Error = "ERROR";
        public const string Fatal = "FATAL";
    }

    public static class Conversion
    {
        public const double NanoToMilli = 1_000_000.0;
        public const long NanoPerMilli = 1_000_000L;
    }

    public static class OtlpAttributes
    {
        public const string ServiceName = "service.name";
    }

    public static class OtlpFields
    {
        public const string ResourceMetrics = "resourceMetrics";
        public const string ScopeMetrics = "scopeMetrics";
        public const string Metrics = "metrics";
        public const string Name = "name";
        public const string Sum = "sum";
        public const string Gauge = "gauge";
        public const string DataPoints = "dataPoints";
        public const string AsDouble = "asDouble";
        public const string AsInt = "asInt";
        public const string TimeUnixNano = "timeUnixNano";
        public const string ObservedTimeUnixNano = "observedTimeUnixNano";

        public const string ResourceSpans = "resourceSpans";
        public const string ScopeSpans = "scopeSpans";
        public const string Spans = "spans";
        public const string TraceId = "traceId";
        public const string SpanId = "spanId";
        public const string ParentSpanId = "parentSpanId";
        public const string StartTimeUnixNano = "startTimeUnixNano";
        public const string EndTimeUnixNano = "endTimeUnixNano";
        public const string Status = "status";
        public const string Code = "code";

        public const string ResourceLogs = "resourceLogs";
        public const string ScopeLogs = "scopeLogs";
        public const string LogRecords = "logRecords";
        public const string SeverityText = "severityText";
        public const string SeverityNumber = "severityNumber";
        public const string Body = "body";
        public const string StringValue = "stringValue";
        public const string Attributes = "attributes";

        public const string Resource = "resource";
        public const string Key = "key";
        public const string Value = "value";
    }

    public static class SeedDefaults
    {
        public const int MetricsRecentCheckMinutes = 15;
        public const int TracesRecentCheckMinutes = 30;
        public const int LogsRecentCheckMinutes = 30;
        public const int RandomSeed = 42;
        public const int MaxHistoryMinutes = 1440;
    }
}

