using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>
/// Detalle de una orden: productos, descuentos, pagos y saldo. Desde aquí se
/// registra un abono o la cancelación de la cuenta y se editan las notas.
/// </summary>
internal sealed class OrdenDetalleForm : Form
{
    private readonly BaseDatos _db;
    private readonly OrdenRepositorio _ordenes;
    private readonly long _ordenId;
    private Orden _orden = null!;

    private readonly TableLayoutPanel _info = new() { AutoSize = true, ColumnCount = 4 };
    private readonly DataGridView _gridLineas = Ui.Grid();
    private readonly DataGridView _gridPagos = Ui.Grid();
    private readonly FlowLayoutPanel _totales = Ui.Fila();
    private readonly Button _btnPago;
    private readonly Button _btnEliminarPago;
    private readonly Button _btnNotas;

    /// <summary>True si se registró algún cambio (para refrescar la lista al cerrar).</summary>
    public bool HuboCambios { get; private set; }

    public OrdenDetalleForm(BaseDatos db, long ordenId)
    {
        _db = db;
        _ordenes = new OrdenRepositorio(db);
        _ordenId = ordenId;

        Font = Ui.Fuente;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(960, 680);
        MinimizeBox = false;
        ShowInTaskbar = false;

        Ui.Columna(_gridLineas, "Producto", 200);
        Ui.Columna(_gridLineas, "Precio unit.", 100, derecha: true);
        Ui.Columna(_gridLineas, "Cant.", 45, derecha: true);
        Ui.Columna(_gridLineas, "Días", 45, derecha: true);
        Ui.Columna(_gridLineas, "Subtotal", 100, derecha: true);
        Ui.Columna(_gridLineas, "Descuento", 110, derecha: true);
        Ui.Columna(_gridLineas, "Total", 100, derecha: true);
        Ui.Columna(_gridLineas, "IVA", 90, derecha: true);

        Ui.Columna(_gridPagos, "Fecha", 90);
        Ui.Columna(_gridPagos, "Método", 140);
        Ui.Columna(_gridPagos, "Monto", 120, derecha: true);
        Ui.Columna(_gridPagos, "Registrado", 140);

        _btnPago = Ui.Boton("Registrar abono / cancelar cuenta…", (_, _) => RegistrarPago(), principal: true);
        _btnEliminarPago = Ui.Boton("Eliminar pago seleccionado…", (_, _) => EliminarPago());
        _btnNotas = Ui.Boton("Editar notas…", (_, _) => EditarNotas());
        var cerrar = Ui.Boton("Cerrar", (_, _) => Close());
        CancelButton = cerrar;

        Controls.Add(Ui.Pila(
            _info,
            Ui.Etiqueta("Productos", Ui.FuenteNegrita),
            _gridLineas,
            Ui.Etiqueta("Pagos", Ui.FuenteNegrita),
            _gridPagos,
            _totales,
            Ui.Fila(_btnPago, _btnEliminarPago, _btnNotas, cerrar)));

        Cargar();
    }

    private void Cargar()
    {
        _orden = _ordenes.Obtener(_ordenId) ?? throw new ReglaNegocioException("La orden ya no existe.");
        var o = _orden;
        Text = $"Orden N° {o.Numero}" + (o.Eliminada ? "  (ELIMINADA)" : "");

        _info.SuspendLayout();
        _info.Controls.Clear();
        _info.RowCount = 0;
        void Dato(string etiqueta, string valor, int col, int fila, Color? color = null)
        {
            _info.Controls.Add(Ui.Etiqueta(etiqueta, Ui.FuenteNegrita), col, fila);
            var v = Ui.Etiqueta(valor);
            v.MaximumSize = new Size(360, 0);
            if (color is { } c) v.ForeColor = c;
            _info.Controls.Add(v, col + 1, fila);
        }
        Dato("Cliente:", o.ClienteNombre, 0, 0);
        Dato("Estado:", o.Estado.ToString(), 2, 0, o.Estado == EstadoOrden.Anulada ? Color.Firebrick : null);
        Dato("Fecha de la orden:", Ui.Fmt(o.Fecha), 0, 1);
        Dato("Inicio / devolución:", $"{Ui.Fmt(o.FechaInicio)} – {(o.FechaFin is null ? "pendiente" : Ui.Fmt(o.FechaFin))}", 2, 1);
        Dato("Notas:", string.IsNullOrWhiteSpace(o.Notas) ? "—" : o.Notas!, 0, 2);
        Dato("Pago:", o.EstadoPago, 2, 2, o.SaldoCentimos > 0 ? Color.Firebrick : Color.ForestGreen);
        int fila = 3;
        if (o.Estado == EstadoOrden.Anulada) Dato("Motivo anulación:", o.MotivoAnulacion ?? "", 0, fila++);
        if (o.Eliminada)
            Dato("Eliminada:", $"{o.EliminadaEn:dd/MM/yyyy HH:mm} por {o.EliminadaPor} — {o.MotivoEliminacion}", 0, fila, Color.Firebrick);
        _info.ResumeLayout();

        _gridLineas.Rows.Clear();
        foreach (var l in o.Lineas)
            _gridLineas.Rows.Add(l.ProductoNombre, Ui.Fmt(l.PrecioUnitarioCentimos), l.Cantidad, l.Dias,
                Ui.Fmt(l.SubtotalCentimos),
                l.DescuentoPct > 0 ? $"{l.DescuentoPct} % (−{Ui.Fmt(l.DescuentoCentimos)})" : "",
                Ui.Fmt(l.TotalCentimos), Ui.Fmt(l.IvaCentimos));

        _gridPagos.Rows.Clear();
        foreach (var p in o.Pagos)
        {
            int i = _gridPagos.Rows.Add(Ui.Fmt(p.Fecha), p.MetodoTexto, Ui.Fmt(p.MontoCentimos), p.CreadoEn.ToString("dd/MM/yyyy HH:mm"));
            _gridPagos.Rows[i].Tag = p;
        }

        _totales.Controls.Clear();
        if (o.DescuentoCentimos > 0)
        {
            _totales.Controls.Add(Ui.Etiqueta($"Subtotal: {Ui.Fmt(o.SubtotalCentimos)}"));
            _totales.Controls.Add(Ui.Etiqueta($"   Descuento: −{Ui.Fmt(o.DescuentoCentimos)}"));
        }
        _totales.Controls.Add(Ui.Etiqueta($"   Total: {Ui.Fmt(o.TotalCentimos)}", Ui.FuenteNegrita));
        _totales.Controls.Add(Ui.Etiqueta($"   (IVA incl. {Ui.Fmt(o.IvaCentimos)})"));
        _totales.Controls.Add(Ui.Etiqueta($"   Pagado: {Ui.Fmt(o.PagadoCentimos)}", Ui.FuenteNegrita));
        var saldo = Ui.Etiqueta($"   SALDO: {Ui.Fmt(o.SaldoCentimos)}", Ui.FuenteTotal);
        saldo.ForeColor = o.SaldoCentimos > 0 ? Color.Firebrick : Color.ForestGreen;
        _totales.Controls.Add(saldo);

        bool editable = !o.Eliminada;
        _btnPago.Enabled = editable && o.Estado != EstadoOrden.Anulada && o.SaldoCentimos > 0;
        _btnEliminarPago.Enabled = editable && o.Pagos.Count > 0;
        _btnNotas.Enabled = editable;
    }

    private void RegistrarPago()
    {
        using var dlg = new PagoDialog(_orden);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.RegistrarPago(_orden.Id, dlg.Pago);
            HuboCambios = true;
            Cargar();
        });
    }

    private void EliminarPago()
    {
        if (Ui.Seleccionado<Pago>(_gridPagos) is not { } p) { Ui.Info("Seleccione el pago en la lista de pagos."); return; }
        string accion = $"Eliminar pago de {Ui.Fmt(p.MontoCentimos)} ({p.MetodoTexto}, {Ui.Fmt(p.Fecha)}) — Orden N° {_orden.Numero}";
        var empleado = Autorizacion.Empleado(this, _db, accion, "Use esta opción solo para corregir un pago registrado por error.");
        if (empleado is null) return;
        Ui.Intentar(() =>
        {
            _ordenes.EliminarPago(p.Id, empleado);
            HuboCambios = true;
            Cargar();
        });
    }

    private void EditarNotas()
    {
        using var dlg = new TextoDialog($"Notas — Orden N° {_orden.Numero}", "Notas:", obligatorio: false, valorInicial: _orden.Notas);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _ordenes.EditarNotas(_orden.Id, dlg.Valor);
            HuboCambios = true;
            Cargar();
        });
    }
}

/// <summary>Papelera: solo las órdenes eliminadas, con la opción de restaurarlas.</summary>
internal sealed class OrdenesEliminadasForm : Form
{
    private readonly BaseDatos _db;
    private readonly OrdenRepositorio _ordenes;
    private readonly DataGridView _grid = Ui.Grid();

    public bool HuboCambios { get; private set; }

    public OrdenesEliminadasForm(BaseDatos db)
    {
        _db = db;
        _ordenes = new OrdenRepositorio(db);

        Text = "Órdenes eliminadas";
        Font = Ui.Fuente;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 560);
        MinimizeBox = false;
        ShowInTaskbar = false;

        Ui.Columna(_grid, "N°", 80);
        Ui.Columna(_grid, "Fecha", 80);
        Ui.Columna(_grid, "Cliente", 180);
        Ui.Columna(_grid, "Total", 100, derecha: true);
        Ui.Columna(_grid, "Pagado", 100, derecha: true);
        Ui.Columna(_grid, "Eliminada el", 120);
        Ui.Columna(_grid, "Por", 100);
        Ui.Columna(_grid, "Motivo", 220);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) VerDetalle(); };

        var nota = Ui.Etiqueta("Estas órdenes no aparecen en Órdenes ni en el Flujo de caja. Al restaurarlas vuelven con su mismo número.");
        nota.ForeColor = Color.DimGray;

        var cerrar = Ui.Boton("Cerrar", (_, _) => Close());
        CancelButton = cerrar;
        Controls.Add(Ui.Pila(nota, _grid, Ui.Fila(
            Ui.Boton("Ver detalle", (_, _) => VerDetalle()),
            Ui.Boton("Restaurar orden…", (_, _) => Restaurar(), principal: true),
            cerrar)));

        Cargar();
    }

    private void Cargar()
    {
        _grid.Rows.Clear();
        foreach (var o in _ordenes.ListarEliminadas())
        {
            int i = _grid.Rows.Add(o.Numero, Ui.Fmt(o.Fecha), o.ClienteNombre, Ui.Fmt(o.TotalCentimos), Ui.Fmt(o.PagadoCentimos),
                o.EliminadaEn?.ToString("dd/MM/yyyy HH:mm") ?? "", o.EliminadaPor ?? "", o.MotivoEliminacion ?? "");
            _grid.Rows[i].Tag = o;
        }
    }

    private Orden? Seleccionada()
    {
        var o = Ui.Seleccionado<Orden>(_grid);
        if (o is null) Ui.Info("Seleccione una orden de la lista.");
        return o;
    }

    private void VerDetalle()
    {
        if (Seleccionada() is not { } o) return;
        Ui.Intentar(() =>
        {
            using var f = new OrdenDetalleForm(_db, o.Id);
            f.ShowDialog(this);
        });
    }

    private void Restaurar()
    {
        if (Seleccionada() is not { } o) return;
        var empleado = Autorizacion.Empleado(this, _db, $"Restaurar la orden N° {o.Numero} de {o.ClienteNombre} ({Ui.Fmt(o.TotalCentimos)})",
            "La orden volverá a aparecer en Órdenes y a contar en el Flujo de caja.");
        if (empleado is null) return;
        Ui.Intentar(() =>
        {
            _ordenes.Restaurar(o.Id, empleado);
            HuboCambios = true;
            Cargar();
            Ui.Info($"Orden N° {o.Numero} restaurada.");
        });
    }
}
