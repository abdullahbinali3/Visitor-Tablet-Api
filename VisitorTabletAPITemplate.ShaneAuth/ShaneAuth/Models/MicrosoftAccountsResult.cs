namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class MicrosoftAccountsResult<T>
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public T? Result { get; set; }
    }
}
