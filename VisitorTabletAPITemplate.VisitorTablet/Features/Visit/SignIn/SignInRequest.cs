
namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignIn
{
    public sealed class SignInRequest
    {
        public Guid WorkplaceVisitId { get; set; }
        public List<Guid> Uid { get; set; }
        public DateTime? SignInDate { get; set; }
    }
}
   