using ClosedXML.Excel;

namespace Alquileres.Core;

/// <summary>
/// Flujo de caja mensual: ingresos brutos (IVA incluido), ingresos netos (sin IVA)
/// e IVA 13 %. No cuenta órdenes anuladas ni eliminadas. El mes lo define la fecha
/// de la orden. El IVA es el cobrado (débito fiscal); el IVA a pagar en el
/// formulario 150 de TRIBU-CR se obtiene restando el IVA de compras (crédito fiscal).
/// Utilidad = ingresos netos − gastos sin IVA (los gastos van por su propia fecha).
/// </summary>
public class ReporteServicio(BaseDatos db)
{
    private readonly OrdenRepositorio _ordenes = new(db);
    private readonly GastoRepositorio _gastos = new(db);

    /// <summary>Devuelve los 12 meses del año, incluidos los meses sin ventas.</summary>
    public List<ResumenMensual> ResumenAnual(int anio)
    {
        var datos = new Dictionary<int, ResumenMensual>();
        using (var cn = db.Abrir())
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT CAST(substr(o.fecha, 6, 2) AS INTEGER) AS mes,
                       COUNT(DISTINCT o.id),
                       COALESCE(SUM(l.total), 0), COALESCE(SUM(l.base), 0), COALESCE(SUM(l.iva), 0)
                FROM ordenes o
                JOIN orden_lineas l ON l.orden_id = o.id
                WHERE o.estado <> 'Anulada' AND o.eliminada = 0 AND substr(o.fecha, 1, 4) = $a
                GROUP BY mes;
                """;
            cmd.Parameters.AddWithValue("$a", anio.ToString("D4"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int mes = r.GetInt32(0);
                datos[mes] = new ResumenMensual(anio, mes, r.GetInt32(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4));
            }
        }

        // Gastos del mes (monto pagado).
        var gastos = new Dictionary<int, long>();
        using (var cn = db.Abrir())
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT CAST(substr(fecha, 6, 2) AS INTEGER), COALESCE(SUM(monto), 0)
                FROM gastos WHERE substr(fecha, 1, 4) = $a GROUP BY 1;
                """;
            cmd.Parameters.AddWithValue("$a", anio.ToString("D4"));
            using var r = cmd.ExecuteReader();
            while (r.Read()) gastos[r.GetInt32(0)] = r.GetInt64(1);
        }

        return Enumerable.Range(1, 12)
            .Select(m => (datos.TryGetValue(m, out var d) ? d : new ResumenMensual(anio, m, 0, 0, 0, 0))
                with { GastosCentimos = gastos.GetValueOrDefault(m) })
            .ToList();
    }

    public ResumenMensual ResumenMes(int anio, int mes) => ResumenAnual(anio)[mes - 1];

    /// <summary>Órdenes no anuladas ni eliminadas del mes, para el detalle.</summary>
    public List<Orden> DetalleMes(int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);
        return _ordenes.Listar(desde, hasta)
            .Where(o => o.Estado != EstadoOrden.Anulada)
            .OrderBy(o => o.Fecha).ThenBy(o => o.Id)
            .ToList();
    }

    /// <summary>Gastos del mes, para el detalle y el Excel.</summary>
    public List<Gasto> GastosMes(int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        return _gastos.Listar(desde, desde.AddMonths(1).AddDays(-1))
            .OrderBy(g => g.Fecha).ThenBy(g => g.Id).ToList();
    }

    private const string FormatoMoneda = "\"₡\"#,##0.00";

    /// <summary>Excel del mes: hoja "Resumen" y hoja "Detalle" (una fila por orden).</summary>
    public void ExportarMesExcel(int anio, int mes, string rutaArchivo)
    {
        var resumen = ResumenMes(anio, mes);
        var detalle = DetalleMes(anio, mes);

        using var wb = new XLWorkbook();
        var hr = wb.Worksheets.Add("Resumen");
        hr.Cell(1, 1).Value = $"Flujo de caja — {Dinero.NombreMes(mes)} {anio}";
        hr.Cell(1, 1).Style.Font.Bold = true;
        hr.Cell(1, 1).Style.Font.FontSize = 14;

        var filas = new (string, XLCellValue, bool)[]
        {
            ("Órdenes (no anuladas)", resumen.CantidadOrdenes, false),
            ("Ingresos brutos (IVA incluido)", Dinero.ADecimal(resumen.TotalCentimos), true),
            ("Ingresos netos (sin IVA)", Dinero.ADecimal(resumen.BaseCentimos), true),
            ("Total IVA 13 % (débito fiscal)", Dinero.ADecimal(resumen.IvaCentimos), true),
            ("Gastos", Dinero.ADecimal(resumen.GastosCentimos), true),
            ("Utilidad (ingresos netos − gastos)", Dinero.ADecimal(resumen.UtilidadCentimos), true),
        };
        for (int i = 0; i < filas.Length; i++)
        {
            hr.Cell(3 + i, 1).Value = filas[i].Item1;
            hr.Cell(3 + i, 2).Value = filas[i].Item2;
            if (filas[i].Item3) hr.Cell(3 + i, 2).Style.NumberFormat.Format = FormatoMoneda;
        }
        hr.Range(6, 1, 6, 2).Style.Font.Bold = true;
        hr.Range(8, 1, 8, 2).Style.Font.Bold = true;
        hr.Range(10, 1, 10, 2).Style.Font.Bold = true;
        hr.Cell(12, 1).Value = "Nota: el IVA neto es una estimación para el formulario 150; solo es crédito fiscal el IVA de facturas electrónicas " +
                               "a nombre del negocio y relacionadas con su actividad. Confirme con su contador. La utilidad es antes del impuesto sobre la renta.";
        hr.Cell(12, 1).Style.Font.Italic = true;
        hr.Cell(13, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        hr.Columns(1, 2).AdjustToContents();

        var hd = wb.Worksheets.Add("Detalle");
        string[] encabezados = { "N° orden", "Fecha", "Cliente", "Estado", "Ingresos brutos", "Ingresos netos", "IVA 13 %" };
        for (int c = 0; c < encabezados.Length; c++) hd.Cell(1, c + 1).Value = encabezados[c];
        hd.Row(1).Style.Font.Bold = true;

        int fila = 2;
        foreach (var o in detalle)
        {
            hd.Cell(fila, 1).Value = o.Numero;
            hd.Cell(fila, 2).Value = o.Fecha;
            hd.Cell(fila, 2).Style.DateFormat.Format = "dd/MM/yyyy";
            hd.Cell(fila, 3).Value = o.ClienteNombre;
            hd.Cell(fila, 4).Value = o.Estado.ToString();
            hd.Cell(fila, 5).Value = Dinero.ADecimal(o.TotalCentimos);
            hd.Cell(fila, 6).Value = Dinero.ADecimal(o.BaseCentimos);
            hd.Cell(fila, 7).Value = Dinero.ADecimal(o.IvaCentimos);
            fila++;
        }
        hd.Cell(fila, 4).Value = "TOTAL";
        hd.Cell(fila, 5).Value = Dinero.ADecimal(resumen.TotalCentimos);
        hd.Cell(fila, 6).Value = Dinero.ADecimal(resumen.BaseCentimos);
        hd.Cell(fila, 7).Value = Dinero.ADecimal(resumen.IvaCentimos);
        hd.Row(fila).Style.Font.Bold = true;
        hd.Range(2, 5, fila, 7).Style.NumberFormat.Format = FormatoMoneda;
        if (detalle.Count > 0) hd.Range(1, 1, fila - 1, 7).SetAutoFilter();
        hd.Columns().AdjustToContents();

        var gastos = GastosMes(anio, mes);
        var hg = wb.Worksheets.Add("Gastos");
        string[] encGastos = { "Fecha", "Categoría", "Detalle", "Método", "Monto" };
        for (int c = 0; c < encGastos.Length; c++) hg.Cell(1, c + 1).Value = encGastos[c];
        hg.Row(1).Style.Font.Bold = true;
        int fg = 2;
        foreach (var g in gastos)
        {
            hg.Cell(fg, 1).Value = g.Fecha;
            hg.Cell(fg, 1).Style.DateFormat.Format = "dd/MM/yyyy";
            hg.Cell(fg, 2).Value = g.Categoria;
            hg.Cell(fg, 3).Value = g.Detalle;
            hg.Cell(fg, 4).Value = g.MetodoTexto;
            hg.Cell(fg, 5).Value = Dinero.ADecimal(g.MontoCentimos);
            fg++;
        }
        hg.Cell(fg, 4).Value = "TOTAL";
        hg.Cell(fg, 5).Value = Dinero.ADecimal(gastos.Sum(g => g.MontoCentimos));
        hg.Row(fg).Style.Font.Bold = true;
        hg.Range(2, 5, fg, 5).Style.NumberFormat.Format = FormatoMoneda;
        hg.Columns().AdjustToContents();

        wb.SaveAs(rutaArchivo);
    }

    /// <summary>Excel del año: una fila por mes con totales.</summary>
    public void ExportarAnioExcel(int anio, string rutaArchivo)
    {
        var meses = ResumenAnual(anio);
        using var wb = new XLWorkbook();
        var h = wb.Worksheets.Add($"Flujo de caja {anio}");
        string[] encabezados = { "Mes", "Órdenes", "Ingresos brutos", "Ingresos netos", "Total IVA 13 %", "Gastos", "Utilidad" };
        for (int c = 0; c < encabezados.Length; c++) h.Cell(1, c + 1).Value = encabezados[c];
        h.Row(1).Style.Font.Bold = true;

        int fila = 2;
        foreach (var m in meses)
        {
            h.Cell(fila, 1).Value = m.NombreMes;
            h.Cell(fila, 2).Value = m.CantidadOrdenes;
            h.Cell(fila, 3).Value = Dinero.ADecimal(m.TotalCentimos);
            h.Cell(fila, 4).Value = Dinero.ADecimal(m.BaseCentimos);
            h.Cell(fila, 5).Value = Dinero.ADecimal(m.IvaCentimos);
            h.Cell(fila, 6).Value = Dinero.ADecimal(m.GastosCentimos);
            h.Cell(fila, 7).Value = Dinero.ADecimal(m.UtilidadCentimos);
            fila++;
        }
        h.Cell(fila, 1).Value = "TOTAL";
        h.Cell(fila, 2).Value = meses.Sum(m => m.CantidadOrdenes);
        h.Cell(fila, 3).Value = Dinero.ADecimal(meses.Sum(m => m.TotalCentimos));
        h.Cell(fila, 4).Value = Dinero.ADecimal(meses.Sum(m => m.BaseCentimos));
        h.Cell(fila, 5).Value = Dinero.ADecimal(meses.Sum(m => m.IvaCentimos));
        h.Cell(fila, 6).Value = Dinero.ADecimal(meses.Sum(m => m.GastosCentimos));
        h.Cell(fila, 7).Value = Dinero.ADecimal(meses.Sum(m => m.UtilidadCentimos));
        h.Row(fila).Style.Font.Bold = true;
        h.Range(2, 3, fila, 7).Style.NumberFormat.Format = FormatoMoneda;
        h.Columns().AdjustToContents();
        wb.SaveAs(rutaArchivo);
    }
}
