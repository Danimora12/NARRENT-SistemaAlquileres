using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class NuevaOrdenControl : UserControl, IRefrescable
{
    private readonly ClienteRepositorio _clientes;
    private readonly ProductoRepositorio _productos;
    private readonly OrdenRepositorio _ordenes;
    private readonly List<LineaOrden> _lineas = new();

    private readonly ComboBox _cboCliente = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Font = Ui.Fuente };
    private readonly DateTimePicker _dtpFecha = Ui.Fecha();
    private readonly DateTimePicker _dtpInicio = Ui.Fecha();
    private readonly TextBox _txtNotas = Ui.Texto(500);

    private readonly TextBox _buscarProducto = Ui.Texto(150);
    private readonly ComboBox _cboProducto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Font = Ui.Fuente };
    private List<Producto> _productosActivos = new();
    private readonly CampoMonto _precio = new(120);
    private readonly CampoEntero _cantidad = new(1, 1000);
    private readonly CampoEntero _dias = new(1, 3650);

    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _lblBase = Ui.Etiqueta("₡0,00", Ui.FuenteNegrita);
    private readonly Label _lblIva = Ui.Etiqueta("₡0,00", Ui.FuenteNegrita);
    private readonly Label _lblTotal = Ui.Etiqueta("₡0,00", Ui.FuenteTotal);

    public NuevaOrdenControl(BaseDatos db)
    {
        _clientes = new ClienteRepositorio(db);
        _productos = new ProductoRepositorio(db);
        _ordenes = new OrdenRepositorio(db);

        // El orden importa: con DropDownList, primero el origen y después el modo.
        _cboCliente.AutoCompleteSource = AutoCompleteSource.ListItems;
        _cboCliente.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _lblTotal.ForeColor = Ui.Primario;

        _buscarProducto.PlaceholderText = "Código o nombre";
        _buscarProducto.TextChanged += (_, _) => FiltrarProductos();
        _buscarProducto.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _cantidad.Focus(); }
            else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; _cboProducto.Focus(); _cboProducto.DroppedDown = true; }
        };

        Ui.Columna(_grid, "Producto", 200);
        Ui.Columna(_grid, "Precio unit. (IVA incl.)", 120, derecha: true);
        Ui.Columna(_grid, "Cantidad", 60, derecha: true);
        Ui.Columna(_grid, "Días", 50, derecha: true);
        Ui.Columna(_grid, "Total", 110, derecha: true);
        Ui.Columna(_grid, "Base", 110, derecha: true);
        Ui.Columna(_grid, "IVA", 100, derecha: true);

        var grupoDatos = Ui.Grupo("Datos de la orden",
            Ui.Fila(Ui.Etiqueta("Cliente:"), _cboCliente, Ui.Boton("Nuevo cliente…", (_, _) => NuevoCliente())),
            Ui.Fila(Ui.Etiqueta("Fecha de la orden:"), _dtpFecha, Ui.Etiqueta("   Inicio del alquiler:"), _dtpInicio),
            Ui.Fila(Ui.Etiqueta("Notas:"), _txtNotas));

        var grupoProducto = Ui.Grupo("Agregar producto",
            Ui.Fila(
                Ui.Etiqueta("Buscar:"), _buscarProducto,
                Ui.Etiqueta("Producto:"), _cboProducto,
                Ui.Etiqueta("Precio (IVA incl.) ₡:"), _precio,
                Ui.Etiqueta("Cantidad:"), _cantidad,
                Ui.Etiqueta("Días:"), _dias,
                Ui.Boton("Agregar a la orden", (_, _) => AgregarLinea(), principal: true)));

        var totales = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Right };
        totales.Controls.Add(Ui.Etiqueta("Base imponible (sin IVA):"), 0, 0);
        totales.Controls.Add(_lblBase, 1, 0);
        totales.Controls.Add(Ui.Etiqueta("IVA 13 % (incluido):"), 0, 1);
        totales.Controls.Add(_lblIva, 1, 1);
        totales.Controls.Add(Ui.Etiqueta("TOTAL:", Ui.FuenteTotal), 0, 2);
        totales.Controls.Add(_lblTotal, 1, 2);

        var pie = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        pie.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pie.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pie.Controls.Add(Ui.Fila(
            Ui.Boton("Quitar línea seleccionada", (_, _) => QuitarLinea()),
            Ui.Boton("Limpiar orden", (_, _) => { if (_lineas.Count == 0 || Ui.Confirmar("¿Descartar la orden actual?")) Limpiar(); })), 0, 0);
        pie.Controls.Add(totales, 1, 0);
        var nota = Ui.Etiqueta("El descuento y el pago adelantado se indican al confirmar.");
        nota.ForeColor = Color.DimGray;
        pie.Controls.Add(nota, 0, 1);
        pie.Controls.Add(Ui.Fila(Ui.Boton("Confirmar orden", (_, _) => Confirmar(), principal: true)), 1, 1);

        Controls.Add(Ui.Pila(grupoDatos, grupoProducto, _grid, pie));

        _cboProducto.SelectedIndexChanged += (_, _) =>
        {
            if (_cboProducto.SelectedItem is Producto p) _precio.Centimos = p.PrecioConIvaCentimos;
        };
    }

    public void Refrescar()
    {
        long? clienteSel = (_cboCliente.SelectedItem as Cliente)?.Id;
        long? productoSel = (_cboProducto.SelectedItem as Producto)?.Id;

        _cboCliente.Items.Clear();
        _cboCliente.Items.AddRange(_clientes.Listar().Cast<object>().ToArray());
        Seleccionar(_cboCliente, clienteSel);

        _productosActivos = _productos.Listar(soloActivos: true);
        FiltrarProductos(productoSel);
    }

    /// <summary>Muestra en la lista solo los productos cuyo código o nombre contienen lo buscado.</summary>
    private void FiltrarProductos(long? mantener = null)
    {
        mantener ??= (_cboProducto.SelectedItem as Producto)?.Id;
        string b = SinTildes(_buscarProducto.Text.Trim());
        var visibles = b.Length == 0
            ? _productosActivos
            : _productosActivos.Where(p => SinTildes(p.Codigo).Contains(b, StringComparison.OrdinalIgnoreCase)
                                        || SinTildes(p.Nombre).Contains(b, StringComparison.OrdinalIgnoreCase)).ToList();
        _cboProducto.BeginUpdate();
        _cboProducto.Items.Clear();
        _cboProducto.Items.AddRange(visibles.Cast<object>().ToArray());
        _cboProducto.EndUpdate();
        Seleccionar(_cboProducto, mantener);
        if (_cboProducto.SelectedIndex < 0 && _cboProducto.Items.Count > 0) _cboProducto.SelectedIndex = 0;
        _buscarProducto.BackColor = b.Length > 0 && visibles.Count == 0 ? Color.MistyRose : SystemColors.Window;
    }

    private static string SinTildes(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());

    private static void Seleccionar(ComboBox cbo, long? id)
    {
        if (id is null) return;
        for (int i = 0; i < cbo.Items.Count; i++)
        {
            long itemId = cbo.Items[i] switch { Cliente c => c.Id, Producto p => p.Id, _ => 0 };
            if (itemId == id) { cbo.SelectedIndex = i; return; }
        }
    }

    private void NuevoCliente()
    {
        var nuevo = new Cliente();
        using var dlg = new ClienteDialog(nuevo, c => _clientes.Guardar(c));
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            Refrescar();
            Seleccionar(_cboCliente, nuevo.Id);
        });
    }

    private void AgregarLinea()
    {
        if (_cboProducto.SelectedItem is not Producto p)
        {
            Ui.Info("Seleccione un producto. Si no aparece ninguno, revise la pestaña Productos.");
            return;
        }
        if (_precio.Colones <= 0 && !Ui.Confirmar("El precio es ₡0. ¿Agregar de todas formas?")) return;

        var linea = new LineaOrden
        {
            ProductoId = p.Id,
            ProductoNombre = p.Nombre,
            PrecioUnitarioCentimos = _precio.Centimos,
            Cantidad = _cantidad.Valor,
            Dias = _dias.Valor
        };
        linea.Calcular();
        _lineas.Add(linea);
        _cantidad.Valor = 1;
        MostrarLineas();
        _buscarProducto.Clear();
        _buscarProducto.Focus();
    }

    private void QuitarLinea()
    {
        if (Ui.Seleccionado<LineaOrden>(_grid) is { } l)
        {
            _lineas.Remove(l);
            MostrarLineas();
        }
    }

    private void MostrarLineas()
    {
        _grid.Rows.Clear();
        foreach (var l in _lineas)
        {
            int i = _grid.Rows.Add(l.ProductoNombre, Ui.Fmt(l.PrecioUnitarioCentimos), l.Cantidad, l.Dias,
                Ui.Fmt(l.TotalCentimos), Ui.Fmt(l.BaseCentimos), Ui.Fmt(l.IvaCentimos));
            _grid.Rows[i].Tag = l;
        }
        _lblBase.Text = Ui.Fmt(_lineas.Sum(l => l.BaseCentimos));
        _lblIva.Text = Ui.Fmt(_lineas.Sum(l => l.IvaCentimos));
        _lblTotal.Text = Ui.Fmt(_lineas.Sum(l => l.TotalCentimos));
    }

    /// <summary>Abre la ventana de descuento + pago adelantado y, si se confirma, guarda la orden.</summary>
    private void Confirmar()
    {
        if (_cboCliente.SelectedItem is not Cliente cliente) { Ui.Info("Seleccione un cliente."); _cboCliente.Focus(); return; }
        if (_lineas.Count == 0) { Ui.Info("Agregue al menos un producto a la orden."); return; }

        using var dlg = new ConfirmarOrdenDialog(_lineas, cliente.Nombre, _dtpFecha.Value.Date);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            QuitarDescuentos();
            return;
        }

        try
        {
            var orden = new Orden
            {
                ClienteId = cliente.Id,
                Fecha = _dtpFecha.Value.Date,
                FechaInicio = _dtpInicio.Value.Date,
                Notas = _txtNotas.Text,
                Lineas = _lineas.ToList()
            };
            _ordenes.Crear(orden, dlg.Pago);

            string mensaje = $"Orden N° {orden.Numero} confirmada.\n\nTotal: {Ui.Fmt(orden.TotalCentimos)}";
            if (orden.DescuentoCentimos > 0) mensaje += $"  (descuento {Ui.Fmt(orden.DescuentoCentimos)})";
            mensaje += $"\nPagado: {Ui.Fmt(orden.PagadoCentimos)}" +
                       (orden.PagadoCentimos > 0 ? $" por {dlg.Pago.MetodoTexto}" : "") +
                       $"\nSaldo pendiente: {Ui.Fmt(orden.SaldoCentimos)}";
            Ui.Info(mensaje);
            Limpiar();
        }
        catch (Exception ex)
        {
            QuitarDescuentos();
            Ui.MostrarError(ex);
        }
    }

    private void QuitarDescuentos()
    {
        foreach (var l in _lineas)
        {
            l.DescuentoPct = 0;
            l.Calcular();
        }
        MostrarLineas();
    }

    private void Limpiar()
    {
        _lineas.Clear();
        MostrarLineas();
        _cboCliente.SelectedIndex = -1;
        _dtpFecha.Value = _dtpInicio.Value = DateTime.Today;
        _txtNotas.Clear();
        _cantidad.Valor = _dias.Valor = 1;
    }
}
