using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>Error de validación con un mensaje apto para mostrar al usuario.</summary>
public class ReglaNegocioException(string mensaje) : Exception(mensaje);

public class ProductoRepositorio(BaseDatos db)
{
    private const string SelectProductos = """
        SELECT p.id, p.nombre, p.precio_con_iva, p.activo, p.inventario,
               (SELECT COUNT(*) FROM producto_variantes v WHERE v.producto_id = p.id)
        FROM productos p
        """;

    public List<Producto> Listar(bool soloActivos = false)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectProductos + (soloActivos ? " WHERE p.activo = 1" : "") + " ORDER BY p.nombre;";
        return Leer(cmd);
    }

    public Producto? Obtener(long id)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectProductos + " WHERE p.id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Leer(cmd).FirstOrDefault();
    }

    private static List<Producto> Leer(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var lista = new List<Producto>();
        while (r.Read())
            lista.Add(new Producto
            {
                Id = r.GetInt64(0),
                Nombre = r.GetString(1),
                PrecioConIvaCentimos = r.GetInt64(2),
                Activo = r.GetInt64(3) == 1,
                Inventario = r.GetInt32(4),
                CantidadVariantes = r.GetInt32(5)
            });
        return lista;
    }

    /// <summary>
    /// Crea o actualiza un producto y lo deja en Logs con el empleado que autorizó.
    /// Si el producto tiene características de inventario, el inventario total
    /// no se toma de aquí (es la suma de las características).
    /// </summary>
    public void Guardar(Producto p, string? empleado = null)
    {
        if (string.IsNullOrWhiteSpace(p.Nombre)) throw new ReglaNegocioException("El nombre del producto es obligatorio.");
        if (p.PrecioConIvaCentimos < 0) throw new ReglaNegocioException("El precio no puede ser negativo.");
        if (p.Inventario < 0) throw new ReglaNegocioException("El inventario no puede ser negativo.");

        var antes = p.Id == 0 ? null : Obtener(p.Id) ?? throw new ReglaNegocioException("El producto ya no existe.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = p.Id == 0
                ? "INSERT INTO productos (nombre, precio_con_iva, activo, inventario) VALUES ($n, $p, $a, $inv) RETURNING id;"
                : """
                  UPDATE productos SET nombre = $n, precio_con_iva = $p, activo = $a,
                         inventario = CASE WHEN EXISTS (SELECT 1 FROM producto_variantes WHERE producto_id = $id)
                                           THEN inventario ELSE $inv END
                  WHERE id = $id RETURNING id;
                  """;
            cmd.Parameters.AddWithValue("$n", p.Nombre.Trim());
            cmd.Parameters.AddWithValue("$p", p.PrecioConIvaCentimos);
            cmd.Parameters.AddWithValue("$a", p.Activo ? 1 : 0);
            cmd.Parameters.AddWithValue("$inv", p.Inventario);
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

        if (antes is null)
        {
            LogRepositorio.Registrar(cn, tx, empleado, "Producto creado", $"Producto {p.Nombre.Trim()}",
                $"Precio {Dinero.Formatear(p.PrecioConIvaCentimos)} · Inventario {p.Inventario} · Disponible {(p.Activo ? "Sí" : "No")}");
        }
        else
        {
            var cambios = new Cambios()
                .Campo("Nombre", antes.Nombre, p.Nombre.Trim())
                .Monto("Precio", antes.PrecioConIvaCentimos, p.PrecioConIvaCentimos)
                .Campo("Disponible", antes.Activo, p.Activo);
            if (!antes.TieneVariantes) cambios.Campo("Inventario", antes.Inventario, p.Inventario);
            if (cambios.HayCambios)
                LogRepositorio.Registrar(cn, tx, empleado, "Producto editado", $"Producto {p.Nombre.Trim()}", cambios.ToString());
        }
        tx.Commit();
    }

    public List<VarianteInventario> ListarVariantes(long productoId)
    {
        using var cn = db.Abrir();
        return ListarVariantes(cn, null, productoId);
    }

    private static List<VarianteInventario> ListarVariantes(SqliteConnection cn, SqliteTransaction? tx, long productoId)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, descripcion, cantidad FROM producto_variantes WHERE producto_id = $p ORDER BY id;";
        cmd.Parameters.AddWithValue("$p", productoId);
        using var r = cmd.ExecuteReader();
        var lista = new List<VarianteInventario>();
        while (r.Read())
            lista.Add(new VarianteInventario
            {
                Id = r.GetInt64(0),
                ProductoId = productoId,
                Descripcion = r.GetString(1),
                Cantidad = r.GetInt32(2)
            });
        return lista;
    }

    /// <summary>
    /// Reemplaza las características de inventario del producto (ej. colores y cantidad
    /// por color). Si quedan características, el inventario total pasa a ser su suma.
    /// </summary>
    public void GuardarVariantes(long productoId, IEnumerable<VarianteInventario> variantes, string? empleado = null)
    {
        var nuevas = variantes
            .Select(v => new VarianteInventario { ProductoId = productoId, Descripcion = (v.Descripcion ?? "").Trim(), Cantidad = v.Cantidad })
            .ToList();
        if (nuevas.Any(v => v.Descripcion.Length == 0))
            throw new ReglaNegocioException("Cada característica necesita una descripción (por ejemplo, el color).");
        if (nuevas.Any(v => v.Cantidad < 0))
            throw new ReglaNegocioException("Las cantidades no pueden ser negativas.");
        var repetida = nuevas.GroupBy(v => v.Descripcion, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null)
            throw new ReglaNegocioException($"La característica \"{repetida.Key}\" está repetida.");

        var producto = Obtener(productoId) ?? throw new ReglaNegocioException("El producto ya no existe.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        var anteriores = ListarVariantes(cn, tx, productoId);

        BaseDatos.Ejecutar(cn, tx, $"DELETE FROM producto_variantes WHERE producto_id = {productoId};");
        foreach (var v in nuevas)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO producto_variantes (producto_id, descripcion, cantidad) VALUES ($p, $d, $c) RETURNING id;";
            cmd.Parameters.AddWithValue("$p", productoId);
            cmd.Parameters.AddWithValue("$d", v.Descripcion);
            cmd.Parameters.AddWithValue("$c", v.Cantidad);
            v.Id = (long)cmd.ExecuteScalar()!;
        }
        if (nuevas.Count > 0)
            BaseDatos.Ejecutar(cn, tx, $"UPDATE productos SET inventario = {nuevas.Sum(v => v.Cantidad)} WHERE id = {productoId};");

        static string Describir(List<VarianteInventario> l) =>
            l.Count == 0 ? "sin características" : $"{string.Join(", ", l.Select(v => $"{v.Descripcion} {v.Cantidad}"))} (total {l.Sum(v => v.Cantidad)})";

        string antes = Describir(anteriores), despues = Describir(nuevas);
        if (antes != despues)
            LogRepositorio.Registrar(cn, tx, empleado, "Inventario actualizado", $"Producto {producto.Nombre}",
                $"Antes: {antes} → Ahora: {despues}");
        tx.Commit();
    }
}

public class ClienteRepositorio(BaseDatos db)
{
    private const string SelectClientes = "SELECT id, nombre, identificacion, telefono, correo, notas FROM clientes";

    public List<Cliente> Listar(string? filtro = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectClientes + """
             WHERE $f IS NULL OR nombre LIKE $f OR identificacion LIKE $f OR telefono LIKE $f
            ORDER BY nombre;
            """;
        cmd.Parameters.AddWithValue("$f",
            string.IsNullOrWhiteSpace(filtro) ? DBNull.Value : $"%{filtro.Trim()}%");
        return Leer(cmd);
    }

    public Cliente? Obtener(long id)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectClientes + " WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Leer(cmd).FirstOrDefault();
    }

    private static List<Cliente> Leer(SqliteCommand cmd)
    {
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

    /// <summary>Crea o edita un cliente. Las ediciones quedan en Logs con el empleado que autorizó.</summary>
    public void Guardar(Cliente c, string? empleado = null)
    {
        if (string.IsNullOrWhiteSpace(c.Nombre)) throw new ReglaNegocioException("El nombre del cliente es obligatorio.");
        var antes = c.Id == 0 ? null : Obtener(c.Id) ?? throw new ReglaNegocioException("El cliente ya no existe.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
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

        if (antes is null)
        {
            LogRepositorio.Registrar(cn, tx, empleado, "Cliente creado", $"Cliente {c.Nombre.Trim()}",
                string.Join(" · ", new[] { c.Identificacion, c.Telefono, c.Correo }.Where(x => !string.IsNullOrWhiteSpace(x)).DefaultIfEmpty("Sin datos adicionales")));
        }
        else
        {
            var cambios = new Cambios()
                .Campo("Nombre", antes.Nombre, c.Nombre)
                .Campo("Identificación", antes.Identificacion, c.Identificacion)
                .Campo("Teléfono", antes.Telefono, c.Telefono)
                .Campo("Correo", antes.Correo, c.Correo)
                .Campo("Notas", antes.Notas, c.Notas);
            if (cambios.HayCambios)
                LogRepositorio.Registrar(cn, tx, empleado, "Cliente editado", $"Cliente {c.Nombre.Trim()}", cambios.ToString());
        }
        tx.Commit();
    }

    /// <summary>Solo se puede eliminar un cliente que no tenga órdenes (ni siquiera eliminadas).</summary>
    public void Eliminar(long id, string? empleado = null)
    {
        var c = Obtener(id) ?? throw new ReglaNegocioException("El cliente ya no existe.");
        using var cn = db.Abrir();
        long ordenes = BaseDatos.Escalar<long>(cn, $"SELECT COUNT(*) FROM ordenes WHERE cliente_id = {id};");
        if (ordenes > 0)
            throw new ReglaNegocioException("No se puede eliminar: el cliente tiene órdenes registradas.");
        using var tx = cn.BeginTransaction();
        BaseDatos.Ejecutar(cn, tx, $"DELETE FROM clientes WHERE id = {id};");
        LogRepositorio.Registrar(cn, tx, empleado, "Cliente eliminado", $"Cliente {c.Nombre}",
            string.Join(" · ", new[] { c.Identificacion, c.Telefono }.Where(x => !string.IsNullOrWhiteSpace(x)).DefaultIfEmpty("—")));
        tx.Commit();
    }

    internal static object Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();
}

public class OrdenRepositorio(BaseDatos db)
{
    /// <summary>
    /// Guarda una orden nueva con su pago adelantado (opcional). Los totales se
    /// recalculan aquí, nunca se confía en lo que venga de la pantalla. Orden,
    /// líneas, pago y log se guardan en una sola transacción.
    /// </summary>
    public long Crear(Orden o, Pago? pagoInicial = null)
    {
        if (o.ClienteId == 0) throw new ReglaNegocioException("Seleccione un cliente.");
        if (o.Lineas.Count == 0) throw new ReglaNegocioException("La orden debe tener al menos un producto.");
        if (o.FechaFin is { } fin && fin.Date < o.FechaInicio.Date)
            throw new ReglaNegocioException("La fecha de devolución no puede ser anterior a la fecha de inicio.");

        foreach (var l in o.Lineas)
        {
            if (l.Cantidad <= 0) throw new ReglaNegocioException("La cantidad debe ser mayor que cero.");
            if (l.Dias <= 0) throw new ReglaNegocioException("Los días deben ser mayores que cero.");
            if (l.DescuentoPct is < 0 or > CalculadoraIva.DescuentoMaximo)
                throw new ReglaNegocioException($"El descuento debe estar entre 1 y {CalculadoraIva.DescuentoMaximo} %.");
            l.Calcular();
        }
        long total = o.Lineas.Sum(l => l.TotalCentimos);
        if (pagoInicial is { MontoCentimos: < 0 }) throw new ReglaNegocioException("El monto pagado no puede ser negativo.");
        if (pagoInicial is not null && pagoInicial.MontoCentimos > total)
            throw new ReglaNegocioException($"El monto pagado ({Dinero.Formatear(pagoInicial.MontoCentimos)}) es mayor que el total de la orden ({Dinero.Formatear(total)}).");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();

        o.Numero = Numeracion.Siguiente(cn, tx, o.Fecha);
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ordenes (numero, fecha, cliente_id, fecha_inicio, fecha_fin, estado, notas, creada_en)
                VALUES ($num, $f, $c, $fi, $ff, 'Activa', $n, $creada) RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$num", o.Numero);
            cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(o.Fecha));
            cmd.Parameters.AddWithValue("$c", o.ClienteId);
            cmd.Parameters.AddWithValue("$fi", BaseDatos.Fecha(o.FechaInicio));
            cmd.Parameters.AddWithValue("$ff", o.FechaFin is { } ff ? BaseDatos.Fecha(ff) : DBNull.Value);
            cmd.Parameters.AddWithValue("$n", ClienteRepositorio.Nulo(o.Notas));
            cmd.Parameters.AddWithValue("$creada", BaseDatos.FechaHora(DateTime.Now));
            o.Id = (long)cmd.ExecuteScalar()!;
        }

        foreach (var l in o.Lineas)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO orden_lineas (orden_id, producto_id, producto_nombre, precio_unitario,
                                          cantidad, dias, tasa_iva, descuento_pct, descuento, total, base, iva)
                VALUES ($o, $p, $pn, $pu, $c, $d, $t, $dp, $dm, $tot, $b, $i) RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$o", o.Id);
            cmd.Parameters.AddWithValue("$p", l.ProductoId);
            cmd.Parameters.AddWithValue("$pn", l.ProductoNombre);
            cmd.Parameters.AddWithValue("$pu", l.PrecioUnitarioCentimos);
            cmd.Parameters.AddWithValue("$c", l.Cantidad);
            cmd.Parameters.AddWithValue("$d", l.Dias);
            cmd.Parameters.AddWithValue("$t", l.TasaIva);
            cmd.Parameters.AddWithValue("$dp", l.DescuentoPct);
            cmd.Parameters.AddWithValue("$dm", l.DescuentoCentimos);
            cmd.Parameters.AddWithValue("$tot", l.TotalCentimos);
            cmd.Parameters.AddWithValue("$b", l.BaseCentimos);
            cmd.Parameters.AddWithValue("$i", l.IvaCentimos);
            l.Id = (long)cmd.ExecuteScalar()!;
            l.OrdenId = o.Id;
        }

        o.TotalCentimos = total;
        o.BaseCentimos = o.Lineas.Sum(l => l.BaseCentimos);
        o.IvaCentimos = o.Lineas.Sum(l => l.IvaCentimos);
        o.DescuentoCentimos = o.Lineas.Sum(l => l.DescuentoCentimos);
        o.Pagos.Clear();
        o.PagadoCentimos = 0;

        if (pagoInicial is { MontoCentimos: > 0 })
        {
            pagoInicial.OrdenId = o.Id;
            InsertarPago(cn, tx, pagoInicial);
            o.Pagos.Add(pagoInicial);
            o.PagadoCentimos = pagoInicial.MontoCentimos;
        }

        string cliente = BaseDatos.Escalar<string>(cn, $"SELECT nombre FROM clientes WHERE id = {o.ClienteId};", tx) ?? "";
        var detalle = new List<string> { $"Cliente {cliente}", $"Total {Dinero.Formatear(o.TotalCentimos)}" };
        if (o.DescuentoCentimos > 0) detalle.Add($"Descuento {Dinero.Formatear(o.DescuentoCentimos)} ({o.DescribirDescuento()})");
        detalle.Add(o.PagadoCentimos > 0
            ? $"Pago inicial {Dinero.Formatear(o.PagadoCentimos)} ({pagoInicial!.MetodoTexto}) · Saldo {Dinero.Formatear(o.SaldoCentimos)}"
            : "Sin pago inicial");
        LogRepositorio.Registrar(cn, tx, null, "Orden creada", Ref(o), string.Join(" · ", detalle));

        tx.Commit();
        return o.Id;
    }

    private static void InsertarPago(SqliteConnection cn, SqliteTransaction tx, Pago p)
    {
        p.CreadoEn = DateTime.Now;
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO pagos (orden_id, fecha, metodo, monto, creado_en) VALUES ($o, $f, $m, $monto, $c) RETURNING id;";
        cmd.Parameters.AddWithValue("$o", p.OrdenId);
        cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(p.Fecha));
        cmd.Parameters.AddWithValue("$m", p.Metodo.ToString());
        cmd.Parameters.AddWithValue("$monto", p.MontoCentimos);
        cmd.Parameters.AddWithValue("$c", BaseDatos.FechaHora(p.CreadoEn));
        p.Id = (long)cmd.ExecuteScalar()!;
    }

    private const string SelectOrdenes = """
        SELECT o.id, o.numero, o.fecha, o.cliente_id, c.nombre, o.fecha_inicio, o.fecha_fin, o.estado,
               o.notas, o.motivo_anulacion, o.eliminada, o.eliminada_en, o.eliminada_por, o.motivo_eliminacion,
               COALESCE(SUM(l.total), 0), COALESCE(SUM(l.base), 0), COALESCE(SUM(l.iva), 0), COALESCE(SUM(l.descuento), 0),
               (SELECT COALESCE(SUM(p.monto), 0) FROM pagos p WHERE p.orden_id = o.id)
        FROM ordenes o
        JOIN clientes c ON c.id = o.cliente_id
        LEFT JOIN orden_lineas l ON l.orden_id = o.id
        """;

    /// <summary>Órdenes del rango (sin las eliminadas). El texto busca por cliente, cédula o número.</summary>
    public List<Orden> Listar(DateTime desde, DateTime hasta, EstadoOrden? estado = null, string? textoCliente = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectOrdenes + """
             WHERE o.eliminada = 0
               AND o.fecha BETWEEN $d AND $h
               AND ($e IS NULL OR o.estado = $e)
               AND ($t IS NULL OR c.nombre LIKE $t OR c.identificacion LIKE $t OR o.numero LIKE $t)
            GROUP BY o.id
            ORDER BY o.fecha DESC, o.numero DESC;
            """;
        cmd.Parameters.AddWithValue("$d", BaseDatos.Fecha(desde));
        cmd.Parameters.AddWithValue("$h", BaseDatos.Fecha(hasta));
        cmd.Parameters.AddWithValue("$e", estado is { } e ? e.ToString() : DBNull.Value);
        cmd.Parameters.AddWithValue("$t",
            string.IsNullOrWhiteSpace(textoCliente) ? DBNull.Value : $"%{textoCliente.Trim()}%");
        return LeerOrdenes(cmd);
    }

    /// <summary>Papelera: solo las órdenes eliminadas, de la más reciente a la más antigua.</summary>
    public List<Orden> ListarEliminadas()
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectOrdenes + " WHERE o.eliminada = 1 GROUP BY o.id ORDER BY o.eliminada_en DESC, o.id DESC;";
        return LeerOrdenes(cmd);
    }

    /// <summary>Orden completa (líneas y pagos). Devuelve también órdenes eliminadas.</summary>
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

        using (var cmdL = cn.CreateCommand())
        {
            cmdL.CommandText = """
                SELECT id, producto_id, producto_nombre, precio_unitario, cantidad, dias, tasa_iva,
                       descuento_pct, descuento, total, base, iva
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
                    DescuentoPct = r.GetInt32(7),
                    DescuentoCentimos = r.GetInt64(8),
                    TotalCentimos = r.GetInt64(9),
                    BaseCentimos = r.GetInt64(10),
                    IvaCentimos = r.GetInt64(11)
                });
        }

        using var cmdP = cn.CreateCommand();
        cmdP.CommandText = "SELECT id, fecha, metodo, monto, creado_en FROM pagos WHERE orden_id = $id ORDER BY fecha, id;";
        cmdP.Parameters.AddWithValue("$id", id);
        using var rp = cmdP.ExecuteReader();
        while (rp.Read())
            orden.Pagos.Add(new Pago
            {
                Id = rp.GetInt64(0),
                OrdenId = id,
                Fecha = BaseDatos.LeerFecha(rp.GetString(1)),
                Metodo = Enum.Parse<MetodoPago>(rp.GetString(2)),
                MontoCentimos = rp.GetInt64(3),
                CreadoEn = BaseDatos.LeerFechaHora(rp.GetString(4))
            });
        return orden;
    }

    private Orden ObtenerVigente(long id)
    {
        var o = Obtener(id) ?? throw new ReglaNegocioException("La orden no existe.");
        if (o.Eliminada) throw new ReglaNegocioException($"La orden N° {o.Numero} está eliminada. Restáurela primero.");
        return o;
    }

    /// <summary>Registra un abono o la cancelación total del saldo.</summary>
    public Pago RegistrarPago(long ordenId, Pago pago)
    {
        var o = ObtenerVigente(ordenId);
        if (o.Estado == EstadoOrden.Anulada) throw new ReglaNegocioException("No se pueden registrar pagos en una orden anulada.");
        if (pago.MontoCentimos <= 0) throw new ReglaNegocioException("El monto del pago debe ser mayor que cero.");
        if (o.SaldoCentimos <= 0) throw new ReglaNegocioException("Esta orden ya está pagada por completo.");
        if (pago.MontoCentimos > o.SaldoCentimos)
            throw new ReglaNegocioException($"El monto ({Dinero.Formatear(pago.MontoCentimos)}) es mayor que el saldo pendiente ({Dinero.Formatear(o.SaldoCentimos)}).");

        pago.OrdenId = ordenId;
        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        InsertarPago(cn, tx, pago);
        long saldo = o.SaldoCentimos - pago.MontoCentimos;
        LogRepositorio.Registrar(cn, tx, null, saldo == 0 ? "Cuenta cancelada" : "Abono registrado", Ref(o),
            $"{Dinero.Formatear(pago.MontoCentimos)} por {pago.MetodoTexto} ({pago.Fecha:dd/MM/yyyy}) · Saldo {Dinero.Formatear(saldo)}");
        tx.Commit();
        return pago;
    }

    /// <summary>Corrige un pago registrado por error. Requiere autorización.</summary>
    public void EliminarPago(long pagoId, string empleado)
    {
        if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización del empleado.");
        long ordenId;
        using (var cn0 = db.Abrir())
            ordenId = BaseDatos.Escalar<long>(cn0, $"SELECT orden_id FROM pagos WHERE id = {pagoId};");
        if (ordenId == 0) throw new ReglaNegocioException("El pago ya no existe.");

        var o = ObtenerVigente(ordenId);
        var p = o.Pagos.First(x => x.Id == pagoId);

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        BaseDatos.Ejecutar(cn, tx, $"DELETE FROM pagos WHERE id = {pagoId};");
        LogRepositorio.Registrar(cn, tx, empleado, "Pago eliminado", Ref(o),
            $"{Dinero.Formatear(p.MontoCentimos)} por {p.MetodoTexto} ({p.Fecha:dd/MM/yyyy}) · Nuevo saldo {Dinero.Formatear(o.SaldoCentimos + p.MontoCentimos)}");
        tx.Commit();
    }

    public void EditarNotas(long id, string? notas)
    {
        var o = ObtenerVigente(id);
        var cambios = new Cambios().Campo("Notas", o.Notas, notas);
        if (!cambios.HayCambios) return;

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE ordenes SET notas = $n WHERE id = $id;";
            cmd.Parameters.AddWithValue("$n", ClienteRepositorio.Nulo(notas));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        LogRepositorio.Registrar(cn, tx, null, "Notas editadas", Ref(o), cambios.ToString());
        tx.Commit();
    }

    public void MarcarDevuelta(long id, DateTime fechaDevolucion)
    {
        var o = ObtenerVigente(id);
        if (o.Estado != EstadoOrden.Activa)
            throw new ReglaNegocioException($"Solo se pueden marcar como devueltas las órdenes activas (esta está {o.Estado}).");
        if (fechaDevolucion.Date < o.FechaInicio.Date)
            throw new ReglaNegocioException("La fecha de devolución no puede ser anterior a la fecha de inicio.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE ordenes SET estado = 'Devuelta', fecha_fin = $f WHERE id = $id;";
            cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(fechaDevolucion));
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        LogRepositorio.Registrar(cn, tx, null, "Orden devuelta", Ref(o), $"Fecha de devolución {fechaDevolucion:dd/MM/yyyy}");
        tx.Commit();
    }

    /// <summary>
    /// Anular: la orden queda visible (tachada) con su motivo y deja de contar en el
    /// flujo de caja. Es distinto de eliminar, que la manda a la papelera.
    /// </summary>
    public void Anular(long id, string motivo, string? empleado = null)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new ReglaNegocioException("Indique el motivo de la anulación.");
        var o = ObtenerVigente(id);
        if (o.Estado == EstadoOrden.Anulada) throw new ReglaNegocioException("La orden ya está anulada.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE ordenes SET estado = 'Anulada', motivo_anulacion = $m WHERE id = $id;";
            cmd.Parameters.AddWithValue("$m", motivo.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        LogRepositorio.Registrar(cn, tx, empleado, "Orden anulada", Ref(o),
            $"Total {Dinero.Formatear(o.TotalCentimos)} · Pagado {Dinero.Formatear(o.PagadoCentimos)} · Motivo: {motivo.Trim()}");
        tx.Commit();
    }

    /// <summary>
    /// Elimina una orden (fantasma o de prueba): desaparece de Órdenes y del flujo de
    /// caja, pero queda en la papelera y se puede restaurar. Requiere autorización.
    /// </summary>
    public void Eliminar(long id, string empleado, string motivo)
    {
        if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización del empleado.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new ReglaNegocioException("Indique el motivo de la eliminación.");
        var o = Obtener(id) ?? throw new ReglaNegocioException("La orden no existe.");
        if (o.Eliminada) throw new ReglaNegocioException("La orden ya está eliminada.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE ordenes SET eliminada = 1, eliminada_en = $en, eliminada_por = $por, motivo_eliminacion = $m
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$en", BaseDatos.FechaHora(DateTime.Now));
            cmd.Parameters.AddWithValue("$por", empleado.Trim());
            cmd.Parameters.AddWithValue("$m", motivo.Trim());
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        LogRepositorio.Registrar(cn, tx, empleado, "Orden eliminada", Ref(o),
            $"Cliente {o.ClienteNombre} · Fecha {o.Fecha:dd/MM/yyyy} · Estado {o.Estado} · Total {Dinero.Formatear(o.TotalCentimos)} · " +
            $"Pagado {Dinero.Formatear(o.PagadoCentimos)} · Motivo: {motivo.Trim()}");
        tx.Commit();
    }

    public void Restaurar(long id, string empleado)
    {
        if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización del empleado.");
        var o = Obtener(id) ?? throw new ReglaNegocioException("La orden no existe.");
        if (!o.Eliminada) throw new ReglaNegocioException("La orden no está eliminada.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        BaseDatos.Ejecutar(cn, tx,
            $"UPDATE ordenes SET eliminada = 0, eliminada_en = NULL, eliminada_por = NULL, motivo_eliminacion = NULL WHERE id = {id};");
        LogRepositorio.Registrar(cn, tx, empleado, "Orden restaurada", Ref(o),
            $"Había sido eliminada el {o.EliminadaEn:dd/MM/yyyy HH:mm} por {o.EliminadaPor} (motivo: {o.MotivoEliminacion})");
        tx.Commit();
    }

    private static string Ref(Orden o) => $"Orden {o.Numero}";

    private static List<Orden> LeerOrdenes(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var lista = new List<Orden>();
        while (r.Read())
            lista.Add(new Orden
            {
                Id = r.GetInt64(0),
                Numero = r.IsDBNull(1) ? r.GetInt64(0).ToString() : r.GetString(1),
                Fecha = BaseDatos.LeerFecha(r.GetString(2)),
                ClienteId = r.GetInt64(3),
                ClienteNombre = r.GetString(4),
                FechaInicio = BaseDatos.LeerFecha(r.GetString(5)),
                FechaFin = r.IsDBNull(6) ? null : BaseDatos.LeerFecha(r.GetString(6)),
                Estado = Enum.Parse<EstadoOrden>(r.GetString(7)),
                Notas = r.IsDBNull(8) ? null : r.GetString(8),
                MotivoAnulacion = r.IsDBNull(9) ? null : r.GetString(9),
                Eliminada = r.GetInt64(10) == 1,
                EliminadaEn = r.IsDBNull(11) ? null : BaseDatos.LeerFechaHora(r.GetString(11)),
                EliminadaPor = r.IsDBNull(12) ? null : r.GetString(12),
                MotivoEliminacion = r.IsDBNull(13) ? null : r.GetString(13),
                TotalCentimos = r.GetInt64(14),
                BaseCentimos = r.GetInt64(15),
                IvaCentimos = r.GetInt64(16),
                DescuentoCentimos = r.GetInt64(17),
                PagadoCentimos = r.GetInt64(18)
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
