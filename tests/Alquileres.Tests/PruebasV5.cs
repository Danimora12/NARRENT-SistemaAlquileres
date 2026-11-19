using Alquileres.Core;
using Microsoft.Data.Sqlite;

namespace Alquileres.Tests;

/// <summary>Identificación de clientes, código de producto, gastos sin IVA y migración v4 → v5.</summary>
public sealed class PruebasV5 : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "narrent_" + Guid.NewGuid().ToString("N"));
    private readonly BaseDatos _db;
    private readonly ClienteRepositorio _clientes;
    private readonly ProductoRepositorio _productos;

    public PruebasV5()
    {
        _db = new BaseDatos(Path.Combine(_carpeta, "datos.db"));
        _db.Inicializar();
        _clientes = new ClienteRepositorio(_db);
        _productos = new ProductoRepositorio(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_carpeta, true); } catch { /* temporal */ }
    }

    private List<EntradaLog> Logs(BaseDatos? db = null) =>
        new LogRepositorio(db ?? _db).Listar(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

    private void SesionAdmin() =>
        new UsuarioServicio(_db).ConfigurarPrimerUso("María Pérez", "maria", "clave123", "clave123", "9999");

    // ───────────── Identificación ─────────────

    [Theory]
    [InlineData(TipoIdentificacion.CedulaFisica, "109130259", true)]
    [InlineData(TipoIdentificacion.CedulaFisica, "1-0913-0259", true)]
    [InlineData(TipoIdentificacion.CedulaFisica, "009130259", false)]   // provincia 0 no existe
    [InlineData(TipoIdentificacion.CedulaFisica, "10913025", false)]    // 8 dígitos
    [InlineData(TipoIdentificacion.CedulaFisica, "1091302591", false)]  // 10 dígitos
    [InlineData(TipoIdentificacion.CedulaJuridica, "3101981657", true)]
    [InlineData(TipoIdentificacion.CedulaJuridica, "3-101-a00001", true)]
    [InlineData(TipoIdentificacion.CedulaJuridica, "4000042139", true)]  // institución autónoma
    [InlineData(TipoIdentificacion.CedulaJuridica, "310198165", false)]
    [InlineData(TipoIdentificacion.CedulaJuridica, "1101981657", false)]
    [InlineData(TipoIdentificacion.Dimex, "155812345678", true)]
    [InlineData(TipoIdentificacion.Dimex, "15581234567", true)]          // 11 dígitos
    [InlineData(TipoIdentificacion.Dimex, "1558123456", false)]
    [InlineData(TipoIdentificacion.Dimex, "15581234567A", false)]
    [InlineData(TipoIdentificacion.Nite, "3120123456", true)]
    [InlineData(TipoIdentificacion.Nite, "3-130-123456", true)]
    [InlineData(TipoIdentificacion.Nite, "3101123456", false)]
    [InlineData(TipoIdentificacion.Pasaporte, "A12345678", true)]
    [InlineData(TipoIdentificacion.Pasaporte, "AB12", false)]
    [InlineData(TipoIdentificacion.Pasaporte, "A123456789012345678901", false)] // 22
    public void Formato_de_cada_tipo_de_identificacion(TipoIdentificacion tipo, string numero, bool valida) =>
        Assert.Equal(valida, Identificaciones.EsValida(tipo, numero));

    [Fact]
    public void Se_muestra_con_guiones_y_se_guarda_sin_ellos()
    {
        Assert.Equal("1-0913-0259", Identificaciones.Formatear(TipoIdentificacion.CedulaFisica, "109130259"));
        Assert.Equal("3-101-981657", Identificaciones.Formatear(TipoIdentificacion.CedulaJuridica, "3101981657"));
        Assert.Equal("3-120-123456", Identificaciones.Formatear(TipoIdentificacion.Nite, "3120123456"));
        Assert.Equal("155812345678", Identificaciones.Formatear(TipoIdentificacion.Dimex, "155812345678"));

        var c = new Cliente { Nombre = "Ana", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = " 1-0913-0259 " };
        _clientes.Guardar(c);
        Assert.Equal("109130259", _clientes.Obtener(c.Id)!.Identificacion);
        Assert.Equal("Ana (1-0913-0259)", _clientes.Obtener(c.Id)!.ToString());
        // Se encuentra buscando con o sin guiones
        Assert.Single(_clientes.Listar("1-0913-0259"));
        Assert.Single(_clientes.Listar("109130259"));
    }

    [Fact]
    public void Tipo_y_numero_son_obligatorios_y_validos()
    {
        Assert.Throws<ReglaNegocioException>(() => _clientes.Guardar(new Cliente { Nombre = "Ana", Identificacion = "109130259" }));
        Assert.Throws<ReglaNegocioException>(() => _clientes.Guardar(new Cliente { Nombre = "Ana", TipoIdentificacion = TipoIdentificacion.CedulaFisica }));
        var e = Assert.Throws<ReglaNegocioException>(() =>
            _clientes.Guardar(new Cliente { Nombre = "Ana", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "12345" }));
        Assert.Contains("9 dígitos", e.Message);
        Assert.Empty(_clientes.Listar());
    }

    [Fact]
    public void No_se_puede_repetir_la_identificacion()
    {
        var ana = new Cliente { Nombre = "Ana", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "109130259" };
        _clientes.Guardar(ana);

        var e = Assert.Throws<ReglaNegocioException>(() => _clientes.Guardar(
            new Cliente { Nombre = "Ana Solís", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "1-0913-0259" }));
        Assert.Contains("\"Ana\"", e.Message);
        // Tampoco con otro tipo y el mismo número
        Assert.Throws<ReglaNegocioException>(() => _clientes.Guardar(
            new Cliente { Nombre = "Otra", TipoIdentificacion = TipoIdentificacion.Pasaporte, Identificacion = "109130259" }));
        Assert.Single(_clientes.Listar());

        // Editar el mismo cliente sin cambiar la identificación sí se puede
        ana.Telefono = "8888-0000";
        _clientes.Guardar(ana, "María");
        Assert.Equal("8888-0000", _clientes.Obtener(ana.Id)!.Telefono);
    }

    [Fact]
    public void Un_cliente_viejo_sin_tipo_lo_completa_al_editarlo_y_no_se_puede_duplicar()
    {
        long id;
        using (var cn = _db.Abrir())
        {
            BaseDatos.Ejecutar(cn, null, "INSERT INTO clientes (nombre, identificacion) VALUES ('Viejo', '1-0913-0259');");
            id = BaseDatos.Escalar<long>(cn, "SELECT id FROM clientes;");
        }
        var viejo = _clientes.Obtener(id)!;
        Assert.Null(viejo.TipoIdentificacion);

        // El número viejo con guiones también cuenta para no duplicar
        Assert.Throws<ReglaNegocioException>(() => _clientes.Guardar(
            new Cliente { Nombre = "Nuevo", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "109130259" }));

        // Editarlo exige el tipo
        Assert.Throws<ReglaNegocioException>(() => _clientes.Guardar(viejo, "María"));
        viejo.TipoIdentificacion = TipoIdentificacion.CedulaFisica;
        _clientes.Guardar(viejo, "María");
        Assert.Equal("109130259", _clientes.Obtener(id)!.Identificacion);
        Assert.Contains(Logs(), l => l.Accion == "Cliente editado" && l.Detalle.Contains("Tipo de identificación"));
    }

    // ───────────── Código de producto ─────────────

    [Fact]
    public void Codigo_automatico_por_prefijo_y_consecutivo()
    {
        var a = new Producto { Nombre = "Manteles blancos" };
        var b = new Producto { Nombre = "Mantas" };
        var c = new Producto { Nombre = "Ñandú" };
        var d = new Producto { Nombre = "Té" };
        _productos.Guardar(a);
        _productos.Guardar(b);
        _productos.Guardar(c);
        _productos.Guardar(d);

        Assert.Equal("MAN001", a.Codigo);
        Assert.Equal("MAN002", b.Codigo);
        Assert.Equal("NAN001", c.Codigo);
        Assert.Equal("TEX001", d.Codigo);
        Assert.Equal("MAN003", _productos.SugerirCodigo("Manzanas"));
        // Los 4 productos iniciales también tienen código
        Assert.Contains(_productos.Listar(), p => p.Codigo == "PRO001");

        // Cambiar el nombre no cambia el código
        a.Nombre = "Sillas";
        _productos.Guardar(a, "José");
        Assert.Equal("MAN001", _productos.Obtener(a.Id)!.Codigo);
    }

    [Fact]
    public void Solo_un_administrador_cambia_el_codigo()
    {
        var p = new Producto { Nombre = "Manteles" };
        _productos.Guardar(p);

        p.Codigo = "MESA-01";
        Assert.Throws<ReglaNegocioException>(() => _productos.Guardar(p, "José"));          // sin sesión de admin
        Assert.Throws<ReglaNegocioException>(() => _productos.Guardar(new Producto { Nombre = "X", Codigo = "XYZ999" }));

        SesionAdmin();
        Assert.Throws<ReglaNegocioException>(() => _productos.Guardar(new Producto { Id = p.Id, Nombre = "Manteles", Codigo = "MÁS 1" }));
        _productos.Guardar(p, "María");
        Assert.Equal("MESA-01", _productos.Obtener(p.Id)!.Codigo);
        Assert.Contains(Logs(), l => l.Accion == "Producto editado" && l.Detalle.Contains("Código: \"MAN001\" → \"MESA-01\""));

        // No se puede repetir
        var otro = new Producto { Nombre = "Otro", Codigo = "mesa-01" };
        var e = Assert.Throws<ReglaNegocioException>(() => _productos.Guardar(otro));
        Assert.Contains("código", e.Message);

        // Un empleado (no admin) sí puede editar lo demás sin tocar el código
        var u = new UsuarioServicio(_db);
        u.Crear(new Usuario { Nombre = "José", NombreUsuario = "jose", Rol = Roles.Empleado }, "temp123", "temp123");
        u.CerrarSesion();
        u.IniciarSesion("jose", "temp123");
        var leido = _productos.Obtener(p.Id)!;
        leido.PrecioConIva = 2_000m;
        _productos.Guardar(leido, "José");
        leido.Codigo = "OTRO01";
        Assert.Throws<ReglaNegocioException>(() => _productos.Guardar(leido, "José"));
    }

    [Fact]
    public void Eliminar_producto_solo_si_nunca_se_uso()
    {
        var libre = new Producto { Nombre = "Sillas", PrecioConIvaCentimos = 1_000_00 };
        var usado = new Producto { Nombre = "Mesas", PrecioConIvaCentimos = 1_000_00 };
        _productos.Guardar(libre);
        _productos.Guardar(usado);
        var cliente = new Cliente { Nombre = "Ana", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "109130259" };
        _clientes.Guardar(cliente);
        var orden = new Orden { ClienteId = cliente.Id, Fecha = DateTime.Today, FechaInicio = DateTime.Today };
        orden.Lineas.Add(new LineaOrden { ProductoId = usado.Id, ProductoNombre = usado.Nombre, PrecioUnitarioCentimos = 1_000_00 });
        new OrdenRepositorio(_db).Crear(orden);

        Assert.Throws<ReglaNegocioException>(() => _productos.Eliminar(libre.Id, ""));          // sin autorización
        var e = Assert.Throws<ReglaNegocioException>(() => _productos.Eliminar(usado.Id, "María"));
        Assert.Contains("1 orden", e.Message);
        Assert.NotNull(_productos.Obtener(usado.Id));

        _productos.Eliminar(libre.Id, "María");
        Assert.Null(_productos.Obtener(libre.Id));
        Assert.Contains(Logs(), l => l.Accion == "Producto eliminado" && l.Empleado == "María" && l.Referencia == "Producto SIL001 Sillas");
        // Su código queda libre para otro producto con el mismo prefijo
        Assert.Equal("SIL001", _productos.SugerirCodigo("Silla plegable"));
    }

    // ───────────── Gastos ─────────────

    [Fact]
    public void Categoria_libre_sin_duplicados_por_mayusculas()
    {
        var gastos = new GastoRepositorio(_db);
        Assert.Empty(gastos.Categorias());     // ya no hay lista fija
        gastos.Guardar(new Gasto { Categoria = "Transporte", Detalle = "Gasolina", MontoCentimos = 100 });
        gastos.Guardar(new Gasto { Categoria = "  transporte ", Detalle = "Peaje", MontoCentimos = 100 });
        gastos.Guardar(new Gasto { Categoria = "Publicidad  en   redes", Detalle = "Anuncio", MontoCentimos = 100 });
        Assert.Equal(new[] { "Publicidad en redes", "Transporte" }, gastos.Categorias());
        Assert.Throws<ReglaNegocioException>(() => gastos.Guardar(new Gasto { Categoria = "Otros", Detalle = " ", MontoCentimos = 100 }));
    }

    // ───────────── Migración v4 → v5 ─────────────

    [Fact]
    public void Migracion_a_v5_conserva_inventario_y_proveedor()
    {
        var db = new BaseDatos(Path.Combine(_carpeta, "v4.db"));
        db.Inicializar(4);
        using (var cn = db.Abrir())
        {
            BaseDatos.Ejecutar(cn, null, """
                UPDATE productos SET nombre = 'Manteles', inventario = 35 WHERE id = 1;
                INSERT INTO producto_variantes (producto_id, descripcion, cantidad) VALUES (1, 'Blanco', 20), (1, 'Rojo', 15);
                INSERT INTO clientes (nombre, identificacion) VALUES ('Viejo', '1-0913-0259');
                INSERT INTO gastos (fecha, categoria, descripcion, proveedor, metodo, monto, iva, creado_en) VALUES
                    ('2026-11-01', 'Limpieza', 'Lavado', 'Lavandería Sol', 'Efectivo', 11300, 1300, '2026-11-01 10:00:00'),
                    ('2026-11-02', 'Otros', 'Varios', NULL, 'Efectivo', 5000, 0, '2026-11-02 10:00:00');
                """);
        }

        db.Inicializar();

        var productos = new ProductoRepositorio(db).Listar();
        Assert.Equal(new[] { "MAN001", "PRO001", "PRO002", "PRO003" }, productos.Select(p => p.Codigo).ToArray());
        Assert.Equal(35, productos.Single(p => p.Codigo == "MAN001").Inventario);

        using (var cn = db.Abrir())
            Assert.Equal(0L, BaseDatos.Escalar<long>(cn, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'producto_variantes';"));

        var gastos = new GastoRepositorio(db).Listar(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
        Assert.Contains(gastos, g => g.Detalle == "Lavado · Proveedor: Lavandería Sol" && g.MontoCentimos == 11300);
        Assert.Contains(gastos, g => g.Detalle == "Varios");
        Assert.Equal(16300, new ReporteServicio(db).ResumenMes(2026, 11).GastosCentimos); // monto completo

        Assert.Null(new ClienteRepositorio(db).Listar().Single().TipoIdentificacion);

        var log = Logs(db).Single(l => l.Accion == "Actualización: clientes, productos y gastos");
        Assert.Contains("código a 4 productos", log.Detalle);
        Assert.Contains("características de 1 productos", log.Detalle);
        Assert.Contains("proveedor de 1 gastos", log.Detalle);
        Assert.Contains("1 gastos tenían IVA", log.Detalle);
    }
}
