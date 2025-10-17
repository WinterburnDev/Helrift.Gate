using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;

public sealed class GoogleAuthDelegatingHandler : DelegatingHandler
{
    private readonly GoogleCredential _credential;
    private string? _accessToken;
    private DateTimeOffset _expiresAt;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GoogleAuthDelegatingHandler(GoogleCredential credential)
        => _credential = credential;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return await base.SendAsync(request, ct);
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        // quick check outside lock for perf
        if (!NeedRefresh()) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (!NeedRefresh()) return;

            // Request a new access token from the service account credentials
            var token = await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
            _accessToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddMinutes(40); // assume 1-hour lifetime, refresh at T-5
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool NeedRefresh()
        => string.IsNullOrEmpty(_accessToken) || DateTimeOffset.UtcNow >= _expiresAt.AddMinutes(-5);
}
