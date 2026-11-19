namespace Alquileres.Core;

/// <summary>
/// Código de confirmación común. Quien hace el cambio ya está identificado por la
/// sesión (ver <see cref="UsuarioServicio"/>); el código es la aprobación y el
/// "¿está seguro?" antes de eliminar órdenes o editar productos, clientes y gastos.
///
/// Se guarda solo como hash PBKDF2 con sal. El archivo de datos no está cifrado:
/// esto es control y trazabilidad, no protección contra alguien con acceso técnico.
/// </summary>
public class SeguridadServicio(BaseDatos db)
{
    public const int LargoMinimoCodigo = 4;

    internal const string ClaveConfirmacion = "seg_confirmacion";

    private readonly ConfigRepositorio _config = new(db);

    public bool Configurado => _config.Obtener(ClaveConfirmacion) is not null;

    /// <summary>
    /// Define o cambia el código de confirmación. Solo un administrador; si ya
    /// existía uno, se exige el actual.
    /// </summary>
    public void DefinirConfirmacion(string nuevo, string? actual = null)
    {
        UsuarioServicio.ExigirAdministrador(db);
        if (Configurado && !Claves.Coincide(actual, _config.Obtener(ClaveConfirmacion)))
            throw new ReglaNegocioException("El código de confirmación actual no es correcto.");
        GuardarConfirmacion(nuevo);
        new LogRepositorio(db).Registrar(null, "Código de confirmación actualizado", "Seguridad", "Se definió un nuevo código de confirmación.");
    }

    /// <summary>Usado solo en la configuración inicial (antes de que exista una sesión).</summary>
    internal void GuardarConfirmacion(string codigo)
    {
        string c = (codigo ?? "").Trim();
        if (c.Length < LargoMinimoCodigo)
            throw new ReglaNegocioException($"El código de confirmación debe tener al menos {LargoMinimoCodigo} caracteres.");
        _config.Guardar(ClaveConfirmacion, Claves.Hash(c));
    }

    /// <summary>
    /// Verifica el código de confirmación y devuelve el nombre del usuario de la
    /// sesión, que es quien queda en Logs.
    /// </summary>
    public string Validar(string? codigoConfirmacion)
    {
        var usuario = db.UsuarioActual ?? throw new ReglaNegocioException("No hay una sesión iniciada.");
        if (!Configurado)
            throw new ReglaNegocioException("Todavía no se ha configurado el código de confirmación.");
        if (!Claves.Coincide(codigoConfirmacion, _config.Obtener(ClaveConfirmacion)))
            throw new ReglaNegocioException("El código de confirmación no es correcto.");
        return usuario.Nombre;
    }

    /// <summary>Deja constancia en Logs de un intento fallido.</summary>
    public void RegistrarIntentoFallido(string accion) =>
        new LogRepositorio(db).Registrar(null, "Autorización rechazada", "Seguridad",
            $"Código de confirmación incorrecto al intentar: {accion}.");
}
