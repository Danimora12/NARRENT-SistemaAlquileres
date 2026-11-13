using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>
/// Acceso al archivo SQLite. No hay servidor: la base de datos es un solo archivo
/// que se crea automáticamente la primera vez que se abre el programa.
/// </summary>
public class BaseDatos
{
    private const int VersionEsquema = 3;

    public string RutaArchivo { get; }

    public BaseDatos(string rutaArchivo)
    {
        RutaArchivo = Path.GetFullPath(rutaArchivo);
    }

    /// <summary>
    /// Carpeta por defecto: %LOCALAPPDATA%\SistemaAlquileres. Se conserva el nombre
    /// original (aunque el programa ahora se llame NARRENT) para que al actualizar
    /// el .exe el cliente siga viendo sus datos.
    /// </summary>
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

    /// <summary>
    /// Crea la carpeta, el archivo y las tablas si no existen, y actualiza bases
    /// de versiones anteriores paso a paso (v1 → v2). Cada paso va en su propia
    /// transacción: si algo falla, la base queda como estaba.
    /// </summary>
    public void Inicializar()
    {
        Directory.CreateDirectory(CarpetaDatos);
        using var cn = Abrir();

        long version = Escalar<long>(cn, "PRAGMA user_version;");
        if (version >= VersionEsquema) return;

        if (version < 1) Migrar(cn, 1, CrearEsquemaV1);
        if (version < 2) Migrar(cn, 2, MigrarAV2);
        if (version < 3) Migrar(cn, 3, MigrarAV3);
    }

    /// <summary>v3: registro de gastos de la empresa (para la utilidad del flujo de caja).</summary>
    private static void MigrarAV3(SqliteConnection cn, SqliteTransaction tx)
    {
        Ejecutar(cn, tx, """
            CREATE TABLE gastos (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                fecha        TEXT    NOT NULL,                      -- yyyy-MM-dd
                categoria    TEXT    NOT NULL,
                descripcion  TEXT    NOT NULL,
                proveedor    TEXT,
                metodo       TEXT    NOT NULL CHECK (metodo IN ('Sinpe','Transferencia','Efectivo','Tarjeta')),
                monto        INTEGER NOT NULL CHECK (monto > 0),    -- céntimos, total pagado
                iva          INTEGER NOT NULL DEFAULT 0 CHECK (iva >= 0 AND iva <= monto),
                creado_en    TEXT    NOT NULL
            );
            CREATE INDEX ix_gastos_fecha ON gastos(fecha);
            """);
    }

    private static void Migrar(SqliteConnection cn, int versionDestino, Action<SqliteConnection, SqliteTransaction> pasos)
    {
        using var tx = cn.BeginTransaction();
        pasos(cn, tx);
        Ejecutar(cn, tx, $"PRAGMA user_version = {versionDestino};");
        tx.Commit();
    }

    /// <summary>Esquema original (v1). No se modifica: las mejoras van en migraciones.</summary>
    private static void CrearEsquemaV1(SqliteConnection cn, SqliteTransaction tx)
    {
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
    }

    /// <summary>
    /// v2 (NARRENT): número de orden año+mes+consecutivo, papelera de órdenes,
    /// descuentos por línea, pagos/abonos, inventario con características y logs.
    /// </summary>
    private static void MigrarAV2(SqliteConnection cn, SqliteTransaction tx)
    {
        Ejecutar(cn, tx, """
            ALTER TABLE productos    ADD COLUMN inventario INTEGER NOT NULL DEFAULT 0 CHECK (inventario >= 0);

            ALTER TABLE ordenes      ADD COLUMN numero             TEXT;
            ALTER TABLE ordenes      ADD COLUMN eliminada          INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE ordenes      ADD COLUMN eliminada_en       TEXT;
            ALTER TABLE ordenes      ADD COLUMN eliminada_por      TEXT;
            ALTER TABLE ordenes      ADD COLUMN motivo_eliminacion TEXT;

            ALTER TABLE orden_lineas ADD COLUMN descuento_pct INTEGER NOT NULL DEFAULT 0 CHECK (descuento_pct BETWEEN 0 AND 15);
            ALTER TABLE orden_lineas ADD COLUMN descuento     INTEGER NOT NULL DEFAULT 0 CHECK (descuento >= 0);  -- céntimos

            CREATE TABLE producto_variantes (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                producto_id  INTEGER NOT NULL REFERENCES productos(id) ON DELETE CASCADE,
                descripcion  TEXT    NOT NULL,
                cantidad     INTEGER NOT NULL CHECK (cantidad >= 0)
            );

            CREATE TABLE pagos (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                orden_id   INTEGER NOT NULL REFERENCES ordenes(id) ON DELETE CASCADE,
                fecha      TEXT    NOT NULL,                     -- yyyy-MM-dd
                metodo     TEXT    NOT NULL CHECK (metodo IN ('Sinpe','Transferencia','Efectivo','Tarjeta')),
                monto      INTEGER NOT NULL CHECK (monto > 0),   -- céntimos
                creado_en  TEXT    NOT NULL
            );

            CREATE TABLE logs (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                fecha_hora  TEXT NOT NULL,                       -- yyyy-MM-dd HH:mm:ss
                empleado    TEXT,
                accion      TEXT NOT NULL,
                referencia  TEXT NOT NULL,
                detalle     TEXT NOT NULL
            );

            CREATE INDEX ix_variantes_producto ON producto_variantes(producto_id);
            CREATE INDEX ix_pagos_orden        ON pagos(orden_id);
            CREATE INDEX ix_logs_fecha         ON logs(fecha_hora);
            """);

        // Numerar las órdenes existentes con el nuevo formato, según su fecha.
        var existentes = new List<(long Id, string Fecha)>();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, fecha FROM ordenes ORDER BY fecha, id;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) existentes.Add((r.GetInt64(0), r.GetString(1)));
        }
        var consecutivos = new Dictionary<string, int>();
        foreach (var (id, fecha) in existentes)
        {
            string prefijo = Numeracion.Prefijo(LeerFecha(fecha));
            int n = consecutivos[prefijo] = consecutivos.GetValueOrDefault(prefijo) + 1;
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE ordenes SET numero = $n WHERE id = $id;";
            cmd.Parameters.AddWithValue("$n", Numeracion.Formatear(prefijo, n));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        Ejecutar(cn, tx, "CREATE UNIQUE INDEX ux_ordenes_numero ON ordenes(numero);");

        // El inventario se pone en 0 y el cliente lo completa en Productos.
        LogRepositorio.Registrar(cn, tx, null, "Actualización a NARRENT v2", "Sistema",
            existentes.Count == 0
                ? "Base de datos actualizada."
                : $"Base de datos actualizada. Se renumeraron {existentes.Count} órdenes existentes con el formato año-mes-consecutivo.");
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
    internal const string FormatoFechaHora = "yyyy-MM-dd HH:mm:ss";

    internal static string FechaHora(DateTime d) => d.ToString(FormatoFechaHora);
    internal static DateTime LeerFechaHora(string s) =>
        DateTime.ParseExact(s, FormatoFechaHora, System.Globalization.CultureInfo.InvariantCulture);

    internal static string Fecha(DateTime d) => d.ToString(FormatoFecha);
    internal static DateTime LeerFecha(string s) =>
        DateTime.ParseExact(s, FormatoFecha, System.Globalization.CultureInfo.InvariantCulture);
}
