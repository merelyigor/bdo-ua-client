using System.Net.Sockets;
using System.Security.Authentication;
using BdoClient.Api;

namespace BdoClient.Tests.Api;

public class NetworkDiagnosticsTests
{
    [Fact]
    public void HttpRequestException_InnerAuthException_ContainsFields()
    {
        var inner = new AuthenticationException("Certificate chain invalid");
        var ex = new HttpRequestException("The SSL connection could not be established, see inner exception.", inner);

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("exception=HttpRequestException", diag);
        Assert.Contains("inner_exception=AuthenticationException", diag);
        Assert.Contains("inner_message=\"Certificate chain invalid\"", diag);
    }

    [Fact]
    public void HttpRequestException_InnerSocketException_ContainsSocketErrorCode()
    {
        var inner = new SocketException((int)SocketError.ConnectionRefused);
        var ex = new HttpRequestException("Connection refused", inner);

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("exception=HttpRequestException", diag);
        Assert.Contains("inner_exception=SocketException", diag);
        Assert.Contains("socket_error=ConnectionRefused", diag);
    }

    [Fact]
    public void HttpRequestException_HttpRequestError_ContainsField()
    {
        var ex = new HttpRequestException(HttpRequestError.SecureConnectionError, "SSL error");

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("http_request_error=SecureConnectionError", diag);
    }

    [Fact]
    public void HttpRequestException_NoInner_NoInnerFields()
    {
        var ex = new HttpRequestException("Simple error");

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("exception=HttpRequestException", diag);
        Assert.Contains("message=\"Simple error\"", diag);
        Assert.DoesNotContain("inner_exception", diag);
    }

    [Fact]
    public void NonHttpRequestException_UsesTypeName()
    {
        var ex = new InvalidOperationException("test");

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("exception=InvalidOperationException", diag);
        Assert.Contains("message=\"test\"", diag);
    }

    [Fact]
    public void LongMessage_Truncated()
    {
        var longMsg = new string('x', 300);
        var ex = new HttpRequestException(longMsg);

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("...", diag);
        Assert.DoesNotContain(new string('x', 300), diag);
    }

    [Fact]
    public void Inner2Exception_ContainsFields()
    {
        var inner2 = new Exception("root cause");
        var inner = new AuthenticationException("auth failed", inner2);
        var ex = new HttpRequestException("SSL error", inner);

        var diag = NetworkDiagnostics.FormatNetworkError(ex);

        Assert.Contains("inner2_exception=Exception", diag);
        Assert.Contains("inner2_message=\"root cause\"", diag);
    }
}
