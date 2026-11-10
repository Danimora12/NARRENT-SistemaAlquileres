namespace Alquileres.Core;

/// <summary>Producto que se alquila. El precio YA incluye el IVA.</summary>
public class Producto
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    /// <summary>Precio con IVA incluido, en céntimos (₡1 = 100).</summary>
    public long PrecioConIvaCentimos { get; set; }
    public bool Activo { get; set; } = true;

    public decimal PrecioConIva
    {
        get => Dinero.ADecimal(PrecioConIvaCentimos);
        set => PrecioConIvaCentimos = Dinero.ACentimos(value);
    }

    public override string ToString() => Nombre;
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

public class Orden
{
    public long Id { get; set; }
    /// <summary>Fecha de la orden. Es la que define el mes del reporte de IVA.</summary>
    public DateTime Fecha { get; set; } = DateTime.Today;
    public long ClienteId { get; set; }
    public string ClienteNombre { get; set; } = "";
    public DateTime FechaInicio { get; set; } = DateTime.Today;
    public DateTime? FechaFin { get; set; }
    public EstadoOrden Estado { get; set; } = EstadoOrden.Activa;
    public string? Notas { get; set; }
    public string? MotivoAnulacion { get; set; }
    public List<LineaOrden> Lineas { get; set; } = new();

    // Totales (se llenan al leer de la base de datos o al calcular).
    public long TotalCentimos { get; set; }
    public long BaseCentimos { get; set; }
    public long IvaCentimos { get; set; }

    public string Numero => Id.ToString("D5");
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

    public long TotalCentimos { get; set; }
    public long BaseCentimos { get; set; }
    public long IvaCentimos { get; set; }

    /// <summary>Recalcula total, base e IVA de la línea a partir de precio, cantidad y días.</summary>
    public void Calcular()
    {
        var r = CalculadoraIva.Calcular(PrecioUnitarioCentimos, Cantidad, Dias, TasaIva);
        TotalCentimos = r.TotalCentimos;
        BaseCentimos = r.BaseCentimos;
        IvaCentimos = r.IvaCentimos;
    }
}

public record ResumenMensual(
    int Anio,
    int Mes,
    int CantidadOrdenes,
    long TotalCentimos,
    long BaseCentimos,
    long IvaCentimos)
{
    public string NombreMes => Dinero.NombreMes(Mes);
}
