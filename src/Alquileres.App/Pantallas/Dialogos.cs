using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Base para ventanas de formulario con campos etiquetados y botones Aceptar/Cancelar.</summary>
internal abstract class DialogoBase : Form
{
    private readonly TableLayoutPanel _campos = new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2,
        Padding = new Padding(12)
    };

    protected DialogoBase(string titulo, string textoAceptar = "Aceptar", string textoCancelar = "Cancelar")
    {
        Text = titulo;
        Font = Ui.Fuente;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ShowInTaskbar = false;

        var aceptar = Ui.Boton(textoAceptar, (_, _) => { if (Validar()) { DialogResult = DialogResult.OK; Close(); } }, principal: true);
        var cancelar = Ui.Boton(textoCancelar, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        AcceptButton = aceptar;
        CancelButton = cancelar;

        var botones = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Padding = new Padding(12, 0, 12, 12)
        };
        botones.Controls.Add(aceptar);
        botones.Controls.Add(cancelar);

        var raiz = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Location = new Point(0, 0)
        };
        raiz.Controls.Add(_campos, 0, 0);
        raiz.Controls.Add(botones, 0, 1);
        Controls.Add(raiz);
    }

    protected void Campo(string etiqueta, Control control)
    {
        int fila = _campos.RowCount++;
        _campos.Controls.Add(Ui.Etiqueta(etiqueta), 0, fila);
        control.Margin = new Padding(3, 4, 3, 4);
        _campos.Controls.Add(control, 1, fila);
    }

    /// <summary>Control que ocupa todo el ancho (texto explicativo, grilla, recuadro).</summary>
    protected void Bloque(Control control)
    {
        int fila = _campos.RowCount++;
        _campos.Controls.Add(control, 0, fila);
        _campos.SetColumnSpan(control, 2);
    }

    protected static Label Explicacion(string texto, int ancho = 460)
    {
        var l = Ui.Etiqueta(texto);
        l.ForeColor = Color.DimGray;
        l.MaximumSize = new Size(ancho, 0);
        return l;
    }

    /// <summary>Devuelve false para impedir que se cierre la ventana.</summary>
    protected virtual bool Validar() => true;

    /// <summary>Ejecuta el guardado; si falla, muestra el error y deja la ventana abierta.</summary>
    protected static bool IntentarGuardar(Action guardar)
    {
        try { guardar(); return true; }
        catch (Exception ex) { Ui.MostrarError(ex); return false; }
    }
}

internal sealed class ClienteDialog : DialogoBase
{
    private readonly TextBox _nombre = Ui.Texto(320);
    private readonly TextBox _identificacion = Ui.Texto(200);
    private readonly TextBox _telefono = Ui.Texto(200);
    private readonly TextBox _correo = Ui.Texto(320);
    private readonly TextBox _notas = Ui.Texto(320, multilinea: true);
    private readonly Action<Cliente> _guardar;

    public Cliente Cliente { get; }

    public ClienteDialog(Cliente cliente, Action<Cliente> guardar, string? empleado = null)
        : base(cliente.Id == 0 ? "Nuevo cliente" : "Editar cliente", "Guardar")
    {
        Cliente = cliente;
        _guardar = guardar;
        _nombre.Text = cliente.Nombre;
        _identificacion.Text = cliente.Identificacion;
        _telefono.Text = cliente.Telefono;
        _correo.Text = cliente.Correo;
        _notas.Text = cliente.Notas;

        if (empleado is not null) Bloque(Explicacion($"Cambio autorizado por {empleado}. Quedará registrado en Logs."));
        Campo("Nombre *", _nombre);
        Campo("Cédula / identificación", _identificacion);
        Campo("Teléfono", _telefono);
        Campo("Correo", _correo);
        Campo("Notas", _notas);
    }

    protected override bool Validar()
    {
        if (string.IsNullOrWhiteSpace(_nombre.Text))
        {
            Ui.Info("El nombre es obligatorio.");
            _nombre.Focus();
            return false;
        }
        var c = new Cliente
        {
            Id = Cliente.Id,
            Nombre = _nombre.Text.Trim(),
            Identificacion = _identificacion.Text,
            Telefono = _telefono.Text,
            Correo = _correo.Text,
            Notas = _notas.Text
        };
        if (!IntentarGuardar(() => _guardar(c))) return false;
        Cliente.Id = c.Id;
        Cliente.Nombre = c.Nombre;
        Cliente.Identificacion = c.Identificacion;
        Cliente.Telefono = c.Telefono;
        Cliente.Correo = c.Correo;
        Cliente.Notas = c.Notas;
        return true;
    }
}

internal sealed class ProductoDialog : DialogoBase
{
    private readonly TextBox _nombre = Ui.Texto(300);
    private readonly CampoMonto _precio = new();
    private readonly Label _desglose = Ui.Etiqueta("");
    private readonly CampoEntero _inventario = new(0, 1_000_000, 0, anchoTexto: 80);
    private readonly CheckBox _activo = new() { Text = "Disponible para nuevas órdenes", AutoSize = true, Font = Ui.Fuente };
    private readonly Producto _producto;
    private readonly Action<Producto> _guardar;

    public ProductoDialog(Producto producto, Action<Producto> guardar, string empleado)
        : base(producto.Id == 0 ? "Nuevo producto" : "Editar producto", "Guardar")
    {
        _producto = producto;
        _guardar = guardar;
        _nombre.Text = producto.Nombre;
        _precio.Centimos = producto.PrecioConIvaCentimos;
        _inventario.Valor = producto.Inventario;
        _activo.Checked = producto.Activo;
        _desglose.ForeColor = Color.DimGray;

        Bloque(Explicacion($"Cambio autorizado por {empleado}. Quedará registrado en Logs."));
        Campo("Nombre *", _nombre);
        Campo("Precio con IVA incluido (₡)", _precio);
        Campo("", _desglose);
        if (producto.TieneVariantes)
        {
            _inventario.Enabled = false;
            Campo("Inventario", Ui.Fila(_inventario,
                Explicacion("Se calcula sumando sus características. Cámbielo con el botón \"Inventario / características\".", 300)));
        }
        else
        {
            Campo("Inventario (cantidad)", _inventario);
        }
        Campo("", _activo);

        _precio.TextChanged += (_, _) => ActualizarDesglose();
        ActualizarDesglose();
    }

    private void ActualizarDesglose()
    {
        var r = CalculadoraIva.DesglosarTotal(_precio.Centimos);
        _desglose.Text = $"Base: {Ui.Fmt(r.BaseCentimos)}   +   IVA 13 %: {Ui.Fmt(r.IvaCentimos)}";
    }

    protected override bool Validar()
    {
        if (string.IsNullOrWhiteSpace(_nombre.Text))
        {
            Ui.Info("El nombre es obligatorio.");
            _nombre.Focus();
            return false;
        }
        var p = new Producto
        {
            Id = _producto.Id,
            Nombre = _nombre.Text.Trim(),
            PrecioConIvaCentimos = _precio.Centimos,
            Inventario = _inventario.Valor,
            Activo = _activo.Checked
        };
        return IntentarGuardar(() => _guardar(p));
    }
}

/// <summary>Características de inventario de un producto (ej. colores y cantidad por color).</summary>
internal sealed class InventarioDialog : DialogoBase
{
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _total = Ui.Etiqueta("", Ui.FuenteNegrita);
    private readonly Action<List<VarianteInventario>> _guardar;

    public InventarioDialog(Producto producto, List<VarianteInventario> actuales, Action<List<VarianteInventario>> guardar, string empleado)
        : base($"Inventario — {producto.Nombre}", "Guardar")
    {
        _guardar = guardar;

        _grid.Dock = DockStyle.None;
        _grid.Size = new Size(460, 240);
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = true;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        Ui.Columna(_grid, "Característica (ej. color)", 260);
        Ui.Columna(_grid, "Cantidad", 100, derecha: true);
        foreach (var v in actuales) _grid.Rows.Add(v.Descripcion, v.Cantidad);
        _grid.CellValueChanged += (_, _) => ActualizarTotal();
        _grid.RowsRemoved += (_, _) => ActualizarTotal();
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not TextBox t) return;
            t.KeyPress -= SoloDigitos;
            if (_grid.CurrentCell?.ColumnIndex == 1) t.KeyPress += SoloDigitos;
        };

        Bloque(Explicacion(
            "Escriba cada característica y su cantidad en una fila nueva (por ejemplo: Blanco 20, Rojo 15). " +
            "El inventario del producto será la suma. Si el producto no tiene características, deje la lista vacía " +
            "y escriba la cantidad en \"Editar\"."));
        Bloque(Explicacion($"Cambio autorizado por {empleado}. Quedará registrado en Logs."));
        Bloque(_grid);
        Bloque(Ui.Fila(
            Ui.Boton("Quitar fila seleccionada", (_, _) => QuitarFila()),
            _total));
        ActualizarTotal();
    }

    private static void SoloDigitos(object? s, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
    }

    private void QuitarFila()
    {
        var fila = _grid.CurrentRow;
        if (fila is null || fila.IsNewRow) return;
        _grid.Rows.Remove(fila);
    }

    private List<(string Descripcion, string Cantidad)> Filas() =>
        _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => ((r.Cells[0].Value?.ToString() ?? "").Trim(), (r.Cells[1].Value?.ToString() ?? "").Trim()))
            .Where(f => f.Item1.Length > 0 || f.Item2.Length > 0)
            .ToList();

    private void ActualizarTotal()
    {
        int total = Filas().Sum(f => int.TryParse(f.Cantidad, out int n) ? n : 0);
        _total.Text = $"     Inventario total: {total}";
    }

    protected override bool Validar()
    {
        _grid.EndEdit();
        var variantes = new List<VarianteInventario>();
        foreach (var (desc, cant) in Filas())
        {
            if (desc.Length == 0) { Ui.Info($"Falta la descripción de la fila con cantidad {cant}."); return false; }
            if (!int.TryParse(cant, out int n) || n < 0) { Ui.Info($"La cantidad de \"{desc}\" no es válida."); return false; }
            variantes.Add(new VarianteInventario { Descripcion = desc, Cantidad = n });
        }
        return IntentarGuardar(() => _guardar(variantes));
    }
}

/// <summary>Pide un texto (por ejemplo, el motivo de una anulación o las notas).</summary>
internal sealed class TextoDialog : DialogoBase
{
    private readonly TextBox _texto = Ui.Texto(380, multilinea: true);
    private readonly bool _obligatorio;

    public string Valor => _texto.Text.Trim();

    public TextoDialog(string titulo, string etiqueta, bool obligatorio = true, string? valorInicial = null) : base(titulo)
    {
        _obligatorio = obligatorio;
        _texto.Text = valorInicial ?? "";
        _texto.Height = 80;
        Campo(etiqueta, _texto);
    }

    protected override bool Validar()
    {
        if (_obligatorio && Valor.Length == 0)
        {
            Ui.Info("Este dato es obligatorio.");
            return false;
        }
        return true;
    }
}

/// <summary>Pide una fecha (por ejemplo, la fecha de devolución).</summary>
internal sealed class FechaDialog : DialogoBase
{
    private readonly DateTimePicker _fecha = Ui.Fecha();
    public DateTime Valor => _fecha.Value.Date;

    public FechaDialog(string titulo, string etiqueta) : base(titulo)
    {
        _fecha.Value = DateTime.Today;
        Campo(etiqueta, _fecha);
    }
}
