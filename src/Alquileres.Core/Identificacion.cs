using System.Text.RegularExpressions;

namespace Alquileres.Core;

public enum TipoIdentificacion
{
    CedulaFisica,
    CedulaJuridica,
    Dimex,
    Nite,
    Pasaporte
}

/// <summary>
/// Reglas de los tipos de identificación de Costa Rica. El número se guarda sin
/// guiones ni espacios y en mayúsculas; los guiones solo se agregan para mostrarlo.
/// </summary>
public static class Identificaciones
{
    public static readonly TipoIdentificacion[] Todos = Enum.GetValues<TipoIdentificacion>();

    public static string Nombre(TipoIdentificacion t) => t switch
    {
        TipoIdentificacion.CedulaFisica => "Cédula física",
        TipoIdentificacion.CedulaJuridica => "Cédula jurídica",
        TipoIdentificacion.Dimex => "DIMEX",
        TipoIdentificacion.Nite => "NITE (Hacienda)",
        TipoIdentificacion.Pasaporte => "Pasaporte",
        _ => t.ToString()
    };

    /// <summary>Caracteres máximos que se pueden escribir (sin guiones).</summary>
    public static int Largo(TipoIdentificacion t) => t switch
    {
        TipoIdentificacion.CedulaFisica => 9,
        TipoIdentificacion.CedulaJuridica => 10,
        TipoIdentificacion.Dimex => 12,
        TipoIdentificacion.Nite => 10,
        _ => 20
    };

    /// <summary>Si acepta letras además de números.</summary>
    public static bool AceptaLetras(TipoIdentificacion t) =>
        t is TipoIdentificacion.CedulaJuridica or TipoIdentificacion.Pasaporte;

    /// <summary>Formato y ejemplo, para mostrar debajo del campo.</summary>
    public static string Ayuda(TipoIdentificacion t) => t switch
    {
        TipoIdentificacion.CedulaFisica => "9 dígitos (P-TTTT-AAAA). Ej.: 109130259",
        TipoIdentificacion.CedulaJuridica => "10 caracteres (3-TTT-CCCCCC). Ej.: 3101981657 o 3101A00001",
        TipoIdentificacion.Dimex => "11 o 12 dígitos, sin guiones. Ej.: 155812345678",
        TipoIdentificacion.Nite => "10 dígitos (3-120-CCCCCC o 3-130-CCCCCC). Ej.: 3120123456",
        _ => "Letras y números, hasta 20 caracteres. Ej.: A12345678"
    };

    private static readonly Dictionary<TipoIdentificacion, Regex> Patrones = new()
    {
        // Provincia 1-9 + 8 dígitos
        [TipoIdentificacion.CedulaFisica] = new(@"^[1-9]\d{8}$"),
        // 2 = Gobierno central, 3 = personas jurídicas, 4 = instituciones autónomas
        [TipoIdentificacion.CedulaJuridica] = new(@"^[2-4]\d{3}[0-9A-Z]{6}$"),
        [TipoIdentificacion.Dimex] = new(@"^[1-9]\d{10,11}$"),
        [TipoIdentificacion.Nite] = new(@"^31[23]0\d{6}$"),
        [TipoIdentificacion.Pasaporte] = new(@"^[A-Z0-9]{5,20}$"),
    };

    /// <summary>Quita guiones y espacios y pasa a mayúsculas.</summary>
    public static string Normalizar(string? numero) =>
        Regex.Replace(numero ?? "", @"[\s\-]", "").ToUpperInvariant();

    public static bool EsValida(TipoIdentificacion t, string? numero) => Patrones[t].IsMatch(Normalizar(numero));

    /// <summary>Devuelve el número normalizado o lanza un error explicando el formato esperado.</summary>
    public static string Validar(TipoIdentificacion t, string? numero)
    {
        string n = Normalizar(numero);
        if (n.Length == 0) throw new ReglaNegocioException("El número de identificación es obligatorio.");
        if (!Patrones[t].IsMatch(n))
            throw new ReglaNegocioException($"El número no es una {Nombre(t).ToLowerInvariant()} válida. Formato: {Ayuda(t)}.");
        return n;
    }

    /// <summary>Con guiones para mostrar: 109130259 → 1-0913-0259.</summary>
    public static string Formatear(TipoIdentificacion? t, string? numero)
    {
        string n = Normalizar(numero);
        if (t is null || !EsValida(t.Value, n)) return numero ?? "";
        return t switch
        {
            TipoIdentificacion.CedulaFisica => $"{n[0]}-{n[1..5]}-{n[5..]}",
            TipoIdentificacion.CedulaJuridica or TipoIdentificacion.Nite => $"{n[0]}-{n[1..4]}-{n[4..]}",
            _ => n
        };
    }
}
