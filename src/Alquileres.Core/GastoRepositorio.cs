using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>Gastos de la empresa. Registrar no pide código; editar y eliminar sí (quedan en Logs).</summary>
public class GastoRepositorio(BaseDatos db)
{
    private const string SelectGastos = "SELECT id, fecha, categoria, detalle, metodo, monto FROM gastos";

    public List<Gasto> Listar(DateTime desde, DateTime hasta, string? categoria = null, string? texto = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = SelectGastos + """
             WHERE fecha BETWEEN $d AND $h
               AND ($c IS NULL OR categoria = $c)
               AND ($t IS NULL OR detalle LIKE $t OR categoria LIKE $t)
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

    /// <summary>Categorías que ya se han escrito (para sugerirlas y filtrar). No hay una lista fija.</summary>
    public List<string> Categorias()
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT categoria FROM gastos ORDER BY categoria COLLATE NOCASE;";
        using var r = cmd.ExecuteReader();
        var usadas = new List<string>();
        while (r.Read()) usadas.Add(r.GetString(0));
        return usadas;
    }

    /// <summary>
    /// Si ya existe la misma categoría escrita con otras mayúsculas o espacios, usa la existente
    /// ("transporte " → "Transporte") para que el filtro y los totales no se partan.
    /// </summary>
    private string CategoriaExistente(string categoria)
    {
        string c = string.Join(' ', categoria.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Categorias().FirstOrDefault(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)) ?? c;
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
                Detalle = r.GetString(3),
                Metodo = Enum.Parse<MetodoPago>(r.GetString(4)),
                MontoCentimos = r.GetInt64(5)
            });
        return lista;
    }

    /// <summary>Registra un gasto nuevo o edita uno existente (editar exige empleado).</summary>
    public void Guardar(Gasto g, string? empleado = null)
    {
        if (string.IsNullOrWhiteSpace(g.Categoria)) throw new ReglaNegocioException("Indique la categoría del gasto.");
        if (string.IsNullOrWhiteSpace(g.Detalle)) throw new ReglaNegocioException("Indique el detalle del gasto.");
        if (g.MontoCentimos <= 0) throw new ReglaNegocioException("El monto del gasto debe ser mayor que cero.");
        g.Categoria = CategoriaExistente(g.Categoria);

        Gasto? antes = null;
        if (g.Id != 0)
        {
            if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización con el código de confirmación.");
            antes = Obtener(g.Id) ?? throw new ReglaNegocioException("El gasto ya no existe.");
        }

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = g.Id == 0
                ? """
                  INSERT INTO gastos (fecha, categoria, detalle, metodo, monto, creado_en)
                  VALUES ($f, $c, $d, $m, $monto, $creado) RETURNING id;
                  """
                : """
                  UPDATE gastos SET fecha = $f, categoria = $c, detalle = $d, metodo = $m, monto = $monto
                  WHERE id = $id RETURNING id;
                  """;
            cmd.Parameters.AddWithValue("$f", BaseDatos.Fecha(g.Fecha));
            cmd.Parameters.AddWithValue("$c", g.Categoria.Trim());
            cmd.Parameters.AddWithValue("$d", g.Detalle.Trim());
            cmd.Parameters.AddWithValue("$m", g.Metodo.ToString());
            cmd.Parameters.AddWithValue("$monto", g.MontoCentimos);
            cmd.Parameters.AddWithValue("$creado", BaseDatos.FechaHora(DateTime.Now));
            cmd.Parameters.AddWithValue("$id", g.Id);
            g.Id = (long)cmd.ExecuteScalar()!;
        }

        if (antes is null)
        {
            LogRepositorio.Registrar(db, cn, tx, empleado, "Gasto registrado", Referencia(g), Describir(g));
        }
        else
        {
            var cambios = new Cambios()
                .Fecha("Fecha", antes.Fecha, g.Fecha)
                .Campo("Categoría", antes.Categoria, g.Categoria)
                .Campo("Detalle", antes.Detalle, g.Detalle)
                .Campo("Método", antes.Metodo, g.Metodo)
                .Monto("Monto", antes.MontoCentimos, g.MontoCentimos);
            if (cambios.HayCambios)
                LogRepositorio.Registrar(db, cn, tx, empleado, "Gasto editado", Referencia(g), cambios.ToString());
        }
        tx.Commit();
    }

    public void Eliminar(long id, string empleado)
    {
        if (string.IsNullOrWhiteSpace(empleado)) throw new ReglaNegocioException("Falta la autorización con el código de confirmación.");
        var g = Obtener(id) ?? throw new ReglaNegocioException("El gasto ya no existe.");

        using var cn = db.Abrir();
        using var tx = cn.BeginTransaction();
        BaseDatos.Ejecutar(cn, tx, $"DELETE FROM gastos WHERE id = {id};");
        LogRepositorio.Registrar(db, cn, tx, empleado, "Gasto eliminado", Referencia(g), Describir(g));
        tx.Commit();
    }

    private static string Referencia(Gasto g) => $"Gasto {g.Fecha:dd/MM/yyyy} {g.Categoria.Trim()}";

    private static string Describir(Gasto g) =>
        string.Join(" · ", new[]
        {
            g.Detalle.Trim(),
            $"{Dinero.Formatear(g.MontoCentimos)} por {g.MetodoTexto}"
        });
}
