using Alquileres.Core;
using ClosedXML.Excel;

namespace Alquileres.Tests;

public class PruebasIva
{
    [Fact]
    public void Iva_incluido_es_13_sobre_113_no_13_por_ciento_del_total()
    {
        var r = CalculadoraIva.DesglosarTotal(Dinero.ACentimos(113_000m));
        Assert.Equal(Dinero.ACentimos(100_000m), r.BaseCentimos);
        Assert.Equal(Dinero.ACentimos(13_000m), r.IvaCentimos);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(12_345_67)]
    [InlineData(999_999_99)]
    public void Base_mas_iva_siempre_suma_el_total(long total)
    {
        var r = CalculadoraIva.DesglosarTotal(total);
        Assert.Equal(total, r.BaseCentimos + r.IvaCentimos);
    }

    [Fact]
    public void Calcula_precio_por_cantidad_por_dias()
    {
        var r = CalculadoraIva.Calcular(Dinero.ACentimos(11_300m), cantidad: 2, dias: 3);
        Assert.Equal(Dinero.ACentimos(67_800m), r.TotalCentimos);
        Assert.Equal(Dinero.ACentimos(60_000m), r.BaseCentimos);
        Assert.Equal(Dinero.ACentimos(7_800m), r.IvaCentimos);
    }

    [Fact]
    public void Rechaza_cantidad_o_dias_en_cero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraIva.Calcular(100, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraIva.Calcular(100, 1, 0));
    }

    [Fact]
    public void Formato_colones()
    {
        Assert.Equal("₡113.000,00", Dinero.Formatear(11_300_000));
        Assert.Equal("₡0,50", Dinero.Formatear(50));
    }
}

public sealed class PruebasBaseDatos : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "alq_" + Guid.NewGuid().ToString("N"));
    private readonly BaseDatos _db;
    private readonly ProductoRepositorio _productos;
    private readonly ClienteRepositorio _clientes;
    private readonly OrdenRepositorio _ordenes;
    private readonly ReporteServicio _reporte;
    private int _siguienteId;

    public PruebasBaseDatos()
    {
        _db = new BaseDatos(Path.Combine(_carpeta, "datos.db"));
        _db.Inicializar();
        _productos = new ProductoRepositorio(_db);
        _clientes = new ClienteRepositorio(_db);
        _ordenes = new OrdenRepositorio(_db);
        _reporte = new ReporteServicio(_db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch { /* temporal */ }
    }

    private Cliente NuevoCliente(string nombre = "Ana Solís")
    {
        var c = new Cliente { Nombre = nombre, Telefono = "8888-8888", TipoIdentificacion = TipoIdentificacion.Pasaporte, Identificacion = "P" + (++_siguienteId).ToString("D6") };
        _clientes.Guardar(c);
        return c;
    }

    private Producto ProductoConPrecio(decimal precio)
    {
        var p = _productos.Listar().First();
        p.PrecioConIva = precio;
        _productos.Guardar(p);
        return p;
    }

    private Orden CrearOrden(Cliente c, Producto p, DateTime fecha, int cantidad = 1, int dias = 1)
    {
        var o = new Orden { ClienteId = c.Id, Fecha = fecha, FechaInicio = fecha };
        o.Lineas.Add(new LineaOrden
        {
            ProductoId = p.Id,
            ProductoNombre = p.Nombre,
            PrecioUnitarioCentimos = p.PrecioConIvaCentimos,
            Cantidad = cantidad,
            Dias = dias
        });
        _ordenes.Crear(o);
        return o;
    }

    [Fact]
    public void Inicializar_crea_4_productos_y_es_idempotente()
    {
        _db.Inicializar();
        _db.Inicializar();
        Assert.Equal(4, _productos.Listar().Count);
    }

    [Fact]
    public void No_permite_productos_con_nombre_repetido()
    {
        var p = new Producto { Nombre = "producto 1", PrecioConIvaCentimos = 100 };
        var ex = Assert.Throws<ReglaNegocioException>(() => _productos.Guardar(p));
        Assert.Contains("Ya existe", ex.Message);
    }

    [Fact]
    public void Crear_orden_recalcula_totales_ignorando_lo_que_venga_de_pantalla()
    {
        var c = NuevoCliente();
        var p = ProductoConPrecio(11_300m);
        var o = new Orden { ClienteId = c.Id, Fecha = new DateTime(2026, 11, 5), FechaInicio = new DateTime(2026, 11, 5) };
        o.Lineas.Add(new LineaOrden
        {
            ProductoId = p.Id, ProductoNombre = p.Nombre,
            PrecioUnitarioCentimos = p.PrecioConIvaCentimos, Cantidad = 2, Dias = 3,
            TotalCentimos = 1, IvaCentimos = 999 // valores basura
        });
        long id = _ordenes.Crear(o);

        var leida = _ordenes.Obtener(id)!;
        Assert.Equal("Ana Solís", leida.ClienteNombre);
        Assert.Equal(Dinero.ACentimos(67_800m), leida.TotalCentimos);
        Assert.Equal(Dinero.ACentimos(7_800m), leida.IvaCentimos);
        Assert.Single(leida.Lineas);
    }

    [Fact]
    public void Cambiar_precio_del_producto_no_altera_ordenes_viejas()
    {
        var c = NuevoCliente();
        var p = ProductoConPrecio(11_300m);
        var o = CrearOrden(c, p, new DateTime(2026, 10, 1));

        p.PrecioConIva = 22_600m;
        _productos.Guardar(p);

        Assert.Equal(Dinero.ACentimos(11_300m), _ordenes.Obtener(o.Id)!.TotalCentimos);
    }

    [Fact]
    public void Orden_sin_lineas_o_sin_cliente_se_rechaza()
    {
        var c = NuevoCliente();
        Assert.Throws<ReglaNegocioException>(() => _ordenes.Crear(new Orden { ClienteId = c.Id }));
        Assert.Throws<ReglaNegocioException>(() => _ordenes.Crear(new Orden
        {
            Lineas = { new LineaOrden { ProductoId = 1, ProductoNombre = "x", PrecioUnitarioCentimos = 1 } }
        }));
    }

    [Fact]
    public void Reporte_mensual_suma_por_mes_y_excluye_anuladas()
    {
        var c = NuevoCliente();
        var p = ProductoConPrecio(11_300m);
        CrearOrden(c, p, new DateTime(2026, 10, 3));
        CrearOrden(c, p, new DateTime(2026, 10, 28), cantidad: 2);
        var anulada = CrearOrden(c, p, new DateTime(2026, 10, 15), cantidad: 10);
        CrearOrden(c, p, new DateTime(2026, 11, 1));
        CrearOrden(c, p, new DateTime(2025, 10, 1)); // otro año

        _ordenes.Anular(anulada.Id, "Error de digitación");

        var anio = _reporte.ResumenAnual(2026);
        Assert.Equal(12, anio.Count);

        var octubre = anio[9];
        Assert.Equal(2, octubre.CantidadOrdenes);
        Assert.Equal(Dinero.ACentimos(33_900m), octubre.TotalCentimos);
        Assert.Equal(Dinero.ACentimos(30_000m), octubre.BaseCentimos);
        Assert.Equal(Dinero.ACentimos(3_900m), octubre.IvaCentimos);

        Assert.Equal(1, anio[10].CantidadOrdenes);
        Assert.Equal(0, anio[0].CantidadOrdenes);
        Assert.Equal(2, _reporte.DetalleMes(2026, 10).Count);
    }

    [Fact]
    public void Estados_de_orden()
    {
        var c = NuevoCliente();
        var p = ProductoConPrecio(5_000m);
        var o = CrearOrden(c, p, new DateTime(2026, 11, 2));

        _ordenes.MarcarDevuelta(o.Id, new DateTime(2026, 11, 4));
        var leida = _ordenes.Obtener(o.Id)!;
        Assert.Equal(EstadoOrden.Devuelta, leida.Estado);
        Assert.Equal(new DateTime(2026, 11, 4), leida.FechaFin);

        Assert.Throws<ReglaNegocioException>(() => _ordenes.MarcarDevuelta(o.Id, DateTime.Today));
        Assert.Throws<ReglaNegocioException>(() => _ordenes.Anular(o.Id, " "));

        _ordenes.Anular(o.Id, "Cliente canceló");
        Assert.Equal(EstadoOrden.Anulada, _ordenes.Obtener(o.Id)!.Estado);
        Assert.Throws<ReglaNegocioException>(() => _ordenes.Anular(o.Id, "otra vez"));
    }

    [Fact]
    public void Filtros_de_listado()
    {
        var ana = NuevoCliente("Ana Solís");
        var luis = NuevoCliente("Luis Mora");
        var p = ProductoConPrecio(1_000m);
        CrearOrden(ana, p, new DateTime(2026, 11, 1));
        var o2 = CrearOrden(luis, p, new DateTime(2026, 11, 2));
        _ordenes.Anular(o2.Id, "x");

        var nov = (Desde: new DateTime(2026, 11, 1), Hasta: new DateTime(2026, 11, 30));
        Assert.Equal(2, _ordenes.Listar(nov.Desde, nov.Hasta).Count);
        Assert.Single(_ordenes.Listar(nov.Desde, nov.Hasta, EstadoOrden.Anulada));
        Assert.Equal("Ana Solís", Assert.Single(_ordenes.Listar(nov.Desde, nov.Hasta, textoCliente: "ana")).ClienteNombre);
    }

    [Fact]
    public void No_se_elimina_cliente_con_ordenes()
    {
        var c = NuevoCliente();
        CrearOrden(c, ProductoConPrecio(1_000m), DateTime.Today);
        Assert.Throws<ReglaNegocioException>(() => _clientes.Eliminar(c.Id));

        var sinOrdenes = NuevoCliente("Temporal");
        _clientes.Eliminar(sinOrdenes.Id);
        Assert.DoesNotContain(_clientes.Listar(), x => x.Id == sinOrdenes.Id);
    }

    [Fact]
    public void Respaldo_y_restauracion()
    {
        var resp = new RespaldoServicio(_db);
        var c = NuevoCliente();
        var p = ProductoConPrecio(11_300m);
        CrearOrden(c, p, new DateTime(2026, 11, 1));

        string respaldo = resp.CrearRespaldo(resp.CarpetaRespaldosLocal);
        Assert.True(File.Exists(respaldo));

        // Datos nuevos después del respaldo
        CrearOrden(c, p, new DateTime(2026, 11, 2));
        Assert.Equal(2, _reporte.ResumenMes(2026, 11).CantidadOrdenes);

        string copiaAntes = resp.Restaurar(respaldo);
        Assert.True(File.Exists(copiaAntes));
        Assert.Equal(1, _reporte.ResumenMes(2026, 11).CantidadOrdenes);

        // La copia "antes de restaurar" conserva las 2 órdenes
        var dbCopia = new BaseDatos(copiaAntes);
        Assert.Equal(2, new ReporteServicio(dbCopia).ResumenMes(2026, 11).CantidadOrdenes);
    }

    [Fact]
    public void Respaldo_automatico_usa_carpeta_extra_y_conserva_30()
    {
        var resp = new RespaldoServicio(_db);
        string extra = Path.Combine(_carpeta, "OneDrive");
        resp.CarpetaExtra = extra;

        for (int i = 0; i < 33; i++)
        {
            var (creados, errores) = resp.RespaldoAutomatico();
            Assert.Empty(errores);
            Assert.Equal(2, creados.Count);
        }
        Assert.Equal(30, RespaldoServicio.ListarRespaldos(resp.CarpetaRespaldosLocal).Count);
        Assert.Equal(30, RespaldoServicio.ListarRespaldos(extra).Count);
    }

    [Fact]
    public void Restaurar_rechaza_archivos_invalidos()
    {
        var resp = new RespaldoServicio(_db);
        string falso = Path.Combine(_carpeta, "falso.db");
        File.WriteAllText(falso, "no soy una base de datos");
        Assert.Throws<ReglaNegocioException>(() => resp.Restaurar(falso));
        Assert.Throws<ReglaNegocioException>(() => resp.Restaurar(Path.Combine(_carpeta, "no-existe.db")));
    }

    [Fact]
    public void Exporta_excel_del_mes_y_del_anio()
    {
        var c = NuevoCliente();
        var p = ProductoConPrecio(11_300m);
        CrearOrden(c, p, new DateTime(2026, 10, 3));
        CrearOrden(c, p, new DateTime(2026, 10, 9), cantidad: 2);

        string mes = Path.Combine(_carpeta, "mes.xlsx");
        _reporte.ExportarMesExcel(2026, 10, mes);
        using (var wb = new XLWorkbook(mes))
        {
            Assert.Equal(3_900m, wb.Worksheet("Resumen").Cell(6, 2).GetValue<decimal>());
            Assert.Equal(33_900m, wb.Worksheet("Detalle").Cell(4, 5).GetValue<decimal>());
        }

        string anio = Path.Combine(_carpeta, "anio.xlsx");
        _reporte.ExportarAnioExcel(2026, anio);
        using var wb2 = new XLWorkbook(anio);
        Assert.Equal(3_900m, wb2.Worksheet(1).Cell(14, 5).GetValue<decimal>());
    }
}
