namespace Alquileres.Core;

public readonly record struct ResultadoIva(long TotalCentimos, long BaseCentimos, long IvaCentimos);

/// <summary>
/// Cálculo de IVA cuando el precio YA lo incluye.
///   base = total / 1,13      (redondeado a céntimos)
///   IVA  = total − base      (así base + IVA siempre suman exactamente el total)
/// Ojo: el IVA NO es el 13 % del total; es total × 13/113 (≈ 11,50 %).
/// </summary>
public static class CalculadoraIva
{
    public const int TasaGeneral = 13;

    public static ResultadoIva DesglosarTotal(long totalCentimos, int tasa = TasaGeneral)
    {
        if (totalCentimos < 0) throw new ArgumentOutOfRangeException(nameof(totalCentimos));
        if (tasa < 0) throw new ArgumentOutOfRangeException(nameof(tasa));

        decimal baseExacta = totalCentimos * 100m / (100m + tasa);
        long baseCentimos = (long)Math.Round(baseExacta, 0, MidpointRounding.AwayFromZero);
        return new ResultadoIva(totalCentimos, baseCentimos, totalCentimos - baseCentimos);
    }

    public static ResultadoIva Calcular(long precioUnitarioCentimos, int cantidad, int dias, int tasa = TasaGeneral)
    {
        if (precioUnitarioCentimos < 0) throw new ArgumentOutOfRangeException(nameof(precioUnitarioCentimos), "El precio no puede ser negativo.");
        if (cantidad <= 0) throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        if (dias <= 0) throw new ArgumentOutOfRangeException(nameof(dias), "Los días deben ser mayores que cero.");

        long total = checked(precioUnitarioCentimos * cantidad * dias);
        return DesglosarTotal(total, tasa);
    }
}
