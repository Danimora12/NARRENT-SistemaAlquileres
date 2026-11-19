using System.Security.Cryptography;
using System.Text;

namespace Alquileres.Core;

/// <summary>
/// Hash de contraseñas y códigos: PBKDF2-SHA256 con sal aleatoria, guardado como
/// "sal:hash" en Base64. Nunca se guarda el texto original.
/// </summary>
public static class Claves
{
    private const int Iteraciones = 100_000;

    public static string Hash(string clave)
    {
        byte[] sal = RandomNumberGenerator.GetBytes(16);
        byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave), sal, Iteraciones, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(sal)}:{Convert.ToBase64String(h)}";
    }

    /// <summary>Compara en tiempo constante. Los espacios al inicio y al final se ignoran.</summary>
    public static bool Coincide(string? clave, string? guardado)
    {
        if (string.IsNullOrWhiteSpace(clave) || string.IsNullOrEmpty(guardado)) return false;
        var partes = guardado.Split(':');
        if (partes.Length != 2) return false;
        byte[] sal = Convert.FromBase64String(partes[0]);
        byte[] esperado = Convert.FromBase64String(partes[1]);
        byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(clave.Trim()), sal, Iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(h, esperado);
    }
}
