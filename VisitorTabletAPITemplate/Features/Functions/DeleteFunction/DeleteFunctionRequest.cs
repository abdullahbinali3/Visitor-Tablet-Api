namespace VisitorTabletAPITemplate.Features.Functions.DeleteFunction
{
    public sealed class DeleteFunctionRequest
    {
        public Guid? id { get; set; }
        public Guid? OrganizationId { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
