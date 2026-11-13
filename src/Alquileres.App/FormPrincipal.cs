using Alquileres.Core;
using Alquileres.App.Pantallas;

namespace Alquileres.App;

internal sealed class FormPrincipal : Form
{
    private readonly BaseDatos _db;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Font = Ui.FuenteNegrita, Padding = new Point(14, 6) };

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

        _tabs.SelectedIndexChanged += (_, _) => RefrescarPestanaActual();
        Controls.Add(_tabs);
        Shown += (_, _) => RefrescarPestanaActual();
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
