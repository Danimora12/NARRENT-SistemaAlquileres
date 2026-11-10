using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>
/// Acceso al archivo SQLite. No hay servidor: la base de datos es un solo archivo
/// que se crea automáticamente la primera vez que se abre el programa.
/// </summary>
public class BaseDatos
{
    private const int VersionEsquema = 1;

    public string RutaArchivo { get; }

    public BaseDatos(string rutaArchivo)
    {
        RutaArchivo = Path.GetFullPath(rutaArchivo);
    }

    /// <summary>Carpeta por defecto: %LOCALAPPDATA%\SistemaAlquileres</summary>
    public static string CarpetaDatosPorDefecto =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SistemaAlquileres");

    public static BaseDatos PorDefecto() =>
        new(Path.Combine(CarpetaDatosPorDefecto, "datos.db"));

    public string CarpetaDatos => Path.GetDirectoryName(RutaArchivo)!;

    /// <summary>
    /// Pooling desactivado: así el archivo no queda bloqueado y se puede restaurar
    /// un respaldo sin cerrar el programa. Para un solo usuario no afecta el rendimiento.
    /// </summary>
    public string CadenaConexion => new SqliteConnectionStringBuilder
    {
        DataSource = RutaArchivo,
        Pooling = false,
        ForeignKeys = true
    }.ToString();

    public SqliteConnection Abrir()
    {
        var cn = new SqliteConnection(CadenaConexion);
        cn.Open();
        return cn;
    }

    /// <summary>Crea la carpeta, el archivo y las tablas si no existen.</summary>
    public void Inicializar()
    {
        Directory.CreateDirectory(CarpetaDatos);
        using var cn = Abrir();

        long version = Escalar<long>(cn, "PRAGMA user_version;");
        if (version >= VersionEsquema) return;

        using var tx = cn.BeginTransaction();
        Ejecutar(cn, tx, """
            CREATE TABLE IF NOT EXISTS productos (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                nombre          TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                precio_con_iva  INTEGER NOT NULL CHECK (precio_con_iva >= 0),  -- céntimos
                activo          INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS clientes (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                nombre          TEXT NOT NULL,
                identificacion  TEXT,
                telefono        TEXT,
                correo          TEXT,
                notas           TEXT
            );

            CREATE TABLE IF NOT EXISTS ordenes (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                fecha             TEXT    NOT NULL,             -- yyyy-MM-dd
                cliente_id        INTEGER NOT NULL REFERENCES clientes(id),
                fecha_inicio      TEXT    NOT NULL,
                fecha_fin         TEXT,
                estado            TEXT    NOT NULL DEFAULT 'Activa'
                                  CHECK (estado IN ('Activa','Devuelta','Anulada')),
                notas             TEXT,
                motivo_anulacion  TEXT,
                creada_en         TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS orden_lineas (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                orden_id         INTEGER NOT NULL REFERENCES ordenes(id) ON DELETE CASCADE,
                producto_id      INTEGER NOT NULL REFERENCES productos(id),
                producto_nombre  TEXT    NOT NULL,
                precio_unitario  INTEGER NOT NULL CHECK (precio_unitario >= 0),
                cantidad         INTEGER NOT NULL CHECK (cantidad > 0),
                dias             INTEGER NOT NULL CHECK (dias > 0),
                tasa_iva         INTEGER NOT NULL,
                total            INTEGER NOT NULL,
                base             INTEGER NOT NULL,
                iva              INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS config (
                clave  TEXT PRIMARY KEY,
                valor  TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_ordenes_fecha  ON ordenes(fecha);
            CREATE INDEX IF NOT EXISTS ix_lineas_orden   ON orden_lineas(orden_id);
            """);

        // Los 4 productos iniciales: el cliente les cambia nombre y precio en la pantalla Productos.
        long cantidad = Escalar<long>(cn, "SELECT COUNT(*) FROM productos;", tx);
        if (cantidad == 0)
        {
            for (int i = 1; i <= 4; i++)
                Ejecutar(cn, tx, $"INSERT INTO productos (nombre, precio_con_iva) VALUES ('Producto {i}', 0);");
        }

        Ejecutar(cn, tx, $"PRAGMA user_version = {VersionEsquema};");
        tx.Commit();
    }

    internal static void Ejecutar(SqliteConnection cn, SqliteTransaction? tx, string sql)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    internal static T Escalar<T>(SqliteConnection cn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        object? r = cmd.ExecuteScalar();
        return r is null or DBNull ? default! : (T)Convert.ChangeType(r, typeof(T));
    }

    internal const string FormatoFecha = "yyyy-MM-dd";

    internal static string Fecha(DateTime d) => d.ToString(FormatoFecha);
    internal static DateTime LeerFecha(string s) =>
        DateTime.ParseExact(s, FormatoFecha, System.Globalization.CultureInfo.InvariantCulture);
}
