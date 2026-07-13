namespace Vanished.UI;

public static class AuthErrorMapper
{
    public static string GetGenericMessage(int statusCode = 401)
        => statusCode switch
        {
            401 => "Credenciais inválidas. Verifica os teus dados e tenta novamente.",
            403 => "Não foi possível verificar a tua identidade.",
            429 => "Demasiadas tentativas. Aguarda antes de tentar novamente.",
            _ => "Ocorreu um erro. Tenta novamente."
        };

    public static string GenericCredentials => "Credenciais inválidas. Verifica os teus dados e tenta novamente.";
    public static string GenericIdentity => "Não foi possível verificar a tua identidade.";
}
