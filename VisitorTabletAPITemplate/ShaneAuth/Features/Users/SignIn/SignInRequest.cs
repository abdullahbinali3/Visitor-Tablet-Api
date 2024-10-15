using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.SignIn
{
    public sealed class SignInRequest
    {
        public Guid WorkplaceVisitId { get; set; }
        public Guid Uid { get; set; }
        public DateTime? SignInDateUtc { get; set; }
    }
}
   