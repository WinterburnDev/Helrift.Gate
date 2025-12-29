using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;

public sealed class GoogleAuthDelegatingHandler : DelegatingHandler
{
    private readonly GoogleCredential _credential;

    public GoogleAuthDelegatingHandler(GoogleCredential credential)
        => _credential = credential;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct)
    {
        var token = await _credential
            .UnderlyingCredential
            .GetAccessTokenForRequestAsync(null, ct);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, ct);
    }
}
