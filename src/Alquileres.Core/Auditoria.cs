using System.Text;
using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>
/// Registro de auditoría (pestaña Logs). Solo se agregan entradas: el programa
/// no tiene ninguna función para editarlas o borrarlas.
/// </summary>
public class LogRepositorio(BaseDatos db)
{
    /// <summary>Registra dentro de una transacción existente (así el log y el cambio se guardan juntos).</summary>
    internal static void Registrar(SqliteConnection cn, SqliteTransaction? tx, string? empleado,
        string accion, string referencia, string detalle)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO logs (fecha_hora, empleado, accion, referencia, detalle)
            VALUES ($f, $e, $a, $r, $d);
            """;
        cmd.Parameters.AddWithValue("$f", BaseDatos.FechaHora(DateTime.Now));
        cmd.Parameters.AddWithValue("$e", string.IsNullOrWhiteSpace(empleado) ? DBNull.Value : empleado.Trim());
        cmd.Parameters.AddWithValue("$a", accion);
        cmd.Parameters.AddWithValue("$r", referencia);
        cmd.Parameters.AddWithValue("$d", detalle);
        cmd.ExecuteNonQuery();
    }

    public void Registrar(string? empleado, string accion, string referencia, string detalle)
    {
        using var cn = db.Abrir();
        Registrar(cn, null, empleado, accion, referencia, detalle);
    }

    public List<EntradaLog> Listar(DateTime desde, DateTime hasta, string? texto = null)
    {
        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT id, fecha_hora, empleado, accion, referencia, detalle FROM logs
            WHERE substr(fecha_hora, 1, 10) BETWEEN $d AND $h
              AND ($t IS NULL OR accion LIKE $t OR referencia LIKE $t OR detalle LIKE $t OR empleado LIKE $t)
            ORDER BY id DESC;
            """;
        cmd.Parameters.AddWithValue("$d", BaseDatos.Fecha(desde));
        cmd.Parameters.AddWithValue("$h", BaseDatos.Fecha(hasta));
        cmd.Parameters.AddWithValue("$t", string.IsNullOrWhiteSpace(texto) ? DBNull.Value : $"%{texto.Trim()}%");
        using var r = cmd.ExecuteReader();
        var lista = new List<EntradaLog>();
        while (r.Read())
            lista.Add(new EntradaLog
            {
                Id = r.GetInt64(0),
                FechaHora = BaseDatos.LeerFechaHora(r.GetString(1)),
                Empleado = r.IsDBNull(2) ? null : r.GetString(2),
                Accion = r.GetString(3),
                Referencia = r.GetString(4),
                Detalle = r.GetString(5)
            });
        return lista;
    }
}

/// <summary>Arma el texto "Campo: antes → después" para los logs de edición.</summary>
public sealed class Cambios
{
    private readonly StringBuilder _sb = new();

    public Cambios Campo(string nombre, object? antes, object? despues) => Agregar(nombre, Texto(antes), Texto(despues));

    /// <summary>Montos en céntimos, sin comillas: "Precio: ₡10.000,00 → ₡12.000,00".</summary>
    public Cambios Monto(string nombre, long antes, long despues) =>
        Agregar(nombre, Dinero.Formatear(antes), Dinero.Formatear(despues));

    /// <summary>Fechas sin comillas.</summary>
    public Cambios Fecha(string nombre, DateTime antes, DateTime despues) =>
        Agregar(nombre, antes.ToString("dd/MM/yyyy"), despues.ToString("dd/MM/yyyy"));

    private Cambios Agregar(string nombre, string a, string d)
    {
        if (a == d) return this;
        if (_sb.Length > 0) _sb.Append(" · ");
        _sb.Append($"{nombre}: {a} → {d}");
        return this;
    }

    public bool HayCambios => _sb.Length > 0;

    public override string ToString() => _sb.ToString();

    private static string Texto(object? v) => v switch
    {
        null => "(vacío)",
        string s when string.IsNullOrWhiteSpace(s) => "(vacío)",
        string s => $"\"{s.Trim()}\"",
        bool b => b ? "Sí" : "No",
        _ => v.ToString() ?? ""
    };
}

/// <summary>Número de orden: año (4) + mes (2) + consecutivo del mes (3). Ej.: 202612001.</summary>
public static class Numeracion
{
    public static string Prefijo(DateTime fecha) => fecha.ToString("yyyyMM");

    /// <summary>Si un mes pasara de 999 órdenes, el número crece a 4 dígitos (no se repite).</summary>
    public static string Formatear(string prefijo, int consecutivo) => prefijo + consecutivo.ToString("D3");

    internal static string Siguiente(SqliteConnection cn, SqliteTransaction tx, DateTime fecha)
    {
        string prefijo = Prefijo(fecha);
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        // Incluye las órdenes eliminadas: si se restauran, su número no choca con otro.
        cmd.CommandText = "SELECT COALESCE(MAX(CAST(substr(numero, 7) AS INTEGER)), 0) FROM ordenes WHERE substr(numero, 1, 6) = $p;";
        cmd.Parameters.AddWithValue("$p", prefijo);
        long ultimo = (long)cmd.ExecuteScalar()!;
        return Formatear(prefijo, (int)ultimo + 1);
    }
}
