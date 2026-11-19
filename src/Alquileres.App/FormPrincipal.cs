using Alquileres.Core;
using Alquileres.App.Pantallas;

namespace Alquileres.App;

internal sealed class FormPrincipal : Form
{
    private readonly BaseDatos _db;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Font = Ui.FuenteNegrita, Padding = new Point(14, 6) };
    private readonly Label _sesion = new() { AutoSize = true, Font = Ui.Fuente, Margin = new Padding(3, 9, 12, 3) };

    /// <summary>Se pone en false después de restaurar un respaldo, para no respaldar encima.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool RespaldarAlCerrar { get; set; } = true;

    public FormPrincipal(BaseDatos db)
    {
        _db = db;
        Text = Ui.Titulo;
        Font = Ui.Fuente;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 650);
        Size = new Size(1200, 760);
        WindowState = FormWindowState.Maximized;

        using (var icono = typeof(FormPrincipal).Assembly.GetManifestResourceStream("app.ico"))
            if (icono is not null) Icon = new Icon(icono);

        Agregar("Nueva orden", new NuevaOrdenControl(db));
        Agregar("Órdenes", new OrdenesControl(db));
        Agregar("Productos", new ProductosControl(db));
        Agregar("Clientes", new ClientesControl(db));
        Agregar("Gastos", new GastosControl(db));
        Agregar("Flujo de caja", new FlujoCajaControl(db));
        Agregar("Respaldos", new RespaldosControl(db, this));
        Agregar("Logs", new LogsControl(db));
        Agregar("Usuarios", new UsuariosControl(db));

        // Barra de sesión arriba a la derecha (se agrega después de las pestañas para que se acomode primero).
        var barra = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6, 4, 6, 2),
            BackColor = Color.FromArgb(235, 241, 247)
        };
        barra.Controls.Add(Ui.Boton("Bloquear / Cambiar usuario", (_, _) => Bloquear(), principal: true));
        barra.Controls.Add(Ui.Boton("Cambiar mi contraseña", (_, _) => CambiarMiClave()));
        barra.Controls.Add(_sesion);

        _tabs.SelectedIndexChanged += (_, _) => RefrescarPestanaActual();
        Controls.Add(_tabs);
        Controls.Add(barra);
        MostrarSesion();
        Shown += (_, _) => RefrescarPestanaActual();
    }

    /// <summary>Actualiza la barra y el título (también tras editar el propio usuario).</summary>
    internal void MostrarSesion()
    {
        var u = _db.UsuarioActual;
        _sesion.Text = u is null ? "" : $"Sesión: {u.Nombre} ({u.Rol})";
        Text = u is null ? Ui.Titulo : $"{Ui.Titulo} — {u.Nombre}";
    }

    /// <summary>
    /// Oculta el programa y pide iniciar sesión otra vez (el mismo usuario u otro).
    /// Si en lugar de ingresar se elige "Salir", el programa se cierra (con su respaldo automático).
    /// </summary>
    private void Bloquear()
    {
        string? anterior = _db.UsuarioActual?.Nombre;
        Ui.Intentar(new UsuarioServicio(_db).CerrarSesion);
        Hide();
        if (Sesion.Iniciar(_db, anterior))
        {
            MostrarSesion();
            Show();
            Activate();
            RefrescarPestanaActual();
        }
        else Close();
    }

    private void CambiarMiClave()
    {
        using var dlg = new CambiarClaveDialog(new UsuarioServicio(_db));
        dlg.ShowDialog(this);
    }

    private void Agregar(string titulo, UserControl control)
    {
        control.Dock = DockStyle.Fill;
        control.Font = Ui.Fuente;
        var pagina = new TabPage(titulo) { Font = Ui.Fuente };
        pagina.Controls.Add(control);
        _tabs.TabPages.Add(pagina);
    }

    private void RefrescarPestanaActual()
    {
        if (_tabs.SelectedTab?.Controls.Count > 0 && _tabs.SelectedTab.Controls[0] is IRefrescable r)
            Ui.Intentar(r.Refrescar);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!RespaldarAlCerrar) return;

        Cursor = Cursors.WaitCursor;
        var (_, errores) = new RespaldoServicio(_db).RespaldoAutomatico();
        Cursor = Cursors.Default;

        if (errores.Count > 0)
        {
            var r = MessageBox.Show(
                "No se pudo completar el respaldo automático:\n\n" + string.Join("\n", errores) +
                "\n\n¿Desea cerrar de todas formas?",
                Ui.Titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.No) e.Cancel = true;
        }
    }
}
