using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class OrdenesControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly OrdenRepositorio _ordenes;

    private readonly DateTimePicker _desde = Ui.Fecha();
    private readonly DateTimePicker _hasta = Ui.Fecha();
    private readonly ComboBox _estado = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Font = Ui.Fuente };
    private readonly TextBox _buscar = Ui.Texto(200);
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _resumen = Ui.Etiqueta("", Ui.FuenteNegrita);

    public OrdenesControl(BaseDatos db)
    {
        _db = db;
        _ordenes = new OrdenRepositorio(db);

        var hoy = DateTime.Today;
        _desde.Value = new DateTime(hoy.Year, hoy.Month, 1);
        _hasta.Value = hoy;
        _estado.Items.AddRange(new object[] { "Todos", EstadoOrden.Activa, EstadoOrden.Devuelta, EstadoOrden.Anulada });
        _estado.SelectedIndex = 0;

        Ui.Columna(_grid, "N°", 85);
        Ui.Columna(_grid, "Fecha", 80);
        Ui.Columna(_grid, "Cliente", 200);
        Ui.Columna(_grid, "Inicio", 80);
        Ui.Columna(_grid, "Devolución", 80);
        Ui.Columna(_grid, "Estado", 75);
        Ui.Columna(_grid, "Total", 105, derecha: true);
        Ui.Columna(_grid, "Pagado", 105, derecha: true);
        Ui.Columna(_grid, "Saldo", 105, derecha: true);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) VerDetalle(); };
        _buscar.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ui.Intentar(Refrescar); } };

        var filtros = Ui.Fila(
            Ui.Etiqueta("Desde:"), _desde,
            Ui.Etiqueta("Hasta:"), _hasta,
            Ui.Etiqueta("Estado:"), _estado,
            Ui.Etiqueta("Cliente o N°:"), _buscar,
            Ui.Boton("Buscar", (_, _) => Ui.Intentar(Refrescar), principal: true));

        var acciones = Ui.Fila(
            Ui.Boton("Ver detalle / pagos", (_, _) => VerDetalle(), principal: true),
            Ui.Boton("Registrar pago…", (_, _) => RegistrarPago()),
            Ui.Boton("Editar notas…", (_, _) => EditarNotas()),
            Ui.Boton("Marcar como devuelta…", (_, _) => MarcarDevuelta()),
            Ui.Boton("Anular orden…", (_, _) => Anular()),
            Ui.Boton("Eliminar orden…", (_, _) => Eliminar()),
            Ui.Boton("Órdenes eliminadas…", (_, _) => VerEliminadas()));

        Controls.Add(Ui.Pila(filtros, _grid, acciones, _resumen));
    }

    public void Refrescar()
    {
        if (_hasta.Value.Date < _desde.Value.Date)
            throw new ReglaNegocioException("La fecha \"hasta\" no puede ser anterior a \"desde\".");

        long? seleccionada = Ui.Seleccionado<Orden>(_grid)?.Id;
        EstadoOrden? estado = _estado.SelectedItem is EstadoOrden e ? e : null;
        var lista = _ordenes.Listar(_desde.Value.Date, _hasta.Value.Date, estado, _buscar.Text);

        _grid.Rows.Clear();
        foreach (var o in lista)
        {
            int i = _grid.Rows.Add(o.Numero, Ui.Fmt(o.Fecha), o.ClienteNombre, Ui.Fmt(o.FechaInicio),
                Ui.Fmt(o.FechaFin), o.Estado, Ui.Fmt(o.TotalCentimos), Ui.Fmt(o.PagadoCentimos), Ui.Fmt(o.SaldoCentimos));
            var fila = _grid.Rows[i];
            fila.Tag = o;
            if (o.Estado == EstadoOrden.Anulada)
            {
                fila.DefaultCellStyle.ForeColor = Color.Gray;
                fila.DefaultCellStyle.Font = new Font(Ui.Fuente, FontStyle.Strikeout);
            }
            else if (o.SaldoCentimos > 0)
            {
                fila.Cells[8].Style.ForeColor = Color.Firebrick;
                fila.Cells[8].Style.Font = Ui.FuenteNegrita;
            }
            if (o.Id == seleccionada) _grid.CurrentCell = fila.Cells[0];
        }

        var validas = lista.Where(o => o.Estado != EstadoOrden.Anulada).ToList();
        _resumen.Text = $"{validas.Count} órdenes válidas · Total {Ui.Fmt(validas.Sum(o => o.TotalCentimos))} · " +
                        $"Pagado {Ui.Fmt(validas.Sum(o => o.PagadoCentimos))} · Por cobrar {Ui.Fmt(validas.Sum(o => o.SaldoCentimos))}";
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
            using var f = new OrdenDetalleForm(_db, sel.Id);
            f.ShowDialog(this);
            if (f.HuboCambios) Refrescar();
        });
    }

    private void RegistrarPago()
    {
        if (Seleccionada() is not { } sel) return;
        if (sel.Estado == EstadoOrden.Anulada) { Ui.Info("La orden está anulada: no se le pueden registrar pagos."); return; }
        if (sel.SaldoCentimos <= 0) { Ui.Info($"La orden N° {sel.Numero} ya está pagada por completo."); return; }

        using var dlg = new PagoDialog(sel);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.RegistrarPago(sel.Id, dlg.Pago);
            Refrescar();
        });
    }

    private void EditarNotas()
    {
        if (Seleccionada() is not { } o) return;
        using var dlg = new TextoDialog($"Notas — Orden N° {o.Numero}", "Notas:", obligatorio: false, valorInicial: o.Notas);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.EditarNotas(o.Id, dlg.Valor);
            Refrescar();
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
                          "La orden sigue visible (tachada), pero deja de contar en el Flujo de caja. Esta acción no se puede deshacer."))
            return;

        using var dlg = new TextoDialog($"Anular orden N° {o.Numero}", "Motivo de la anulación:");
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.Anular(o.Id, dlg.Valor);
            Refrescar();
        });
    }

    /// <summary>Para órdenes fantasma o de prueba: van a la papelera y se pueden restaurar.</summary>
    private void Eliminar()
    {
        if (Seleccionada() is not { } o) return;

        string advertencia = "La orden desaparecerá de esta lista y del Flujo de caja. Se puede restaurar desde \"Órdenes eliminadas\".";
        if (o.PagadoCentimos > 0)
            advertencia = $"ATENCIÓN: esta orden tiene pagos por {Ui.Fmt(o.PagadoCentimos)}. Ese dinero también dejará de verse en el Flujo de caja.\n\n" + advertencia;

        var autorizacion = Autorizacion.Pedir(this, _db,
            $"Eliminar la orden N° {o.Numero} de {o.ClienteNombre} ({Ui.Fmt(o.TotalCentimos)})",
            advertencia, pedirMotivo: true);
        if (autorizacion is not { } a) return;

        Ui.Intentar(() =>
        {
            _ordenes.Eliminar(o.Id, a.Empleado, a.Motivo);
            Refrescar();
        });
    }

    private void VerEliminadas()
    {
        Ui.Intentar(() =>
        {
            using var f = new OrdenesEliminadasForm(_db);
            f.ShowDialog(this);
            if (f.HuboCambios) Refrescar();
        });
    }
}
