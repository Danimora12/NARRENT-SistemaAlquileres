using System.Globalization;

namespace Alquileres.Core;

/// <summary>
/// Utilidades de dinero. Todos los montos se guardan como enteros en céntimos
/// para que las sumas sean exactas (nunca se usa double).
/// </summary>
public static class Dinero
{
    private static readonly NumberFormatInfo Formato = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberDecimalDigits = 2
    };

    private static readonly string[] Meses =
    {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Setiembre", "Octubre", "Noviembre", "Diciembre"
    };

    public static long ACentimos(decimal monto) =>
        (long)Math.Round(monto * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal ADecimal(long centimos) => centimos / 100m;

    /// <summary>Ejemplo: 11300000 → "₡113.000,00".</summary>
    public static string Formatear(long centimos) =>
        "₡" + ADecimal(centimos).ToString("N2", Formato);

    public static string NombreMes(int mes) => Meses[mes - 1];
}
