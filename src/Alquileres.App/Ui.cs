using Alquileres.Core;

namespace Alquileres.App;

/// <summary>Ayudantes para construir pantallas con un estilo uniforme.</summary>
internal static class Ui
{
    public const string Titulo = "NARRENT";
    public static readonly Font Fuente = new("Segoe UI", 10f);
    public static readonly Font FuenteNegrita = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font FuenteTotal = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Color Primario = Color.FromArgb(31, 78, 121);

    public static void MostrarError(Exception ex)
    {
        if (ex is ReglaNegocioException)
            MessageBox.Show(ex.Message, Titulo, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        else
            MessageBox.Show("Ocurrió un error inesperado:\n\n" + ex.Message, Titulo,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public static void Info(string mensaje) =>
        MessageBox.Show(mensaje, Titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirmar(string mensaje) =>
        MessageBox.Show(mensaje, Titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    /// <summary>Ejecuta una acción mostrando los errores en pantalla en lugar de cerrar el programa.</summary>
    public static void Intentar(Action accion)
    {
        try { accion(); }
        catch (Exception ex) { MostrarError(ex); }
    }

    public static Label Etiqueta(string texto, Font? fuente = null) => new()
    {
        Text = texto,
        AutoSize = true,
        Font = fuente ?? Fuente,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 8, 3, 3)
    };

    public static Button Boton(string texto, EventHandler alHacerClic, bool principal = false)
    {
        var b = new Button
        {
            Text = texto,
            AutoSize = true,
            Font = principal ? FuenteNegrita : Fuente,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(3, 3, 6, 3)
        };
        if (principal)
        {
            b.BackColor = Primario;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
        }
        b.Click += alHacerClic;
        return b;
    }

    public static NumericUpDown Monto(decimal maximo = 100_000_000m) => new()
    {
        DecimalPlaces = 2,
        Maximum = maximo,
        Minimum = 0,
        ThousandsSeparator = true,
        Width = 140,
        Font = Fuente,
        TextAlign = HorizontalAlignment.Right
    };

    public static NumericUpDown Entero(int minimo, int maximo, int valor = 1) => new()
    {
        Minimum = minimo,
        Maximum = maximo,
        Value = valor,
        Width = 70,
        Font = Fuente,
        TextAlign = HorizontalAlignment.Right
    };

    public static DateTimePicker Fecha(bool opcional = false) => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "dd/MM/yyyy",
        Width = 130,
        Font = Fuente,
        ShowCheckBox = opcional,
        Checked = !opcional
    };

    public static TextBox Texto(int ancho = 250, bool multilinea = false) => new()
    {
        Width = ancho,
        Font = Fuente,
        Multiline = multilinea,
        Height = multilinea ? 60 : 27
    };

    /// <summary>Recuadro con título y filas apiladas (se ajusta a su contenido).</summary>
    public static GroupBox Grupo(string titulo, params Control[] filas)
    {
        var g = new GroupBox { Text = titulo, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Font = FuenteNegrita, Padding = new Padding(8) };
        var pila = new FlowLayoutPanel
        {
            Location = new Point(8, 24),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Font = Fuente
        };
        pila.Controls.AddRange(filas);
        g.Controls.Add(pila);
        return g;
    }

    public static FlowLayoutPanel Fila(params Control[] controles)
    {
        var f = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(4)
        };
        f.Controls.AddRange(controles);
        return f;
    }

    /// <summary>
    /// Apila controles verticalmente. Las grillas (y SplitContainer) se reparten el
    /// espacio sobrante; el resto ocupa solo lo que necesita.
    /// </summary>
    public static TableLayoutPanel Pila(params Control[] filas)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = filas.Length,
            Padding = new Padding(8)
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < filas.Length; i++)
        {
            bool expandir = filas[i] is DataGridView or SplitContainer;
            t.RowStyles.Add(expandir ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            filas[i].Dock = DockStyle.Fill;
            t.Controls.Add(filas[i], 0, i);
        }
        return t;
    }

    public static DataGridView Grid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            Font = Fuente,
            BorderStyle = BorderStyle.FixedSingle
        };
        g.ColumnHeadersDefaultCellStyle.Font = FuenteNegrita;
        g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 252);
        g.EnableHeadersVisualStyles = true;
        return g;
    }

    public static void Columna(DataGridView g, string encabezado, int peso = 100, bool derecha = false)
    {
        int i = g.Columns.Add("c" + g.Columns.Count, encabezado);
        g.Columns[i].FillWeight = peso;
        g.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
        if (derecha) g.Columns[i].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    /// <summary>Objeto asociado a la fila seleccionada (se guarda en Row.Tag).</summary>
    public static T? Seleccionado<T>(DataGridView g) where T : class =>
        g.CurrentRow?.Tag as T;

    public static string Fmt(long centimos) => Dinero.Formatear(centimos);
    public static string Fmt(DateTime d) => d.ToString("dd/MM/yyyy");
    public static string Fmt(DateTime? d) => d is { } x ? Fmt(x) : "";
}

/// <summary>Pantallas que deben recargar datos al mostrarse.</summary>
internal interface IRefrescable
{
    void Refrescar();
}
