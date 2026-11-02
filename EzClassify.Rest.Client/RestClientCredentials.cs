using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace EzClassify;

/// <summary>
/// Carries caller and Context authorization, or an opaque application credential.
/// </summary>
public sealed class RestClientCredentials
{
    /// <summary>Credentials that add no authentication headers.</summary>
    public static RestClientCredentials None { get; } = new RestClientCredentials(null, null);

    /// <summary>Bearer token identifying the caller, or <c>null</c> when authentication is disabled.</summary>
    public string? AccessToken { get; }

    /// <summary>Signed token authorizing Context operations, or <c>null</c> when not required.</summary>
    public string? ContextToken { get; }

    /// <summary>
    /// Opaque application credential sent only to Context routes. Mutually exclusive
    /// with caller and Context tokens; never decoded, refreshed or replayed by the SDK.
    /// </summary>
    public string? ApplicationCredential { get; }

    /// <summary>Create independent caller and Context credentials.</summary>
    /// <param name="accessToken">Bearer token identifying the caller, or <c>null</c>.</param>
    /// <param name="contextToken">Signed Context authorization token, or <c>null</c>.</param>
    public RestClientCredentials(string? accessToken, string? contextToken)
        : this(accessToken, contextToken, null)
    { }

    /// <summary>Create ordinary credentials or an opaque application credential.</summary>
    /// <param name="accessToken">Caller bearer token, or <c>null</c>.</param>
    /// <param name="contextToken">Context token, or <c>null</c>.</param>
    /// <param name="applicationCredential">Application credential, or <c>null</c>. Supply alone, as nonempty ASCII without controls or surrounding whitespace.</param>
    /// <exception cref="ArgumentException">Credentials are mixed or malformed.</exception>
    public RestClientCredentials(string? accessToken, string? contextToken, string? applicationCredential)
    {
        if (applicationCredential is not null)
        {
            if (accessToken is not null || contextToken is not null)
                throw new ArgumentException("Application credentials cannot be combined with caller or Context tokens", nameof(applicationCredential));
            if (applicationCredential.Length == 0 || applicationCredential[0] == ' ' || applicationCredential[applicationCredential.Length - 1] == ' ')
                throw new ArgumentException("Application credential must be nonempty ASCII without controls or surrounding whitespace", nameof(applicationCredential));
            foreach (var character in applicationCredential)
                if (character < 0x20 || character > 0x7e)
                    throw new ArgumentException("Application credential must be nonempty ASCII without controls or surrounding whitespace", nameof(applicationCredential));
        }
        if (accessToken is not null && string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("accessToken must be null or non-blank", nameof(accessToken));
        if (contextToken is not null && string.IsNullOrWhiteSpace(contextToken))
            throw new ArgumentException("contextToken must be null or non-blank", nameof(contextToken));
        AccessToken = accessToken;
        ContextToken = contextToken;
        ApplicationCredential = applicationCredential;
    }

    internal void ValidateTransport(HttpClient http)
    {
        if (ApplicationCredential is null) return;
        if (http.DefaultRequestHeaders.Contains("Authorization")
            || http.DefaultRequestHeaders.Contains("X-EzClassify-Context-Token")
            || http.DefaultRequestHeaders.Contains("X-EzClassify-Application-Grant"))
            throw new ArgumentException("Application credentials cannot be combined with transport authentication headers", "httpClient");
    }

    internal void Apply(HttpRequestMessage request, bool contextPath)
    {
        if (AccessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (contextPath && ContextToken is not null)
            request.Headers.TryAddWithoutValidation("x-ezclassify-context-token", ContextToken);
        if (contextPath && ApplicationCredential is not null)
            request.Headers.TryAddWithoutValidation("X-EzClassify-Application-Grant", ApplicationCredential);
    }
}
