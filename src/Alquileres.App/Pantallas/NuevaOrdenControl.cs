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
    private readonly DateTimePicker _dtpFin = Ui.Fecha(opcional: true);
    private readonly TextBox _txtNotas = Ui.Texto(500);

    private readonly ComboBox _cboProducto = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Font = Ui.Fuente };
    private readonly NumericUpDown _nudPrecio = Ui.Monto();
    private readonly NumericUpDown _nudCantidad = Ui.Entero(1, 1000);
    private readonly NumericUpDown _nudDias = Ui.Entero(1, 3650);

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

        Ui.Columna(_grid, "Producto", 200);
        Ui.Columna(_grid, "Precio unit. (IVA incl.)", 120, derecha: true);
        Ui.Columna(_grid, "Cantidad", 60, derecha: true);
        Ui.Columna(_grid, "Días", 50, derecha: true);
        Ui.Columna(_grid, "Total", 110, derecha: true);
        Ui.Columna(_grid, "Base", 110, derecha: true);
        Ui.Columna(_grid, "IVA", 100, derecha: true);

        var grupoDatos = new GroupBox { Text = "Datos de la orden", AutoSize = true, Font = Ui.FuenteNegrita, Padding = new Padding(8) };
        var pilaDatos = new FlowLayoutPanel { Location = new Point(8, 24), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Font = Ui.Fuente };
        pilaDatos.Controls.Add(Ui.Fila(
            Ui.Etiqueta("Cliente:"), _cboCliente,
            Ui.Boton("Nuevo cliente…", (_, _) => NuevoCliente())));
        pilaDatos.Controls.Add(Ui.Fila(
            Ui.Etiqueta("Fecha de la orden:"), _dtpFecha,
            Ui.Etiqueta("   Inicio del alquiler:"), _dtpInicio,
            Ui.Etiqueta("   Devolución estimada:"), _dtpFin));
        pilaDatos.Controls.Add(Ui.Fila(Ui.Etiqueta("Notas:"), _txtNotas));
        grupoDatos.Controls.Add(pilaDatos);

        var grupoProducto = new GroupBox { Text = "Agregar producto", AutoSize = true, Font = Ui.FuenteNegrita, Padding = new Padding(8) };
        var filaProducto = Ui.Fila(
            Ui.Etiqueta("Producto:"), _cboProducto,
            Ui.Etiqueta("Precio (IVA incl.):"), _nudPrecio,
            Ui.Etiqueta("Cantidad:"), _nudCantidad,
            Ui.Etiqueta("Días:"), _nudDias,
            Ui.Boton("Agregar a la orden", (_, _) => AgregarLinea(), principal: true));
        filaProducto.Font = Ui.Fuente;
        filaProducto.Location = new Point(8, 24);
        grupoProducto.Controls.Add(filaProducto);

        var totales = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Right };
        totales.Controls.Add(Ui.Etiqueta("Base imponible (sin IVA):"), 0, 0);
        totales.Controls.Add(_lblBase, 1, 0);
        totales.Controls.Add(Ui.Etiqueta("IVA 13 % (incluido):"), 0, 1);
        totales.Controls.Add(_lblIva, 1, 1);
        totales.Controls.Add(Ui.Etiqueta("TOTAL A PAGAR:", Ui.FuenteTotal), 0, 2);
        totales.Controls.Add(_lblTotal, 1, 2);

        var pie = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        pie.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pie.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pie.Controls.Add(Ui.Fila(
            Ui.Boton("Quitar línea seleccionada", (_, _) => QuitarLinea()),
            Ui.Boton("Limpiar orden", (_, _) => { if (_lineas.Count == 0 || Ui.Confirmar("¿Descartar la orden actual?")) Limpiar(); })), 0, 0);
        pie.Controls.Add(totales, 1, 0);
        pie.Controls.Add(new Panel(), 0, 1);
        pie.Controls.Add(Ui.Fila(Ui.Boton("Guardar orden", (_, _) => Guardar(), principal: true)), 1, 1);

        Controls.Add(Ui.Pila(grupoDatos, grupoProducto, _grid, pie));

        _cboProducto.SelectedIndexChanged += (_, _) =>
        {
            if (_cboProducto.SelectedItem is Producto p) _nudPrecio.Value = Math.Min(p.PrecioConIva, _nudPrecio.Maximum);
        };
        _dtpInicio.ValueChanged += (_, _) => SugerirDias();
        _dtpFin.ValueChanged += (_, _) => SugerirDias();
    }

    public void Refrescar()
    {
        long? clienteSel = (_cboCliente.SelectedItem as Cliente)?.Id;
        long? productoSel = (_cboProducto.SelectedItem as Producto)?.Id;

        _cboCliente.Items.Clear();
        _cboCliente.Items.AddRange(_clientes.Listar().Cast<object>().ToArray());
        Seleccionar(_cboCliente, clienteSel);

        _cboProducto.Items.Clear();
        _cboProducto.Items.AddRange(_productos.Listar(soloActivos: true).Cast<object>().ToArray());
        Seleccionar(_cboProducto, productoSel);
        if (_cboProducto.SelectedIndex < 0 && _cboProducto.Items.Count > 0) _cboProducto.SelectedIndex = 0;
    }

    private static void Seleccionar(ComboBox cbo, long? id)
    {
        if (id is null) return;
        for (int i = 0; i < cbo.Items.Count; i++)
        {
            long itemId = cbo.Items[i] switch { Cliente c => c.Id, Producto p => p.Id, _ => 0 };
            if (itemId == id) { cbo.SelectedIndex = i; return; }
        }
    }

    private void SugerirDias()
    {
        if (!_dtpFin.Checked) return;
        int dias = (_dtpFin.Value.Date - _dtpInicio.Value.Date).Days;
        _nudDias.Value = Math.Clamp(dias, 1, (int)_nudDias.Maximum);
    }

    private void NuevoCliente()
    {
        using var dlg = new ClienteDialog(new Cliente());
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _clientes.Guardar(dlg.Cliente);
            Refrescar();
            Seleccionar(_cboCliente, dlg.Cliente.Id);
        });
    }

    private void AgregarLinea()
    {
        if (_cboProducto.SelectedItem is not Producto p)
        {
            Ui.Info("Seleccione un producto. Si no aparece ninguno, revise la pestaña Productos.");
            return;
        }
        if (_nudPrecio.Value <= 0 && !Ui.Confirmar("El precio es ₡0. ¿Agregar de todas formas?")) return;

        var linea = new LineaOrden
        {
            ProductoId = p.Id,
            ProductoNombre = p.Nombre,
            PrecioUnitarioCentimos = Dinero.ACentimos(_nudPrecio.Value),
            Cantidad = (int)_nudCantidad.Value,
            Dias = (int)_nudDias.Value
        };
        linea.Calcular();
        _lineas.Add(linea);
        _nudCantidad.Value = 1;
        MostrarLineas();
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

    private void Guardar()
    {
        Ui.Intentar(() =>
        {
            var orden = new Orden
            {
                ClienteId = (_cboCliente.SelectedItem as Cliente)?.Id ?? 0,
                Fecha = _dtpFecha.Value.Date,
                FechaInicio = _dtpInicio.Value.Date,
                FechaFin = _dtpFin.Checked ? _dtpFin.Value.Date : null,
                Notas = _txtNotas.Text,
                Lineas = _lineas.ToList()
            };
            _ordenes.Crear(orden);
            Ui.Info($"Orden N° {orden.Numero} guardada.\n\nTotal a pagar: {Ui.Fmt(orden.TotalCentimos)}\n(IVA incluido: {Ui.Fmt(orden.IvaCentimos)})");
            Limpiar();
        });
    }

    private void Limpiar()
    {
        _lineas.Clear();
        MostrarLineas();
        _cboCliente.SelectedIndex = -1;
        _dtpFecha.Value = _dtpInicio.Value = DateTime.Today;
        _dtpFin.Checked = false;
        _txtNotas.Clear();
        _nudCantidad.Value = _nudDias.Value = 1;
    }
}
