using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.ShaneAuth.Models;

namespace VisitorTabletAPITemplate.ShaneAuth.Services
{
    public sealed class MicrosoftAccountService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public MicrosoftAccountService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<MicrosoftAccountsResult<MicrosoftTokenResponse>> GetTokenFromAuthorizationCodeAsync(Guid clientId, string clientSecret, Guid tenantId, string scope, string redirectUri, string authorizationCode)
        {
            HttpClient httpClient = _httpClientFactory.CreateClient("graph-api");

            using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token"))
            {
                requestMessage.Content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>("client_id", clientId.ToString()),
                    new KeyValuePair<string, string>("scope", scope),
                    new KeyValuePair<string, string>("code", authorizationCode),
                    new KeyValuePair<string, string>("redirect_uri", redirectUri),
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                });

                using (HttpResponseMessage responseMessage = await httpClient.SendAsync(requestMessage))
                {
                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        MicrosoftErrorResponse? microsoftErrorResponse = await responseMessage.Content.ReadFromJsonAsync<MicrosoftErrorResponse>();

                        return new MicrosoftAccountsResult<MicrosoftTokenResponse>
                        {
                            Success = false,
                            Error = microsoftErrorResponse?.error_description
                        };
                    }

                    MicrosoftTokenResponse? microsoftTokenResponse = await responseMessage.Content.ReadFromJsonAsync<MicrosoftTokenResponse>();

                    if (microsoftTokenResponse is null)
                    {
                        return new MicrosoftAccountsResult<MicrosoftTokenResponse>
                        {
                            Success = false,
                            Error = null
                        };
                    }

                    return new MicrosoftAccountsResult<MicrosoftTokenResponse>
                    {
                        Success = true,
                        Result = microsoftTokenResponse
                    };
                }
            }
        }

        public async Task<MicrosoftAccountsResult<MicrosoftUserData>> GetUserData(string access_token, CancellationToken cancellationToken = default)
        {
            HttpClient httpClient = _httpClientFactory.CreateClient("graph-api");

            using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me"))
            {
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access_token);

                using (HttpResponseMessage responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken))
                {
                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        MicrosoftErrorResponse? microsoftErrorResponse = await responseMessage.Content.ReadFromJsonAsync<MicrosoftErrorResponse>();

                        return new MicrosoftAccountsResult<MicrosoftUserData>
                        {
                            Success = false,
                            Error = microsoftErrorResponse?.error_description
                        };
                    }

                    MicrosoftUserData? microsoftUserData = await responseMessage.Content.ReadFromJsonAsync<MicrosoftUserData>(cancellationToken);

                    if (microsoftUserData is null)
                    {
                        return new MicrosoftAccountsResult<MicrosoftUserData>
                        {
                            Success = false,
                            Error = null
                        };
                    }

                    JwtSecurityToken jwtSecurityToken = new JwtSecurityToken(access_token);

                    MicrosoftAccountsResult<MicrosoftUserData> result = new MicrosoftAccountsResult<MicrosoftUserData>
                    {
                        Success = true,
                        Result = microsoftUserData
                    };

                    foreach (Claim? claim in jwtSecurityToken.Claims)
                    {
                        if (claim.Type == "tid")
                        {
                            if (Guid.TryParse(claim.Value, out Guid tenantId))
                            {
                                result.Result.TenantId = tenantId;
                            }
                        }
                        else if (claim.Type == "oid")
                        {
                            if (Guid.TryParse(claim.Value, out Guid objectId))
                            {
                                result.Result.ObjectId = objectId;
                            }
                        }
                    }

                    // Trim strings
                    result.Result.givenName = result.Result.givenName?.Trim();
                    result.Result.surname = result.Result.surname?.Trim();
                    result.Result.mail = result.Result.mail?.ToLowerInvariant().Trim();
                    result.Result.userPrincipalName = result.Result.userPrincipalName?.ToLowerInvariant().Trim();

                    return result;
                }
            }
        }

        public async Task<MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>> GetUserProfilePhoto(string access_token, CancellationToken cancellationToken = default)
        {
            HttpClient httpClient = _httpClientFactory.CreateClient("graph-api");

            using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/photo/$value"))
            {
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access_token);

                using (HttpResponseMessage responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken))
                {
                    if (responseMessage.StatusCode == System.Net.HttpStatusCode.NotFound || (responseMessage.Content.Headers.ContentLength ?? 0) == 0)
                    {
                        return new MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>
                        {
                            Result = null,
                            Success = false,
                            Error = null
                        };
                    }

                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        try
                        {
                            MicrosoftErrorResponse? microsoftErrorResponse = await responseMessage.Content.ReadFromJsonAsync<MicrosoftErrorResponse>(cancellationToken);

                            return new MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>
                            {
                                Result = null,
                                Success = false,
                                Error = microsoftErrorResponse?.error_description
                            };
                        }
                        catch
                        {
                            return new MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>
                            {
                                Result = null,
                                Success = false,
                                Error = null
                            };
                        }
                    }

                    long contentLength = responseMessage.Content.Headers.ContentLength ?? 0;

                    if (contentLength == 0)
                    {
                        return new MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>
                        {
                            Result = null,
                            Success = false,
                            Error = null
                        };
                    }

                    // Inspect content of image
                    ContentInspectorResultWithMemoryStream? contentInspectorResultWithMemoryStream = await ImageStorageHelpers.CopyFormStreamAndInspectImageAsync(
                        await responseMessage.Content.ReadAsStreamAsync(cancellationToken),
                        responseMessage.Content.Headers.ContentDisposition?.FileNameStar,
                        cancellationToken);

                    if (contentInspectorResultWithMemoryStream is null)
                    {
                        return new MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>
                        {
                            Result = null,
                            Success = false,
                            Error = null
                        };
                    }

                    MicrosoftAccountsResult<MicrosoftUserProfilePhotoData> result = new MicrosoftAccountsResult<MicrosoftUserProfilePhotoData>
                    {
                        Success = true,
                        Result = new MicrosoftUserProfilePhotoData
                        {
                            ProfilePhoto = contentInspectorResultWithMemoryStream
                        }
                    };

                    JwtSecurityToken jwtSecurityToken = new JwtSecurityToken(access_token);

                    foreach (Claim? claim in jwtSecurityToken.Claims)
                    {
                        if (claim.Type == "tid")
                        {
                            if (Guid.TryParse(claim.Value, out Guid tenantId))
                            {
                                result.Result.TenantId = tenantId;
                            }
                        }
                        else if (claim.Type == "oid")
                        {
                            if (Guid.TryParse(claim.Value, out Guid objectId))
                            {
                                result.Result.ObjectId = objectId;
                            }
                        }
                    }

                    return result;
                }
            }
        }
    }
}
