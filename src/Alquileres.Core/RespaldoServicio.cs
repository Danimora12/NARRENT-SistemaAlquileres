using Microsoft.Data.Sqlite;

namespace Alquileres.Core;

/// <summary>
/// Respaldos de la base de datos. Usa "VACUUM INTO", que genera una copia
/// consistente aunque el programa esté abierto (copiar el .db a mano puede corromperlo).
/// </summary>
public class RespaldoServicio(BaseDatos db)
{
    public const int RespaldosAConservar = 30;
    private const string Prefijo = "respaldo_";

    private readonly ConfigRepositorio _config = new(db);

    public string CarpetaRespaldosLocal => Path.Combine(db.CarpetaDatos, "Respaldos");

    /// <summary>Carpeta adicional opcional (por ejemplo, una carpeta de OneDrive o Google Drive).</summary>
    public string? CarpetaExtra
    {
        get => _config.Obtener(ConfigRepositorio.CarpetaRespaldoExtra);
        set => _config.Guardar(ConfigRepositorio.CarpetaRespaldoExtra, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    /// <summary>Crea un respaldo en la carpeta indicada y devuelve la ruta del archivo.</summary>
    public string CrearRespaldo(string carpetaDestino, string sufijo = "")
    {
        Directory.CreateDirectory(carpetaDestino);
        string nombre = $"{Prefijo}{DateTime.Now:yyyy-MM-dd_HHmmss}{sufijo}.db";
        string destino = Path.Combine(carpetaDestino, nombre);
        int n = 1;
        while (File.Exists(destino))
            destino = Path.Combine(carpetaDestino, $"{Path.GetFileNameWithoutExtension(nombre)}_{n++}.db");

        using var cn = db.Abrir();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "VACUUM INTO $destino;";
        cmd.Parameters.AddWithValue("$destino", destino);
        cmd.ExecuteNonQuery();
        return destino;
    }

    /// <summary>
    /// Respaldo automático (se llama al cerrar el programa): carpeta local + carpeta extra
    /// si está configurada. Conserva los últimos 30 de cada carpeta.
    /// Devuelve las rutas creadas y los errores (no lanza excepciones).
    /// </summary>
    public (List<string> Creados, List<string> Errores) RespaldoAutomatico()
    {
        var creados = new List<string>();
        var errores = new List<string>();

        var carpetas = new List<string> { CarpetaRespaldosLocal };
        string? extra = null;
        try { extra = CarpetaExtra; } catch (Exception ex) { errores.Add(ex.Message); }
        if (!string.IsNullOrWhiteSpace(extra)) carpetas.Add(extra);

        foreach (var carpeta in carpetas)
        {
            try
            {
                creados.Add(CrearRespaldo(carpeta));
                LimpiarAntiguos(carpeta);
            }
            catch (Exception ex)
            {
                errores.Add($"{carpeta}: {ex.Message}");
            }
        }
        return (creados, errores);
    }

    public void LimpiarAntiguos(string carpeta, int conservar = RespaldosAConservar)
    {
        foreach (var f in ListarRespaldos(carpeta).Skip(conservar))
            File.Delete(f.FullName);
    }

    /// <summary>Respaldos de la carpeta, del más reciente al más antiguo.</summary>
    public static List<FileInfo> ListarRespaldos(string carpeta)
    {
        if (!Directory.Exists(carpeta)) return new();
        return new DirectoryInfo(carpeta)
            .GetFiles($"{Prefijo}*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    /// <summary>
    /// Reemplaza la base de datos actual por un respaldo. Antes guarda una copia de
    /// seguridad de los datos actuales (sufijo "_antes_de_restaurar").
    /// </summary>
    public string Restaurar(string archivoRespaldo)
    {
        if (!File.Exists(archivoRespaldo))
            throw new ReglaNegocioException("El archivo de respaldo no existe.");
        if (Path.GetFullPath(archivoRespaldo) == db.RutaArchivo)
            throw new ReglaNegocioException("Ese archivo es la base de datos actual.");
        ValidarArchivo(archivoRespaldo);

        string copiaSeguridad = CrearRespaldo(CarpetaRespaldosLocal, "_antes_de_restaurar");

        SqliteConnection.ClearAllPools();
        File.Copy(archivoRespaldo, db.RutaArchivo, overwrite: true);
        db.Inicializar(); // por si el respaldo es de una versión anterior del esquema
        return copiaSeguridad;
    }

    /// <summary>Verifica que el archivo sea una base SQLite íntegra de este sistema.</summary>
    public static void ValidarArchivo(string archivo)
    {
        try
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = archivo,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            using var cn = new SqliteConnection(cs);
            cn.Open();

            string integridad = BaseDatos.Escalar<string>(cn, "PRAGMA integrity_check;");
            if (integridad != "ok")
                throw new ReglaNegocioException("El respaldo está dañado (falló la verificación de integridad).");

            long tablas = BaseDatos.Escalar<long>(cn,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('productos','clientes','ordenes','orden_lineas');");
            if (tablas != 4)
                throw new ReglaNegocioException("El archivo no es un respaldo de este sistema.");
        }
        catch (SqliteException)
        {
            throw new ReglaNegocioException("El archivo no es una base de datos válida.");
        }
    }
}
