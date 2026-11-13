using System.ComponentModel;
using System.Globalization;

namespace Alquileres.App;

/// <summary>
/// Número entero con botones − y + a los lados (en lugar de las flechitas del
/// NumericUpDown). También acepta escribir el número y las teclas ↑ ↓.
/// </summary>
internal sealed class CampoEntero : FlowLayoutPanel
{
    private readonly TextBox _texto;
    private readonly Button _menos;
    private readonly Button _mas;
    private int _valor;

    public int Minimo { get; }
    public int Maximo { get; }

    public event EventHandler? ValorCambiado;

    public CampoEntero(int minimo, int maximo, int valor = 1, int anchoTexto = 52)
    {
        Minimo = minimo;
        Maximo = maximo;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = new Padding(3, 3, 6, 3);
        Padding = Padding.Empty;
        Font = Ui.Fuente;

        _menos = BotonPaso("−", -1);
        _mas = BotonPaso("+", +1);
        _texto = new TextBox
        {
            Width = anchoTexto,
            TextAlign = HorizontalAlignment.Center,
            Font = Ui.Fuente,
            Margin = new Padding(2, 2, 2, 0),
            MaxLength = maximo.ToString().Length
        };
        _texto.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
        _texto.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Up) { Valor++; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Valor--; e.Handled = true; }
        };
        _texto.TextChanged += (_, _) =>
        {
            if (int.TryParse(_texto.Text, out int v) && v >= Minimo && v <= Maximo) Asignar(v, reescribir: false);
        };
        _texto.Leave += (_, _) => Valor = int.TryParse(_texto.Text, out int v) ? v : _valor;
        _texto.Enter += (_, _) => BeginInvoke(_texto.SelectAll);

        Controls.AddRange(new Control[] { _menos, _texto, _mas });

        _valor = Math.Clamp(valor, minimo, maximo);
        _texto.Text = _valor.ToString();
        ActualizarBotones();
    }

    private Button BotonPaso(string texto, int delta)
    {
        var b = new Button
        {
            Text = texto,
            Size = new Size(30, 27),
            Font = Ui.FuenteNegrita,
            Margin = new Padding(0, 1, 0, 0),
            TabStop = false
        };
        b.Click += (_, _) => { Valor += delta; _texto.Focus(); };
        return b;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Valor
    {
        get => _valor;
        set => Asignar(Math.Clamp(value, Minimo, Maximo), reescribir: true);
    }

    private void Asignar(int v, bool reescribir)
    {
        bool cambio = v != _valor;
        _valor = v;
        if (reescribir && _texto.Text != v.ToString()) _texto.Text = v.ToString();
        ActualizarBotones();
        if (cambio) ValorCambiado?.Invoke(this, EventArgs.Empty);
    }

    private void ActualizarBotones()
    {
        _menos.Enabled = Enabled && _valor > Minimo;
        _mas.Enabled = Enabled && _valor < Maximo;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ActualizarBotones();
    }
}

/// <summary>
/// Monto en colones enteros, sin decimales ni flechas. Solo acepta dígitos y
/// agrega el separador de miles mientras se escribe (12500 → 12.500).
/// </summary>
internal sealed class CampoMonto : TextBox
{
    private static readonly NumberFormatInfo Formato = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };
    private const int MaximoDigitos = 11; // hasta ₡99.999.999.999

    private bool _formateando;

    public CampoMonto(int ancho = 140)
    {
        Width = ancho;
        Font = Ui.Fuente;
        TextAlign = HorizontalAlignment.Right;
        Text = "0";
    }

    /// <summary>Monto en colones enteros.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long Colones
    {
        get => long.TryParse(Digitos(Text), out long v) ? v : 0;
        set => Text = Formatear(Math.Max(0, value));
    }

    /// <summary>Monto en céntimos (como se guarda en la base de datos).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long Centimos
    {
        get => Colones * 100;
        set => Colones = (long)Math.Round(value / 100m, 0, MidpointRounding.AwayFromZero);
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BeginInvoke(SelectAll);
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        if (Text.Length == 0) Text = "0";
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (_formateando) return;

        // Reformatea conservando la posición del cursor respecto de los dígitos a su derecha.
        int digitosALaDerecha = Text.Skip(SelectionStart).Count(char.IsDigit);
        string digitos = Digitos(Text);
        if (digitos.Length > MaximoDigitos) digitos = digitos[..MaximoDigitos];
        string nuevo = digitos.Length == 0 ? "" : Formatear(long.Parse(digitos));

        if (nuevo != Text)
        {
            _formateando = true;
            Text = nuevo;
            _formateando = false;

            int pos = nuevo.Length, vistos = 0;
            while (pos > 0 && vistos < digitosALaDerecha)
            {
                pos--;
                if (char.IsDigit(nuevo[pos])) vistos++;
            }
            SelectionStart = pos;
        }
        base.OnTextChanged(e);
    }

    private static string Digitos(string s) => new(s.Where(char.IsDigit).ToArray());
    private static string Formatear(long v) => v.ToString("#,0", Formato);
}
