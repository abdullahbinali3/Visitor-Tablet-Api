using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.GenerateMobileQRCode
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(VisitorTabletGetMobileQRCodeRequest))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class VisitorTabletGetMobileQRCodeContext : JsonSerializerContext { }
}
