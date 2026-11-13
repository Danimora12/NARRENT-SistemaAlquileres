namespace Alquileres.Core;

/// <summary>Producto que se alquila. El precio YA incluye el IVA.</summary>
public class Producto
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Precio con IVA incluido, en céntimos (₡1 = 100).</summary>
    public long PrecioConIvaCentimos { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>
    /// Cantidad en inventario. Si el producto tiene características (variantes),
    /// es la suma de sus cantidades y no se edita a mano.
    /// </summary>
    public int Inventario { get; set; }
    /// <summary>Cantidad de características de inventario registradas (ej. colores).</summary>
    public int CantidadVariantes { get; set; }

    public bool TieneVariantes => CantidadVariantes > 0;

    public decimal PrecioConIva
    {
        get => Dinero.ADecimal(PrecioConIvaCentimos);
        set => PrecioConIvaCentimos = Dinero.ACentimos(value);
    }

    public override string ToString() => Nombre;
}

/// <summary>Característica de inventario de un producto. Ej.: Manteles → "Blanco", 20.</summary>
public class VarianteInventario
{
    public long Id { get; set; }
    public long ProductoId { get; set; }
    public string Descripcion { get; set; } = "";
    public int Cantidad { get; set; }
}

public class Cliente
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Identificacion { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Notas { get; set; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Identificacion) ? Nombre : $"{Nombre} ({Identificacion})";
}

public enum EstadoOrden
{
    Activa,
    Devuelta,
    Anulada
}

public enum MetodoPago
{
    Sinpe,
    Transferencia,
    Efectivo,
    Tarjeta
}

public static class MetodosPago
{
    public static readonly MetodoPago[] Todos = Enum.GetValues<MetodoPago>();

    public static string Nombre(MetodoPago m) => m switch
    {
        MetodoPago.Sinpe => "SINPE Móvil",
        MetodoPago.Transferencia => "Transferencia",
        MetodoPago.Efectivo => "Efectivo",
        MetodoPago.Tarjeta => "Tarjeta",
        _ => m.ToString()
    };
}

public class Pago
{
    public long Id { get; set; }
    public long OrdenId { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public MetodoPago Metodo { get; set; } = MetodoPago.Sinpe;
    public long MontoCentimos { get; set; }
    public DateTime CreadoEn { get; set; }

    public string MetodoTexto => MetodosPago.Nombre(Metodo);
}

public class Orden
{
    public long Id { get; set; }
    /// <summary>Número visible: año + mes + consecutivo del mes. Ej.: 202612001.</summary>
    public string Numero { get; set; } = "";
    /// <summary>Fecha de la orden. Define el mes del número y del flujo de caja.</summary>
    public DateTime Fecha { get; set; } = DateTime.Today;
    public long ClienteId { get; set; }
    public string ClienteNombre { get; set; } = "";
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    /// <summary>Fecha real de devolución (se llena al marcar la orden como devuelta).</summary>
    public DateTime? FechaFin { get; set; }
    public EstadoOrden Estado { get; set; } = EstadoOrden.Activa;
    public string? Notas { get; set; }
    public string? MotivoAnulacion { get; set; }

    // Eliminación (papelera): la orden se oculta, pero se puede restaurar.
    public bool Eliminada { get; set; }
    public DateTime? EliminadaEn { get; set; }
    public string? EliminadaPor { get; set; }
    public string? MotivoEliminacion { get; set; }

    public List<LineaOrden> Lineas { get; set; } = new();
    public List<Pago> Pagos { get; set; } = new();

    // Totales (se llenan al leer de la base de datos o al calcular).
    /// <summary>Total a pagar, ya con descuento e IVA incluido.</summary>
    public long TotalCentimos { get; set; }
    public long BaseCentimos { get; set; }
    public long IvaCentimos { get; set; }
    public long DescuentoCentimos { get; set; }
    public long PagadoCentimos { get; set; }

    /// <summary>Total antes de descuento.</summary>
    public long SubtotalCentimos => TotalCentimos + DescuentoCentimos;
    public long SaldoCentimos => TotalCentimos - PagadoCentimos;

    public string EstadoPago =>
        PagadoCentimos <= 0 ? "Sin pago"
        : SaldoCentimos <= 0 ? "Pagada"
        : "Abono";

    /// <summary>Ej.: "10 % a toda la orden" o "5 % en Manteles". Vacío si no hay descuento.</summary>
    public string DescribirDescuento()
    {
        var conDescuento = Lineas.Where(l => l.DescuentoPct > 0).ToList();
        if (conDescuento.Count == 0) return "";
        if (Lineas.Count > 1 && conDescuento.Count == Lineas.Count && conDescuento.Select(l => l.DescuentoPct).Distinct().Count() == 1)
            return $"{conDescuento[0].DescuentoPct} % a toda la orden";
        return string.Join(", ", conDescuento.Select(l => $"{l.DescuentoPct} % en {l.ProductoNombre}"));
    }
}

public class LineaOrden
{
    public long Id { get; set; }
    public long OrdenId { get; set; }
    public long ProductoId { get; set; }
    /// <summary>Copia del nombre al momento de la orden (por si luego cambia).</summary>
    public string ProductoNombre { get; set; } = "";
    /// <summary>Precio unitario con IVA al momento de la orden, en céntimos.</summary>
    public long PrecioUnitarioCentimos { get; set; }
    public int Cantidad { get; set; } = 1;
    public int Dias { get; set; } = 1;
    public int TasaIva { get; set; } = CalculadoraIva.TasaGeneral;
    /// <summary>Descuento aplicado a la línea: 0 (sin descuento) o de 1 a 15 %.</summary>
    public int DescuentoPct { get; set; }

    /// <summary>Total de la línea con descuento (IVA incluido).</summary>
    public long TotalCentimos { get; set; }
    public long BaseCentimos { get; set; }
    public long IvaCentimos { get; set; }
    public long DescuentoCentimos { get; set; }

    public long SubtotalCentimos => TotalCentimos + DescuentoCentimos;

    /// <summary>Recalcula descuento, total, base e IVA de la línea.</summary>
    public void Calcular()
    {
        var r = CalculadoraIva.CalcularLinea(PrecioUnitarioCentimos, Cantidad, Dias, DescuentoPct, TasaIva);
        DescuentoCentimos = r.DescuentoCentimos;
        TotalCentimos = r.TotalCentimos;
        BaseCentimos = r.BaseCentimos;
        IvaCentimos = r.IvaCentimos;
    }
}

/// <summary>Una fila del flujo de caja (un mes).</summary>
public record ResumenMensual(
    int Anio,
    int Mes,
    int CantidadOrdenes,
    long TotalCentimos,
    long BaseCentimos,
    long IvaCentimos,
    long GastosCentimos = 0,
    long IvaGastosCentimos = 0)
{
    public string NombreMes => Dinero.NombreMes(Mes);

    /// <summary>
    /// IVA neto a pagar = IVA cobrado − IVA de las facturas de gastos (crédito fiscal).
    /// Negativo = saldo a favor.
    /// </summary>
    public long IvaNetoCentimos => IvaCentimos - IvaGastosCentimos;

    /// <summary>Utilidad = ingresos netos (sin IVA) − gastos (sin el IVA de las facturas).</summary>
    public long UtilidadCentimos => BaseCentimos - GastosCentimos;
}

/// <summary>Gasto de la empresa (alquiler del local, salarios, servicios, compras, etc.).</summary>
public class Gasto
{
    public long Id { get; set; }
    public DateTime Fecha { get; set; } = DateTime.Today;
    public string Categoria { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string? Proveedor { get; set; }
    public MetodoPago Metodo { get; set; } = MetodoPago.Transferencia;
    /// <summary>Monto total pagado (con IVA si la factura lo trae), en céntimos.</summary>
    public long MontoCentimos { get; set; }
    /// <summary>IVA incluido en la factura (0 si no trae o no se sabe), en céntimos.</summary>
    public long IvaCentimos { get; set; }

    /// <summary>Costo real para la utilidad: el IVA de la factura se recupera como crédito fiscal.</summary>
    public long NetoCentimos => MontoCentimos - IvaCentimos;
    public string MetodoTexto => MetodosPago.Nombre(Metodo);

    public static readonly string[] CategoriasSugeridas =
    {
        "Alquiler del local", "Salarios y cargas sociales", "Servicios (luz, agua, internet, teléfono)",
        "Transporte y combustible", "Mantenimiento y reparaciones", "Compra de productos para alquilar",
        "Limpieza y lavandería", "Publicidad", "Impuestos, patentes y permisos", "Comisiones bancarias", "Otros"
    };
}

/// <summary>Registro de auditoría (pestaña Logs).</summary>
public class EntradaLog
{
    public long Id { get; set; }
    public DateTime FechaHora { get; set; }
    /// <summary>Empleado que autorizó el cambio (null si la acción no pide código).</summary>
    public string? Empleado { get; set; }
    public string Accion { get; set; } = "";
    /// <summary>A qué se refiere: "Orden 202612001", "Producto Manteles", etc.</summary>
    public string Referencia { get; set; } = "";
    public string Detalle { get; set; } = "";
}
