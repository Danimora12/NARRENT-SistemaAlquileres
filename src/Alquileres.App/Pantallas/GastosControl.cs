using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Registro de gastos de la empresa. Alimenta la columna Utilidad del Flujo de caja.</summary>
internal sealed class GastosControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly GastoRepositorio _gastos;

    private readonly DateTimePicker _desde = Ui.Fecha();
    private readonly DateTimePicker _hasta = Ui.Fecha();
    private readonly ComboBox _categoria = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Font = Ui.Fuente };
    private readonly TextBox _buscar = Ui.Texto(180);
    private readonly DataGridView _grid = Ui.Grid();
    private readonly Label _resumen = Ui.Etiqueta("", Ui.FuenteNegrita);

    public GastosControl(BaseDatos db)
    {
        _db = db;
        _gastos = new GastoRepositorio(db);

        var hoy = DateTime.Today;
        _desde.Value = new DateTime(hoy.Year, hoy.Month, 1);
        _hasta.Value = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(1).AddDays(-1);

        Ui.Columna(_grid, "Fecha", 80);
        Ui.Columna(_grid, "Categoría", 170);
        Ui.Columna(_grid, "Detalle", 330);
        Ui.Columna(_grid, "Método", 100);
        Ui.Columna(_grid, "Monto", 110, derecha: true);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Editar(); };
        _buscar.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Ui.Intentar(Refrescar); } };

        var filtros = Ui.Fila(
            Ui.Etiqueta("Desde:"), _desde,
            Ui.Etiqueta("Hasta:"), _hasta,
            Ui.Etiqueta("Categoría:"), _categoria,
            Ui.Etiqueta("Buscar:"), _buscar,
            Ui.Boton("Buscar", (_, _) => Ui.Intentar(Refrescar), principal: true));

        var ayuda = Ui.Etiqueta(
            "Registre aquí todos los gastos de la empresa; el monto pagado resta en la utilidad del Flujo de caja. " +
            "La categoría la escribe usted: el sistema sugiere las que ya usó. Editar o eliminar un gasto pide el código de confirmación.");
        ayuda.ForeColor = Color.DimGray;
        ayuda.MaximumSize = new Size(1100, 0);

        var acciones = Ui.Fila(
            Ui.Boton("Nuevo gasto…", (_, _) => Nuevo(), principal: true),
            Ui.Boton("Editar…", (_, _) => Editar()),
            Ui.Boton("Eliminar…", (_, _) => Eliminar()));

        Controls.Add(Ui.Pila(filtros, ayuda, _grid, acciones, _resumen));
    }

    public void Refrescar()
    {
        if (_hasta.Value.Date < _desde.Value.Date)
            throw new ReglaNegocioException("La fecha \"hasta\" no puede ser anterior a \"desde\".");

        string? categoriaSel = _categoria.SelectedIndex > 0 ? _categoria.SelectedItem as string : null;
        _categoria.Items.Clear();
        _categoria.Items.Add("Todas");
        _categoria.Items.AddRange(_gastos.Categorias().Cast<object>().ToArray());
        _categoria.SelectedItem = categoriaSel is not null && _categoria.Items.Contains(categoriaSel) ? categoriaSel : "Todas";

        var lista = _gastos.Listar(_desde.Value.Date, _hasta.Value.Date, categoriaSel, _buscar.Text);
        _grid.Rows.Clear();
        foreach (var g in lista)
        {
            int i = _grid.Rows.Add(Ui.Fmt(g.Fecha), g.Categoria, g.Detalle, g.MetodoTexto, Ui.Fmt(g.MontoCentimos));
            _grid.Rows[i].Tag = g;
        }
        _resumen.Text = $"{lista.Count} gastos · Total pagado {Ui.Fmt(lista.Sum(g => g.MontoCentimos))}";
    }

    private Gasto? Seleccionado()
    {
        var g = Ui.Seleccionado<Gasto>(_grid);
        if (g is null) Ui.Info("Seleccione un gasto de la lista.");
        return g;
    }

    private void Nuevo()
    {
        using var dlg = new GastoDialog(new Gasto(), _gastos.Categorias(), g => _gastos.Guardar(g), empleado: null);
        if (dlg.ShowDialog(this) == DialogResult.OK) Ui.Intentar(Refrescar);
    }

    private void Editar()
    {
        if (Seleccionado() is not { } g) return;
        if (Autorizacion.Empleado(this, _db, $"Editar el gasto \"{g.Detalle}\" ({Ui.Fmt(g.MontoCentimos)})") is not { } empleado) return;
        using var dlg = new GastoDialog(g, _gastos.Categorias(), x => _gastos.Guardar(x, empleado), empleado);
        if (dlg.ShowDialog(this) == DialogResult.OK) Ui.Intentar(Refrescar);
    }

    private void Eliminar()
    {
        if (Seleccionado() is not { } g) return;
        if (Autorizacion.Empleado(this, _db, $"Eliminar el gasto \"{g.Detalle}\" del {Ui.Fmt(g.Fecha)} ({Ui.Fmt(g.MontoCentimos)})",
                "El gasto se borra y deja de restar en la utilidad. Queda constancia en Logs.") is not { } empleado) return;
        Ui.Intentar(() =>
        {
            _gastos.Eliminar(g.Id, empleado);
            Refrescar();
        });
    }
}

internal sealed class GastoDialog : DialogoBase
{
    private readonly DateTimePicker _fecha = Ui.Fecha();
    private readonly ComboBox _categoria = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 300, Font = Ui.Fuente };
    private readonly TextBox _detalle = Ui.Texto(340);
    private readonly ComboBox _metodo = PagoUi.ComboMetodo();
    private readonly CampoMonto _monto = new();
    private readonly Gasto _gasto;
    private readonly Action<Gasto> _guardar;

    public GastoDialog(Gasto gasto, List<string> categorias, Action<Gasto> guardar, string? empleado)
        : base(gasto.Id == 0 ? "Nuevo gasto" : "Editar gasto", "Guardar")
    {
        _gasto = gasto;
        _guardar = guardar;
        _categoria.Items.AddRange(categorias.Cast<object>().ToArray());
        _categoria.AutoCompleteSource = AutoCompleteSource.ListItems;
        _categoria.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _categoria.MaxLength = 60;

        _fecha.Value = gasto.Fecha;
        _categoria.Text = gasto.Categoria;
        _detalle.Text = gasto.Detalle;
        _metodo.SelectedIndex = Array.IndexOf(MetodosPago.Todos, gasto.Metodo);
        _monto.Centimos = gasto.MontoCentimos;

        if (empleado is not null) Bloque(Explicacion($"Cambio autorizado por {empleado}. Quedará registrado en Logs."));
        Campo("Fecha *", _fecha);
        Campo("Categoría *", _categoria);
        Campo("", Explicacion("Escriba la categoría o elija una que ya usó.", 300));
        Campo("Detalle *", _detalle);
        Campo("Método de pago", _metodo);
        Campo("Monto pagado (₡) *", _monto);
    }

    protected override bool Validar()
    {
        var g = new Gasto
        {
            Id = _gasto.Id,
            Fecha = _fecha.Value.Date,
            Categoria = _categoria.Text.Trim(),
            Detalle = _detalle.Text.Trim(),
            Metodo = PagoUi.Metodo(_metodo),
            MontoCentimos = _monto.Centimos
        };
        return IntentarGuardar(() => _guardar(g));
    }
}
