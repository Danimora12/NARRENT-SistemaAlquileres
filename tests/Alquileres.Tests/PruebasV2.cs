using Alquileres.Core;
using Microsoft.Data.Sqlite;

namespace Alquileres.Tests;

/// <summary>Pruebas de lo nuevo en NARRENT v2.</summary>
public sealed class PruebasV2 : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "narrent_" + Guid.NewGuid().ToString("N"));
    private readonly BaseDatos _db;
    private readonly ProductoRepositorio _productos;
    private readonly ClienteRepositorio _clientes;
    private readonly OrdenRepositorio _ordenes;
    private readonly ReporteServicio _reporte;
    private readonly LogRepositorio _logs;

    public PruebasV2()
    {
        _db = new BaseDatos(Path.Combine(_carpeta, "datos.db"));
        _db.Inicializar();
        _productos = new ProductoRepositorio(_db);
        _clientes = new ClienteRepositorio(_db);
        _ordenes = new OrdenRepositorio(_db);
        _reporte = new ReporteServicio(_db);
        _logs = new LogRepositorio(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_carpeta, true); } catch { /* temporal */ }
    }

    private Cliente NuevoCliente(string nombre = "Ana Solís")
    {
        var c = new Cliente { Nombre = nombre };
        _clientes.Guardar(c);
        return c;
    }

    private Producto Producto(int indice, decimal precio)
    {
        var p = _productos.Listar()[indice];
        p.PrecioConIva = precio;
        _productos.Guardar(p);
        return p;
    }

    private static LineaOrden Linea(Producto p, int cantidad = 1, int dias = 1, int descuento = 0) => new()
    {
        ProductoId = p.Id, ProductoNombre = p.Nombre, PrecioUnitarioCentimos = p.PrecioConIvaCentimos,
        Cantidad = cantidad, Dias = dias, DescuentoPct = descuento
    };

    private Orden CrearOrden(DateTime fecha, Pago? pago = null, params LineaOrden[] lineas)
    {
        var c = _clientes.Listar().FirstOrDefault() ?? NuevoCliente();
        var o = new Orden { ClienteId = c.Id, Fecha = fecha, FechaInicio = fecha, Lineas = lineas.ToList() };
        if (o.Lineas.Count == 0) o.Lineas.Add(Linea(Producto(0, 10_000m)));
        _ordenes.Crear(o, pago);
        return o;
    }

    private List<EntradaLog> Logs() => _logs.Listar(DateTime.Today.AddYears(-1), DateTime.Today.AddDays(1));

    // ───────────── Número de orden ─────────────

    [Fact]
    public void Numero_es_anio_mes_consecutivo_y_reinicia_cada_mes()
    {
        var a = CrearOrden(new DateTime(2026, 12, 3));
        var b = CrearOrden(new DateTime(2026, 12, 20));
        var c = CrearOrden(new DateTime(2027, 1, 2));
        var d = CrearOrden(new DateTime(2026, 11, 30)); // fecha de otro mes: su propio consecutivo

        Assert.Equal("202612001", a.Numero);
        Assert.Equal("202612002", b.Numero);
        Assert.Equal("202701001", c.Numero);
        Assert.Equal("202611001", d.Numero);
        Assert.Equal("202612002", _ordenes.Obtener(b.Id)!.Numero);
    }

    [Fact]
    public void Numero_no_se_reutiliza_si_la_ultima_orden_esta_eliminada()
    {
        CrearOrden(new DateTime(2026, 12, 1));
        var b = CrearOrden(new DateTime(2026, 12, 2));
        _ordenes.Eliminar(b.Id, "María", "Prueba");
        var c = CrearOrden(new DateTime(2026, 12, 3));
        Assert.Equal("202612003", c.Numero);
    }

    [Fact]
    public void Se_puede_buscar_por_numero()
    {
        CrearOrden(new DateTime(2026, 12, 1));
        var b = CrearOrden(new DateTime(2026, 12, 2));
        var r = _ordenes.Listar(new DateTime(2026, 12, 1), new DateTime(2026, 12, 31), textoCliente: "202612002");
        Assert.Equal(b.Id, Assert.Single(r).Id);
    }

    // ───────────── Descuentos ─────────────

    [Fact]
    public void Descuento_a_toda_la_orden_y_el_iva_se_calcula_sobre_lo_descontado()
    {
        var p1 = Producto(0, 11_300m);
        var p2 = Producto(1, 22_600m);
        var o = CrearOrden(new DateTime(2026, 12, 1), null, Linea(p1, descuento: 10), Linea(p2, descuento: 10));

        var leida = _ordenes.Obtener(o.Id)!;
        Assert.Equal(Dinero.ACentimos(33_900m), leida.SubtotalCentimos);
        Assert.Equal(Dinero.ACentimos(3_390m), leida.DescuentoCentimos);
        Assert.Equal(Dinero.ACentimos(30_510m), leida.TotalCentimos);
        Assert.Equal(Dinero.ACentimos(27_000m), leida.BaseCentimos);   // 30.510 / 1,13
        Assert.Equal(Dinero.ACentimos(3_510m), leida.IvaCentimos);
        Assert.Equal("10 % a toda la orden", leida.DescribirDescuento());

        // El flujo de caja usa el monto ya descontado
        Assert.Equal(Dinero.ACentimos(30_510m), _reporte.ResumenMes(2026, 12).TotalCentimos);
    }

    [Fact]
    public void Descuento_a_un_solo_producto()
    {
        var p1 = Producto(0, 10_000m);
        var p2 = Producto(1, 5_000m);
        var o = CrearOrden(new DateTime(2026, 12, 1), null, Linea(p1, cantidad: 2), Linea(p2, descuento: 15));

        var leida = _ordenes.Obtener(o.Id)!;
        Assert.Equal(0, leida.Lineas[0].DescuentoCentimos);
        Assert.Equal(Dinero.ACentimos(750m), leida.Lineas[1].DescuentoCentimos);
        Assert.Equal(Dinero.ACentimos(24_250m), leida.TotalCentimos);
        Assert.Contains("15 % en", leida.DescribirDescuento());
    }

    [Fact]
    public void Descuento_se_redondea_al_colon()
    {
        var r = CalculadoraIva.CalcularLinea(Dinero.ACentimos(12_345m), 1, 1, 3); // 370,35 → 370
        Assert.Equal(Dinero.ACentimos(370m), r.DescuentoCentimos);
        Assert.Equal(Dinero.ACentimos(11_975m), r.TotalCentimos);
        Assert.Equal(r.TotalCentimos, r.BaseCentimos + r.IvaCentimos);
    }

    [Fact]
    public void Descuento_mayor_a_15_se_rechaza()
    {
        var p = Producto(0, 10_000m);
        var c = NuevoCliente();
        var o = new Orden { ClienteId = c.Id, Lineas = { Linea(p, descuento: 16) } };
        Assert.Throws<ReglaNegocioException>(() => _ordenes.Crear(o));
    }

    // ───────────── Pagos ─────────────

    [Fact]
    public void Pago_adelantado_abono_y_cancelacion()
    {
        var p = Producto(0, 50_000m);
        var o = CrearOrden(new DateTime(2026, 12, 1),
            new Pago { Metodo = MetodoPago.Sinpe, MontoCentimos = Dinero.ACentimos(20_000m), Fecha = new DateTime(2026, 12, 1) },
            Linea(p));

        var leida = _ordenes.Obtener(o.Id)!;
        Assert.Equal(Dinero.ACentimos(20_000m), leida.PagadoCentimos);
        Assert.Equal(Dinero.ACentimos(30_000m), leida.SaldoCentimos);
        Assert.Equal("Abono", leida.EstadoPago);

        // No se puede pagar más que el saldo
        Assert.Throws<ReglaNegocioException>(() =>
            _ordenes.RegistrarPago(o.Id, new Pago { Metodo = MetodoPago.Efectivo, MontoCentimos = Dinero.ACentimos(30_001m) }));

        _ordenes.RegistrarPago(o.Id, new Pago { Metodo = MetodoPago.Efectivo, MontoCentimos = Dinero.ACentimos(30_000m) });
        leida = _ordenes.Obtener(o.Id)!;
        Assert.Equal(0, leida.SaldoCentimos);
        Assert.Equal("Pagada", leida.EstadoPago);
        Assert.Equal(2, leida.Pagos.Count);

        // También se ve en el listado
        Assert.Equal(0, _ordenes.Listar(new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)).Single().SaldoCentimos);
        Assert.Contains(Logs(), l => l.Accion == "Cuenta cancelada");
    }

    [Fact]
    public void Pago_inicial_mayor_al_total_se_rechaza_y_no_guarda_nada()
    {
        var p = Producto(0, 10_000m);
        var c = NuevoCliente();
        var o = new Orden { ClienteId = c.Id, Fecha = new DateTime(2026, 12, 1), Lineas = { Linea(p) } };
        Assert.Throws<ReglaNegocioException>(() =>
            _ordenes.Crear(o, new Pago { MontoCentimos = Dinero.ACentimos(10_001m) }));
        Assert.Empty(_ordenes.Listar(new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)));
    }

    [Fact]
    public void No_se_registran_pagos_en_ordenes_anuladas()
    {
        var o = CrearOrden(new DateTime(2026, 12, 1));
        _ordenes.Anular(o.Id, "Canceló");
        Assert.Throws<ReglaNegocioException>(() =>
            _ordenes.RegistrarPago(o.Id, new Pago { MontoCentimos = 100 }));
    }

    [Fact]
    public void Eliminar_pago_restituye_el_saldo_y_queda_en_logs()
    {
        var p = Producto(0, 10_000m);
        var o = CrearOrden(new DateTime(2026, 12, 1), new Pago { MontoCentimos = Dinero.ACentimos(4_000m) }, Linea(p));
        long pagoId = _ordenes.Obtener(o.Id)!.Pagos.Single().Id;

        _ordenes.EliminarPago(pagoId, "José");

        Assert.Equal(Dinero.ACentimos(10_000m), _ordenes.Obtener(o.Id)!.SaldoCentimos);
        var log = Logs().First(l => l.Accion == "Pago eliminado");
        Assert.Equal("José", log.Empleado);
        Assert.Equal($"Orden {o.Numero}", log.Referencia);
    }

    // ───────────── Eliminar / restaurar ─────────────

    [Fact]
    public void Eliminar_oculta_la_orden_del_listado_y_del_flujo_de_caja_y_restaurar_la_devuelve()
    {
        var p = Producto(0, 11_300m);
        CrearOrden(new DateTime(2026, 12, 1), null, Linea(p));
        var fantasma = CrearOrden(new DateTime(2026, 12, 2), null, Linea(p, cantidad: 5));
        var rango = (new DateTime(2026, 12, 1), new DateTime(2026, 12, 31));

        Assert.Throws<ReglaNegocioException>(() => _ordenes.Eliminar(fantasma.Id, "María", " "));
        _ordenes.Eliminar(fantasma.Id, "María", "Orden de prueba");

        Assert.Single(_ordenes.Listar(rango.Item1, rango.Item2));
        Assert.Equal(1, _reporte.ResumenMes(2026, 12).CantidadOrdenes);
        Assert.Equal(Dinero.ACentimos(11_300m), _reporte.ResumenMes(2026, 12).TotalCentimos);
        var eliminada = Assert.Single(_ordenes.ListarEliminadas());
        Assert.Equal("María", eliminada.EliminadaPor);
        Assert.Equal("Orden de prueba", eliminada.MotivoEliminacion);

        // Una orden eliminada no se puede tocar hasta restaurarla
        Assert.Throws<ReglaNegocioException>(() => _ordenes.EditarNotas(fantasma.Id, "x"));

        _ordenes.Restaurar(fantasma.Id, "José");
        Assert.Empty(_ordenes.ListarEliminadas());
        Assert.Equal(2, _ordenes.Listar(rango.Item1, rango.Item2).Count);
        Assert.Equal(2, _reporte.ResumenMes(2026, 12).CantidadOrdenes);

        var logs = Logs();
        Assert.Contains(logs, l => l.Accion == "Orden eliminada" && l.Empleado == "María");
        Assert.Contains(logs, l => l.Accion == "Orden restaurada" && l.Empleado == "José");
    }

    [Fact]
    public void Editar_notas_queda_en_logs_con_antes_y_despues()
    {
        var o = CrearOrden(new DateTime(2026, 12, 1));
        _ordenes.EditarNotas(o.Id, "Entregar en el salón");
        _ordenes.EditarNotas(o.Id, "Entregar en el salón"); // sin cambio: no registra otra vez

        Assert.Equal("Entregar en el salón", _ordenes.Obtener(o.Id)!.Notas);
        var log = Assert.Single(Logs(), l => l.Accion == "Notas editadas");
        Assert.Contains("(vacío) → \"Entregar en el salón\"", log.Detalle);
    }

    // ───────────── Inventario ─────────────

    [Fact]
    public void Inventario_simple_y_con_caracteristicas()
    {
        var p = _productos.Listar().First();
        p.Inventario = 12;
        _productos.Guardar(p, "María");
        Assert.Equal(12, _productos.Obtener(p.Id)!.Inventario);

        _productos.GuardarVariantes(p.Id, new[]
        {
            new VarianteInventario { Descripcion = "Blanco", Cantidad = 20 },
            new VarianteInventario { Descripcion = "Rojo", Cantidad = 15 }
        }, "José");
        var leido = _productos.Obtener(p.Id)!;
        Assert.Equal(35, leido.Inventario);
        Assert.Equal(2, leido.CantidadVariantes);

        // Con características, el total no se sobrescribe desde la edición del producto
        leido.Inventario = 1;
        leido.PrecioConIva = 5_000m;
        _productos.Guardar(leido, "María");
        Assert.Equal(35, _productos.Obtener(p.Id)!.Inventario);

        Assert.Throws<ReglaNegocioException>(() => _productos.GuardarVariantes(p.Id, new[]
        {
            new VarianteInventario { Descripcion = "Azul", Cantidad = 1 },
            new VarianteInventario { Descripcion = "azul", Cantidad = 2 }
        }));

        var logs = Logs();
        Assert.Contains(logs, l => l.Accion == "Producto editado" && l.Empleado == "María" && l.Detalle.Contains("Inventario: 0 → 12"));
        Assert.Contains(logs, l => l.Accion == "Inventario actualizado" && l.Empleado == "José" && l.Detalle.Contains("Blanco 20, Rojo 15 (total 35)"));
    }

    // ───────────── Clientes ─────────────

    [Fact]
    public void Editar_y_eliminar_cliente_quedan_en_logs_con_el_empleado()
    {
        var c = NuevoCliente("Luis Mora");
        c.Telefono = "8888-0000";
        _clientes.Guardar(c, "María");
        var temporal = NuevoCliente("Temporal");
        _clientes.Eliminar(temporal.Id, "José");

        var logs = Logs();
        Assert.Contains(logs, l => l.Accion == "Cliente editado" && l.Empleado == "María" && l.Detalle.Contains("Teléfono: (vacío) → \"8888-0000\""));
        Assert.Contains(logs, l => l.Accion == "Cliente eliminado" && l.Empleado == "José" && l.Referencia == "Cliente Temporal");
    }

    // ───────────── Códigos de autorización ─────────────

    [Fact]
    public void Codigos_de_autorizacion()
    {
        var seg = new SeguridadServicio(_db);
        Assert.False(seg.Configurado);
        Assert.Throws<ReglaNegocioException>(() => seg.Validar("1111", "9999"));

        // Los 3 códigos deben ser distintos y de al menos 4 caracteres
        Assert.Throws<ReglaNegocioException>(() => seg.Configurar(new[] { ("María", "1111"), ("José", "1111") }, "9999"));
        Assert.Throws<ReglaNegocioException>(() => seg.Configurar(new[] { ("María", "1111"), ("José", "2222") }, "123"));

        seg.Configurar(new[] { ("María", "1111"), ("José", "2222") }, "9999");
        Assert.True(seg.Configurado);
        Assert.Equal("María", seg.Validar("1111", "9999"));
        Assert.Equal("José", seg.Validar(" 2222 ", "9999"));
        Assert.Throws<ReglaNegocioException>(() => seg.Validar("1111", "0000"));
        Assert.Throws<ReglaNegocioException>(() => seg.Validar("9999", "9999"));
        Assert.Throws<ReglaNegocioException>(() => seg.Validar("3333", "9999"));

        // Los códigos no quedan en texto plano
        using (var cn = _db.Abrir())
        {
            long enClaro = BaseDatos_Escalar(cn, "SELECT COUNT(*) FROM config WHERE valor IN ('1111','2222','9999');");
            Assert.Equal(0, enClaro);
        }

        // Cambiarlos exige el código de confirmación actual
        Assert.Throws<ReglaNegocioException>(() => seg.Configurar(new[] { ("María", "5555"), ("José", "6666") }, "7777", "0000"));
        seg.Configurar(new[] { ("María", "5555"), ("José", "6666") }, "7777", "9999");
        Assert.Equal("José", seg.Validar("6666", "7777"));
        Assert.Throws<ReglaNegocioException>(() => seg.Validar("1111", "9999"));
    }

    private static long BaseDatos_Escalar(SqliteConnection cn, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    // ───────────── Gastos y utilidad ─────────────

    [Fact]
    public void Gastos_y_utilidad_en_el_flujo_de_caja()
    {
        var gastos = new GastoRepositorio(_db);
        var p = Producto(0, 113_000m);
        CrearOrden(new DateTime(2026, 12, 5), null, Linea(p));            // neto 100.000

        gastos.Guardar(new Gasto { Fecha = new DateTime(2026, 12, 1), Categoria = "Alquiler del local", Descripcion = "Local diciembre",
            MontoCentimos = Dinero.ACentimos(30_000m) });
        gastos.Guardar(new Gasto { Fecha = new DateTime(2026, 12, 10), Categoria = "Limpieza y lavandería", Descripcion = "Lavado manteles",
            MontoCentimos = Dinero.ACentimos(11_300m), IvaCentimos = Dinero.ACentimos(1_300m) });  // cuenta 10.000
        gastos.Guardar(new Gasto { Fecha = new DateTime(2027, 1, 2), Categoria = "Otros", Descripcion = "Otro año",
            MontoCentimos = Dinero.ACentimos(5_000m) });

        var dic = _reporte.ResumenMes(2026, 12);
        Assert.Equal(Dinero.ACentimos(40_000m), dic.GastosCentimos);
        Assert.Equal(Dinero.ACentimos(60_000m), dic.UtilidadCentimos);
        Assert.Equal(Dinero.ACentimos(13_000m), dic.IvaCentimos);
        Assert.Equal(Dinero.ACentimos(1_300m), dic.IvaGastosCentimos);
        Assert.Equal(Dinero.ACentimos(11_700m), dic.IvaNetoCentimos);

        // Un mes con gastos y sin ventas da utilidad negativa
        var nov = _reporte.ResumenMes(2026, 11);
        Assert.Equal(0, nov.GastosCentimos);
        gastos.Guardar(new Gasto { Fecha = new DateTime(2026, 11, 3), Categoria = "Otros", Descripcion = "x", MontoCentimos = 100_00 });
        Assert.Equal(-100_00, _reporte.ResumenMes(2026, 11).UtilidadCentimos);
        gastos.Guardar(new Gasto { Fecha = new DateTime(2026, 11, 4), Categoria = "Otros", Descripcion = "con IVA",
            MontoCentimos = 1_130_00, IvaCentimos = 130_00 });
        Assert.Equal(-130_00, _reporte.ResumenMes(2026, 11).IvaNetoCentimos);   // saldo a favor

        Assert.Equal(2, gastos.Listar(new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)).Count);
        Assert.Single(gastos.Listar(new DateTime(2026, 12, 1), new DateTime(2026, 12, 31), categoria: "Alquiler del local"));
        Assert.Single(gastos.Listar(new DateTime(2026, 12, 1), new DateTime(2026, 12, 31), texto: "manteles"));
        Assert.Contains("Limpieza y lavandería", gastos.Categorias());

        // Excel: utilidad en la hoja Resumen y hoja Gastos
        string ruta = Path.Combine(_carpeta, "dic.xlsx");
        _reporte.ExportarMesExcel(2026, 12, ruta);
        using var wb = new ClosedXML.Excel.XLWorkbook(ruta);
        Assert.Equal(40_000m, wb.Worksheet("Resumen").Cell(7, 2).GetValue<decimal>());
        Assert.Equal(60_000m, wb.Worksheet("Resumen").Cell(8, 2).GetValue<decimal>());
        Assert.Equal(11_700m, wb.Worksheet("Resumen").Cell(10, 2).GetValue<decimal>());
        Assert.Equal(40_000m, wb.Worksheet("Gastos").Cell(4, 8).GetValue<decimal>());
    }

    [Fact]
    public void Editar_y_eliminar_gastos_exigen_empleado_y_quedan_en_logs()
    {
        var gastos = new GastoRepositorio(_db);
        var g = new Gasto { Categoria = "Otros", Descripcion = "Gasolina", MontoCentimos = 10_000_00 };
        gastos.Guardar(g);

        Assert.Throws<ReglaNegocioException>(() => gastos.Guardar(new Gasto { Categoria = "Otros", Descripcion = "x", MontoCentimos = 0 }));
        Assert.Throws<ReglaNegocioException>(() => gastos.Guardar(new Gasto { Categoria = "Otros", Descripcion = "x", MontoCentimos = 100, IvaCentimos = 200 }));

        g.MontoCentimos = 12_000_00;
        Assert.Throws<ReglaNegocioException>(() => gastos.Guardar(g));       // sin empleado
        gastos.Guardar(g, "María");
        gastos.Eliminar(g.Id, "José");
        Assert.Null(gastos.Obtener(g.Id));

        var logs = Logs();
        Assert.Contains(logs, l => l.Accion == "Gasto registrado" && l.Empleado is null);
        Assert.Contains(logs, l => l.Accion == "Gasto editado" && l.Empleado == "María" && l.Detalle.Contains("₡10.000,00 → ₡12.000,00"));
        Assert.Contains(logs, l => l.Accion == "Gasto eliminado" && l.Empleado == "José");
    }

    // ───────────── Actualización desde la v1 ─────────────

    [Fact]
    public void Una_base_de_la_v1_se_actualiza_sin_perder_datos()
    {
        string ruta = Path.Combine(_carpeta, "v1.db");
        Directory.CreateDirectory(_carpeta);
        using (var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false }.ToString()))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE productos (id INTEGER PRIMARY KEY AUTOINCREMENT, nombre TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    precio_con_iva INTEGER NOT NULL CHECK (precio_con_iva >= 0), activo INTEGER NOT NULL DEFAULT 1);
                CREATE TABLE clientes (id INTEGER PRIMARY KEY AUTOINCREMENT, nombre TEXT NOT NULL, identificacion TEXT,
                    telefono TEXT, correo TEXT, notas TEXT);
                CREATE TABLE ordenes (id INTEGER PRIMARY KEY AUTOINCREMENT, fecha TEXT NOT NULL,
                    cliente_id INTEGER NOT NULL REFERENCES clientes(id), fecha_inicio TEXT NOT NULL, fecha_fin TEXT,
                    estado TEXT NOT NULL DEFAULT 'Activa' CHECK (estado IN ('Activa','Devuelta','Anulada')),
                    notas TEXT, motivo_anulacion TEXT, creada_en TEXT NOT NULL);
                CREATE TABLE orden_lineas (id INTEGER PRIMARY KEY AUTOINCREMENT,
                    orden_id INTEGER NOT NULL REFERENCES ordenes(id) ON DELETE CASCADE,
                    producto_id INTEGER NOT NULL REFERENCES productos(id), producto_nombre TEXT NOT NULL,
                    precio_unitario INTEGER NOT NULL, cantidad INTEGER NOT NULL, dias INTEGER NOT NULL,
                    tasa_iva INTEGER NOT NULL, total INTEGER NOT NULL, base INTEGER NOT NULL, iva INTEGER NOT NULL);
                CREATE TABLE config (clave TEXT PRIMARY KEY, valor TEXT);
                INSERT INTO productos (nombre, precio_con_iva) VALUES ('Manteles', 1130000);
                INSERT INTO clientes (nombre) VALUES ('Cliente viejo');
                INSERT INTO ordenes (fecha, cliente_id, fecha_inicio, creada_en) VALUES
                    ('2026-11-20', 1, '2026-11-20', '2026-11-20 10:00:00'),
                    ('2026-11-05', 1, '2026-11-05', '2026-11-21 10:00:00'),
                    ('2026-10-30', 1, '2026-10-30', '2026-11-21 11:00:00');
                INSERT INTO orden_lineas (orden_id, producto_id, producto_nombre, precio_unitario, cantidad, dias, tasa_iva, total, base, iva)
                    VALUES (1, 1, 'Manteles', 1130000, 1, 1, 13, 1130000, 1000000, 130000),
                           (2, 1, 'Manteles', 1130000, 2, 1, 13, 2260000, 2000000, 260000),
                           (3, 1, 'Manteles', 1130000, 1, 1, 13, 1130000, 1000000, 130000);
                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
        }

        var db = new BaseDatos(ruta);
        db.Inicializar();
        var ordenes = new OrdenRepositorio(db);

        // Numeradas por fecha dentro de cada mes
        Assert.Equal("202611002", ordenes.Obtener(1)!.Numero);
        Assert.Equal("202611001", ordenes.Obtener(2)!.Numero);
        Assert.Equal("202610001", ordenes.Obtener(3)!.Numero);
        Assert.Equal(Dinero.ACentimos(22_600m), ordenes.Obtener(2)!.TotalCentimos);
        Assert.Equal(Dinero.ACentimos(22_600m), ordenes.Obtener(2)!.SaldoCentimos); // la v1 no registraba pagos

        // La siguiente orden de noviembre continúa el consecutivo
        var nueva = new Orden { ClienteId = 1, Fecha = new DateTime(2026, 11, 25), FechaInicio = new DateTime(2026, 11, 25) };
        nueva.Lineas.Add(new LineaOrden { ProductoId = 1, ProductoNombre = "Manteles", PrecioUnitarioCentimos = 1130000 });
        ordenes.Crear(nueva);
        Assert.Equal("202611003", nueva.Numero);

        Assert.Equal(3, new ReporteServicio(db).ResumenMes(2026, 11).CantidadOrdenes); // 2 viejas + la nueva
        Assert.Contains(new LogRepositorio(db).Listar(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1)),
            l => l.Accion == "Actualización a NARRENT v2" && l.Detalle.Contains("3 órdenes"));

        new GastoRepositorio(db).Guardar(new Gasto { Categoria = "Otros", Descripcion = "Prueba", MontoCentimos = 100 });

        // Volver a inicializar no repite la migración
        db.Inicializar();
        Assert.Equal("202611002", ordenes.Obtener(1)!.Numero);
    }
}
