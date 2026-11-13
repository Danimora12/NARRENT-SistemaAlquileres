using Alquileres.Core;

namespace Alquileres.App.Pantallas;

/// <summary>Flujo de caja mensual (antes "Reporte mensual (IVA)").</summary>
internal sealed class FlujoCajaControl : UserControl, IRefrescable
{
    private readonly ReporteServicio _reporte;
    private readonly NumericUpDown _anio = Ui.Entero(2000, 2100, DateTime.Today.Year);
    private readonly DataGridView _gridMeses = Ui.Grid();
    private readonly DataGridView _gridDetalle = Ui.Grid();
    private readonly Label _totalAnio = Ui.Etiqueta("", Ui.FuenteNegrita);
    private readonly Label _tituloDetalle = Ui.Etiqueta("Detalle del mes", Ui.FuenteNegrita);

    public FlujoCajaControl(BaseDatos db)
    {
        _reporte = new ReporteServicio(db);

        Ui.Columna(_gridMeses, "Mes", 120);
        Ui.Columna(_gridMeses, "Órdenes", 70, derecha: true);
        Ui.Columna(_gridMeses, "Ingresos brutos", 130, derecha: true);
        Ui.Columna(_gridMeses, "Ingresos netos", 130, derecha: true);
        Ui.Columna(_gridMeses, "Total IVA 13 %", 130, derecha: true);
        Ui.Columna(_gridMeses, "Gastos", 120, derecha: true);
        Ui.Columna(_gridMeses, "Utilidad", 130, derecha: true);
        Ui.Columna(_gridMeses, "IVA de gastos", 110, derecha: true);
        Ui.Columna(_gridMeses, "IVA neto a pagar", 130, derecha: true);
        _gridMeses.SelectionChanged += (_, _) => Ui.Intentar(MostrarDetalle);

        Ui.Columna(_gridDetalle, "N°", 85);
        Ui.Columna(_gridDetalle, "Fecha", 80);
        Ui.Columna(_gridDetalle, "Cliente", 220);
        Ui.Columna(_gridDetalle, "Estado", 80);
        Ui.Columna(_gridDetalle, "Ingresos brutos", 110, derecha: true);
        Ui.Columna(_gridDetalle, "Ingresos netos", 110, derecha: true);
        Ui.Columna(_gridDetalle, "IVA 13 %", 100, derecha: true);

        _anio.ValueChanged += (_, _) => Ui.Intentar(Refrescar);

        var barra = Ui.Fila(
            Ui.Etiqueta("Año:"), _anio,
            Ui.Boton("Exportar mes seleccionado a Excel…", (_, _) => ExportarMes(), principal: true),
            Ui.Boton("Exportar año a Excel…", (_, _) => ExportarAnio()));

        var nota = Ui.Etiqueta(
            "Ingresos brutos = lo cobrado con IVA incluido (ya con descuentos). Ingresos netos = sin IVA. El mes lo define la fecha de la orden. " +
            "El Total IVA es el IVA cobrado (débito fiscal): el IVA a pagar en el formulario 150 de TRIBU-CR es este monto menos el IVA de las compras del negocio. " +
            "No se cuentan órdenes anuladas ni eliminadas. Gastos = gastos del mes sin el IVA de sus facturas (pestaña Gastos). " +
            "Utilidad = ingresos netos − gastos, antes del impuesto sobre la renta. " +
            "IVA neto a pagar = Total IVA − IVA de las facturas de gastos (crédito fiscal); si es negativo, es saldo a favor. " +
            "Es una estimación: solo cuenta como crédito el IVA de facturas electrónicas a nombre del negocio. Confírmelo con el contador.");
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
                Ui.Fmt(m.BaseCentimos), Ui.Fmt(m.IvaCentimos), Ui.Fmt(m.GastosCentimos), FmtUtilidad(m.UtilidadCentimos),
                Ui.Fmt(m.IvaGastosCentimos), FmtIvaNeto(m.IvaNetoCentimos));
            _gridMeses.Rows[i].Tag = m;
            if (m.CantidadOrdenes == 0 && m.GastosCentimos == 0) _gridMeses.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
            var celdaUtilidad = _gridMeses.Rows[i].Cells[6];
            celdaUtilidad.Style.Font = Ui.FuenteNegrita;
            if (m.UtilidadCentimos < 0) celdaUtilidad.Style.ForeColor = Color.Firebrick;
            else if (m.UtilidadCentimos > 0) celdaUtilidad.Style.ForeColor = Color.ForestGreen;
            _gridMeses.Rows[i].Cells[8].Style.Font = Ui.FuenteNegrita;
        }
        _totalAnio.Text = $"Total {Anio}:  {meses.Sum(m => m.CantidadOrdenes)} órdenes · " +
                          $"Ingresos brutos {Ui.Fmt(meses.Sum(m => m.TotalCentimos))} · " +
                          $"Ingresos netos {Ui.Fmt(meses.Sum(m => m.BaseCentimos))} · " +
                          $"Total IVA {Ui.Fmt(meses.Sum(m => m.IvaCentimos))} · " +
                          $"Gastos {Ui.Fmt(meses.Sum(m => m.GastosCentimos))} · " +
                          $"Utilidad {FmtUtilidad(meses.Sum(m => m.UtilidadCentimos))} · " +
                          $"IVA neto {FmtIvaNeto(meses.Sum(m => m.IvaNetoCentimos))}";

        _gridMeses.ClearSelection();
        _gridMeses.CurrentCell = _gridMeses.Rows[mesAntes - 1].Cells[0];
        MostrarDetalle();
    }

    private static string FmtUtilidad(long centimos) => centimos < 0 ? "−" + Ui.Fmt(-centimos) : Ui.Fmt(centimos);
    private static string FmtIvaNeto(long centimos) => centimos < 0 ? $"{Ui.Fmt(-centimos)} a favor" : Ui.Fmt(centimos);

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
        if (PedirRuta($"FlujoCaja_{Anio}-{mes:D2}_{Dinero.NombreMes(mes)}.xlsx") is not { } ruta) return;
        Ui.Intentar(() =>
        {
            _reporte.ExportarMesExcel(Anio, mes, ruta);
            Ui.Info("Flujo de caja exportado:\n" + ruta);
        });
    }

    private void ExportarAnio()
    {
        if (PedirRuta($"FlujoCaja_{Anio}_resumen_anual.xlsx") is not { } ruta) return;
        Ui.Intentar(() =>
        {
            _reporte.ExportarAnioExcel(Anio, ruta);
            Ui.Info("Flujo de caja exportado:\n" + ruta);
        });
    }
}
