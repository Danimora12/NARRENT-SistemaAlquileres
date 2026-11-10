using Alquileres.Core;

namespace Alquileres.App.Pantallas;

internal sealed class ProductosControl : UserControl, IRefrescable
{
    private readonly ProductoRepositorio _productos;
    private readonly DataGridView _grid = Ui.Grid();

    public ProductosControl(BaseDatos db)
    {
        _productos = new ProductoRepositorio(db);

        Ui.Columna(_grid, "Producto", 220);
        Ui.Columna(_grid, "Precio (IVA incluido)", 130, derecha: true);
        Ui.Columna(_grid, "Base", 120, derecha: true);
        Ui.Columna(_grid, "IVA incluido", 110, derecha: true);
        Ui.Columna(_grid, "Disponible", 80);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Editar(); };

        var ayuda = Ui.Etiqueta("Los precios se ingresan con el IVA ya incluido. Cambiar un precio no modifica las órdenes ya registradas.");
        ayuda.ForeColor = Color.DimGray;

        var acciones = Ui.Fila(
            Ui.Boton("Nuevo producto…", (_, _) => Editar(nuevo: true)),
            Ui.Boton("Editar…", (_, _) => Editar(), principal: true),
            Ui.Boton("Activar / desactivar", (_, _) => CambiarActivo()));

        Controls.Add(Ui.Pila(ayuda, _grid, acciones));
    }

    public void Refrescar()
    {
        _grid.Rows.Clear();
        foreach (var p in _productos.Listar())
        {
            var r = CalculadoraIva.DesglosarTotal(p.PrecioConIvaCentimos);
            int i = _grid.Rows.Add(p.Nombre, Ui.Fmt(p.PrecioConIvaCentimos), Ui.Fmt(r.BaseCentimos),
                Ui.Fmt(r.IvaCentimos), p.Activo ? "Sí" : "No");
            _grid.Rows[i].Tag = p;
            if (!p.Activo) _grid.Rows[i].DefaultCellStyle.ForeColor = Color.Gray;
        }
    }

    private void Editar(bool nuevo = false)
    {
        Producto? p = nuevo ? new Producto() : Ui.Seleccionado<Producto>(_grid);
        if (p is null) { Ui.Info("Seleccione un producto."); return; }

        using var dlg = new ProductoDialog(p);
        if (dlg.ShowDialog(this) != DialogResult.OK) { Ui.Intentar(Refrescar); return; }
        Ui.Intentar(() => _productos.Guardar(dlg.Producto));
        Ui.Intentar(Refrescar);
    }

    private void CambiarActivo()
    {
        if (Ui.Seleccionado<Producto>(_grid) is not { } p) { Ui.Info("Seleccione un producto."); return; }
        p.Activo = !p.Activo;
        Ui.Intentar(() => _productos.Guardar(p));
        Ui.Intentar(Refrescar);
    }
}

internal sealed class ClientesControl : UserControl, IRefrescable
{
    private readonly ClienteRepositorio _clientes;
    private readonly DataGridView _grid = Ui.Grid();
    private readonly TextBox _buscar = Ui.Texto(250);

    public ClientesControl(BaseDatos db)
    {
        _clientes = new ClienteRepositorio(db);

        Ui.Columna(_grid, "Nombre", 220);
        Ui.Columna(_grid, "Identificación", 120);
        Ui.Columna(_grid, "Teléfono", 100);
        Ui.Columna(_grid, "Correo", 180);
        Ui.Columna(_grid, "Notas", 200);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Editar(); };
        _buscar.TextChanged += (_, _) => Ui.Intentar(Refrescar);

        var barra = Ui.Fila(Ui.Etiqueta("Buscar:"), _buscar);
        var acciones = Ui.Fila(
            Ui.Boton("Nuevo cliente…", (_, _) => Editar(nuevo: true)),
            Ui.Boton("Editar…", (_, _) => Editar(), principal: true),
            Ui.Boton("Eliminar", (_, _) => Eliminar()));

        Controls.Add(Ui.Pila(barra, _grid, acciones));
    }

    public void Refrescar()
    {
        _grid.Rows.Clear();
        foreach (var c in _clientes.Listar(_buscar.Text))
        {
            int i = _grid.Rows.Add(c.Nombre, c.Identificacion ?? "", c.Telefono ?? "", c.Correo ?? "", c.Notas ?? "");
            _grid.Rows[i].Tag = c;
        }
    }

    private void Editar(bool nuevo = false)
    {
        Cliente? c = nuevo ? new Cliente() : Ui.Seleccionado<Cliente>(_grid);
        if (c is null) { Ui.Info("Seleccione un cliente."); return; }

        using var dlg = new ClienteDialog(c);
        if (dlg.ShowDialog(this) != DialogResult.OK) { Ui.Intentar(Refrescar); return; }
        Ui.Intentar(() => _clientes.Guardar(dlg.Cliente));
        Ui.Intentar(Refrescar);
    }

    private void Eliminar()
    {
        if (Ui.Seleccionado<Cliente>(_grid) is not { } c) { Ui.Info("Seleccione un cliente."); return; }
        if (!Ui.Confirmar($"¿Eliminar a {c.Nombre}?")) return;
        Ui.Intentar(() => _clientes.Eliminar(c.Id));
        Ui.Intentar(Refrescar);
    }
}
