using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class OrdenesControl : UserControl, IRefrescable
{
    private readonly OrdenRepositorio _ordenes;

    private readonly DateTimePicker _desde = Ui.Fecha();
    private readonly DateTimePicker _hasta = Ui.Fecha();
    private readonly ComboBox _estado = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Font = Ui.Fuente };
    private readonly TextBox _cliente = Ui.Texto(200);
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _resumen = Ui.Etiqueta("", Ui.FuenteNegrita);

    public OrdenesControl(BaseDatos db)
    {
        _ordenes = new OrdenRepositorio(db);

        var hoy = DateTime.Today;
        _desde.Value = new DateTime(hoy.Year, hoy.Month, 1);
        _hasta.Value = hoy;
        _estado.Items.AddRange(new object[] { "Todos", EstadoOrden.Activa, EstadoOrden.Devuelta, EstadoOrden.Anulada });
        _estado.SelectedIndex = 0;

        Ui.Columna(_grid, "N°", 60);
        Ui.Columna(_grid, "Fecha", 80);
        Ui.Columna(_grid, "Cliente", 220);
        Ui.Columna(_grid, "Inicio", 80);
        Ui.Columna(_grid, "Devolución", 80);
        Ui.Columna(_grid, "Estado", 80);
        Ui.Columna(_grid, "Total", 110, derecha: true);
        Ui.Columna(_grid, "IVA", 100, derecha: true);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) VerDetalle(); };
        _cliente.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Refrescar(); } };

        var filtros = Ui.Fila(
            Ui.Etiqueta("Desde:"), _desde,
            Ui.Etiqueta("Hasta:"), _hasta,
            Ui.Etiqueta("Estado:"), _estado,
            Ui.Etiqueta("Cliente:"), _cliente,
            Ui.Boton("Buscar", (_, _) => Ui.Intentar(Refrescar), principal: true));

        var acciones = Ui.Fila(
            Ui.Boton("Ver detalle", (_, _) => VerDetalle()),
            Ui.Boton("Marcar como devuelta…", (_, _) => MarcarDevuelta()),
            Ui.Boton("Anular orden…", (_, _) => Anular()),
            _resumen);

        Controls.Add(Ui.Pila(filtros, _grid, acciones));
    }

    public void Refrescar()
    {
        if (_hasta.Value.Date < _desde.Value.Date)
            throw new ReglaNegocioException("La fecha \"hasta\" no puede ser anterior a \"desde\".");

        EstadoOrden? estado = _estado.SelectedItem is EstadoOrden e ? e : null;
        var lista = _ordenes.Listar(_desde.Value.Date, _hasta.Value.Date, estado, _cliente.Text);

        _grid.Rows.Clear();
        foreach (var o in lista)
        {
            int i = _grid.Rows.Add(o.Numero, Ui.Fmt(o.Fecha), o.ClienteNombre, Ui.Fmt(o.FechaInicio),
                Ui.Fmt(o.FechaFin), o.Estado, Ui.Fmt(o.TotalCentimos), Ui.Fmt(o.IvaCentimos));
            var fila = _grid.Rows[i];
            fila.Tag = o;
            if (o.Estado == EstadoOrden.Anulada)
            {
                fila.DefaultCellStyle.ForeColor = Color.Gray;
                fila.DefaultCellStyle.Font = new Font(Ui.Fuente, FontStyle.Strikeout);
            }
        }

        var validas = lista.Where(o => o.Estado != EstadoOrden.Anulada).ToList();
        _resumen.Text = $"     {validas.Count} órdenes válidas · Total {Ui.Fmt(validas.Sum(o => o.TotalCentimos))} · IVA {Ui.Fmt(validas.Sum(o => o.IvaCentimos))}";
    }

    private Orden? Seleccionada()
    {
        var o = Ui.Seleccionado<Orden>(_grid);
        if (o is null) Ui.Info("Seleccione una orden de la lista.");
        return o;
    }

    private void VerDetalle()
    {
        if (Seleccionada() is not { } sel) return;
        Ui.Intentar(() =>
        {
            var o = _ordenes.Obtener(sel.Id) ?? throw new ReglaNegocioException("La orden ya no existe.");
            using var f = new OrdenDetalleForm(o);
            f.ShowDialog(this);
        });
    }

    private void MarcarDevuelta()
    {
        if (Seleccionada() is not { } o) return;
        using var dlg = new FechaDialog($"Devolución de la orden N° {o.Numero}", "Fecha de devolución:");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.MarcarDevuelta(o.Id, dlg.Valor);
            Refrescar();
        });
    }

    private void Anular()
    {
        if (Seleccionada() is not { } o) return;
        if (!Ui.Confirmar($"¿Anular la orden N° {o.Numero} de {o.ClienteNombre} por {Ui.Fmt(o.TotalCentimos)}?\n\n" +
                          "La orden no se borra, pero deja de contar en el reporte de IVA. Esta acción no se puede deshacer."))
            return;

        using var dlg = new TextoDialog($"Anular orden N° {o.Numero}", "Motivo de la anulación:");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.Anular(o.Id, dlg.Valor);
            Refrescar();
        });
    }
}
