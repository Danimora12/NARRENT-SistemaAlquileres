using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Historial de cambios: quién hizo qué y cuándo. Solo lectura.</summary>
internal sealed class LogsControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly LogRepositorio _logs;
    private readonly DateTimePicker _desde = Ui.Fecha();
    private readonly DateTimePicker _hasta = Ui.Fecha();
    private readonly TextBox _buscar = Ui.Texto(220);
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _resumen = Ui.Etiqueta("");

    public LogsControl(BaseDatos db)
    {
        _db = db;
        _logs = new LogRepositorio(db);

        _desde.Value = DateTime.Today.AddDays(-30);
        _hasta.Value = DateTime.Today;

        Ui.Columna(_grid, "Fecha y hora", 110);
        Ui.Columna(_grid, "Empleado", 90);
        Ui.Columna(_grid, "Acción", 140);
        Ui.Columna(_grid, "Referencia", 140);
        Ui.Columna(_grid, "Detalle", 420);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) VerDetalle(); };
        _buscar.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ui.Intentar(Refrescar); } };

        var filtros = Ui.Fila(
            Ui.Etiqueta("Desde:"), _desde,
            Ui.Etiqueta("Hasta:"), _hasta,
            Ui.Etiqueta("Buscar (orden, cliente, empleado…):"), _buscar,
            Ui.Boton("Buscar", (_, _) => Ui.Intentar(Refrescar), principal: true));

        var nota = Ui.Etiqueta(
            "Cada cambio queda registrado aquí con la fecha, la acción y, cuando se pidió código, el empleado que lo autorizó. " +
            "Los registros no se pueden editar ni borrar desde el programa.");
        nota.ForeColor = Color.DimGray;
        nota.MaximumSize = new Size(1100, 0);

        var acciones = Ui.Fila(
            Ui.Boton("Ver detalle", (_, _) => VerDetalle()),
            Ui.Boton("Códigos de autorización…", (_, _) => ConfigurarCodigos()),
            _resumen);

        Controls.Add(Ui.Pila(filtros, nota, _grid, acciones));
    }

    public void Refrescar()
    {
        if (_hasta.Value.Date < _desde.Value.Date)
            throw new ReglaNegocioException("La fecha \"hasta\" no puede ser anterior a \"desde\".");

        var lista = _logs.Listar(_desde.Value.Date, _hasta.Value.Date, _buscar.Text);
        _grid.Rows.Clear();
        foreach (var l in lista)
        {
            int i = _grid.Rows.Add(l.FechaHora.ToString("dd/MM/yyyy HH:mm"), l.Empleado ?? "—", l.Accion, l.Referencia, l.Detalle);
            _grid.Rows[i].Tag = l;
            if (l.Accion is "Orden eliminada" or "Pago eliminado" or "Autorización rechazada" or "Cliente eliminado")
                _grid.Rows[i].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
        _resumen.Text = $"     {lista.Count} registros";
    }

    private void VerDetalle()
    {
        if (Ui.Seleccionado<EntradaLog>(_grid) is not { } l) { Ui.Info("Seleccione un registro."); return; }
        Ui.Info($"{l.FechaHora:dd/MM/yyyy HH:mm:ss}\n" +
                $"Empleado: {l.Empleado ?? "—"}\n" +
                $"Acción: {l.Accion}\n" +
                $"Referencia: {l.Referencia}\n\n" +
                l.Detalle.Replace(" · ", "\n"));
    }

    private void ConfigurarCodigos()
    {
        using var dlg = new CodigosDialog(new SeguridadServicio(_db));
        if (dlg.ShowDialog(this) == DialogResult.OK) Ui.Intentar(Refrescar);
    }
}
