using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>Error de validación con un mensaje apto para mostrar al usuario.</summary>
public class ReglaNegocioException(string mensaje) : Exception(mensaje);

public class ProductoRepositorio(BaseDatos db)
{
    public List<Producto> Listar(bool soloActivos = false)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id, nombre, precio_con_iva, activo FROM productos"
                          + (soloActivos ? " WHERE activo = 1" : "")
                          + " ORDER BY nombre;";
        using var r = cmd.ExecuteReader();
        var lista = new List<Producto>();
        while (r.Read())
            lista.Add(new Producto
            {
                Id = r.GetInt64(0),
                Nombre = r.GetString(1),
                PrecioConIvaCentimos = r.GetInt64(2),
                Activo = r.GetInt64(3) == 1
            });
        return lista;
    }

    public void Guardar(Producto p)
    {
        if (string.IsNullOrWhiteSpace(p.Nombre)) throw new ReglaNegocioException("El nombre del producto es obligatorio.");
        if (p.PrecioConIvaCentimos < 0) throw new ReglaNegocioException("El precio no puede ser negativo.");

        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = p.Id == 0
            ? "INSERT INTO productos (nombre, precio_con_iva, activo) VALUES ($n, $p, $a) RETURNING id;"
            : "UPDATE productos SET nombre = $n, precio_con_iva = $p, activo = $a WHERE id = $id RETURNING id;";
        cmd.Parameters.AddWithValue("$n", p.Nombre.Trim());
        cmd.Parameters.AddWithValue("$p", p.PrecioConIvaCentimos);
        cmd.Parameters.AddWithValue("$a", p.Activo ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", p.Id);
        try
        {
            p.Id = (long)cmd.ExecuteScalar()!;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE
        {
            throw new ReglaNegocioException($"Ya existe un producto llamado \"{p.Nombre.Trim()}\".");
        }
    }
}

public class ClienteRepositorio(BaseDatos db)
{
    public List<Cliente> Listar(string? filtro = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT id, nombre, identificacion, telefono, correo, notas FROM clientes
            WHERE $f IS NULL OR nombre LIKE $f OR identificacion LIKE $f OR telefono LIKE $f
            ORDER BY nombre;
            """;
        cmd.Parameters.AddWithValue("$f",
            string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : $"%{filtro.Trim()}%");
        using var r = cmd.ExecuteReader();
        var lista = new List<Cliente>();
        while (r.Read())
            lista.Add(new Cliente
            {
                Id = r.GetInt64(0),
                Nombre = r.GetString(1),
                Identificacion = r.IsDBNull(2) ? null : r.GetString(2),
                Telefono = r.IsDBNull(3) ? null : r.GetString(3),
                Correo = r.IsDBNull(4) ? null : r.GetString(4),
                Notas = r.IsDBNull(5) ? null : r.GetString(5)
            });
        return lista;
    }

    public void Guardar(Cliente c)
    {
        if (string.IsNullOrWhiteSpace(c.Nombre)) throw new ReglaNegocioException("El nombre del cliente es obligatorio.");

        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = c.Id == 0
            ? "INSERT INTO clientes (nombre, identificacion, telefono, correo, notas) VALUES ($n, $i, $t, $c, $no) RETURNING id;"
            : "UPDATE clientes SET nombre=$n, identificacion=$i, telefono=$t, correo=$c, notas=$no WHERE id=$id RETURNING id;";
        cmd.Parameters.AddWithValue("$n", c.Nombre.Trim());
        cmd.Parameters.AddWithValue("$i", Nulo(c.Identificacion));
        cmd.Parameters.AddWithValue("$t", Nulo(c.Telefono));
        cmd.Parameters.AddWithValue("$c", Nulo(c.Correo));
        cmd.Parameters.AddWithValue("$no", Nulo(c.Notas));
        cmd.Parameters.AddWithValue("$id", c.Id);
        c.Id = (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Solo se puede eliminar un cliente que no tenga órdenes.</summary>
    public void Eliminar(long id)
    {
        using var cn = db.Abrir();
        long ordenes = BaseDatos.Escalar<long>(cn, $"SELECT COUNT(*) FROM ordenes WHERE cliente_id = {id};");
        if (ordenes > 0)
            throw new ReglaNegocioException("No se puede eliminar: el cliente tiene órdenes registradas.");
        BaseDatos.Ejecutar(cn, null, $"DELETE FROM clientes WHERE id = {id};");
    }

    internal static object Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();
}

public class OrdenRepositorio(BaseDatos db)
{
    /// <summary>
    /// Guarda una orden nueva. Los totales se recalculan aquí, nunca se confía
    /// en lo que venga de la pantalla.
    /// </summary>
    public long Crear(Orden o)
    {
        if (o.ClienteId == 0) throw new ReglaNegocioException("Seleccione un cliente.");
        if (o.Lineas.Count == 0) throw new ReglaNegocioException("La orden debe tener al menos un producto.");
        if (o.FechaFin is { } fin && fin.Date < o.FechaInicio.Date)
            throw new ReglaNegocioException("La fecha de devolución no puede ser anterior a la fecha de inicio.");

        foreach (var l in o.Lineas)
        {
            if (l.Cantidad <= 0) throw new ReglaNegocioException("La cantidad debe ser mayor que cero.");
            if (l.Dias <= 0) throw new ReglaNegocioException("Los días deben ser mayores que cero.");
            l.Calcular();
        }

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();

        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ordenes (fecha, cliente_id, fecha_inicio, fecha_fin, estado, notas, creada_en)
                VALUES ($f, $c, $fi, $ff, 'Activa', $n, $creada) RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(o.Fecha));
            cmd.Parameters.AddWithValue("$c", o.ClienteId);
            cmd.Parameters.AddWithValue("$fi", BaseDatos.Fecha(o.FechaInicio));
            cmd.Parameters.AddWithValue("$ff", o.FechaFin is { } ff ? BaseDatos.Fecha(ff) : DBNull.Value);
            cmd.Parameters.AddWithValue("$n", ClienteRepositorio.Nulo(o.Notas));
            cmd.Parameters.AddWithValue("$creada", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            o.Id = (long)cmd.ExecuteScalar()!;
        }

        foreach (var l in o.Lineas)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO orden_lineas (orden_id, producto_id, producto_nombre, precio_unitario,
                                          cantidad, dias, tasa_iva, total, base, iva)
                VALUES ($o, $p, $pn, $pu, $c, $d, $t, $tot, $b, $i) RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$o", o.Id);
            cmd.Parameters.AddWithValue("$p", l.ProductoId);
            cmd.Parameters.AddWithValue("$pn", l.ProductoNombre);
            cmd.Parameters.AddWithValue("$pu", l.PrecioUnitarioCentimos);
            cmd.Parameters.AddWithValue("$c", l.Cantidad);
            cmd.Parameters.AddWithValue("$d", l.Dias);
            cmd.Parameters.AddWithValue("$t", l.TasaIva);
            cmd.Parameters.AddWithValue("$tot", l.TotalCentimos);
            cmd.Parameters.AddWithValue("$b", l.BaseCentimos);
            cmd.Parameters.AddWithValue("$i", l.IvaCentimos);
            l.Id = (long)cmd.ExecuteScalar()!;
            l.OrdenId = o.Id;
        }

        tx.Commit();
        o.TotalCentimos = o.Lineas.Sum(l => l.TotalCentimos);
        o.BaseCentimos = o.Lineas.Sum(l => l.BaseCentimos);
        o.IvaCentimos = o.Lineas.Sum(l => l.IvaCentimos);
        return o.Id;
    }

    private const string SelectOrdenes = """
        SELECT o.id, o.fecha, o.cliente_id, c.nombre, o.fecha_inicio, o.fecha_fin, o.estado,
               o.notas, o.motivo_anulacion,
               COALESCE(SUM(l.total), 0), COALESCE(SUM(l.base), 0), COALESCE(SUM(l.iva), 0)
        FROM ordenes o
        JOIN clientes c ON c.id = o.cliente_id
        LEFT JOIN orden_lineas l ON l.orden_id = o.id
        """;

    public List<Orden> Listar(DateTime desde, DateTime hasta, EstadoOrden? estado = null, string? textoCliente = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectOrdenes + """
             WHERE o.fecha BETWEEN $d AND $h
               AND ($e IS NULL OR o.estado = $e)
               AND ($t IS NULL OR c.nombre LIKE $t OR c.identificacion LIKE $t)
            GROUP BY o.id
            ORDER BY o.fecha DESC, o.id DESC;
            """;
        cmd.Parameters.AddWithValue("$d", BaseDatos.Fecha(desde));
        cmd.Parameters.AddWithValue("$h", BaseDatos.Fecha(hasta));
        cmd.Parameters.AddWithValue("$e", estado is { } e ? e.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$t",
            string.IsNullOrWhiteSpace(textoCliente) ? DBNull.Value : $"%{textoCliente.Trim()}%");
        return LeerOrdenes(cmd);
    }

    public Orden? Obtener(long id)
    {
        using var cn = db.Abrir();
        Orden? orden;
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = SelectOrdenes + " WHERE o.id = $id GROUP BY o.id;";
            cmd.Parameters.AddWithValue("$id", id);
            orden = LeerOrdenes(cmd).FirstOrDefault();
        }
        if (orden is null) return null;

        using var cmdL = cn.CreateCommand();
        cmdL.CommandText = """
            SELECT id, producto_id, producto_nombre, precio_unitario, cantidad, dias, tasa_iva, total, base, iva
            FROM orden_lineas WHERE orden_id = $id ORDER BY id;
            """;
        cmdL.Parameters.AddWithValue("$id", id);
        using var r = cmdL.ExecuteReader();
        while (r.Read())
            orden.Lineas.Add(new LineaOrden
            {
                Id = r.GetInt64(0),
                OrdenId = id,
                ProductoId = r.GetInt64(1),
                ProductoNombre = r.GetString(2),
                PrecioUnitarioCentimos = r.GetInt64(3),
                Cantidad = r.GetInt32(4),
                Dias = r.GetInt32(5),
                TasaIva = r.GetInt32(6),
                TotalCentimos = r.GetInt64(7),
                BaseCentimos = r.GetInt64(8),
                IvaCentimos = r.GetInt64(9)
            });
        return orden;
    }

    public void MarcarDevuelta(long id, DateTime fechaDevolucion)
    {
        var o = Obtener(id) ?? throw new ReglaNegocioException("La orden no existe.");
        if (o.Estado != EstadoOrden.Activa)
            throw new ReglaNegocioException($"Solo se pueden marcar como devueltas las órdenes activas (esta está {o.Estado}).");
        if (fechaDevolucion.Date < o.FechaInicio.Date)
            throw new ReglaNegocioException("La fecha de devolución no puede ser anterior a la fecha de inicio.");

        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE ordenes SET estado = 'Devuelta', fecha_fin = $f WHERE id = $id;";
        cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(fechaDevolucion));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Las órdenes no se borran: se anulan con un motivo, para mantener la trazabilidad.
    /// Las anuladas no cuentan en el reporte de IVA.
    /// </summary>
    public void Anular(long id, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new ReglaNegocioException("Indique el motivo de la anulación.");
        var o = Obtener(id) ?? throw new ReglaNegocioException("La orden no existe.");
        if (o.Estado == EstadoOrden.Anulada) throw new ReglaNegocioException("La orden ya está anulada.");

        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE ordenes SET estado = 'Anulada', motivo_anulacion = $m WHERE id = $id;";
        cmd.Parameters.AddWithValue("$m", motivo.Trim());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<Orden> LeerOrdenes(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var lista = new List<Orden>();
        while (r.Read())
            lista.Add(new Orden
            {
                Id = r.GetInt64(0),
                Fecha = BaseDatos.LeerFecha(r.GetString(1)),
                ClienteId = r.GetInt64(2),
                ClienteNombre = r.GetString(3),
                FechaInicio = BaseDatos.LeerFecha(r.GetString(4)),
                FechaFin = r.IsDBNull(5) ? null : BaseDatos.LeerFecha(r.GetString(5)),
                Estado = Enum.Parse<EstadoOrden>(r.GetString(6)),
                Notas = r.IsDBNull(7) ? null : r.GetString(7),
                MotivoAnulacion = r.IsDBNull(8) ? null : r.GetString(8),
                TotalCentimos = r.GetInt64(9),
                BaseCentimos = r.GetInt64(10),
                IvaCentimos = r.GetInt64(11)
            });
        return lista;
    }
}

public class ConfigRepositorio(BaseDatos db)
{
    public const string CarpetaRespaldoExtra = "carpeta_respaldo_extra";

    public string? Obtener(string clave)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT valor FROM config WHERE clave = $k;";
        cmd.Parameters.AddWithValue("$k", clave);
        return cmd.ExecuteScalar() as string;
    }

    public void Guardar(string clave, string? valor)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = valor is null
            ? "DELETE FROM config WHERE clave = $k;"
            : "INSERT INTO config (clave, valor) VALUES ($k, $v) ON CONFLICT(clave) DO UPDATE SET valor = excluded.valor;";
        cmd.Parameters.AddWithValue("$k", clave);
        cmd.Parameters.AddWithValue("$v", (object?)valor ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
