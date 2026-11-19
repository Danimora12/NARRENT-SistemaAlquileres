using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Empleado = "Empleado";
    public static readonly string[] Todos = { Administrador, Empleado };
}

public class Usuario
{
    public long Id { get; set; }
    public string NombreUsuario { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? Correo { get; set; }
    public string Rol { get; set; } = Roles.Empleado;
    public bool Activo { get; set; } = true;
    public bool DebeCambiarClave { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime? UltimoAcceso { get; set; }

    public bool EsAdministrador => Rol == Roles.Administrador;
}

/// <summary>
/// Usuarios, inicio de sesión y contraseñas. No hay registro libre: los usuarios
/// los crea un administrador. Si alguien olvida su contraseña, un administrador
/// le asigna una temporal y el sistema le obliga a cambiarla al entrar.
/// </summary>
public partial class UsuarioServicio(BaseDatos db)
{
    public const int LargoMinimoClave = 6;

    private const string Columnas = "id, usuario, nombre, correo, rol, activo, debe_cambiar_clave, creado_en, ultimo_acceso";

    public bool HayUsuarios
    {
        get
        {
            using var cn = db.Abrir();
            return BaseDatos.Escalar<long>(cn, "SELECT COUNT(*) FROM usuarios;") > 0;
        }
    }

    // ---------- Sesión ----------

    /// <summary>
    /// Verifica usuario y contraseña e inicia la sesión. El mensaje de error no dice
    /// si falló el usuario o la contraseña.
    /// </summary>
    public Usuario IniciarSesion(string? nombreUsuario, string? clave)
    {
        string u = Normalizar(nombreUsuario);
        var (usuario, hash) = BuscarConHash(u);
        if (usuario is null || !usuario.Activo || !Claves.Coincide(clave, hash))
        {
            // Lo escrito como usuario solo se anota si el usuario existe: si alguien escribe
            // por error su contraseña en ese campo, no debe quedar en texto plano en Logs.
            new LogRepositorio(db).Registrar(null, "Inicio de sesión fallido", "Seguridad",
                usuario is null ? "Se escribió un usuario que no existe."
                : !usuario.Activo ? $"Intento de ingreso con el usuario desactivado \"{usuario.NombreUsuario}\"."
                : $"Contraseña incorrecta para el usuario \"{usuario.NombreUsuario}\".");
            throw new ReglaNegocioException("Usuario o contraseña incorrectos.");
        }

        using (var cn = db.Abrir())
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "UPDATE usuarios SET ultimo_acceso = $f WHERE id = $id;";
            cmd.Parameters.AddWithValue("$f", BaseDatos.FechaHora(DateTime.Now));
            cmd.Parameters.AddWithValue("$id", usuario.Id);
            cmd.ExecuteNonQuery();
        }

        db.UsuarioActual = usuario;
        new LogRepositorio(db).Registrar(null, "Inicio de sesión", "Seguridad", $"{usuario.Nombre} ({usuario.Rol}) inició sesión.");
        return usuario;
    }

    public void CerrarSesion()
    {
        if (db.UsuarioActual is null) return;
        new LogRepositorio(db).Registrar(null, "Sesión cerrada", "Seguridad", "Bloqueo o cambio de usuario.");
        db.UsuarioActual = null;
    }

    /// <summary>El usuario de la sesión cambia su propia contraseña.</summary>
    public void CambiarMiClave(string? actual, string? nueva, string? repetir)
    {
        var yo = db.UsuarioActual ?? throw new ReglaNegocioException("No hay una sesión iniciada.");
        var (_, hash) = BuscarConHash(yo.NombreUsuario);
        if (!Claves.Coincide(actual, hash))
            throw new ReglaNegocioException("La contraseña actual no es correcta.");
        string c = ValidarClave(nueva, repetir);
        if (Claves.Coincide(c, hash))
            throw new ReglaNegocioException("La contraseña nueva debe ser diferente de la actual.");

        GuardarClave(yo.Id, c, debeCambiar: false);
        yo.DebeCambiarClave = false;
        new LogRepositorio(db).Registrar(null, "Contraseña cambiada", $"Usuario {yo.NombreUsuario}", $"{yo.Nombre} cambió su contraseña.");
    }

    // ---------- Configuración inicial ----------

    /// <summary>
    /// Primer uso del programa (no hay usuarios): crea el primer administrador y, si
    /// falta, el código de confirmación. Deja la sesión iniciada.
    /// </summary>
    public Usuario ConfigurarPrimerUso(string? nombre, string? nombreUsuario, string? clave, string? repetir,
        string? codigoConfirmacion = null)
    {
        if (HayUsuarios) throw new ReglaNegocioException("El programa ya tiene usuarios.");
        var seguridad = new SeguridadServicio(db);
        bool pedirCodigo = !seguridad.Configurado;

        var nuevo = new Usuario { Nombre = nombre ?? "", NombreUsuario = nombreUsuario ?? "", Rol = Roles.Administrador };
        ValidarDatos(nuevo);
        string c = ValidarClave(clave, repetir);
        if (pedirCodigo && (codigoConfirmacion ?? "").Trim().Length < SeguridadServicio.LargoMinimoCodigo)
            throw new ReglaNegocioException($"El código de confirmación debe tener al menos {SeguridadServicio.LargoMinimoCodigo} caracteres.");

        nuevo.Id = Insertar(nuevo, c, debeCambiar: false);
        if (pedirCodigo) seguridad.GuardarConfirmacion(codigoConfirmacion!);

        db.UsuarioActual = Obtener(nuevo.Id);
        new LogRepositorio(db).Registrar(null, "Configuración inicial", "Seguridad",
            $"Se creó el administrador {nuevo.Nombre.Trim()} (usuario {Normalizar(nuevo.NombreUsuario)})" +
            (pedirCodigo ? " y el código de confirmación." : "."));
        return db.UsuarioActual!;
    }

    // ---------- Administración (solo administradores) ----------

    public List<Usuario> Listar()
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM usuarios ORDER BY activo DESC, nombre COLLATE NOCASE;";
        using var r = cmd.ExecuteReader();
        var lista = new List<Usuario>();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    public Usuario? Obtener(long id)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM usuarios WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }

    /// <summary>Crea un usuario con contraseña temporal: deberá cambiarla al entrar.</summary>
    public long Crear(Usuario u, string? claveTemporal, string? repetir)
    {
        ExigirAdministrador(db);
        ValidarDatos(u);
        string c = ValidarClave(claveTemporal, repetir);
        long id = Insertar(u, c, debeCambiar: true);
        new LogRepositorio(db).Registrar(null, "Usuario creado", $"Usuario {Normalizar(u.NombreUsuario)}",
            $"Nombre: {u.Nombre.Trim()} · Rol: {u.Rol} · Correo: {Texto(u.Correo)} · Contraseña temporal asignada.");
        return id;
    }

    /// <summary>Edita nombre, correo, rol y estado. El nombre de usuario no cambia.</summary>
    public void Editar(Usuario u)
    {
        var yo = ExigirAdministrador(db);
        var antes = Obtener(u.Id) ?? throw new ReglaNegocioException("El usuario no existe.");
        u.NombreUsuario = antes.NombreUsuario;
        ValidarDatos(u);

        if (u.Id == yo.Id && !u.Activo)
            throw new ReglaNegocioException("No puede desactivar su propio usuario.");
        if (u.Id == yo.Id && !u.EsAdministrador)
            throw new ReglaNegocioException("No puede quitarse a sí mismo el rol de administrador.");
        if (antes.EsAdministrador && antes.Activo && !(u.EsAdministrador && u.Activo) && AdministradoresActivos() <= 1)
            throw new ReglaNegocioException("Debe quedar al menos un administrador activo.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE usuarios SET nombre = $n, correo = $c, rol = $r, activo = $a WHERE id = $id;";
            cmd.Parameters.AddWithValue("$n", u.Nombre.Trim());
            cmd.Parameters.AddWithValue("$c", Nulo(u.Correo));
            cmd.Parameters.AddWithValue("$r", u.Rol);
            cmd.Parameters.AddWithValue("$a", u.Activo ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", u.Id);
            cmd.ExecuteNonQuery();
        }
        var cambios = new Cambios()
            .Campo("Nombre", antes.Nombre, u.Nombre.Trim())
            .Campo("Correo", antes.Correo, Nulo(u.Correo) as string)
            .Campo("Rol", antes.Rol, u.Rol)
            .Campo("Estado", antes.Activo ? "Activo" : "Inactivo", u.Activo ? "Activo" : "Inactivo");
        if (cambios.HayCambios)
            LogRepositorio.Registrar(db, cn, tx, null, "Usuario editado", $"Usuario {antes.NombreUsuario}", cambios.ToString());
        tx.Commit();

        if (u.Id == yo.Id) db.UsuarioActual = Obtener(yo.Id);
    }

    /// <summary>
    /// Asigna una contraseña temporal a otro usuario (por ejemplo, si la olvidó).
    /// Al entrar, el sistema le obliga a cambiarla.
    /// </summary>
    public void RestablecerClave(long id, string? temporal, string? repetir)
    {
        var yo = ExigirAdministrador(db);
        if (id == yo.Id)
            throw new ReglaNegocioException("Para su propio usuario use \"Cambiar mi contraseña\".");
        var u = Obtener(id) ?? throw new ReglaNegocioException("El usuario no existe.");
        string c = ValidarClave(temporal, repetir);
        GuardarClave(id, c, debeCambiar: true);
        new LogRepositorio(db).Registrar(null, "Contraseña restablecida", $"Usuario {u.NombreUsuario}",
            $"Se asignó una contraseña temporal a {u.Nombre}. Deberá cambiarla al ingresar.");
    }

    public static Usuario ExigirAdministrador(BaseDatos db)
    {
        var u = db.UsuarioActual ?? throw new ReglaNegocioException("No hay una sesión iniciada.");
        if (!u.EsAdministrador) throw new ReglaNegocioException("Esta acción solo la puede hacer un administrador.");
        return u;
    }

    // ---------- Migración desde los códigos de empleado (v3 → v4) ----------

    /// <summary>
    /// Convierte los 2 empleados de la versión anterior en usuarios administradores.
    /// La contraseña inicial es su código de empleado de siempre (el hash se reutiliza
    /// tal cual) y deben cambiarla la primera vez que entran.
    /// </summary>
    internal static List<(string Usuario, string Nombre)> MigrarEmpleados(SqliteConnection cn, SqliteTransaction tx)
    {
        var creados = new List<(string, string)>();
        for (int n = 1; n <= 2; n++)
        {
            string? nombre = LeerConfig(cn, tx, $"seg_empleado{n}_nombre");
            string? hash = LeerConfig(cn, tx, $"seg_empleado{n}_codigo");
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(hash)) continue;

            string usuario = UsuarioDisponible(cn, tx, SugerirUsuario(nombre));
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO usuarios (usuario, nombre, clave_hash, rol, activo, debe_cambiar_clave, creado_en)
                VALUES ($u, $n, $h, $r, 1, 1, $f);
                """;
            cmd.Parameters.AddWithValue("$u", usuario);
            cmd.Parameters.AddWithValue("$n", nombre.Trim());
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$r", Roles.Administrador);
            cmd.Parameters.AddWithValue("$f", BaseDatos.FechaHora(DateTime.Now));
            cmd.ExecuteNonQuery();
            creados.Add((usuario, nombre.Trim()));
        }
        BaseDatos.Ejecutar(cn, tx, "DELETE FROM config WHERE clave LIKE 'seg_empleado%';");
        return creados;
    }

    /// <summary>"María José Pérez" → "maria.perez" (sin tildes, primer nombre + último apellido).</summary>
    public static string SugerirUsuario(string nombre)
    {
        string sinTildes = new string(nombre.Normalize(NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        var partes = Regex.Replace(sinTildes.ToLowerInvariant(), "[^a-z0-9 ]", "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string u = partes.Length switch
        {
            0 => "usuario",
            1 => partes[0],
            _ => $"{partes[0]}.{partes[^1]}"
        };
        return u.Length < 3 ? u.PadRight(3, '1') : u.Length > 30 ? u[..30] : u;
    }

    // ---------- Internos ----------

    private static string? LeerConfig(SqliteConnection cn, SqliteTransaction tx, string clave)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT valor FROM config WHERE clave = $k;";
        cmd.Parameters.AddWithValue("$k", clave);
        return cmd.ExecuteScalar() as string;
    }

    private static string UsuarioDisponible(SqliteConnection cn, SqliteTransaction tx, string base_)
    {
        string candidato = base_;
        for (int i = 2; ; i++)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM usuarios WHERE usuario = $u;";
            cmd.Parameters.AddWithValue("$u", candidato);
            if ((long)cmd.ExecuteScalar()! == 0) return candidato;
            candidato = base_ + i;
        }
    }

    [GeneratedRegex("^[a-z0-9._-]{3,30}$")]
    private static partial Regex FormatoUsuario();

    private static string Normalizar(string? nombreUsuario) => (nombreUsuario ?? "").Trim().ToLowerInvariant();

    private void ValidarDatos(Usuario u)
    {
        if (string.IsNullOrWhiteSpace(u.Nombre))
            throw new ReglaNegocioException("Indique el nombre de la persona.");
        string nu = Normalizar(u.NombreUsuario);
        if (!FormatoUsuario().IsMatch(nu))
            throw new ReglaNegocioException("El usuario debe tener de 3 a 30 caracteres: letras sin tildes, números, punto, guion o guion bajo, sin espacios.");
        u.NombreUsuario = nu;
        if (!Roles.Todos.Contains(u.Rol))
            throw new ReglaNegocioException("Rol no válido.");
        if (!string.IsNullOrWhiteSpace(u.Correo) && !Regex.IsMatch(u.Correo.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            throw new ReglaNegocioException("El correo no tiene un formato válido.");

        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM usuarios WHERE usuario = $u AND id <> $id;";
        cmd.Parameters.AddWithValue("$u", nu);
        cmd.Parameters.AddWithValue("$id", u.Id);
        if ((long)cmd.ExecuteScalar()! > 0)
            throw new ReglaNegocioException($"Ya existe el usuario \"{nu}\".");
    }

    private static string ValidarClave(string? clave, string? repetir)
    {
        string c = (clave ?? "").Trim();
        if (c.Length < LargoMinimoClave)
            throw new ReglaNegocioException($"La contraseña debe tener al menos {LargoMinimoClave} caracteres.");
        if (c != (repetir ?? "").Trim())
            throw new ReglaNegocioException("Las contraseñas no coinciden.");
        return c;
    }

    private long Insertar(Usuario u, string clave, bool debeCambiar)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usuarios (usuario, nombre, correo, clave_hash, rol, activo, debe_cambiar_clave, creado_en)
            VALUES ($u, $n, $c, $h, $r, 1, $d, $f);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$u", Normalizar(u.NombreUsuario));
        cmd.Parameters.AddWithValue("$n", u.Nombre.Trim());
        cmd.Parameters.AddWithValue("$c", Nulo(u.Correo));
        cmd.Parameters.AddWithValue("$h", Claves.Hash(clave));
        cmd.Parameters.AddWithValue("$r", u.Rol);
        cmd.Parameters.AddWithValue("$d", debeCambiar ? 1 : 0);
        cmd.Parameters.AddWithValue("$f", BaseDatos.FechaHora(DateTime.Now));
        return (long)cmd.ExecuteScalar()!;
    }

    private void GuardarClave(long id, string clave, bool debeCambiar)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE usuarios SET clave_hash = $h, debe_cambiar_clave = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$h", Claves.Hash(clave));
        cmd.Parameters.AddWithValue("$d", debeCambiar ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private (Usuario? Usuario, string? Hash) BuscarConHash(string nombreUsuario)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas}, clave_hash FROM usuarios WHERE usuario = $u;";
        cmd.Parameters.AddWithValue("$u", nombreUsuario);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (Leer(r), r.GetString(9)) : (null, null);
    }

    private long AdministradoresActivos()
    {
        using var cn = db.Abrir();
        return BaseDatos.Escalar<long>(cn, $"SELECT COUNT(*) FROM usuarios WHERE activo = 1 AND rol = '{Roles.Administrador}';");
    }

    private static Usuario Leer(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        NombreUsuario = r.GetString(1),
        Nombre = r.GetString(2),
        Correo = r.IsDBNull(3) ? null : r.GetString(3),
        Rol = r.GetString(4),
        Activo = r.GetInt64(5) == 1,
        DebeCambiarClave = r.GetInt64(6) == 1,
        CreadoEn = BaseDatos.LeerFechaHora(r.GetString(7)),
        UltimoAcceso = r.IsDBNull(8) ? null : BaseDatos.LeerFechaHora(r.GetString(8))
    };

    private static object Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();
    private static string Texto(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();
}
