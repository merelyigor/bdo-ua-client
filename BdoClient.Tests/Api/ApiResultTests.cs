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
        Assert.Equal(ApiErrorKind.None, result.ErrorKind);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Failure_ReturnsFailureResult()
    {
        var result = ApiResult<string>.Failure(ApiErrorKind.Network, "error");
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ApiErrorKind.Network, result.ErrorKind);
        Assert.Equal("error", result.ErrorMessage);
    }

    [Fact]
    public void Success_WithNullValue_ReturnsSuccess()
    {
        var result = ApiResult<string?>.Success(null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Failure_VariousErrorKinds()
    {
        Assert.Equal(ApiErrorKind.Cancelled, ApiResult<int>.Failure(ApiErrorKind.Cancelled, "c").ErrorKind);
        Assert.Equal(ApiErrorKind.Timeout, ApiResult<int>.Failure(ApiErrorKind.Timeout, "t").ErrorKind);
        Assert.Equal(ApiErrorKind.InvalidResponse, ApiResult<int>.Failure(ApiErrorKind.InvalidResponse, "i").ErrorKind);
    }
}
