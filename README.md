# Sistema de Alquileres

Programa de escritorio para Windows que registra órdenes de alquiler de 4 productos, muestra el total que paga el cliente y genera el reporte mensual de IVA. Funciona sin internet y no hay que instalar ninguna base de datos.

- **Tecnología:** C# · .NET 10 (LTS, con soporte hasta noviembre de 2028) · WinForms · SQLite (archivo local) · ClosedXML (exportación a Excel)

## Estructura

```
SistemaAlquileres.sln
├── src/Alquileres.Core     → lógica de negocio (sin pantallas): modelos, base de datos, IVA, reportes, respaldos
├── src/Alquileres.App      → pantallas WinForms (el .exe)
└── tests/Alquileres.Tests  → pruebas automáticas (xUnit)
```

| Archivo | Qué contiene |
|---|---|
| `Core/CalculadoraIva.cs` | Cálculo del IVA incluido: base = total / 1,13; IVA = total − base |
| `Core/BaseDatos.cs` | Crea el archivo SQLite y las tablas la primera vez |
| `Core/Repositorios.cs` | Productos, clientes, órdenes y configuración (el único lugar con SQL) |
| `Core/ReporteServicio.cs` | Resumen mensual y anual + exportación a Excel |
| `Core/RespaldoServicio.cs` | Respaldos automáticos (`VACUUM INTO`) y restauración |
| `App/Pantallas/*` | Nueva orden, Órdenes, Reporte, Productos, Clientes, Respaldos |

## Cómo abrirlo y ejecutarlo

1. Instalar **Visual Studio 2026** (o 2022 actualizado) con la carga de trabajo *Desarrollo de escritorio de .NET*, o el **SDK de .NET 10**.
2. Abrir `SistemaAlquileres.sln`.
3. Elegir `Alquileres.App` como proyecto de inicio y presionar **F5**.

Para correr las pruebas: `dotnet test` en la carpeta raíz.

## Cómo generar el .exe para el cliente

La configuración está guardada en el perfil `src/Alquileres.App/Properties/PublishProfiles/Cliente.pubxml`. **No hay que elegir opciones a mano.**

- **Visual Studio:** clic derecho en `Alquileres.App` → **Publicar…** → perfil **Cliente** → **Publicar**.
- **Por comando** (desde la carpeta de la solución): `dotnet publish src/Alquileres.App -p:PublishProfile=Cliente`

El resultado es **un solo archivo**: `D:\Daniel\SistemaAlquileres\publicar\SistemaAlquileres.exe` (≈ 55 MB). Incluye .NET 10, SQLite y el ícono. El cliente no tiene que instalar nada: se copia el `.exe` a su computadora (por ejemplo a `C:\SistemaAlquileres\`) y se le crea un acceso directo en el escritorio.

Antes de cada entrega, probá el `.exe` en una computadora que no tenga Visual Studio.

Para actualizar al cliente basta con reemplazar el `.exe`: los datos están en otra carpeta y no se pierden.

## Dónde quedan los datos

| Qué | Dónde |
|---|---|
| Base de datos | `%LOCALAPPDATA%\SistemaAlquileres\datos.db` |
| Respaldos automáticos | `%LOCALAPPDATA%\SistemaAlquileres\Respaldos\` (se conservan los últimos 30) |
| Respaldo adicional | La carpeta que se configure en la pestaña *Respaldos* (recomendado: OneDrive o Google Drive) |

Cada vez que se cierra el programa se crea un respaldo. **Configurá la carpeta adicional en la computadora del cliente el día de la instalación**: si el disco se daña, esa es la única copia que queda fuera del equipo.

## Reglas de negocio

- **Los precios incluyen el IVA del 13 %.** El IVA no es el 13 % del total sino total × 13/113 (≈ 11,50 %). Ejemplo: de ₡113.000, la base es ₡100.000 y el IVA ₡13.000.
- **Total de cada línea** = precio unitario × cantidad × días. Si el producto se cobra por alquiler y no por día, se deja *días = 1*.
- **Cada línea guarda su precio y su IVA al momento de la orden.** Cambiar un precio después no altera las órdenes anteriores.
- **Los montos se guardan como enteros en céntimos.** Las sumas son exactas (no se usa `double`).
- **Las órdenes no se borran, se anulan con un motivo.** Las anuladas no cuentan en el reporte.
- **El mes del reporte lo define la *fecha de la orden*.**
- **El reporte muestra el IVA COBRADO (débito fiscal).** El IVA a pagar en el formulario 150 de TRIBU-CR es ese monto menos el IVA de las compras del negocio (crédito fiscal), que este sistema no registra.
- **Este sistema no emite factura electrónica.** Si el cliente está inscrito en Hacienda, debe seguir facturando con TicoFactura o con un facturador autorizado. Este programa sirve como control interno y para conciliar.

## Ideas para una versión 2

- Comprobante o recibo imprimible de cada orden (PDF).
- Calendario de disponibilidad de los 4 productos.
- Registro de compras con IVA, para calcular el IVA neto a pagar.
- Contraseña al abrir el programa.
