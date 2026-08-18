using System.Net.Sockets;

namespace BdoClient.Api;

internal static class NetworkDiagnostics
{
    public static string FormatNetworkError(Exception ex)
    {
        var parts = new List<string>();

        if (ex is HttpRequestException httpEx)
        {
            parts.Add("exception=HttpRequestException");

            if (httpEx.HttpRequestError != HttpRequestError.Unknown)
                parts.Add($"http_request_error={httpEx.HttpRequestError}");

            parts.Add($"message=\"{Truncate(httpEx.Message, 120)}\"");

            var inner = httpEx.InnerException;
            if (inner != null)
            {
                parts.Add($"inner_exception={inner.GetType().Name}");
                parts.Add($"inner_message=\"{Truncate(inner.Message, 120)}\"");

                if (inner is SocketException sockEx)
                    parts.Add($"socket_error={sockEx.SocketErrorCode}");

                if (inner.InnerException != null)
                {
                    parts.Add($"inner2_exception={inner.InnerException.GetType().Name}");
                    parts.Add($"inner2_message=\"{Truncate(inner.InnerException.Message, 120)}\"");
                }
            }
        }
        else
        {
            parts.Add($"exception={ex.GetType().Name}");
            parts.Add($"message=\"{Truncate(ex.Message, 120)}\"");
        }

        return string.Join(" ", parts);
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLen ? value : value[..maxLen] + "...";
    }
}
