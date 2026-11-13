using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Pide código de empleado + código de confirmación antes de una acción protegida.</summary>
internal static class Autorizacion
{
    public const string AccionesProtegidas =
        "eliminar y restaurar órdenes, eliminar pagos, crear o editar productos e inventario, editar clientes y editar o eliminar gastos";

    /// <summary>Devuelve el empleado (y el motivo, si se pidió) o null si se canceló.</summary>
    public static (string Empleado, string Motivo)? Pedir(IWin32Window owner, BaseDatos db, string accion,
        string? descripcion = null, bool pedirMotivo = false)
    {
        var seg = new SeguridadServicio(db);
        if (!seg.Configurado)
        {
            if (!Ui.Confirmar("Esta acción requiere los códigos de autorización, pero todavía no se han configurado.\n\n¿Configurarlos ahora?"))
                return null;
            using var cfg = new CodigosDialog(seg);
            if (cfg.ShowDialog(owner) != DialogResult.OK) return null;
        }

        using var dlg = new AutorizacionDialog(seg, accion, descripcion, pedirMotivo);
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
    private readonly TextBox _codigoEmpleado = Ui.Texto(170);
    private readonly TextBox _codigoConfirmacion = Ui.Texto(170);
    private readonly TextBox? _motivo;
    private int _intentosFallidos;

    public string Empleado { get; private set; } = "";
    public string Motivo => _motivo?.Text.Trim() ?? "";

    public AutorizacionDialog(SeguridadServicio seguridad, string accion, string? descripcion, bool pedirMotivo)
        : base("Autorización requerida", "Autorizar")
    {
        _seguridad = seguridad;
        _accion = accion;
        _codigoEmpleado.UseSystemPasswordChar = true;
        _codigoConfirmacion.UseSystemPasswordChar = true;

        var titulo = Ui.Etiqueta(accion, Ui.FuenteNegrita);
        titulo.MaximumSize = new Size(460, 0);
        Bloque(titulo);
        if (!string.IsNullOrWhiteSpace(descripcion)) Bloque(Explicacion(descripcion));
        Campo("Código de empleado:", _codigoEmpleado);
        Campo("Código de confirmación:", _codigoConfirmacion);
        if (pedirMotivo)
        {
            _motivo = Ui.Texto(300, multilinea: true);
            Campo("Motivo *:", _motivo);
        }
        Bloque(Explicacion("El nombre del empleado quedará registrado en Logs."));
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
            Empleado = _seguridad.Validar(_codigoEmpleado.Text, _codigoConfirmacion.Text);
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
            _codigoEmpleado.Clear();
            _codigoConfirmacion.Clear();
            _codigoEmpleado.Focus();
            return false;
        }
    }
}

/// <summary>Define o cambia los códigos de los 2 empleados y el código de confirmación.</summary>
internal sealed class CodigosDialog : DialogoBase
{
    private readonly SeguridadServicio _seguridad;
    private readonly TextBox? _confirmacionActual;
    private readonly TextBox[] _nombres = { Ui.Texto(220), Ui.Texto(220) };
    private readonly TextBox[] _codigos = { Ui.Texto(170), Ui.Texto(170) };
    private readonly TextBox _confirmacion = Ui.Texto(170);
    private readonly CheckBox _mostrar = new() { Text = "Mostrar códigos", AutoSize = true, Font = Ui.Fuente };

    public CodigosDialog(SeguridadServicio seguridad) : base("Códigos de autorización", "Guardar códigos")
    {
        _seguridad = seguridad;

        Bloque(Explicacion(
            $"Se piden para {Autorizacion.AccionesProtegidas}. Cada empleado tiene su propio código (así se sabe " +
            "quién hizo cada cambio) y además se necesita el código de confirmación. Los 3 códigos deben ser " +
            $"diferentes y tener al menos {SeguridadServicio.LargoMinimoCodigo} caracteres."));

        if (seguridad.Configurado)
        {
            _confirmacionActual = Ui.Texto(170);
            Campo("Código de confirmación ACTUAL *", _confirmacionActual);
        }

        var nombres = seguridad.NombresEmpleados();
        for (int i = 0; i < SeguridadServicio.CantidadEmpleados; i++)
        {
            if (i < nombres.Count) _nombres[i].Text = nombres[i];
            Campo($"Empleado {i + 1} — nombre *", _nombres[i]);
            Campo($"Empleado {i + 1} — código *", _codigos[i]);
        }
        Campo("Código de confirmación *", _confirmacion);
        Campo("", _mostrar);

        _mostrar.CheckedChanged += (_, _) => Enmascarar();
        Enmascarar();
    }

    private void Enmascarar()
    {
        bool ocultar = !_mostrar.Checked;
        foreach (var t in _codigos.Append(_confirmacion).Append(_confirmacionActual))
            if (t is not null) t.UseSystemPasswordChar = ocultar;
    }

    protected override bool Validar()
    {
        var empleados = Enumerable.Range(0, SeguridadServicio.CantidadEmpleados)
            .Select(i => (_nombres[i].Text, _codigos[i].Text))
            .ToList();
        bool ok = IntentarGuardar(() => _seguridad.Configurar(empleados, _confirmacion.Text, _confirmacionActual?.Text));
        if (ok) Ui.Info("Códigos guardados. Anótelos en un lugar seguro: el programa no puede mostrarlos después. " +
                        "Si se olvida el código de confirmación, solo el técnico puede restablecerlos.");
        return ok;
    }
}
