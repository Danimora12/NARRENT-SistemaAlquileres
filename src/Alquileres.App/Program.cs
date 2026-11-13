using Alquileres.Core;

namespace Alquileres.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        Application.ThreadException += (_, e) => Ui.MostrarError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Ui.MostrarError(ex);
        };

        BaseDatos db;
        try
        {
            db = BaseDatos.PorDefecto();
            db.Inicializar();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo abrir la base de datos:\n\n" + ex.Message,
                Ui.Titulo, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new FormPrincipal(db));
    }
}
