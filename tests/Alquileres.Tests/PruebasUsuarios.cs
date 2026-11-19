using Alquileres.Core;
using Microsoft.Data.Sqlite;

namespace Alquileres.Tests;

/// <summary>Inicio de sesión, usuarios, código de confirmación y migración desde los códigos de empleado.</summary>
public sealed class PruebasUsuarios : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "narrent_" + Guid.NewGuid().ToString("N"));
    private readonly BaseDatos _db;
    private readonly UsuarioServicio _usuarios;
    private readonly SeguridadServicio _seguridad;

    public PruebasUsuarios()
    {
        _db = new BaseDatos(Path.Combine(_carpeta, "datos.db"));
        _db.Inicializar();
        _usuarios = new UsuarioServicio(_db);
        _seguridad = new SeguridadServicio(_db);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_carpeta, true); } catch { /* temporal */ }
    }

    private List<EntradaLog> Logs() => new LogRepositorio(_db).Listar(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1));

    private void PrimerAdmin() => _usuarios.ConfigurarPrimerUso("María Pérez", "maria", "clave123", "clave123", "9999");

    private long Escalar(string sql)
    {
        using var cn = _db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Primer_uso_crea_administrador_y_codigo_y_deja_la_sesion_iniciada()
    {
        Assert.False(_usuarios.HayUsuarios);
        Assert.Throws<ReglaNegocioException>(() => _usuarios.ConfigurarPrimerUso("María", "maria", "clave123", "otra123", "9999"));
        Assert.Throws<ReglaNegocioException>(() => _usuarios.ConfigurarPrimerUso("María", "maria", "corta", "corta", "9999"));
        Assert.Throws<ReglaNegocioException>(() => _usuarios.ConfigurarPrimerUso("María", "maria", "clave123", "clave123", null));
        Assert.Throws<ReglaNegocioException>(() => _usuarios.ConfigurarPrimerUso("María", "con espacio", "clave123", "clave123", "9999"));
        // Un código corto no deja un usuario creado a medias
        Assert.Throws<ReglaNegocioException>(() => _usuarios.ConfigurarPrimerUso("María", "maria", "clave123", "clave123", "12"));
        Assert.False(_usuarios.HayUsuarios);

        PrimerAdmin();

        Assert.True(_usuarios.HayUsuarios);
        Assert.True(_seguridad.Configurado);
        Assert.Equal("María Pérez", _db.UsuarioActual!.Nombre);
        Assert.True(_db.UsuarioActual.EsAdministrador);
        Assert.False(_db.UsuarioActual.DebeCambiarClave);
        // No se puede repetir
        Assert.Throws<ReglaNegocioException>(PrimerAdmin);
        // Nada en texto plano
        Assert.Equal(0, Escalar("SELECT COUNT(*) FROM usuarios WHERE clave_hash LIKE '%clave123%';"));
        Assert.Equal(0, Escalar("SELECT COUNT(*) FROM config WHERE valor = '9999';"));
    }

    [Fact]
    public void Login_valida_usuario_y_contrasena_sin_decir_cual_fallo()
    {
        PrimerAdmin();
        _usuarios.CerrarSesion();
        Assert.Null(_db.UsuarioActual);

        var e1 = Assert.Throws<ReglaNegocioException>(() => _usuarios.IniciarSesion("maria", "mala123"));
        var e2 = Assert.Throws<ReglaNegocioException>(() => _usuarios.IniciarSesion("nadie", "clave123"));
        Assert.Equal(e1.Message, e2.Message);
        Assert.Null(_db.UsuarioActual);

        // Usuario sin distinguir mayúsculas y con espacios
        _usuarios.IniciarSesion("  MARIA ", "clave123");
        Assert.Equal("maria", _db.UsuarioActual!.NombreUsuario);
        Assert.NotNull(_usuarios.Listar().Single().UltimoAcceso);

        var logs = Logs();
        Assert.Equal(2, logs.Count(l => l.Accion == "Inicio de sesión fallido"));
        // Lo escrito como usuario no queda en Logs si no existe (podría ser una contraseña tecleada por error)
        _usuarios.CerrarSesion();
        Assert.Throws<ReglaNegocioException>(() => _usuarios.IniciarSesion("MiClaveSecreta1", "x"));
        Assert.DoesNotContain(Logs(), l => l.Detalle.Contains("miclavesecreta1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, l => l.Accion == "Inicio de sesión" && l.Empleado == "María Pérez");
        Assert.Contains(logs, l => l.Accion == "Sesión cerrada" && l.Empleado == "María Pérez");
    }

    [Fact]
    public void Todo_lo_registrado_queda_a_nombre_del_usuario_de_la_sesion()
    {
        PrimerAdmin();
        var cliente = new Cliente { Nombre = "Ana", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "109130259" };
        new ClienteRepositorio(_db).Guardar(cliente); // no pide código: antes quedaba sin empleado
        Assert.Contains(Logs(), l => l.Accion == "Cliente creado" && l.Empleado == "María Pérez");

        // Al cambiar de usuario, los registros siguientes quedan a nombre del nuevo
        _usuarios.Crear(new Usuario { Nombre = "José Mora", NombreUsuario = "jose", Rol = Roles.Administrador }, "temp123", "temp123");
        _usuarios.CerrarSesion();
        _usuarios.IniciarSesion("jose", "temp123");
        new ClienteRepositorio(_db).Guardar(new Cliente { Nombre = "Luis", TipoIdentificacion = TipoIdentificacion.CedulaFisica, Identificacion = "209130259" });
        Assert.Contains(Logs(), l => l.Accion == "Cliente creado" && l.Referencia == "Cliente Luis" && l.Empleado == "José Mora");
    }

    [Fact]
    public void Codigo_de_confirmacion_autoriza_a_nombre_del_usuario_de_la_sesion()
    {
        PrimerAdmin();
        Assert.Equal("María Pérez", _seguridad.Validar(" 9999 "));
        Assert.Throws<ReglaNegocioException>(() => _seguridad.Validar("0000"));

        // Cambiarlo exige el actual
        Assert.Throws<ReglaNegocioException>(() => _seguridad.DefinirConfirmacion("7777", "0000"));
        Assert.Throws<ReglaNegocioException>(() => _seguridad.DefinirConfirmacion("123", "9999"));
        _seguridad.DefinirConfirmacion("7777", "9999");
        Assert.Throws<ReglaNegocioException>(() => _seguridad.Validar("9999"));
        Assert.Equal("María Pérez", _seguridad.Validar("7777"));

        // Sin sesión no se autoriza nada
        _usuarios.CerrarSesion();
        Assert.Throws<ReglaNegocioException>(() => _seguridad.Validar("7777"));
    }

    [Fact]
    public void Administrador_crea_usuarios_con_contrasena_temporal_y_la_persona_la_cambia()
    {
        PrimerAdmin();
        long id = _usuarios.Crear(new Usuario { Nombre = "José Mora", NombreUsuario = "Jose", Correo = "jose@x.com", Rol = Roles.Empleado },
            "temp123", "temp123");
        Assert.Throws<ReglaNegocioException>(() =>
            _usuarios.Crear(new Usuario { Nombre = "Otro", NombreUsuario = "jose", Rol = Roles.Empleado }, "temp123", "temp123"));
        Assert.Throws<ReglaNegocioException>(() =>
            _usuarios.Crear(new Usuario { Nombre = "Otro", NombreUsuario = "otro", Correo = "malo", Rol = Roles.Empleado }, "temp123", "temp123"));

        _usuarios.CerrarSesion();
        var jose = _usuarios.IniciarSesion("jose", "temp123");
        Assert.True(jose.DebeCambiarClave);

        Assert.Throws<ReglaNegocioException>(() => _usuarios.CambiarMiClave("temp123", "temp123", "temp123")); // igual a la actual
        Assert.Throws<ReglaNegocioException>(() => _usuarios.CambiarMiClave("mala123", "nueva123", "nueva123"));
        _usuarios.CambiarMiClave("temp123", "nueva123", "nueva123");
        Assert.False(_db.UsuarioActual!.DebeCambiarClave);

        _usuarios.CerrarSesion();
        Assert.Throws<ReglaNegocioException>(() => _usuarios.IniciarSesion("jose", "temp123"));
        Assert.False(_usuarios.IniciarSesion("jose", "nueva123").DebeCambiarClave);

        // Un empleado no administra usuarios ni el código
        Assert.Throws<ReglaNegocioException>(() =>
            _usuarios.Crear(new Usuario { Nombre = "X", NombreUsuario = "xxx", Rol = Roles.Empleado }, "temp123", "temp123"));
        Assert.Throws<ReglaNegocioException>(() => _usuarios.RestablecerClave(1, "temp123", "temp123"));
        Assert.Throws<ReglaNegocioException>(() => _seguridad.DefinirConfirmacion("5555", "9999"));
        Assert.Contains(Logs(), l => l.Accion == "Usuario creado" && l.Referencia == "Usuario jose" && l.Empleado == "María Pérez");
        Assert.Equal(id, _db.UsuarioActual!.Id);
    }

    [Fact]
    public void Restablecer_contrasena_olvidada()
    {
        PrimerAdmin();
        long id = _usuarios.Crear(new Usuario { Nombre = "José Mora", NombreUsuario = "jose", Rol = Roles.Administrador }, "temp123", "temp123");

        // No sobre sí mismo
        Assert.Throws<ReglaNegocioException>(() => _usuarios.RestablecerClave(_db.UsuarioActual!.Id, "otra123", "otra123"));

        _usuarios.RestablecerClave(id, "reset99", "reset99");
        _usuarios.CerrarSesion();
        Assert.Throws<ReglaNegocioException>(() => _usuarios.IniciarSesion("jose", "temp123"));
        Assert.True(_usuarios.IniciarSesion("jose", "reset99").DebeCambiarClave);
        Assert.Contains(Logs(), l => l.Accion == "Contraseña restablecida" && l.Empleado == "María Pérez");
    }

    [Fact]
    public void Siempre_queda_un_administrador_activo_y_los_desactivados_no_entran()
    {
        PrimerAdmin();
        var yo = _db.UsuarioActual!;
        Assert.Throws<ReglaNegocioException>(() => _usuarios.Editar(new Usuario { Id = yo.Id, Nombre = yo.Nombre, Rol = Roles.Administrador, Activo = false }));
        Assert.Throws<ReglaNegocioException>(() => _usuarios.Editar(new Usuario { Id = yo.Id, Nombre = yo.Nombre, Rol = Roles.Empleado, Activo = true }));

        long id = _usuarios.Crear(new Usuario { Nombre = "José Mora", NombreUsuario = "jose", Rol = Roles.Empleado }, "temp123", "temp123");
        _usuarios.Editar(new Usuario { Id = id, Nombre = "José Mora Q.", NombreUsuario = "cambiado", Rol = Roles.Empleado, Activo = false });
        var jose = _usuarios.Obtener(id)!;
        Assert.Equal("jose", jose.NombreUsuario); // el nombre de usuario no cambia
        Assert.False(jose.Activo);
        Assert.Contains(Logs(), l => l.Accion == "Usuario editado" && l.Detalle.Contains("Estado: \"Activo\" → \"Inactivo\""));

        _usuarios.CerrarSesion();
        Assert.Throws<ReglaNegocioException>(() => _usuarios.IniciarSesion("jose", "temp123"));
    }

    [Theory]
    [InlineData("María José Pérez Solís", "maria.solis")]
    [InlineData("José", "jose")]
    [InlineData("  Ñandú  Ortíz ", "nandu.ortiz")]
    [InlineData("Al", "al1")]
    public void Sugerir_usuario(string nombre, string esperado) =>
        Assert.Equal(esperado, UsuarioServicio.SugerirUsuario(nombre));

    /// <summary>Base nueva que se queda en la versión 3 (antes del inicio de sesión).</summary>
    private BaseDatos BaseV3()
    {
        var db = new BaseDatos(Path.Combine(_carpeta, "v3.db"));
        db.Inicializar(3);
        return db;
    }

    [Fact]
    public void Migracion_convierte_los_codigos_de_empleado_en_administradores()
    {
        // Base v3 con los 2 empleados y el código de confirmación de antes
        var db = BaseV3();
        using (var cn = db.Abrir())
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO config (clave, valor) VALUES
                    ('seg_empleado1_nombre', 'María Pérez'), ('seg_empleado1_codigo', $c1),
                    ('seg_empleado2_nombre', 'José Mora'),   ('seg_empleado2_codigo', $c2),
                    ('seg_confirmacion', $c3);
                """;
            cmd.Parameters.AddWithValue("$c1", Claves.Hash("1111"));
            cmd.Parameters.AddWithValue("$c2", Claves.Hash("2222"));
            cmd.Parameters.AddWithValue("$c3", Claves.Hash("9999"));
            cmd.ExecuteNonQuery();
        }

        db.Inicializar();
        var usuarios = new UsuarioServicio(db);
        var seguridad = new SeguridadServicio(db);

        var lista = usuarios.Listar();
        Assert.Equal(2, lista.Count);
        Assert.All(lista, u => Assert.True(u.EsAdministrador && u.Activo && u.DebeCambiarClave));
        using (var cn = db.Abrir()) Assert.Equal(0L, BaseDatos.Escalar<long>(cn, "SELECT COUNT(*) FROM config WHERE clave LIKE 'seg_empleado%';"));

        // Entran con su código de empleado de siempre y deben cambiarlo; el código de confirmación sigue igual
        Assert.True(usuarios.IniciarSesion("maria.perez", "1111").DebeCambiarClave);
        Assert.Equal("María Pérez", seguridad.Validar("9999"));
        usuarios.CambiarMiClave("1111", "nueva123", "nueva123");
        usuarios.CerrarSesion();
        Assert.Equal("José Mora", usuarios.IniciarSesion("jose.mora", "2222").Nombre);

        Assert.Contains(new LogRepositorio(db).Listar(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1)), l => l.Accion == "Actualización: inicio de sesión" && l.Detalle.Contains("María Pérez (usuario maria.perez)"));

        // No se repite
        db.Inicializar();
        Assert.Equal(2, usuarios.Listar().Count);
    }

    [Fact]
    public void Migracion_sin_codigos_configurados_pide_la_configuracion_inicial()
    {
        var db = BaseV3();
        db.Inicializar();
        Assert.False(new UsuarioServicio(db).HayUsuarios);
        Assert.False(new SeguridadServicio(db).Configurado);
    }
}
