using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal static class PagoUi
{
    public static ComboBox ComboMetodo()
    {
        var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Font = Ui.Fuente };
        foreach (var m in MetodosPago.Todos) cbo.Items.Add(new OpcionMetodo(m));
        cbo.SelectedIndex = 0;
        return cbo;
    }

    public static MetodoPago Metodo(ComboBox cbo) => ((OpcionMetodo)cbo.SelectedItem!).Metodo;

    /// <summary>
    /// El campo trabaja en colones enteros. Si lo digitado difiere del saldo en menos
    /// de ₡1 (órdenes viejas con céntimos), se toma el saldo exacto.
    /// </summary>
    public static long AjustarAlSaldo(long montoCentimos, long saldoCentimos) =>
        Math.Abs(montoCentimos - saldoCentimos) < 100 ? saldoCentimos : montoCentimos;

    private sealed record OpcionMetodo(MetodoPago Metodo)
    {
        public override string ToString() => MetodosPago.Nombre(Metodo);
    }
}

/// <summary>Registrar un abono o la cancelación del saldo de una orden ya creada.</summary>
internal sealed class PagoDialog : DialogoBase
{
    private readonly Orden _orden;
    private readonly ComboBox _metodo = PagoUi.ComboMetodo();
    private readonly CampoMonto _monto = new();
    private readonly DateTimePicker _fecha = Ui.Fecha();
    private readonly Label _saldoDespues = Ui.Etiqueta("", Ui.FuenteNegrita);

    public Pago Pago { get; } = new();

    public PagoDialog(Orden orden) : base($"Registrar pago — Orden N° {orden.Numero}", "Registrar pago", "Cerrar")
    {
        _orden = orden;
        _fecha.Value = DateTime.Today;

        var resumen = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        void Fila(string etiqueta, string valor, Font? fuente = null)
        {
            int f = resumen.RowCount++;
            resumen.Controls.Add(Ui.Etiqueta(etiqueta), 0, f);
            var v = Ui.Etiqueta(valor, fuente ?? Ui.Fuente);
            v.Anchor = AnchorStyles.Right;
            resumen.Controls.Add(v, 1, f);
        }
        Fila("Cliente:", orden.ClienteNombre);
        Fila("Total de la orden:", Ui.Fmt(orden.TotalCentimos));
        Fila("Pagado hasta hoy:", Ui.Fmt(orden.PagadoCentimos));
        Fila("Saldo pendiente:", Ui.Fmt(orden.SaldoCentimos), Ui.FuenteNegrita);
        Bloque(resumen);

        Campo("Método de pago:", _metodo);
        Campo("Monto que paga (₡):", Ui.Fila(_monto,
            Ui.Boton("Pagar saldo completo", (_, _) => _monto.Centimos = _orden.SaldoCentimos)));
        Campo("Fecha del pago:", _fecha);
        Campo("", _saldoDespues);

        _monto.TextChanged += (_, _) => ActualizarSaldo();
        ActualizarSaldo();
    }

    private long Monto => PagoUi.AjustarAlSaldo(_monto.Centimos, _orden.SaldoCentimos);

    private void ActualizarSaldo()
    {
        long saldo = _orden.SaldoCentimos - Monto;
        _saldoDespues.ForeColor = saldo < 0 ? Color.Firebrick : Ui.Primario;
        _saldoDespues.Text = saldo switch
        {
            < 0 => $"El monto supera el saldo por {Ui.Fmt(-saldo)}",
            0 => "Con este pago la cuenta queda CANCELADA",
            _ => $"Saldo después del pago: {Ui.Fmt(saldo)}"
        };
    }

    protected override bool Validar()
    {
        if (Monto <= 0) { Ui.Info("Digite el monto que paga el cliente."); _monto.Focus(); return false; }
        if (Monto > _orden.SaldoCentimos) { Ui.Info($"El monto no puede ser mayor que el saldo pendiente ({Ui.Fmt(_orden.SaldoCentimos)})."); return false; }
        Pago.Metodo = PagoUi.Metodo(_metodo);
        Pago.MontoCentimos = Monto;
        Pago.Fecha = _fecha.Value.Date;
        return true;
    }
}

/// <summary>
/// Se abre al presionar "Confirmar orden": descuento opcional (1–15 %, a toda la
/// orden o a un producto) y pago adelantado (método y monto).
/// </summary>
internal sealed class ConfirmarOrdenDialog : DialogoBase
{
    private readonly List<LineaOrden> _lineas;
    private readonly DataGridView _grid = Ui.Grid();

    private readonly CheckBox _aplicarDescuento = new() { Text = "Aplicar descuento", AutoSize = true, Font = Ui.FuenteNegrita };
    private readonly RadioButton _aTodaLaOrden = new() { Text = "A toda la orden", AutoSize = true, Checked = true, Font = Ui.Fuente };
    private readonly RadioButton _aUnProducto = new() { Text = "Solo a un producto:", AutoSize = true, Font = Ui.Fuente };
    private readonly ComboBox _producto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, Font = Ui.Fuente };
    private readonly CampoEntero _porcentaje = new(1, CalculadoraIva.DescuentoMaximo, 5, anchoTexto: 40);

    private readonly Label _subtotal = ValorTotal();
    private readonly Label _descuento = ValorTotal();
    private readonly Label _total = ValorTotal(Ui.FuenteTotal);
    private readonly Label _iva = ValorTotal();

    private readonly ComboBox _metodo = PagoUi.ComboMetodo();
    private readonly CampoMonto _monto = new();
    private readonly Label _saldo = Ui.Etiqueta("", Ui.FuenteNegrita);

    private long _totalCentimos;

    /// <summary>Pago adelantado (MontoCentimos = 0 si no pagó nada).</summary>
    public Pago Pago { get; } = new();

    public ConfirmarOrdenDialog(List<LineaOrden> lineas, string cliente, DateTime fechaOrden)
        : base("Confirmar orden", "Confirmar orden", "Volver")
    {
        _lineas = lineas;
        Pago.Fecha = fechaOrden;

        Bloque(Ui.Etiqueta($"Cliente: {cliente}", Ui.FuenteNegrita));

        _grid.Dock = DockStyle.None;
        _grid.Size = new Size(640, Math.Min(60 + lineas.Count * 28, 200));
        Ui.Columna(_grid, "Producto", 200);
        Ui.Columna(_grid, "Cant.", 45, derecha: true);
        Ui.Columna(_grid, "Días", 45, derecha: true);
        Ui.Columna(_grid, "Subtotal", 100, derecha: true);
        Ui.Columna(_grid, "Desc.", 50, derecha: true);
        Ui.Columna(_grid, "Total", 100, derecha: true);
        Bloque(_grid);

        for (int i = 0; i < lineas.Count; i++)
            _producto.Items.Add($"{i + 1}. {lineas[i].ProductoNombre} ({lineas[i].Cantidad} × {lineas[i].Dias} días)");
        _producto.SelectedIndex = 0;

        Bloque(Ui.Grupo("Descuento",
            _aplicarDescuento,
            Ui.Fila(_aTodaLaOrden),
            Ui.Fila(_aUnProducto, _producto),
            Ui.Fila(Ui.Etiqueta("Porcentaje:"), _porcentaje, Ui.Etiqueta($"%   (de 1 a {CalculadoraIva.DescuentoMaximo} %)"))));

        var totales = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Right };
        totales.Controls.Add(Ui.Etiqueta("Subtotal:"), 0, 0);
        totales.Controls.Add(_subtotal, 1, 0);
        totales.Controls.Add(Ui.Etiqueta("Descuento:"), 0, 1);
        totales.Controls.Add(_descuento, 1, 1);
        totales.Controls.Add(Ui.Etiqueta("TOTAL A PAGAR:", Ui.FuenteTotal), 0, 2);
        totales.Controls.Add(_total, 1, 2);
        totales.Controls.Add(Ui.Etiqueta("IVA 13 % incluido:"), 0, 3);
        totales.Controls.Add(_iva, 1, 3);
        _total.ForeColor = Ui.Primario;
        Bloque(totales);

        Bloque(Ui.Grupo("Pago adelantado",
            Ui.Fila(Ui.Etiqueta("Método de pago:"), _metodo),
            Ui.Fila(Ui.Etiqueta("Monto que paga (₡):"), _monto,
                Ui.Boton("Pago completo", (_, _) => _monto.Centimos = _totalCentimos)),
            _saldo));

        // Los dos radios están en filas distintas (contenedores distintos), así que se excluyen a mano.
        _aTodaLaOrden.CheckedChanged += (_, _) => { if (_aTodaLaOrden.Checked) _aUnProducto.Checked = false; Recalcular(); };
        _aUnProducto.CheckedChanged += (_, _) => { if (_aUnProducto.Checked) _aTodaLaOrden.Checked = false; Recalcular(); };
        _aplicarDescuento.CheckedChanged += (_, _) => Recalcular();
        _producto.SelectedIndexChanged += (_, _) => { if (_producto.Focused) _aUnProducto.Checked = true; Recalcular(); };
        _porcentaje.ValorCambiado += (_, _) => Recalcular();
        _monto.TextChanged += (_, _) => ActualizarSaldo();

        Recalcular();
        Shown += (_, _) => _monto.Focus();
    }

    private static Label ValorTotal(Font? fuente = null)
    {
        var l = Ui.Etiqueta("₡0,00", fuente ?? Ui.FuenteNegrita);
        l.Anchor = AnchorStyles.Right;
        return l;
    }

    /// <summary>Descuento elegido para cada línea (0 = sin descuento).</summary>
    private int[] DescuentosPorLinea()
    {
        var pct = new int[_lineas.Count];
        if (!_aplicarDescuento.Checked) return pct;
        for (int i = 0; i < pct.Length; i++)
            if (_aTodaLaOrden.Checked || _lineas.Count == 1 || i == _producto.SelectedIndex)
                pct[i] = _porcentaje.Valor;
        return pct;
    }

    private void Recalcular()
    {
        bool activo = _aplicarDescuento.Checked;
        _aTodaLaOrden.Enabled = _porcentaje.Enabled = activo;
        _aUnProducto.Enabled = _producto.Enabled = activo && _lineas.Count > 1;

        var pct = DescuentosPorLinea();
        long subtotal = 0, descuento = 0, total = 0, iva = 0;
        _grid.Rows.Clear();
        for (int i = 0; i < _lineas.Count; i++)
        {
            var l = _lineas[i];
            var r = CalculadoraIva.CalcularLinea(l.PrecioUnitarioCentimos, l.Cantidad, l.Dias, pct[i], l.TasaIva);
            _grid.Rows.Add(l.ProductoNombre, l.Cantidad, l.Dias, Ui.Fmt(r.SubtotalCentimos),
                pct[i] > 0 ? $"{pct[i]} %" : "", Ui.Fmt(r.TotalCentimos));
            subtotal += r.SubtotalCentimos;
            descuento += r.DescuentoCentimos;
            total += r.TotalCentimos;
            iva += r.IvaCentimos;
        }
        _totalCentimos = total;
        _subtotal.Text = Ui.Fmt(subtotal);
        _descuento.Text = descuento > 0 ? "−" + Ui.Fmt(descuento) : Ui.Fmt(0);
        _total.Text = Ui.Fmt(total);
        _iva.Text = Ui.Fmt(iva);
        ActualizarSaldo();
    }

    private long Monto => PagoUi.AjustarAlSaldo(_monto.Centimos, _totalCentimos);

    private void ActualizarSaldo()
    {
        long saldo = _totalCentimos - Monto;
        _saldo.ForeColor = saldo < 0 ? Color.Firebrick : Ui.Primario;
        _saldo.Text = saldo switch
        {
            < 0 => $"El monto supera el total por {Ui.Fmt(-saldo)}",
            0 => "Pago completo: la cuenta queda cancelada",
            _ => $"Saldo pendiente: {Ui.Fmt(saldo)}"
        };
    }

    protected override bool Validar()
    {
        if (Monto > _totalCentimos)
        {
            Ui.Info($"El monto pagado no puede ser mayor que el total ({Ui.Fmt(_totalCentimos)}).");
            _monto.Focus();
            return false;
        }
        if (Monto == 0 && !Ui.Confirmar("No se registró ningún pago adelantado.\n\n¿Confirmar la orden sin pago?"))
            return false;

        var pct = DescuentosPorLinea();
        for (int i = 0; i < _lineas.Count; i++)
        {
            _lineas[i].DescuentoPct = pct[i];
            _lineas[i].Calcular();
        }
        Pago.Metodo = PagoUi.Metodo(_metodo);
        Pago.MontoCentimos = Monto;
        return true;
    }
}
