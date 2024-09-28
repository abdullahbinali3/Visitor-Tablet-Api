namespace VisitorTabletAPITemplate
{
    public sealed class MyInternalErrorResponse
    {
        public int StatusCode { get; set; }
        public Dictionary<string, List<MyErrorResponseMessage>> ErrorMessages { get; set; } = new Dictionary<string, List<MyErrorResponseMessage>>();
        public string TraceId { get; set; } = default!;
    }
}
