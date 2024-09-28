namespace VisitorTabletAPITemplate.ShaneAuth.Features.Antiforgery
{
    public sealed class TestAntiforgeryEndpoint : EndpointWithoutRequest
    {
        public override void Configure()
        {
            Get("/antiforgery/test");
            Policies("User");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            await SendAsync($"Antiforgery is working");
        }
    }
}
