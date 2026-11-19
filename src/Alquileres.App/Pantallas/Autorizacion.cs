using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>
/// Pide el código de confirmación antes de una acción protegida. Quien la hace es
/// el usuario de la sesión: su nombre es el que queda en Logs.
/// </summary>
internal static class Autorizacion
{
    public const string AccionesProtegidas =
        "eliminar y restaurar órdenes, eliminar pagos, crear o editar productos e inventario, editar clientes, " +
        "editar o eliminar gastos y administrar usuarios";

    /// <summary>Devuelve el usuario que autoriza (y el motivo, si se pidió) o null si se canceló.</summary>
    public static (string Empleado, string Motivo)? Pedir(IWin32Window owner, BaseDatos db, string accion,
        string? descripcion = null, bool pedirMotivo = false)
    {
        var seg = new SeguridadServicio(db);
        if (!seg.Configurado)
        {
            if (db.UsuarioActual?.EsAdministrador != true)
            {
                Ui.Info("Esta acción requiere el código de confirmación, pero todavía no se ha definido. Pídale a un administrador que lo defina en la pestaña Usuarios.");
                return null;
            }
            if (!Ui.Confirmar("Esta acción requiere el código de confirmación, pero todavía no se ha definido.\n\n¿Definirlo ahora?"))
                return null;
            using var cfg = new CodigoConfirmacionDialog(seg);
            if (cfg.ShowDialog(owner) != DialogResult.OK) return null;
        }

        using var dlg = new AutorizacionDialog(seg, db, accion, descripcion, pedirMotivo);
        return dlg.ShowDialog(owner) == DialogResult.OK ? (dlg.Empleado, dlg.Motivo) : null;
    }

    public static string? Empleado(IWin32Window owner, BaseDatos db, string accion, string? descripcion = null) =>
        Pedir(owner, db, accion, descripcion)?.Empleado;
}

internal sealed class AutorizacionDialog : DialogoBase
{
    private const int IntentosMaximos = 3;

    private readonly SeguridadServicio _seguridad;
    private readonly string _accion;
    private readonly TextBox _codigoConfirmacion = Ui.Texto(170);
    private readonly TextBox? _motivo;
    private int _intentosFallidos;

    public string Empleado { get; private set; } = "";
    public string Motivo => _motivo?.Text.Trim() ?? "";

    public AutorizacionDialog(SeguridadServicio seguridad, BaseDatos db, string accion, string? descripcion, bool pedirMotivo)
        : base("Confirmar cambio", "Confirmar")
    {
        _seguridad = seguridad;
        _accion = accion;

        var titulo = Ui.Etiqueta(accion, Ui.FuenteNegrita);
        titulo.MaximumSize = new Size(460, 0);
        Bloque(titulo);
        if (!string.IsNullOrWhiteSpace(descripcion)) Bloque(Explicacion(descripcion));
        Campo("Código de confirmación:", _codigoConfirmacion);
        CasillaMostrar("Mostrar código", _codigoConfirmacion);
        if (pedirMotivo)
        {
            _motivo = Ui.Texto(300, multilinea: true);
            Campo("Motivo *:", _motivo);
        }
        Bloque(Explicacion($"El cambio quedará registrado en Logs a nombre de {db.UsuarioActual?.Nombre ?? "—"}."));
    }

    protected override bool Validar()
    {
        if (_motivo is not null && Motivo.Length == 0)
        {
            Ui.Info("Indique el motivo.");
            _motivo.Focus();
            return false;
        }
        try
        {
            Empleado = _seguridad.Validar(_codigoConfirmacion.Text);
            return true;
        }
        catch (ReglaNegocioException ex)
        {
            _intentosFallidos++;
            Ui.Intentar(() => _seguridad.RegistrarIntentoFallido(_accion));
            if (_intentosFallidos >= IntentosMaximos)
            {
                Ui.Info("Demasiados intentos fallidos. La acción se canceló y quedó registrada en Logs.");
                DialogResult = DialogResult.Cancel;
                Close();
                return false;
            }
            Ui.MostrarError(ex);
            _codigoConfirmacion.Clear();
            _codigoConfirmacion.Focus();
            return false;
        }
    }
}
