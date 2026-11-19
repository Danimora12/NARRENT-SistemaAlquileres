using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Flujo de entrada: primer uso, login y cambio obligatorio de contraseña temporal.</summary>
internal static class Sesion
{
    /// <summary>Devuelve true si quedó una sesión iniciada; false si el usuario salió.</summary>
    public static bool Iniciar(BaseDatos db, string? bloqueadaPor = null)
    {
        var usuarios = new UsuarioServicio(db);

        if (!usuarios.HayUsuarios)
        {
            using var primerUso = new PrimerUsoDialog(usuarios, new SeguridadServicio(db).Configurado);
            return primerUso.ShowDialog() == DialogResult.OK;
        }

        while (true)
        {
            using var login = new LoginDialog(usuarios, bloqueadaPor);
            if (login.ShowDialog() != DialogResult.OK) return false;
            if (db.UsuarioActual is not { DebeCambiarClave: true }) return true;

            using var cambio = new CambiarClaveDialog(usuarios, claveTemporal: login.ClaveIngresada);
            if (cambio.ShowDialog() == DialogResult.OK) return true;
            usuarios.CerrarSesion(); // no cambió la temporal: vuelve al login
        }
    }
}

internal sealed class LoginDialog : DialogoBase
{
    private readonly UsuarioServicio _usuarios;
    private readonly TextBox _usuario = Ui.Texto(240);
    private readonly TextBox _clave = Ui.Texto(240);

    /// <summary>Se usa solo para no pedir de nuevo la contraseña temporal al cambiarla.</summary>
    public string ClaveIngresada { get; private set; } = "";

    public LoginDialog(UsuarioServicio usuarios, string? bloqueadaPor)
        : base($"{Ui.Titulo} — Iniciar sesión", "Ingresar", "Salir")
    {
        _usuarios = usuarios;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        using (var icono = typeof(LoginDialog).Assembly.GetManifestResourceStream("app.ico"))
            if (icono is not null) Icon = new Icon(icono);

        var titulo = Ui.Etiqueta(Ui.Titulo, Ui.FuenteTotal);
        titulo.ForeColor = Ui.Primario;
        Bloque(titulo);
        if (bloqueadaPor is not null)
            Bloque(Explicacion($"Sesión bloqueada (estaba abierta por {bloqueadaPor}). Ingrese para continuar."));

        Campo("Usuario:", _usuario);
        Campo("Contraseña:", _clave);
        CasillaMostrar("Mostrar contraseña", _clave);
        Bloque(Explicacion("¿Olvidó su contraseña? Pida a un administrador que se la restablezca en la pestaña Usuarios.", 380));

        Shown += (_, _) => _usuario.Focus();
    }

    protected override bool Validar()
    {
        if (_usuario.Text.Trim().Length == 0) { _usuario.Focus(); return false; }
        if (_clave.Text.Length == 0) { _clave.Focus(); return false; }
        try
        {
            _usuarios.IniciarSesion(_usuario.Text, _clave.Text);
            ClaveIngresada = _clave.Text;
            return true;
        }
        catch (Exception ex)
        {
            Ui.MostrarError(ex);
            _clave.Clear();
            _clave.Focus();
            return false;
        }
    }
}

/// <summary>
/// Cambio de contraseña del usuario de la sesión. Con <c>claveTemporal</c> es el
/// cambio obligatorio después de un restablecimiento (no pide la actual otra vez).
/// </summary>
internal sealed class CambiarClaveDialog : DialogoBase
{
    private readonly UsuarioServicio _usuarios;
    private readonly string? _claveTemporal;
    private readonly TextBox? _actual;
    private readonly TextBox _nueva = Ui.Texto(220);
    private readonly TextBox _repetir = Ui.Texto(220);

    public CambiarClaveDialog(UsuarioServicio usuarios, string? claveTemporal = null)
        : base(claveTemporal is null ? "Cambiar mi contraseña" : "Cambio de contraseña obligatorio", "Guardar contraseña")
    {
        _usuarios = usuarios;
        _claveTemporal = claveTemporal;
        if (claveTemporal is not null)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            Bloque(Explicacion("Está usando una contraseña temporal. Defina una contraseña personal para continuar; " +
                               "solo usted debe conocerla."));
        }
        else
        {
            _actual = Ui.Texto(220);
            Campo("Contraseña actual:", _actual);
        }
        Campo("Contraseña nueva:", _nueva);
        Campo("Repetir contraseña nueva:", _repetir);
        CasillaMostrar("Mostrar contraseñas", _actual, _nueva, _repetir);
        Bloque(Explicacion($"Mínimo {UsuarioServicio.LargoMinimoClave} caracteres."));
    }

    protected override bool Validar()
    {
        bool ok = IntentarGuardar(() => _usuarios.CambiarMiClave(_claveTemporal ?? _actual!.Text, _nueva.Text, _repetir.Text));
        if (ok) Ui.Info("Contraseña cambiada.");
        return ok;
    }
}

/// <summary>Primera vez que se abre el programa: crea el primer administrador.</summary>
internal sealed class PrimerUsoDialog : DialogoBase
{
    private readonly UsuarioServicio _usuarios;
    private readonly TextBox _nombre = Ui.Texto(260);
    private readonly TextBox _usuario = Ui.Texto(200);
    private readonly TextBox _clave = Ui.Texto(200);
    private readonly TextBox _repetir = Ui.Texto(200);
    private readonly TextBox? _codigo;
    private readonly TextBox? _codigoRepetir;
    private bool _usuarioEditado;

    public PrimerUsoDialog(UsuarioServicio usuarios, bool codigoYaConfigurado)
        : base($"{Ui.Titulo} — Configuración inicial", "Crear y entrar", "Salir")
    {
        _usuarios = usuarios;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        Bloque(Explicacion("Es la primera vez que se usa el programa. Cree el usuario administrador: podrá manejar todo " +
                           "el sistema y crear los usuarios de las demás personas."));
        Campo("Nombre completo *", _nombre);
        Campo("Usuario *", _usuario);
        Campo("Contraseña *", _clave);
        Campo("Repetir contraseña *", _repetir);

        if (!codigoYaConfigurado)
        {
            _codigo = Ui.Texto(200);
            _codigoRepetir = Ui.Texto(200);
            Bloque(Explicacion($"Código de confirmación: se pide antes de eliminar o editar datos importantes. " +
                               $"Lo conocen los administradores. Mínimo {SeguridadServicio.LargoMinimoCodigo} caracteres."));
            Campo("Código de confirmación *", _codigo);
            Campo("Repetir código *", _codigoRepetir);
        }
        CasillaMostrar(_codigo is null ? "Mostrar contraseña" : "Mostrar contraseña y código", _clave, _repetir, _codigo, _codigoRepetir);

        _nombre.TextChanged += (_, _) => { if (!_usuarioEditado) _usuario.Text = UsuarioServicio.SugerirUsuario(_nombre.Text); };
        _usuario.KeyPress += (_, _) => _usuarioEditado = true;
    }

    protected override bool Validar()
    {
        if (_codigo is not null && _codigo.Text.Trim() != _codigoRepetir!.Text.Trim())
        {
            Ui.Info("Los códigos de confirmación no coinciden.");
            return false;
        }
        bool ok = IntentarGuardar(() =>
            _usuarios.ConfigurarPrimerUso(_nombre.Text, _usuario.Text, _clave.Text, _repetir.Text, _codigo?.Text));
        if (ok) Ui.Info("Listo. Anote su usuario y contraseña" + (_codigo is null ? "" : " y el código de confirmación") +
                        " en un lugar seguro: el programa no puede mostrarlos después.");
        return ok;
    }
}
