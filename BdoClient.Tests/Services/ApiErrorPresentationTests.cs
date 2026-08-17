using BdoClient.Api;
using BdoClient.Services;

namespace BdoClient.Tests.Services;

public class ApiErrorPresentationTests
{
    [Fact]
    public void Timeout_ReturnsUkrainianMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.Timeout);

        Assert.Equal("Сервер не відповів вчасно.", result);
    }

    [Fact]
    public void Network_ReturnsUkrainianMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.Network);

        Assert.Equal("Не вдалося підключитися до сервера.", result);
    }

    [Fact]
    public void Http_ReturnsUkrainianMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.Http);

        Assert.Equal("Сервер повернув помилку.", result);
    }

    [Fact]
    public void InvalidResponse_ReturnsUkrainianMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.InvalidResponse);

        Assert.Equal("Сервер повернув некоректні дані.", result);
    }

    [Fact]
    public void Cancelled_ReturnsUkrainianMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.Cancelled);

        Assert.Equal("Запит скасовано.", result);
    }

    [Fact]
    public void Unexpected_ReturnsUkrainianMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.Unexpected);

        Assert.Equal("Неочікувана помилка при зверненні до сервера.", result);
    }

    [Fact]
    public void None_ReturnsFallbackMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.None);

        Assert.Equal("Не вдалося завантажити режими локалізації.", result);
    }

    [Fact]
    public void Timeout_WithRealTimeoutString_StillReturnsTimeoutMessage()
    {
        var result = ApiErrorPresentation.GetUserMessage(ApiErrorKind.Timeout, "Request timed out after 30s");

        Assert.Equal("Сервер не відповів вчасно.", result);
    }
}
