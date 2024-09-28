using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromOrganization
{
    public sealed class MasterRemoveUserFromOrganizationEndpoint : Endpoint<MasterRemoveUserFromOrganizationRequest>
    {
        private readonly UserOrganizationsRepository _userOrganizationsRepository;

        public MasterRemoveUserFromOrganizationEndpoint(UserOrganizationsRepository userOrganizationsRepository)
        {
            _userOrganizationsRepository = userOrganizationsRepository;
        }

        public override void Configure()
        {
            Post("/master/users/removeUserFromOrganization");
            SerializerContext(MasterRemoveUserFromOrganizationContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterRemoveUserFromOrganizationRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (UserManagementResult queryResult, UserData? userData) = await _userOrganizationsRepository.MasterRemoveUserFromOrganizationAsync(req, userId.Value, adminUserDisplayName, remoteIpAddress);

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

        private void ValidateInput(MasterRemoveUserFromOrganizationRequest req)
        {
            // Validate input

            // Validate OrganizationId
            if (!req.OrganizationId.HasValue)
            {
                AddError(m => m.OrganizationId!, "Organization Id is required.", "error.organizationIdIsRequired");
            }

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.masterSettings.users.uidIsRequired");
            }

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

        private void ValidateOutput(UserManagementResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case UserManagementResult.Ok:
                    return;
                case UserManagementResult.UserDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError(m => m.Uid!, "The user was deleted since you last accessed this page.", "error.masterSettings.users.deletedSinceAccessedPage");
                    break;
                case UserManagementResult.UserDidNotExistInOrganization:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData, MasterRemoveUserFromOrganizationContext.Default.UserData));
                    AddError("The user was removed from the organization since you last accessed this page.", "error.masterSettings.users.userRemovedFromOrganizationSinceAccessedPage");
                    break;
                case UserManagementResult.ConcurrencyKeyInvalid:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData, MasterRemoveUserFromOrganizationContext.Default.UserData));
                    AddError("The user's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit again if you still wish to remove the user from the organization.", "error.masterSettings.users.deleteConcurrencyKeyInvalid.organization");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
