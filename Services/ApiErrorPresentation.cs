using BdoClient.Api;

namespace BdoClient.Services;

internal static class ApiErrorPresentation
{
    public static string GetUserMessage(ApiErrorKind kind, string? technicalMessage = null)
    {
        return kind switch
        {
            ApiErrorKind.Timeout => "Сервер не відповів вчасно.",
            ApiErrorKind.Network => "Не вдалося підключитися до сервера.",
            ApiErrorKind.Http => "Сервер повернув помилку.",
            ApiErrorKind.InvalidResponse => "Сервер повернув некоректні дані.",
            ApiErrorKind.Cancelled => "Запит скасовано.",
            ApiErrorKind.Unexpected => "Неочікувана помилка при зверненні до сервера.",
            _ => "Не вдалося завантажити режими локалізації."
        };
    }
}
