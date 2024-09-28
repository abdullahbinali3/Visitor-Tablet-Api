namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class MicrosoftErrorResponse
    {
        public string? error { get; set; }
        public string? error_description { get; set; }
        public int[]? error_codes { get; set; }
        public string? timestamp { get; set; }
        public string? trace_id { get; set; }
        public string? correlation_id { get; set; }
        public string? error_uri { get; set; }
    }
}
