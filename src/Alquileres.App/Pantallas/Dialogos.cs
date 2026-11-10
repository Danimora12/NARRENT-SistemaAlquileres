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

    protected DialogoBase(string titulo)
    {
        Text = titulo;
        Font = Ui.Fuente;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ShowInTaskbar = false;

        var aceptar = Ui.Boton("Aceptar", (_, _) => { if (Validar()) { DialogResult = DialogResult.OK; Close(); } }, principal: true);
        var cancelar = Ui.Boton("Cancelar", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
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

    /// <summary>Devuelve false para impedir que se cierre la ventana.</summary>
    protected virtual bool Validar() => true;
}

internal sealed class ClienteDialog : DialogoBase
{
    private readonly TextBox _nombre = Ui.Texto(320);
    private readonly TextBox _identificacion = Ui.Texto(200);
    private readonly TextBox _telefono = Ui.Texto(200);
    private readonly TextBox _correo = Ui.Texto(320);
    private readonly TextBox _notas = Ui.Texto(320, multilinea: true);

    public Cliente Cliente { get; }

    public ClienteDialog(Cliente cliente) : base(cliente.Id == 0 ? "Nuevo cliente" : "Editar cliente")
    {
        Cliente = cliente;
        _nombre.Text = cliente.Nombre;
        _identificacion.Text = cliente.Identificacion;
        _telefono.Text = cliente.Telefono;
        _correo.Text = cliente.Correo;
        _notas.Text = cliente.Notas;

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
        Cliente.Nombre = _nombre.Text.Trim();
        Cliente.Identificacion = _identificacion.Text;
        Cliente.Telefono = _telefono.Text;
        Cliente.Correo = _correo.Text;
        Cliente.Notas = _notas.Text;
        return true;
    }
}

internal sealed class ProductoDialog : DialogoBase
{
    private readonly TextBox _nombre = Ui.Texto(300);
    private readonly NumericUpDown _precio = Ui.Monto();
    private readonly Label _desglose = Ui.Etiqueta("");
    private readonly CheckBox _activo = new() { Text = "Disponible para nuevas órdenes", AutoSize = true, Font = Ui.Fuente };

    public Producto Producto { get; }

    public ProductoDialog(Producto producto) : base(producto.Id == 0 ? "Nuevo producto" : "Editar producto")
    {
        Producto = producto;
        _nombre.Text = producto.Nombre;
        _precio.Value = Math.Min(producto.PrecioConIva, _precio.Maximum);
        _activo.Checked = producto.Activo;
        _desglose.ForeColor = Color.DimGray;

        Campo("Nombre *", _nombre);
        Campo("Precio con IVA incluido (₡)", _precio);
        Campo("", _desglose);
        Campo("", _activo);

        _precio.ValueChanged += (_, _) => ActualizarDesglose();
        ActualizarDesglose();
    }

    private void ActualizarDesglose()
    {
        var r = CalculadoraIva.DesglosarTotal(Dinero.ACentimos(_precio.Value));
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
        Producto.Nombre = _nombre.Text.Trim();
        Producto.PrecioConIva = _precio.Value;
        Producto.Activo = _activo.Checked;
        return true;
    }
}

/// <summary>Pide un texto (por ejemplo, el motivo de una anulación).</summary>
internal sealed class TextoDialog : DialogoBase
{
    private readonly TextBox _texto = Ui.Texto(380, multilinea: true);
    private readonly bool _obligatorio;

    public string Valor => _texto.Text.Trim();

    public TextoDialog(string titulo, string etiqueta, bool obligatorio = true) : base(titulo)
    {
        _obligatorio = obligatorio;
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

/// <summary>Detalle de una orden (solo lectura).</summary>
internal sealed class OrdenDetalleForm : Form
{
    public OrdenDetalleForm(Orden o)
    {
        Text = $"Orden N° {o.Numero}";
        Font = Ui.Fuente;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 520);
        MinimizeBox = false;
        ShowInTaskbar = false;

        var info = new TableLayoutPanel { AutoSize = true, ColumnCount = 4 };
        void Dato(string etiqueta, string valor, int col, int fila)
        {
            info.Controls.Add(Ui.Etiqueta(etiqueta, Ui.FuenteNegrita), col, fila);
            info.Controls.Add(Ui.Etiqueta(valor), col + 1, fila);
        }
        Dato("Cliente:", o.ClienteNombre, 0, 0);
        Dato("Estado:", o.Estado.ToString(), 2, 0);
        Dato("Fecha de la orden:", Ui.Fmt(o.Fecha), 0, 1);
        Dato("Inicio / devolución:", $"{Ui.Fmt(o.FechaInicio)} – {(o.FechaFin is null ? "sin definir" : Ui.Fmt(o.FechaFin))}", 2, 1);
        if (!string.IsNullOrWhiteSpace(o.Notas)) Dato("Notas:", o.Notas!, 0, 2);
        if (o.Estado == EstadoOrden.Anulada) Dato("Motivo anulación:", o.MotivoAnulacion ?? "", 2, 2);

        var grid = Ui.Grid();
        Ui.Columna(grid, "Producto", 200);
        Ui.Columna(grid, "Precio unit.", 110, derecha: true);
        Ui.Columna(grid, "Cant.", 50, derecha: true);
        Ui.Columna(grid, "Días", 50, derecha: true);
        Ui.Columna(grid, "Total", 110, derecha: true);
        Ui.Columna(grid, "Base", 110, derecha: true);
        Ui.Columna(grid, "IVA", 100, derecha: true);
        foreach (var l in o.Lineas)
            grid.Rows.Add(l.ProductoNombre, Ui.Fmt(l.PrecioUnitarioCentimos), l.Cantidad, l.Dias,
                Ui.Fmt(l.TotalCentimos), Ui.Fmt(l.BaseCentimos), Ui.Fmt(l.IvaCentimos));

        var totales = Ui.Fila(
            Ui.Etiqueta($"Base: {Ui.Fmt(o.BaseCentimos)}"),
            Ui.Etiqueta($"   IVA: {Ui.Fmt(o.IvaCentimos)}"),
            Ui.Etiqueta($"   TOTAL: {Ui.Fmt(o.TotalCentimos)}", Ui.FuenteTotal));

        Controls.Add(Ui.Pila(info, grid, totales));
    }
}
