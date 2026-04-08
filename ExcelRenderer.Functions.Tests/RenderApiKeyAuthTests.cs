using ExcelRenderer.Functions;
using Xunit;

namespace ExcelRenderer.Functions.Tests;

public sealed class RenderApiKeyAuthTests
{
    [Fact]
    public void Missing_server_key_when_configured_key_empty()
    {
        var r = RenderApiKeyAuth.ValidateCredentials(null, "a", null);
        Assert.Equal(RenderApiKeyAuthResult.MissingServerKey, r);
        r = RenderApiKeyAuth.ValidateCredentials("   ", "secret", null);
        Assert.Equal(RenderApiKeyAuthResult.MissingServerKey, r);
    }

    [Fact]
    public void Ok_when_x_api_key_matches()
    {
        var r = RenderApiKeyAuth.ValidateCredentials("secret", "secret", null);
        Assert.Equal(RenderApiKeyAuthResult.Ok, r);
    }

    [Fact]
    public void Ok_when_bearer_token_matches()
    {
        var r = RenderApiKeyAuth.ValidateCredentials("secret", null, "secret");
        Assert.Equal(RenderApiKeyAuthResult.Ok, r);
    }

    [Fact]
    public void Missing_or_invalid_when_no_match()
    {
        var r = RenderApiKeyAuth.ValidateCredentials("secret", "wrong", null);
        Assert.Equal(RenderApiKeyAuthResult.MissingOrInvalidClientKey, r);
    }
}
