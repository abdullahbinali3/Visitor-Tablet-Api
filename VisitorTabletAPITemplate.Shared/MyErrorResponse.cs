using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate
{
    public sealed class MyErrorResponse
    {
        public int StatusCode { get; set; }
        public bool FatalError { get; set; }
        public bool ConcurrencyKeyInvalid { get; set; }
        public Dictionary<string, List<MyErrorResponseMessage>> ErrorMessages { get; set; } = new Dictionary<string, List<MyErrorResponseMessage>>();
        public string? AdditionalData { get; set; }
    }

    public sealed class MyErrorResponseMessage
    {
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class MyErrorResponseContext : JsonSerializerContext { }
}
