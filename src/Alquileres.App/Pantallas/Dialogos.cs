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

    /// <summary>
    /// Oculta lo que se escribe en los campos y agrega una casilla para mostrarlo.
    /// </summary>
    protected void CasillaMostrar(string texto, params TextBox?[] campos)
    {
        var casilla = new CheckBox { Text = texto, AutoSize = true, Font = Ui.Fuente };
        void Aplicar() { foreach (var c in campos) if (c is not null) c.UseSystemPasswordChar = !casilla.Checked; }
        casilla.CheckedChanged += (_, _) => Aplicar();
        Aplicar();
        Campo("", casilla);
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
    private readonly ComboBox _tipo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Ui.Fuente };
    private readonly TextBox _identificacion = Ui.Texto(200);
    private readonly Label _ayudaIdentificacion = Ui.Etiqueta("");
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
        _tipo.Items.AddRange(Identificaciones.Todos.Select(Identificaciones.Nombre).Cast<object>().ToArray());
        _ayudaIdentificacion.ForeColor = Color.DimGray;
        _ayudaIdentificacion.MaximumSize = new Size(340, 0);

        _nombre.Text = cliente.Nombre;
        if (cliente.TipoIdentificacion is { } t) _tipo.SelectedIndex = Array.IndexOf(Identificaciones.Todos, t);
        _identificacion.Text = cliente.Identificacion;
        _telefono.Text = cliente.Telefono;
        _correo.Text = cliente.Correo;
        _notas.Text = cliente.Notas;

        if (empleado is not null) Bloque(Explicacion($"Cambio autorizado por {empleado}. Quedará registrado en Logs."));
        if (cliente.Id != 0 && cliente.TipoIdentificacion is null)
            Bloque(Explicacion("Este cliente se registró antes de que la identificación fuera obligatoria: seleccione el tipo y revise el número."));
        Campo("Nombre *", _nombre);
        Campo("Tipo de identificación *", _tipo);
        Campo("Número de identificación *", _identificacion);
        Campo("", _ayudaIdentificacion);
        Campo("Teléfono", _telefono);
        Campo("Correo", _correo);
        Campo("Notas", _notas);

        _tipo.SelectedIndexChanged += (_, _) => AplicarTipo();
        _identificacion.KeyPress += FiltrarTecla;
        _identificacion.TextChanged += (_, _) => ActualizarAyuda();
        AplicarTipo();
    }

    private TipoIdentificacion? Tipo => _tipo.SelectedIndex >= 0 ? Identificaciones.Todos[_tipo.SelectedIndex] : null;

    /// <summary>Ajusta el largo máximo y limpia lo que no corresponda al tipo (por ejemplo, al pegar).</summary>
    private void AplicarTipo()
    {
        _identificacion.Enabled = Tipo is not null;
        if (Tipo is { } t)
        {
            string limpio = new string(Identificaciones.Normalizar(_identificacion.Text)
                .Where(ch => char.IsDigit(ch) || (Identificaciones.AceptaLetras(t) && ch is >= 'A' and <= 'Z')).ToArray());
            _identificacion.MaxLength = Identificaciones.Largo(t);
            _identificacion.Text = limpio.Length > _identificacion.MaxLength ? limpio[.._identificacion.MaxLength] : limpio;
            _identificacion.CharacterCasing = CharacterCasing.Upper;
        }
        ActualizarAyuda();
    }

    /// <summary>Solo deja escribir números (y letras en jurídica y pasaporte). Guiones y espacios no hacen falta.</summary>
    private void FiltrarTecla(object? s, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || Tipo is not { } t) return;
        char c = char.ToUpperInvariant(e.KeyChar);
        bool letraValida = Identificaciones.AceptaLetras(t) && c is >= 'A' and <= 'Z';
        if (!char.IsDigit(c) && !letraValida) e.Handled = true;
    }

    private void ActualizarAyuda()
    {
        if (Tipo is not { } t)
        {
            _ayudaIdentificacion.ForeColor = Color.DimGray;
            _ayudaIdentificacion.Text = "Seleccione primero el tipo de identificación.";
            return;
        }
        string n = Identificaciones.Normalizar(_identificacion.Text);
        bool valida = Identificaciones.EsValida(t, n);
        _ayudaIdentificacion.ForeColor = n.Length == 0 || valida ? Color.DimGray : Color.Firebrick;
        _ayudaIdentificacion.Text = valida
            ? $"✓ {Identificaciones.Formatear(t, n)}"
            : Identificaciones.Ayuda(t);
    }

    protected override bool Validar()
    {
        if (string.IsNullOrWhiteSpace(_nombre.Text))
        {
            Ui.Info("El nombre es obligatorio.");
            _nombre.Focus();
            return false;
        }
        if (Tipo is null)
        {
            Ui.Info("Seleccione el tipo de identificación.");
            _tipo.Focus();
            return false;
        }
        var c = new Cliente
        {
            Id = Cliente.Id,
            Nombre = _nombre.Text.Trim(),
            TipoIdentificacion = Tipo,
            Identificacion = _identificacion.Text,
            Telefono = _telefono.Text,
            Correo = _correo.Text,
            Notas = _notas.Text
        };
        if (!IntentarGuardar(() => _guardar(c))) return false;
        Cliente.Id = c.Id;
        Cliente.Nombre = c.Nombre;
        Cliente.TipoIdentificacion = c.TipoIdentificacion;
        Cliente.Identificacion = c.Identificacion;
        Cliente.Telefono = c.Telefono;
        Cliente.Correo = c.Correo;
        Cliente.Notas = c.Notas;
        return true;
    }
}

internal sealed class ProductoDialog : DialogoBase
{
    private readonly TextBox _codigo = Ui.Texto(140);
    private readonly TextBox _nombre = Ui.Texto(300);
    private readonly CampoMonto _precio = new();
    private readonly Label _desglose = Ui.Etiqueta("");
    private readonly CampoEntero _inventario = new(0, 1_000_000, 0, anchoTexto: 80);
    private readonly CheckBox _activo = new() { Text = "Disponible para nuevas órdenes", AutoSize = true, Font = Ui.Fuente };
    private readonly Producto _producto;
    private readonly Action<Producto> _guardar;
    private readonly Func<string, string> _sugerirCodigo;
    private readonly bool _esAdmin;
    private bool _codigoEditado;

    /// <param name="sugerirCodigo">Código automático para un nombre (solo productos nuevos).</param>
    /// <param name="esAdmin">Solo un administrador puede escribir el código a mano.</param>
    public ProductoDialog(Producto producto, Action<Producto> guardar, string empleado, Func<string, string> sugerirCodigo, bool esAdmin)
        : base(producto.Id == 0 ? "Nuevo producto" : "Editar producto", "Guardar")
    {
        _producto = producto;
        _guardar = guardar;
        _sugerirCodigo = sugerirCodigo;
        _esAdmin = esAdmin;
        _codigo.Text = producto.Codigo;
        _codigo.CharacterCasing = CharacterCasing.Upper;
        _codigo.MaxLength = 15;
        _codigo.ReadOnly = !esAdmin;
        _nombre.Text = producto.Nombre;
        _precio.Centimos = producto.PrecioConIvaCentimos;
        _inventario.Valor = producto.Inventario;
        _activo.Checked = producto.Activo;
        _desglose.ForeColor = Color.DimGray;

        Bloque(Explicacion($"Cambio autorizado por {empleado}. Quedará registrado en Logs."));
        Campo("Código", Ui.Fila(_codigo, Explicacion(esAdmin
            ? "Se genera con las 3 primeras letras del nombre + un consecutivo. Como administrador puede cambiarlo."
            : "Se genera con las 3 primeras letras del nombre + un consecutivo. Solo un administrador puede cambiarlo.", 300)));
        Campo("Nombre *", _nombre);
        Campo("Precio con IVA incluido (₡)", _precio);
        Campo("", _desglose);
        Campo("Inventario (cantidad)", _inventario);
        Campo("", _activo);

        _precio.TextChanged += (_, _) => ActualizarDesglose();
        _codigo.KeyPress += (_, _) => _codigoEditado = true;
        if (producto.Id == 0)
            _nombre.TextChanged += (_, _) =>
            {
                if (_codigoEditado) return;
                _codigo.Text = string.IsNullOrWhiteSpace(_nombre.Text) ? "" : Probar(() => _sugerirCodigo(_nombre.Text));
            };
        ActualizarDesglose();
    }

    private static string Probar(Func<string> f) { try { return f(); } catch { return ""; } }

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
        // Si nadie escribió el código, se manda vacío (nuevo) o el mismo (edición) y lo resuelve el sistema.
        string codigo = _esAdmin && _codigoEditado ? _codigo.Text : _producto.Codigo;
        var p = new Producto
        {
            Id = _producto.Id,
            Codigo = codigo,
            Nombre = _nombre.Text.Trim(),
            PrecioConIvaCentimos = _precio.Centimos,
            Inventario = _inventario.Valor,
            Activo = _activo.Checked
        };
        return IntentarGuardar(() => _guardar(p));
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
