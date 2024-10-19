using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.Utilities;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class VisitorTabletOrganizationsRepository
    {
        private readonly AppSettings _appSettings;

        public VisitorTabletOrganizationsRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Retrieves the encryption key for the specified organization.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<string?> GetEncryptionKeyAsync(Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
select EncryptionKey
from tblOrganizations
where id = @organizationId
and Deleted = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                string? encryptedOrganizationEncryptionKey = await sqlConnection.QueryFirstOrDefaultAsync<string>(commandDefinition);

                if (encryptedOrganizationEncryptionKey is null)
                {
                    return null;
                }

                try
                {
                    return StringCipherAesGcm.Decrypt(encryptedOrganizationEncryptionKey, _appSettings.Organization.EncryptionKeyEncryptionKey);
                }
                catch (Exception ex)
                {
                    return null;
                }
            }
        }
    }
}
