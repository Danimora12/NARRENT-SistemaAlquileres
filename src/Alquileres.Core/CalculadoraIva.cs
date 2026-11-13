namespace Alquileres.Core;

public readonly record struct ResultadoIva(long TotalCentimos, long BaseCentimos, long IvaCentimos);

/// <summary>Resultado de una línea: subtotal − descuento = total; total = base + IVA.</summary>
public readonly record struct ResultadoLinea(
    long SubtotalCentimos, long DescuentoCentimos, long TotalCentimos, long BaseCentimos, long IvaCentimos);

/// <summary>
/// Cálculo de IVA cuando el precio YA lo incluye.
///   base = total / 1,13      (redondeado a céntimos)
///   IVA  = total − base      (así base + IVA siempre suman exactamente el total)
/// Ojo: el IVA NO es el 13 % del total; es total × 13/113 (≈ 11,50 %).
/// </summary>
public static class CalculadoraIva
{
    public const int TasaGeneral = 13;
    public const int DescuentoMaximo = 15;

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
        var r = CalcularLinea(precioUnitarioCentimos, cantidad, dias, 0, tasa);
        return new ResultadoIva(r.TotalCentimos, r.BaseCentimos, r.IvaCentimos);
    }

    /// <summary>
    /// precio × cantidad × días, menos el descuento (0 o 1–15 %).
    /// El descuento se redondea al colón entero para que el total no quede con céntimos.
    /// El IVA se calcula sobre el total YA descontado (es lo que realmente se cobra).
    /// </summary>
    public static ResultadoLinea CalcularLinea(long precioUnitarioCentimos, int cantidad, int dias, int descuentoPct, int tasa = TasaGeneral)
    {
        if (precioUnitarioCentimos < 0) throw new ArgumentOutOfRangeException(nameof(precioUnitarioCentimos), "El precio no puede ser negativo.");
        if (cantidad <= 0) throw new ArgumentOutOfRangeException(nameof(cantidad), "La cantidad debe ser mayor que cero.");
        if (dias <= 0) throw new ArgumentOutOfRangeException(nameof(dias), "Los días deben ser mayores que cero.");
        if (descuentoPct < 0 || descuentoPct > DescuentoMaximo)
            throw new ArgumentOutOfRangeException(nameof(descuentoPct), $"El descuento debe estar entre 0 y {DescuentoMaximo} %.");

        long subtotal = checked(precioUnitarioCentimos * cantidad * dias);
        long descuento = 0;
        if (descuentoPct > 0)
        {
            decimal colones = subtotal * descuentoPct / 100m / 100m;
            descuento = (long)Math.Round(colones, 0, MidpointRounding.AwayFromZero) * 100;
            descuento = Math.Min(descuento, subtotal);
        }
        var iva = DesglosarTotal(subtotal - descuento, tasa);
        return new ResultadoLinea(subtotal, descuento, iva.TotalCentimos, iva.BaseCentimos, iva.IvaCentimos);
    }
}
