# NARRENT

Programa de escritorio para Windows que registra órdenes de alquiler, los pagos y abonos de cada orden, el inventario de los productos, los gastos de la empresa y el flujo de caja mensual con el IVA y la utilidad. Funciona sin internet y no hay que instalar ninguna base de datos.

**Versión 2.0** (antes "Sistema de Alquileres").

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
| `Core/CalculadoraIva.cs` | IVA incluido (base = total / 1,13; IVA = total − base) y descuentos por línea |
| `Core/BaseDatos.cs` | Crea el archivo SQLite y actualiza bases viejas paso a paso (v1 → v2) |
| `Core/Repositorios.cs` | Productos e inventario, clientes, órdenes, pagos y configuración |
| `Core/Auditoria.cs` | Logs (registro de cambios) y número de orden año-mes-consecutivo |
| `Core/SeguridadServicio.cs` | Códigos de empleado y de confirmación (guardados con hash, no en texto) |
| `Core/GastoRepositorio.cs` | Gastos de la empresa |
| `Core/ReporteServicio.cs` | Flujo de caja mensual y anual (con gastos y utilidad) + exportación a Excel |
| `Core/RespaldoServicio.cs` | Respaldos automáticos (`VACUUM INTO`) y restauración |
| `App/Controles.cs` | Campo de monto sin decimales y campo numérico con botones − / + |
| `App/Pantallas/*` | Nueva orden, Órdenes, Productos, Clientes, Gastos, Flujo de caja, Respaldos, Logs y sus ventanas |

## Cómo abrirlo y ejecutarlo

1. Instalar **Visual Studio 2026** (o 2022 actualizado) con la carga de trabajo *Desarrollo de escritorio de .NET*, o el **SDK de .NET 10**.
2. Abrir `SistemaAlquileres.sln`.
3. Elegir `Alquileres.App` como proyecto de inicio y presionar **F5**.

Para correr las pruebas: `dotnet test` en la carpeta raíz.

## Cómo generar el .exe para el cliente

La configuración está guardada en el perfil `src/Alquileres.App/Properties/PublishProfiles/Cliente.pubxml`. **No hay que elegir opciones a mano.**

- **Visual Studio:** clic derecho en `Alquileres.App` → **Publicar…** → perfil **Cliente** → **Publicar**.
- **Por comando** (desde la carpeta de la solución): `dotnet publish src/Alquileres.App -p:PublishProfile=Cliente`

El resultado es **un solo archivo**: `D:\Daniel\SistemaAlquileres\publicar\NARRENT.exe` (≈ 55 MB). Incluye .NET 10, SQLite y el ícono. El cliente no tiene que instalar nada: se copia el `.exe` a su computadora (por ejemplo a `C:\SistemaAlquileres\`) y se le crea un acceso directo en el escritorio.

Antes de cada entrega, probá el `.exe` en una computadora que no tenga Visual Studio.

Para actualizar al cliente basta con reemplazar el `.exe`: los datos están en otra carpeta y no se pierden.

### Pasar al cliente de la v1 a NARRENT (v2)

1. Cerrar el programa viejo (al cerrar se crea un respaldo automático).
2. Copiar `NARRENT.exe` junto al viejo, **cambiar el acceso directo del escritorio** para que apunte a `NARRENT.exe` y borrar `SistemaAlquileres.exe`.
3. Abrir NARRENT: la base se actualiza sola y las órdenes existentes se renumeran con el formato nuevo (queda constancia en *Logs*). Las órdenes de la v1 aparecen *sin pago*: si ya estaban pagadas, registrar el pago desde el detalle.
4. Ir a *Logs → Códigos de autorización…* y definir los nombres y códigos de los 2 empleados y el código de confirmación **con el cliente presente**.
5. Completar el inventario en *Productos*.

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
- **Número de orden = año + mes + consecutivo del mes** (ej. `202612001`), según la *fecha de la orden*. Si un mes pasara de 999 órdenes, el número crece a 4 dígitos; nunca se repite.
- **Descuento de 1 % a 15 %**, a toda la orden o a un solo producto, al confirmar la orden. Se redondea al colón y el IVA se calcula sobre el monto ya descontado. Después de confirmada, el descuento no se cambia.
- **Pagos:** al confirmar se registra el pago adelantado (SINPE Móvil, transferencia, efectivo o tarjeta). Los abonos y la cancelación del saldo se registran en el detalle de la orden. No se puede pagar más que el saldo ni pagar órdenes anuladas.
- **Anular ≠ eliminar.** *Anular* deja la orden visible (tachada) con su motivo. *Eliminar* es para órdenes fantasma o de prueba: la manda a *Órdenes eliminadas*, de donde se puede restaurar con su mismo número. En ambos casos deja de contar en el flujo de caja.
- **Acciones que piden código de empleado + código de confirmación:** eliminar y restaurar órdenes, eliminar un pago, crear/editar/activar productos, editar inventario, editar y eliminar clientes, editar y eliminar gastos. El empleado que autorizó queda en *Logs*. Tras 3 intentos fallidos la acción se cancela y el intento queda registrado.
- **Logs:** cada cambio (órdenes, pagos, notas, productos, inventario, clientes, códigos) queda registrado con fecha, acción, detalle *antes → después* y, si aplica, el empleado. El programa no permite editar ni borrar logs.
- **Inventario:** cada producto tiene una cantidad. Si se le agregan características (ej. colores con su cantidad), el inventario es la suma de ellas. Es el inventario que se tiene, no la disponibilidad por fecha.
- **Gastos:** fecha, categoría, descripción, proveedor, método, monto pagado e IVA de la factura (opcional). Registrar no pide código; editar y eliminar sí. El IVA de la factura no cuenta como gasto en la utilidad, porque se recupera como crédito fiscal.
- **Utilidad = ingresos netos (sin IVA) − gastos sin IVA**, por mes. Es antes del impuesto sobre la renta. Los ingresos van por fecha de la orden y los gastos por su propia fecha.
- **IVA neto a pagar = Total IVA − IVA de las facturas de gastos**, por mes. Si da negativo es saldo a favor. Es una estimación para el formulario 150: solo es crédito fiscal el IVA de facturas electrónicas a nombre del negocio y relacionadas con su actividad; el contador tiene la última palabra.
- **El mes del flujo de caja lo define la *fecha de la orden*.** No cuentan las órdenes anuladas ni las eliminadas.
- **Flujo de caja:** *Ingresos brutos* = cobrado con IVA (ya con descuentos); *Ingresos netos* = sin IVA; *Total IVA 13 %* = IVA cobrado (débito fiscal). El IVA a pagar en el formulario 150 de TRIBU-CR es ese monto menos el IVA de las compras del negocio (crédito fiscal), que este sistema no registra.
- **Este sistema no emite factura electrónica.** Si el cliente está inscrito en Hacienda, debe seguir facturando con TicoFactura o con un facturador autorizado. Este programa sirve como control interno y para conciliar.

## Si se olvidan los códigos

Para cambiar los códigos hace falta el código de confirmación actual. Si se perdió, cerrar el programa, abrir `datos.db` con [DB Browser for SQLite](https://sqlitebrowser.org/) y ejecutar:

```sql
DELETE FROM config WHERE clave LIKE 'seg_%';
```

Al abrir NARRENT, la próxima acción protegida pedirá configurarlos de nuevo. Hacer un respaldo antes.

**Limitación honesta:** los códigos se guardan con hash, pero el archivo de datos no está cifrado. Son control y trazabilidad entre empleados, no protección contra alguien con acceso técnico a la computadora.

## Ideas para una versión 3

- Disponibilidad por fecha (cuántas unidades de cada producto/color están alquiladas en un rango), eligiendo la característica en la orden.
- Flujo de caja por fecha de pago (dinero realmente recibido por mes y por método), además del actual por fecha de orden.
- Comprobante o recibo imprimible de cada orden (PDF).
