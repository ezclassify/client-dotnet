using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace EzClassify;

/// <summary>
/// Carries the independent caller identity and Context authorization used by the REST API.
/// </summary>
public sealed class RestClientCredentials
{
    /// <summary>Credentials that add no authentication headers.</summary>
    public static RestClientCredentials None { get; } = new RestClientCredentials(null, null);

    /// <summary>Bearer token identifying the caller, or <c>null</c> when authentication is disabled.</summary>
    public string? AccessToken { get; }

    /// <summary>Signed token authorizing Context operations, or <c>null</c> when not required.</summary>
    public string? ContextToken { get; }

    /// <summary>Create independent caller and Context credentials.</summary>
    /// <param name="accessToken">Bearer token identifying the caller, or <c>null</c>.</param>
    /// <param name="contextToken">Signed Context authorization token, or <c>null</c>.</param>
    public RestClientCredentials(string? accessToken, string? contextToken)
    {
        if (accessToken is not null && string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("accessToken must be null or non-blank", nameof(accessToken));
        if (contextToken is not null && string.IsNullOrWhiteSpace(contextToken))
            throw new ArgumentException("contextToken must be null or non-blank", nameof(contextToken));
        AccessToken = accessToken;
        ContextToken = contextToken;
    }

    internal void Apply(HttpRequestMessage request, bool contextPath)
    {
        if (AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (contextPath && ContextToken is not null)
            request.Headers.TryAddWithoutValidation("x-ezclassify-context-token", ContextToken);
    }
}
