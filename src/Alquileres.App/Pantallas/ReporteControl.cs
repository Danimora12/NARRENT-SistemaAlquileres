using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class ReporteControl : UserControl, IRefrescable
{
    private readonly ReporteServicio _reporte;
    private readonly NumericUpDown _anio = Ui.Entero(2000, 2100, DateTime.Today.Year);
    private readonly DataGridView _gridMeses = Ui.Grid();
    private readonly DataGridView _gridDetalle = Ui.Grid();
    private readonly Label _totalAnio = Ui.Etiqueta("", Ui.FuenteNegrita);
    private readonly Label _tituloDetalle = Ui.Etiqueta("Detalle del mes", Ui.FuenteNegrita);

    public ReporteControl(BaseDatos db)
    {
        _reporte = new ReporteServicio(db);

        Ui.Columna(_gridMeses, "Mes", 120);
        Ui.Columna(_gridMeses, "Órdenes", 70, derecha: true);
        Ui.Columna(_gridMeses, "Total cobrado", 130, derecha: true);
        Ui.Columna(_gridMeses, "Base imponible", 130, derecha: true);
        Ui.Columna(_gridMeses, "IVA cobrado 13 %", 130, derecha: true);
        _gridMeses.SelectionChanged += (_, _) => Ui.Intentar(MostrarDetalle);

        Ui.Columna(_gridDetalle, "N°", 60);
        Ui.Columna(_gridDetalle, "Fecha", 80);
        Ui.Columna(_gridDetalle, "Cliente", 220);
        Ui.Columna(_gridDetalle, "Estado", 80);
        Ui.Columna(_gridDetalle, "Total", 110, derecha: true);
        Ui.Columna(_gridDetalle, "Base", 110, derecha: true);
        Ui.Columna(_gridDetalle, "IVA", 100, derecha: true);

        _anio.ValueChanged += (_, _) => Ui.Intentar(Refrescar);

        var barra = Ui.Fila(
            Ui.Etiqueta("Año:"), _anio,
            Ui.Boton("Exportar mes seleccionado a Excel…", (_, _) => ExportarMes(), principal: true),
            Ui.Boton("Exportar año a Excel…", (_, _) => ExportarAnio()));

        var nota = Ui.Etiqueta(
            "El monto es IVA COBRADO (débito fiscal). El IVA a pagar en el formulario 150 de TRIBU-CR es este monto menos el IVA de las compras del negocio (crédito fiscal). Las órdenes anuladas no se cuentan.");
        nota.ForeColor = Color.DimGray;
        nota.MaximumSize = new Size(1100, 0);

        var division = new SplitContainer { Orientation = Orientation.Horizontal };
        bool ajustado = false;
        division.SizeChanged += (_, _) =>
        {
            // Se ajusta cuando ya tiene tamaño real (asignarlo antes puede lanzar error).
            if (ajustado || division.Height < 200) return;
            ajustado = true;
            division.SplitterDistance = (int)(division.Height * 0.55);
        };
        division.Panel1.Controls.Add(Ui.Pila(_gridMeses, _totalAnio));
        division.Panel2.Controls.Add(Ui.Pila(_tituloDetalle, _gridDetalle));

        Controls.Add(Ui.Pila(barra, nota, division));
    }

    private int Anio => (int)_anio.Value;
    private int? MesSeleccionado => (Ui.Seleccionado<ResumenMensual>(_gridMeses))?.Mes;

    public void Refrescar()
    {
        int mesAntes = MesSeleccionado ?? (Anio == DateTime.Today.Year ? DateTime.Today.Month : 1);
        var meses = _reporte.ResumenAnual(Anio);

        _gridMeses.Rows.Clear();
        foreach (var m in meses)
        {
            int i = _gridMeses.Rows.Add(m.NombreMes, m.CantidadOrdenes, Ui.Fmt(m.TotalCentimos),
                Ui.Fmt(m.BaseCentimos), Ui.Fmt(m.IvaCentimos));
            _gridMeses.Rows[i].Tag = m;
            if (m.CantidadOrdenes == 0) _gridMeses.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
        }
        _totalAnio.Text = $"Total {Anio}:  {meses.Sum(m => m.CantidadOrdenes)} órdenes · " +
                          $"Cobrado {Ui.Fmt(meses.Sum(m => m.TotalCentimos))} · " +
                          $"Base {Ui.Fmt(meses.Sum(m => m.BaseCentimos))} · " +
                          $"IVA {Ui.Fmt(meses.Sum(m => m.IvaCentimos))}";

        _gridMeses.ClearSelection();
        _gridMeses.CurrentCell = _gridMeses.Rows[mesAntes - 1].Cells[0];
        MostrarDetalle();
    }

    private void MostrarDetalle()
    {
        _gridDetalle.Rows.Clear();
        if (MesSeleccionado is not { } mes) return;

        _tituloDetalle.Text = $"Detalle de {Dinero.NombreMes(mes)} {Anio}";
        foreach (var o in _reporte.DetalleMes(Anio, mes))
            _gridDetalle.Rows.Add(o.Numero, Ui.Fmt(o.Fecha), o.ClienteNombre, o.Estado,
                Ui.Fmt(o.TotalCentimos), Ui.Fmt(o.BaseCentimos), Ui.Fmt(o.IvaCentimos));
    }

    private static string? PedirRuta(string nombreSugerido)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Libro de Excel (*.xlsx)|*.xlsx",
            FileName = nombreSugerido,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }

    private void ExportarMes()
    {
        if (MesSeleccionado is not { } mes)
        {
            Ui.Info("Seleccione un mes en la tabla.");
            return;
        }
        if (PedirRuta($"IVA_{Anio}-{mes:D2}_{Dinero.NombreMes(mes)}.xlsx") is not { } ruta) return;
        Ui.Intentar(() =>
        {
            _reporte.ExportarMesExcel(Anio, mes, ruta);
            Ui.Info("Reporte exportado:\n" + ruta);
        });
    }

    private void ExportarAnio()
    {
        if (PedirRuta($"IVA_{Anio}_resumen_anual.xlsx") is not { } ruta) return;
        Ui.Intentar(() =>
        {
            _reporte.ExportarAnioExcel(Anio, ruta);
            Ui.Info("Reporte exportado:\n" + ruta);
        });
    }
}
