using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>Gastos de la empresa. Registrar no pide código; editar y eliminar sí (quedan en Logs).</summary>
public class GastoRepositorio(BaseDatos db)
{
    private const string SelectGastos = "SELECT id, fecha, categoria, descripcion, proveedor, metodo, monto, iva FROM gastos";

    public List<Gasto> Listar(DateTime desde, DateTime hasta, string? categoria = null, string? texto = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectGastos + """
             WHERE fecha BETWEEN $d AND $h
               AND ($c IS NULL OR categoria = $c)
               AND ($t IS NULL OR descripcion LIKE $t OR proveedor LIKE $t OR categoria LIKE $t)
            ORDER BY fecha DESC, id DESC;
            """;
        cmd.Parameters.AddWithValue("$d", BaseDatos.Fecha(desde));
        cmd.Parameters.AddWithValue("$h", BaseDatos.Fecha(hasta));
        cmd.Parameters.AddWithValue("$c", string.IsNullOrWhiteSpace(categoria) ? DBNull.Value : categoria);
        cmd.Parameters.AddWithValue("$t", string.IsNullOrWhiteSpace(texto) ? DBNull.Value : $"%{texto.Trim()}%");
        return Leer(cmd);
    }

    public Gasto? Obtener(long id)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectGastos + " WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return Leer(cmd).FirstOrDefault();
    }

    /// <summary>Categorías sugeridas más las que ya se han usado.</summary>
    public List<string> Categorias()
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT categoria FROM gastos ORDER BY categoria;";
        using var r = cmd.ExecuteReader();
        var usadas = new List<string>();
        while (r.Read()) usadas.Add(r.GetString(0));
        return Gasto.CategoriasSugeridas
            .Concat(usadas.Where(u => !Gasto.CategoriasSugeridas.Contains(u, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    private static List<Gasto> Leer(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var lista = new List<Gasto>();
        while (r.Read())
            lista.Add(new Gasto
            {
                Id = r.GetInt64(0),
                Fecha = BaseDatos.LeerFecha(r.GetString(1)),
                Categoria = r.GetString(2),
                Descripcion = r.GetString(3),
                Proveedor = r.IsDBNull(4) ? null : r.GetString(4),
                Metodo = Enum.Parse<MetodoPago>(r.GetString(5)),
                MontoCentimos = r.GetInt64(6),
                IvaCentimos = r.GetInt64(7)
            });
        return lista;
    }

    /// <summary>Registra un gasto nuevo o edita uno existente (editar exige empleado).</summary>
    public void Guardar(Gasto g, string? empleado = null)
    {
        if (string.IsNullOrWhiteSpace(g.Categoria)) throw new ReglaNegocioException("Indique la categoría del gasto.");
        if (string.IsNullOrWhiteSpace(g.Descripcion)) throw new ReglaNegocioException("Indique la descripción del gasto.");
        if (g.MontoCentimos <= 0) throw new ReglaNegocioException("El monto del gasto debe ser mayor que cero.");
        if (g.IvaCentimos < 0) throw new ReglaNegocioException("El IVA no puede ser negativo.");
        if (g.IvaCentimos > g.MontoCentimos) throw new ReglaNegocioException("El IVA no puede ser mayor que el monto.");

        Gasto? antes = null;
        if (g.Id != 0)
        {
            if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización del empleado.");
            antes = Obtener(g.Id) ?? throw new ReglaNegocioException("El gasto ya no existe.");
        }

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = g.Id == 0
                ? """
                  INSERT INTO gastos (fecha, categoria, descripcion, proveedor, metodo, monto, iva, creado_en)
                  VALUES ($f, $c, $d, $p, $m, $monto, $iva, $creado) RETURNING id;
                  """
                : """
                  UPDATE gastos SET fecha = $f, categoria = $c, descripcion = $d, proveedor = $p, metodo = $m,
                         monto = $monto, iva = $iva
                  WHERE id = $id RETURNING id;
                  """;
            cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(g.Fecha));
            cmd.Parameters.AddWithValue("$c", g.Categoria.Trim());
            cmd.Parameters.AddWithValue("$d", g.Descripcion.Trim());
            cmd.Parameters.AddWithValue("$p", ClienteRepositorio.Nulo(g.Proveedor));
            cmd.Parameters.AddWithValue("$m", g.Metodo.ToString());
            cmd.Parameters.AddWithValue("$monto", g.MontoCentimos);
            cmd.Parameters.AddWithValue("$iva", g.IvaCentimos);
            cmd.Parameters.AddWithValue("$creado", BaseDatos.FechaHora(DateTime.Now));
            cmd.Parameters.AddWithValue("$id", g.Id);
            g.Id = (long)cmd.ExecuteScalar()!;
        }

        if (antes is null)
        {
            LogRepositorio.Registrar(cn, tx, empleado, "Gasto registrado", Referencia(g), Describir(g));
        }
        else
        {
            var cambios = new Cambios()
                .Fecha("Fecha", antes.Fecha, g.Fecha)
                .Campo("Categoría", antes.Categoria, g.Categoria)
                .Campo("Descripción", antes.Descripcion, g.Descripcion)
                .Campo("Proveedor", antes.Proveedor, g.Proveedor)
                .Campo("Método", antes.Metodo, g.Metodo)
                .Monto("Monto", antes.MontoCentimos, g.MontoCentimos)
                .Monto("IVA", antes.IvaCentimos, g.IvaCentimos);
            if (cambios.HayCambios)
                LogRepositorio.Registrar(cn, tx, empleado, "Gasto editado", Referencia(g), cambios.ToString());
        }
        tx.Commit();
    }

    public void Eliminar(long id, string empleado)
    {
        if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización del empleado.");
        var g = Obtener(id) ?? throw new ReglaNegocioException("El gasto ya no existe.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        BaseDatos.Ejecutar(cn, tx, $"DELETE FROM gastos WHERE id = {id};");
        LogRepositorio.Registrar(cn, tx, empleado, "Gasto eliminado", Referencia(g), Describir(g));
        tx.Commit();
    }

    private static string Referencia(Gasto g) => $"Gasto {g.Fecha:dd/MM/yyyy} {g.Categoria.Trim()}";

    private static string Describir(Gasto g) =>
        string.Join(" · ", new[]
        {
            g.Descripcion.Trim(),
            string.IsNullOrWhiteSpace(g.Proveedor) ? null : $"Proveedor {g.Proveedor.Trim()}",
            $"{Dinero.Formatear(g.MontoCentimos)} por {g.MetodoTexto}",
            g.IvaCentimos > 0 ? $"IVA {Dinero.Formatear(g.IvaCentimos)}" : null
        }.Where(x => x is not null));
}
