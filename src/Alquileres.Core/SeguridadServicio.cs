using System.Security.Cryptography;
using System.Text;

namespace Alquileres.Core;

/// <summary>
/// Códigos de autorización: 2 empleados (cada uno con su código personal) y un
/// código de confirmación común. Para eliminar órdenes o editar productos y
/// clientes se piden ambos: el de empleado identifica quién hizo el cambio
/// (queda en Logs) y el de confirmación es la segunda llave.
///
/// Los códigos no se guardan en texto: solo un hash PBKDF2 con sal. Aun así, el
/// archivo de datos no está cifrado; esto es control y trazabilidad, no una
/// protección contra alguien con acceso técnico al archivo.
/// </summary>
public class SeguridadServicio(BaseDatos db)
{
    public const int CantidadEmpleados = 2;
    public const int LargoMinimoCodigo = 4;

    private const string ClaveConfirmacion = "seg_confirmacion";
    private static string ClaveNombre(int n) => $"seg_empleado{n}_nombre";
    private static string ClaveCodigo(int n) => $"seg_empleado{n}_codigo";

    private const int Iteraciones = 100_000;

    private readonly ConfigRepositorio _config = new(db);

    public bool Configurado => _config.Obtener(ClaveConfirmacion) is not null;

    /// <summary>Nombres de los empleados configurados (vacío si no hay configuración).</summary>
    public List<string> NombresEmpleados() =>
        Enumerable.Range(1, CantidadEmpleados)
            .Select(n => _config.Obtener(ClaveNombre(n)))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

    /// <summary>
    /// Define (o redefine) los 3 códigos. Si ya había códigos, se exige el código
    /// de confirmación actual.
    /// </summary>
    public void Configurar(IReadOnlyList<(string Nombre, string Codigo)> empleados, string codigoConfirmacion,
        string? codigoConfirmacionActual = null)
    {
        if (Configurado && !Coincide(codigoConfirmacionActual, _config.Obtener(ClaveConfirmacion)))
            throw new ReglaNegocioException("El código de confirmación actual no es correcto.");

        if (empleados.Count != CantidadEmpleados)
            throw new ReglaNegocioException($"Debe definir {CantidadEmpleados} empleados.");

        var codigos = new List<string>();
        for (int i = 0; i < empleados.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(empleados[i].Nombre))
                throw new ReglaNegocioException($"Falta el nombre del empleado {i + 1}.");
            codigos.Add(ValidarCodigo(empleados[i].Codigo, $"El código del empleado {i + 1}"));
        }
        if (empleados[0].Nombre.Trim().Equals(empleados[1].Nombre.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ReglaNegocioException("Los dos empleados deben tener nombres diferentes (así se distinguen en los logs).");
        codigos.Add(ValidarCodigo(codigoConfirmacion, "El código de confirmación"));

        if (codigos.Distinct().Count() != codigos.Count)
            throw new ReglaNegocioException("Los 3 códigos deben ser diferentes entre sí.");

        for (int i = 0; i < empleados.Count; i++)
        {
            _config.Guardar(ClaveNombre(i + 1), empleados[i].Nombre.Trim());
            _config.Guardar(ClaveCodigo(i + 1), Hash(codigos[i]));
        }
        _config.Guardar(ClaveConfirmacion, Hash(codigos[^1]));

        new LogRepositorio(db).Registrar(null, "Códigos de autorización actualizados", "Seguridad",
            $"Empleados: {string.Join(", ", empleados.Select(e => e.Nombre.Trim()))}.");
    }

    /// <summary>
    /// Verifica los códigos y devuelve el nombre del empleado que autoriza.
    /// No indica cuál de los dos códigos falló.
    /// </summary>
    public string Validar(string? codigoEmpleado, string? codigoConfirmacion)
    {
        if (!Configurado)
            throw new ReglaNegocioException("Todavía no se han configurado los códigos de autorización.");

        bool confirmacionOk = Coincide(codigoConfirmacion, _config.Obtener(ClaveConfirmacion));
        string? empleado = null;
        for (int n = 1; n <= CantidadEmpleados; n++)
            if (Coincide(codigoEmpleado, _config.Obtener(ClaveCodigo(n))))
                empleado = _config.Obtener(ClaveNombre(n));

        if (!confirmacionOk || empleado is null)
            throw new ReglaNegocioException("El código de empleado o el de confirmación no es correcto.");
        return empleado;
    }

    /// <summary>Deja constancia en Logs de un intento fallido.</summary>
    public void RegistrarIntentoFallido(string accion) =>
        new LogRepositorio(db).Registrar(null, "Autorización rechazada", "Seguridad",
            $"Códigos incorrectos al intentar: {accion}.");

    private static string ValidarCodigo(string? codigo, string nombre)
    {
        string c = (codigo ?? "").Trim();
        if (c.Length < LargoMinimoCodigo)
            throw new ReglaNegocioException($"{nombre} debe tener al menos {LargoMinimoCodigo} caracteres.");
        return c;
    }

    private static string Hash(string codigo)
    {
        byte[] sal = RandomNumberGenerator.GetBytes(16);
        byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(codigo), sal, Iteraciones, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(sal)}:{Convert.ToBase64String(h)}";
    }

    private static bool Coincide(string? codigo, string? guardado)
    {
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrEmpty(guardado)) return false;
        var partes = guardado.Split(':');
        if (partes.Length != 2) return false;
        byte[] sal = Convert.FromBase64String(partes[0]);
        byte[] esperado = Convert.FromBase64String(partes[1]);
        byte[] h = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(codigo.Trim()), sal, Iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(h, esperado);
    }
}
