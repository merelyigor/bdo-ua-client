using BdoClient.Api;

namespace BdoClient.Tests.Api;

public class ApiResultTests
{
    [Fact]
    public void Success_ReturnsSuccessResult()
    {
        var result = ApiResult<string>.Success("test");
        Assert.True(result.IsSuccess);
        Assert.Equal("test", result.Value);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Failure_ReturnsFailureResult()
    {
        var result = ApiResult<string>.Failure("error");
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("error", result.ErrorMessage);
    }

    [Fact]
    public void Success_WithNullValue_ReturnsSuccess()
    {
        var result = ApiResult<string?>.Success(null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
