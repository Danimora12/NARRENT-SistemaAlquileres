using System.Diagnostics;
using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class RespaldosControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly RespaldoServicio _respaldos;
    private readonly FormPrincipal _principal;

    private readonly Label _rutaDatos = Ui.Etiqueta("");
    private readonly Label _rutaLocal = Ui.Etiqueta("");
    private readonly TextBox _carpetaExtra = Ui.Texto(450);
    private readonly DataGridView _grid = Ui.Grid();

    public RespaldosControl(BaseDatos db, FormPrincipal principal)
    {
        _db = db;
        _principal = principal;
        _respaldos = new RespaldoServicio(db);
        _carpetaExtra.ReadOnly = true;

        Ui.Columna(_grid, "Archivo", 260);
        Ui.Columna(_grid, "Fecha", 140);
        Ui.Columna(_grid, "Tamaño", 80, derecha: true);
        Ui.Columna(_grid, "Carpeta", 300);

        var explicacion = Ui.Etiqueta(
            "Cada vez que se cierra el programa se crea un respaldo automático (se conservan los últimos " +
            $"{RespaldoServicio.RespaldosAConservar}). Recomendado: elija también una carpeta de OneDrive o Google Drive " +
            "para tener una copia fuera de esta computadora.");
        explicacion.MaximumSize = new Size(1100, 0);

        var info = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        info.Controls.Add(Ui.Etiqueta("Base de datos:", Ui.FuenteNegrita), 0, 0);
        info.Controls.Add(_rutaDatos, 1, 0);
        info.Controls.Add(Ui.Etiqueta("Respaldos locales:", Ui.FuenteNegrita), 0, 1);
        info.Controls.Add(_rutaLocal, 1, 1);
        info.Controls.Add(Ui.Etiqueta("Carpeta adicional:", Ui.FuenteNegrita), 0, 2);
        info.Controls.Add(Ui.Fila(_carpetaExtra,
            Ui.Boton("Elegir…", (_, _) => ElegirCarpeta()),
            Ui.Boton("Quitar", (_, _) => { _respaldos.CarpetaExtra = null; Refrescar(); })), 1, 2);

        var acciones = Ui.Fila(
            Ui.Boton("Crear respaldo ahora", (_, _) => CrearAhora(), principal: true),
            Ui.Boton("Restaurar respaldo seleccionado…", (_, _) => Restaurar(Ui.Seleccionado<FileInfo>(_grid)?.FullName)),
            Ui.Boton("Restaurar desde otro archivo…", (_, _) => RestaurarDesdeArchivo()),
            Ui.Boton("Abrir carpeta de respaldos", (_, _) => AbrirCarpeta(_respaldos.CarpetaRespaldosLocal)));

        Controls.Add(Ui.Pila(explicacion, info, _grid, acciones));
    }

    public void Refrescar()
    {
        _rutaDatos.Text = _db.RutaArchivo;
        _rutaLocal.Text = _respaldos.CarpetaRespaldosLocal;
        string? extra = _respaldos.CarpetaExtra;
        _carpetaExtra.Text = extra ?? "(ninguna)";

        var archivos = RespaldoServicio.ListarRespaldos(_respaldos.CarpetaRespaldosLocal);
        if (!string.IsNullOrWhiteSpace(extra)) archivos.AddRange(RespaldoServicio.ListarRespaldos(extra));

        _grid.Rows.Clear();
        foreach (var f in archivos.OrderByDescending(f => f.LastWriteTime))
        {
            int i = _grid.Rows.Add(f.Name, f.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                $"{Math.Max(1, f.Length / 1024)} KB", f.DirectoryName ?? "");
            _grid.Rows[i].Tag = f;
        }
    }

    private void ElegirCarpeta()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Elija una carpeta adicional para los respaldos (por ejemplo, dentro de OneDrive o Google Drive)",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Ui.Intentar(() =>
        {
            _respaldos.CarpetaExtra = dlg.SelectedPath;
            _respaldos.CrearRespaldo(dlg.SelectedPath);
            Refrescar();
            Ui.Info("Carpeta configurada. Se creó un primer respaldo en ella.");
        });
    }

    private void CrearAhora()
    {
        Ui.Intentar(() =>
        {
            var (creados, errores) = _respaldos.RespaldoAutomatico();
            Refrescar();
            string msg = "Respaldos creados:\n" + string.Join("\n", creados);
            if (errores.Count > 0) msg += "\n\nErrores:\n" + string.Join("\n", errores);
            Ui.Info(msg);
        });
    }

    private void RestaurarDesdeArchivo()
    {
        using var dlg = new OpenFileDialog { Filter = "Respaldo (*.db)|*.db|Todos los archivos|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK) Restaurar(dlg.FileName);
    }

    private void Restaurar(string? archivo)
    {
        if (archivo is null) { Ui.Info("Seleccione un respaldo de la lista."); return; }
        if (!Ui.Confirmar(
                $"Se reemplazarán TODOS los datos actuales por los del respaldo:\n{Path.GetFileName(archivo)}\n\n" +
                "Antes se guardará una copia de los datos actuales. El programa se reiniciará. ¿Continuar?"))
            return;

        Ui.Intentar(() =>
        {
            string copia = _respaldos.Restaurar(archivo);
            Ui.Info($"Respaldo restaurado.\n\nCopia de los datos anteriores:\n{copia}\n\nEl programa se reiniciará.");
            _principal.RespaldarAlCerrar = false;
            Application.Restart();
        });
    }

    private static void AbrirCarpeta(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        Process.Start(new ProcessStartInfo { FileName = carpeta, UseShellExecute = true });
    }
}
