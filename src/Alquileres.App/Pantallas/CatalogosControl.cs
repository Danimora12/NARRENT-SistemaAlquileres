using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class ProductosControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly ProductoRepositorio _productos;
    private readonly DataGridView _grid = Ui.Grid();

    public ProductosControl(BaseDatos db)
    {
        _db = db;
        _productos = new ProductoRepositorio(db);

        Ui.Columna(_grid, "Código", 80);
        Ui.Columna(_grid, "Producto", 220);
        Ui.Columna(_grid, "Precio (IVA incluido)", 130, derecha: true);
        Ui.Columna(_grid, "Base", 110, derecha: true);
        Ui.Columna(_grid, "IVA incluido", 100, derecha: true);
        Ui.Columna(_grid, "Inventario", 90, derecha: true);
        Ui.Columna(_grid, "Disponible", 80);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Editar(); };

        var ayuda = Ui.Etiqueta(
            "Los precios se ingresan con el IVA ya incluido. Cambiar un precio no modifica las órdenes ya registradas. " +
            "Cada producto tiene un código (3 letras del nombre + consecutivo) que solo un administrador puede cambiar. " +
            "Crear, editar o eliminar productos pide el código de confirmación. Solo se pueden eliminar productos que nunca se usaron en una orden; " +
            "los demás se desactivan.");
        ayuda.ForeColor = Color.DimGray;
        ayuda.MaximumSize = new Size(1100, 0);

        var acciones = Ui.Fila(
            Ui.Boton("Nuevo producto…", (_, _) => Editar(nuevo: true)),
            Ui.Boton("Editar…", (_, _) => Editar(), principal: true),
            Ui.Boton("Activar / desactivar", (_, _) => CambiarActivo()),
            Ui.Boton("Eliminar…", (_, _) => Eliminar()));

        Controls.Add(Ui.Pila(ayuda, _grid, acciones));
    }

    public void Refrescar()
    {
        long? seleccionado = Ui.Seleccionado<Producto>(_grid)?.Id;
        _grid.Rows.Clear();
        foreach (var p in _productos.Listar())
        {
            var r = CalculadoraIva.DesglosarTotal(p.PrecioConIvaCentimos);
            int i = _grid.Rows.Add(p.Codigo, p.Nombre, Ui.Fmt(p.PrecioConIvaCentimos), Ui.Fmt(r.BaseCentimos),
                Ui.Fmt(r.IvaCentimos), p.Inventario, p.Activo ? "Sí" : "No");
            _grid.Rows[i].Tag = p;
            if (!p.Activo) _grid.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
            if (p.Id == seleccionado) _grid.CurrentCell = _grid.Rows[i].Cells[0];
        }
    }

    private Producto? Seleccionado()
    {
        var p = Ui.Seleccionado<Producto>(_grid);
        if (p is null) Ui.Info("Seleccione un producto.");
        return p;
    }

    private void Editar(bool nuevo = false)
    {
        Producto? p = nuevo ? new Producto() : Seleccionado();
        if (p is null) return;

        string accion = nuevo ? "Crear un producto nuevo" : $"Editar el producto {p.Codigo} \"{p.Nombre}\"";
        if (Autorizacion.Empleado(this, _db, accion) is not { } empleado) return;

        using var dlg = new ProductoDialog(p, x => _productos.Guardar(x, empleado), empleado,
            _productos.SugerirCodigo, _db.UsuarioActual?.EsAdministrador == true);
        dlg.ShowDialog(this);
        Ui.Intentar(Refrescar);
    }

    private void Eliminar()
    {
        if (Seleccionado() is not { } p) return;
        if (Autorizacion.Empleado(this, _db, $"Eliminar el producto {p.Codigo} \"{p.Nombre}\"",
                "Se borra definitivamente. Solo es posible si nunca se usó en una orden. Queda constancia en Logs.") is not { } empleado) return;
        Ui.Intentar(() => _productos.Eliminar(p.Id, empleado));
        Ui.Intentar(Refrescar);
    }

    private void CambiarActivo()
    {
        if (Seleccionado() is not { } p) return;
        string accion = $"{(p.Activo ? "Desactivar" : "Activar")} el producto \"{p.Nombre}\"";
        if (Autorizacion.Empleado(this, _db, accion) is not { } empleado) return;
        p.Activo = !p.Activo;
        Ui.Intentar(() => _productos.Guardar(p, empleado));
        Ui.Intentar(Refrescar);
    }
}

internal sealed class ClientesControl : UserControl, IRefrescable
{
    private readonly BaseDatos _db;
    private readonly ClienteRepositorio _clientes;
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _buscar = Ui.Texto(250);

    public ClientesControl(BaseDatos db)
    {
        _db = db;
        _clientes = new ClienteRepositorio(db);

        Ui.Columna(_grid, "Nombre", 220);
        Ui.Columna(_grid, "Tipo", 110);
        Ui.Columna(_grid, "Identificación", 120);
        Ui.Columna(_grid, "Teléfono", 100);
        Ui.Columna(_grid, "Correo", 180);
        Ui.Columna(_grid, "Notas", 200);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Editar(); };
        _buscar.TextChanged += (_, _) => Ui.Intentar(Refrescar);

        var ayuda = Ui.Etiqueta("La identificación es obligatoria y no se puede repetir. Editar o eliminar un cliente pide el código de confirmación.");
        ayuda.ForeColor = Color.DimGray;

        var barra = Ui.Fila(Ui.Etiqueta("Buscar:"), _buscar, ayuda);
        var acciones = Ui.Fila(
            Ui.Boton("Nuevo cliente…", (_, _) => Nuevo()),
            Ui.Boton("Editar…", (_, _) => Editar(), principal: true),
            Ui.Boton("Eliminar…", (_, _) => Eliminar()));

        Controls.Add(Ui.Pila(barra, _grid, acciones));
    }

    public void Refrescar()
    {
        _grid.Rows.Clear();
        foreach (var c in _clientes.Listar(_buscar.Text))
        {
            int i = _grid.Rows.Add(c.Nombre, c.TipoIdentificacion is null ? "Sin tipo" : c.TipoIdentificacionTexto,
                c.IdentificacionFormateada, c.Telefono ?? "", c.Correo ?? "", c.Notas ?? "");
            _grid.Rows[i].Tag = c;
            if (c.TipoIdentificacion is null) _grid.Rows[i].Cells[1].Style.ForeColor = Color.Firebrick;
        }
    }

    private Cliente? Seleccionado()
    {
        var c = Ui.Seleccionado<Cliente>(_grid);
        if (c is null) Ui.Info("Seleccione un cliente.");
        return c;
    }

    private void Nuevo()
    {
        using var dlg = new ClienteDialog(new Cliente(), c => _clientes.Guardar(c));
        dlg.ShowDialog(this);
        Ui.Intentar(Refrescar);
    }

    private void Editar()
    {
        if (Seleccionado() is not { } c) return;
        if (Autorizacion.Empleado(this, _db, $"Editar el cliente \"{c.Nombre}\"") is not { } empleado) return;

        using var dlg = new ClienteDialog(c, x => _clientes.Guardar(x, empleado), empleado);
        dlg.ShowDialog(this);
        Ui.Intentar(Refrescar);
    }

    private void Eliminar()
    {
        if (Seleccionado() is not { } c) return;
        if (Autorizacion.Empleado(this, _db, $"Eliminar el cliente \"{c.Nombre}\"",
                "Solo se pueden eliminar clientes sin órdenes.") is not { } empleado) return;
        Ui.Intentar(() => _clientes.Eliminar(c.Id, empleado));
        Ui.Intentar(Refrescar);
    }
}
