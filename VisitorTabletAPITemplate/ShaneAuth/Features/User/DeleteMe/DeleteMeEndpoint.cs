using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.DeleteMe
{
    public sealed class DeleteMeEndpoint : Endpoint<DeleteMeRequest>
    {
        private readonly UsersRepository _usersRepository;

        public DeleteMeEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/user/delete");
            SerializerContext(DeleteMeContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(DeleteMeRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ValidateInput(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, UserData? userData) =  await _usersRepository.MasterDeleteUserAsync(new() { Uid = userId!.Value, ConcurrencyKey = req.ConcurrencyKey! }, userId.Value, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        private void ValidateInput(DeleteMeRequest req, Guid userId)
        {
            // Validate input

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The user was deleted since you last accessed this page.", "error.profile.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData, DeleteMeContext.Default.UserData));
                    AddError("The user's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit again if you still wish to remove the user.", "error.profile.deleteConcurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
