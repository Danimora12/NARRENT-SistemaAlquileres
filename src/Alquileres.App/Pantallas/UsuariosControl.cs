using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Usuarios del sistema y código de confirmación. Solo administradores pueden hacer cambios.</summary>
internal sealed class UsuariosControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly UsuarioServicio _usuarios;
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _aviso = Ui.Etiqueta("");
    private readonly Button[] _botonesAdmin;

    public UsuariosControl(BaseDatos db)
    {
        _db = db;
        _usuarios = new UsuarioServicio(db);

        Ui.Columna(_grid, "Usuario", 90);
        Ui.Columna(_grid, "Nombre", 140);
        Ui.Columna(_grid, "Correo", 140);
        Ui.Columna(_grid, "Rol", 80);
        Ui.Columna(_grid, "Estado", 60);
        Ui.Columna(_grid, "Último ingreso", 90);
        Ui.Columna(_grid, "Contraseña", 90);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && EsAdmin) Editar(); };

        var nota = Ui.Etiqueta(
            "Nadie puede registrarse solo: los usuarios los crea un administrador con una contraseña temporal, " +
            "que la persona cambia al entrar por primera vez. Si alguien olvida su contraseña, use \"Restablecer contraseña\". " +
            "Para dejar de dar acceso a alguien, desactívelo (no se borra, para conservar el historial de Logs).");
        nota.ForeColor = Color.DimGray;
        nota.MaximumSize = new Size(1100, 0);
        _aviso.ForeColor = Color.Firebrick;

        _botonesAdmin = new[]
        {
            Ui.Boton("Nuevo usuario…", (_, _) => Nuevo(), principal: true),
            Ui.Boton("Editar…", (_, _) => Editar()),
            Ui.Boton("Restablecer contraseña…", (_, _) => Restablecer()),
            Ui.Boton("Código de confirmación…", (_, _) => CodigoConfirmacion())
        };

        Controls.Add(Ui.Pila(nota, _aviso, _grid, Ui.Fila(_botonesAdmin)));
    }

    private bool EsAdmin => _db.UsuarioActual?.EsAdministrador == true;

    public void Refrescar()
    {
        foreach (var b in _botonesAdmin) b.Enabled = EsAdmin;
        _aviso.Text = EsAdmin ? "" : "Solo un administrador puede crear o modificar usuarios.";
        _aviso.Visible = !EsAdmin;

        _grid.Rows.Clear();
        foreach (var u in _usuarios.Listar())
        {
            int i = _grid.Rows.Add(u.NombreUsuario, u.Nombre, u.Correo ?? "", u.Rol, u.Activo ? "Activo" : "Inactivo",
                u.UltimoAcceso?.ToString("dd/MM/yyyy HH:mm") ?? "Nunca", u.DebeCambiarClave ? "Temporal" : "Personal");
            _grid.Rows[i].Tag = u;
            if (!u.Activo) _grid.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
        }
    }

    private void Nuevo()
    {
        if (Autorizacion.Empleado(this, _db, "Crear un usuario nuevo") is null) return;
        using var dlg = new UsuarioDialog(_usuarios, null);
        if (dlg.ShowDialog(this) == DialogResult.OK) Ui.Intentar(Refrescar);
    }

    private void Editar()
    {
        if (Ui.Seleccionado<Usuario>(_grid) is not { } u) { Ui.Info("Seleccione un usuario."); return; }
        if (Autorizacion.Empleado(this, _db, $"Editar el usuario \"{u.NombreUsuario}\" ({u.Nombre})") is null) return;
        using var dlg = new UsuarioDialog(_usuarios, u);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        (FindForm() as FormPrincipal)?.MostrarSesion(); // por si se editó el propio nombre
        Ui.Intentar(Refrescar);
    }

    private void Restablecer()
    {
        if (Ui.Seleccionado<Usuario>(_grid) is not { } u) { Ui.Info("Seleccione un usuario."); return; }
        if (u.Id == _db.UsuarioActual?.Id) { Ui.Info("Para su propio usuario use \"Cambiar mi contraseña\" (arriba a la derecha)."); return; }
        if (Autorizacion.Empleado(this, _db, $"Restablecer la contraseña de \"{u.NombreUsuario}\" ({u.Nombre})") is null) return;
        using var dlg = new RestablecerClaveDialog(_usuarios, u);
        if (dlg.ShowDialog(this) == DialogResult.OK) Ui.Intentar(Refrescar);
    }

    private void CodigoConfirmacion()
    {
        using var dlg = new CodigoConfirmacionDialog(new SeguridadServicio(_db));
        dlg.ShowDialog(this);
    }
}

internal sealed class UsuarioDialog : DialogoBase
{
    private readonly UsuarioServicio _usuarios;
    private readonly Usuario? _existente;
    private readonly TextBox _nombre = Ui.Texto(280);
    private readonly TextBox _usuario = Ui.Texto(200);
    private readonly TextBox _correo = Ui.Texto(280);
    private readonly ComboBox _rol = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Font = Ui.Fuente };
    private readonly CheckBox _activo = new() { Text = "Activo (puede ingresar al sistema)", AutoSize = true, Font = Ui.Fuente };
    private readonly TextBox? _clave;
    private readonly TextBox? _repetir;
    private bool _usuarioEditado;

    public UsuarioDialog(UsuarioServicio usuarios, Usuario? existente)
        : base(existente is null ? "Nuevo usuario" : $"Editar usuario — {existente.NombreUsuario}", "Guardar")
    {
        _usuarios = usuarios;
        _existente = existente;
        _rol.Items.AddRange(Roles.Todos);
        _rol.SelectedItem = existente?.Rol ?? Roles.Empleado;

        Campo("Nombre completo *", _nombre);
        Campo("Usuario *", _usuario);
        Campo("Correo", _correo);
        Campo("Rol *", _rol);
        Bloque(Explicacion("Administrador: maneja todo el sistema, incluidos los usuarios. " +
                           "Empleado: trabaja con el sistema pero no administra usuarios ni el código de confirmación."));

        if (existente is null)
        {
            _clave = Ui.Texto(200);
            _repetir = Ui.Texto(200);
            Campo("Contraseña temporal *", _clave);
            Campo("Repetir contraseña *", _repetir);
            CasillaMostrar("Mostrar contraseña", _clave, _repetir);
            Bloque(Explicacion($"Mínimo {UsuarioServicio.LargoMinimoClave} caracteres. Entréguesela a la persona: " +
                               "el sistema le pedirá cambiarla la primera vez que ingrese."));
            _nombre.TextChanged += (_, _) => { if (!_usuarioEditado) _usuario.Text = UsuarioServicio.SugerirUsuario(_nombre.Text); };
            _usuario.KeyPress += (_, _) => _usuarioEditado = true;
        }
        else
        {
            _nombre.Text = existente.Nombre;
            _usuario.Text = existente.NombreUsuario;
            _usuario.ReadOnly = true;
            _correo.Text = existente.Correo ?? "";
            _activo.Checked = existente.Activo;
            Campo("", _activo);
        }
    }

    protected override bool Validar()
    {
        var u = new Usuario
        {
            Id = _existente?.Id ?? 0,
            Nombre = _nombre.Text,
            NombreUsuario = _usuario.Text,
            Correo = _correo.Text,
            Rol = (string)_rol.SelectedItem!,
            Activo = _existente is null || _activo.Checked
        };
        if (_existente is null)
        {
            bool ok = IntentarGuardar(() => _usuarios.Crear(u, _clave!.Text, _repetir!.Text));
            if (ok) Ui.Info($"Usuario \"{u.NombreUsuario}\" creado. Entréguele la contraseña temporal a {u.Nombre.Trim()}.");
            return ok;
        }
        return IntentarGuardar(() => _usuarios.Editar(u));
    }
}

internal sealed class RestablecerClaveDialog : DialogoBase
{
    private readonly UsuarioServicio _usuarios;
    private readonly Usuario _usuario;
    private readonly TextBox _clave = Ui.Texto(200);
    private readonly TextBox _repetir = Ui.Texto(200);

    public RestablecerClaveDialog(UsuarioServicio usuarios, Usuario usuario)
        : base($"Restablecer contraseña — {usuario.NombreUsuario}", "Restablecer")
    {
        _usuarios = usuarios;
        _usuario = usuario;
        Bloque(Explicacion($"Asigne una contraseña temporal a {usuario.Nombre} y entréguesela. Al ingresar, el sistema " +
                           "le pedirá cambiarla por una personal. La contraseña anterior deja de funcionar."));
        Campo("Contraseña temporal *", _clave);
        Campo("Repetir contraseña *", _repetir);
        CasillaMostrar("Mostrar contraseña", _clave, _repetir);
    }

    protected override bool Validar()
    {
        bool ok = IntentarGuardar(() => _usuarios.RestablecerClave(_usuario.Id, _clave.Text, _repetir.Text));
        if (ok) Ui.Info($"Contraseña restablecida. Entréguele la contraseña temporal a {_usuario.Nombre}.");
        return ok;
    }
}

internal sealed class CodigoConfirmacionDialog : DialogoBase
{
    private readonly SeguridadServicio _seguridad;
    private readonly TextBox? _actual;
    private readonly TextBox _nuevo = Ui.Texto(180);
    private readonly TextBox _repetir = Ui.Texto(180);

    public CodigoConfirmacionDialog(SeguridadServicio seguridad) : base("Código de confirmación", "Guardar código")
    {
        _seguridad = seguridad;
        Bloque(Explicacion($"Se pide para {Autorizacion.AccionesProtegidas}. Lo conocen los administradores. " +
                           $"Mínimo {SeguridadServicio.LargoMinimoCodigo} caracteres."));
        if (seguridad.Configurado)
        {
            _actual = Ui.Texto(180);
            Campo("Código ACTUAL *", _actual);
        }
        Campo("Código nuevo *", _nuevo);
        Campo("Repetir código nuevo *", _repetir);
        CasillaMostrar("Mostrar códigos", _actual, _nuevo, _repetir);
    }

    protected override bool Validar()
    {
        if (_nuevo.Text.Trim() != _repetir.Text.Trim()) { Ui.Info("Los códigos nuevos no coinciden."); return false; }
        bool ok = IntentarGuardar(() => _seguridad.DefinirConfirmacion(_nuevo.Text, _actual?.Text));
        if (ok) Ui.Info("Código guardado. Compártalo solo con los administradores: el programa no puede mostrarlo después.");
        return ok;
    }
}
