using ClosedXML.Excel;

namespace Alquileres.Core;

/// <summary>
/// Reporte mensual de ventas e IVA. Las órdenes anuladas no se cuentan.
/// El monto es "IVA cobrado" (débito fiscal). El IVA a pagar en el formulario 150
/// de TRIBU-CR se obtiene restando el IVA pagado en compras (crédito fiscal).
/// </summary>
public class ReporteServicio(BaseDatos db)
{
    private readonly OrdenRepositorio _ordenes = new(db);

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
                WHERE o.estado <> 'Anulada' AND substr(o.fecha, 1, 4) = $a
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

        return Enumerable.Range(1, 12)
            .Select(m => datos.TryGetValue(m, out var d) ? d : new ResumenMensual(anio, m, 0, 0, 0, 0))
            .ToList();
    }

    public ResumenMensual ResumenMes(int anio, int mes) => ResumenAnual(anio)[mes - 1];

    /// <summary>Órdenes no anuladas del mes, para el detalle.</summary>
    public List<Orden> DetalleMes(int anio, int mes)
    {
        var desde = new DateTime(anio, mes, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);
        return _ordenes.Listar(desde, hasta)
            .Where(o => o.Estado != EstadoOrden.Anulada)
            .OrderBy(o => o.Fecha).ThenBy(o => o.Id)
            .ToList();
    }

    private const string FormatoMoneda = "\"₡\"#,##0.00";

    /// <summary>Excel del mes: hoja "Resumen" y hoja "Detalle" (una fila por orden).</summary>
    public void ExportarMesExcel(int anio, int mes, string rutaArchivo)
    {
        var resumen = ResumenMes(anio, mes);
        var detalle = DetalleMes(anio, mes);

        using var wb = new XLWorkbook();
        var hr = wb.Worksheets.Add("Resumen");
        hr.Cell(1, 1).Value = $"Reporte de IVA — {Dinero.NombreMes(mes)} {anio}";
        hr.Cell(1, 1).Style.Font.Bold = true;
        hr.Cell(1, 1).Style.Font.FontSize = 14;

        var filas = new (string, XLCellValue, bool)[]
        {
            ("Órdenes (no anuladas)", resumen.CantidadOrdenes, false),
            ("Total cobrado (IVA incluido)", Dinero.ADecimal(resumen.TotalCentimos), true),
            ("Base imponible (ventas sin IVA)", Dinero.ADecimal(resumen.BaseCentimos), true),
            ("IVA cobrado 13 % (débito fiscal)", Dinero.ADecimal(resumen.IvaCentimos), true),
        };
        for (int i = 0; i < filas.Length; i++)
        {
            hr.Cell(3 + i, 1).Value = filas[i].Item1;
            hr.Cell(3 + i, 2).Value = filas[i].Item2;
            if (filas[i].Item3) hr.Cell(3 + i, 2).Style.NumberFormat.Format = FormatoMoneda;
        }
        hr.Range(6, 1, 6, 2).Style.Font.Bold = true;
        hr.Cell(8, 1).Value = "Nota: el IVA a pagar en el formulario 150 = IVA cobrado − IVA de compras (crédito fiscal).";
        hr.Cell(8, 1).Style.Font.Italic = true;
        hr.Cell(9, 1).Value = $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        hr.Columns(1, 2).AdjustToContents();

        var hd = wb.Worksheets.Add("Detalle");
        string[] encabezados = { "N° orden", "Fecha", "Cliente", "Estado", "Total cobrado", "Base imponible", "IVA" };
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

        wb.SaveAs(rutaArchivo);
    }

    /// <summary>Excel del año: una fila por mes con totales.</summary>
    public void ExportarAnioExcel(int anio, string rutaArchivo)
    {
        var meses = ResumenAnual(anio);
        using var wb = new XLWorkbook();
        var h = wb.Worksheets.Add($"Resumen {anio}");
        string[] encabezados = { "Mes", "Órdenes", "Total cobrado", "Base imponible", "IVA cobrado" };
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
            fila++;
        }
        h.Cell(fila, 1).Value = "TOTAL";
        h.Cell(fila, 2).Value = meses.Sum(m => m.CantidadOrdenes);
        h.Cell(fila, 3).Value = Dinero.ADecimal(meses.Sum(m => m.TotalCentimos));
        h.Cell(fila, 4).Value = Dinero.ADecimal(meses.Sum(m => m.BaseCentimos));
        h.Cell(fila, 5).Value = Dinero.ADecimal(meses.Sum(m => m.IvaCentimos));
        h.Row(fila).Style.Font.Bold = true;
        h.Range(2, 3, fila, 5).Style.NumberFormat.Format = FormatoMoneda;
        h.Columns().AdjustToContents();
        wb.SaveAs(rutaArchivo);
    }
}
