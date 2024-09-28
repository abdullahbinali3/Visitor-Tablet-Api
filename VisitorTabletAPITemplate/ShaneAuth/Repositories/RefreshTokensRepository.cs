using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace VisitorTabletAPITemplate.ShaneAuth.Repositories
{
    public sealed class RefreshTokensRepository
    {
        private readonly AppSettings _appSettings;

        public RefreshTokensRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        public async Task StoreRefreshToken(Guid uid, byte[] refreshToken, DateTime refreshTokenExpiry)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

/*
delete from tblRefreshTokens
where ExpiryDateUtc < @_now
*/

insert into tblRefreshTokens
(Uid
,RefreshToken
,InsertDateUtc
,ExpiryDateUtc)
values
(@uid
,@refreshToken
,@_now
,@expiryDateUtc)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@refreshToken", refreshToken, DbType.Binary, ParameterDirection.Input, 64);
                parameters.Add("@expiryDateUtc", refreshTokenExpiry, DbType.DateTime2, ParameterDirection.Input, 3);

                await sqlConnection.ExecuteAsync(sql, parameters);
            }
        }

        public async Task<bool> ValidateAndConsumeRefreshToken(Guid uid, byte[] refreshToken)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
-- Delete token from database and return it, as refresh tokens can be used one time only.
delete from tblRefreshTokens
output 1
where Uid = @uid
and RefreshToken = @refreshToken
and ExpiryDateUtc > sysutcdatetime()
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@refreshToken", refreshToken, DbType.Binary, ParameterDirection.Input, 64);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(sql, parameters);
            }
        }

        public async Task ClearRefreshTokens(Guid uid)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
delete from tblRefreshTokens
where Uid = @uid
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);

                await sqlConnection.ExecuteAsync(sql, parameters);
            }
        }
    }
}
