# NARRENT

Sistema de escritorio para Windows que administra un negocio de alquiler de artículos: órdenes de alquiler, pagos y abonos, inventario, clientes, gastos y flujo de caja mensual con el IVA y la utilidad.

Funciona sin conexión a internet y no requiere instalar un motor de base de datos: toda la información se guarda en un archivo SQLite local.

**Versión actual: 2.1.0**

| | |
|---|---|
| **Lenguaje y plataforma** | C# · .NET 10 (LTS) · Windows Forms |
| **Datos** | SQLite (`Microsoft.Data.Sqlite`) |
| **Exportación** | Excel con ClosedXML |
| **Pruebas** | xUnit (56 métodos de prueba) |
| **Distribución** | Un solo `.exe` autocontenido; el usuario no instala nada |

---

## Funcionalidades

**Operación**
- Registro de órdenes de alquiler con varias líneas (producto, cantidad, días y precio) y descuento opcional por orden o por línea.
- Pagos adelantados, abonos y cancelación del saldo, con método de pago (SINPE Móvil, transferencia, efectivo o tarjeta).
- Anulación de órdenes con motivo, y eliminación con posibilidad de restaurarlas.
- Catálogos de productos (con código automático e inventario) y de clientes (con validación de identificación de Costa Rica).

**Finanzas**
- Registro de gastos por categoría.
- Flujo de caja mensual y anual: ingresos brutos, ingresos netos, IVA cobrado, gastos y utilidad.
- Exportación del flujo de caja a Excel.

**Administración y seguridad**
- Inicio de sesión con usuarios y dos roles (Administrador y Empleado).
- Código de confirmación para las acciones sensibles.
- Registro de actividad (auditoría) de todos los cambios, que no se puede editar ni borrar.
- Respaldos automáticos al cerrar el programa y restauración desde la aplicación.

---

## Arquitectura

La solución separa la lógica de negocio de la interfaz, para que las reglas se puedan probar sin abrir ventanas.

```
SistemaAlquileres.sln
├── src/Alquileres.Core     → lógica de negocio: modelos, acceso a datos, IVA, reportes, seguridad y respaldos
├── src/Alquileres.App      → interfaz Windows Forms (genera NARRENT.exe)
└── tests/Alquileres.Tests  → pruebas automáticas de Alquileres.Core (xUnit)
```

`Alquileres.App` depende de `Alquileres.Core`; `Alquileres.Core` no conoce la interfaz.

### Alquileres.Core

| Archivo | Responsabilidad |
|---|---|
| `Modelos.cs` | Entidades del dominio: órdenes, líneas, pagos, productos, clientes, gastos |
| `BaseDatos.cs` | Crea el archivo SQLite y migra el esquema versión por versión (`PRAGMA user_version`, actualmente v5) |
| `Repositorios.cs` | Acceso a datos de productos, clientes, órdenes, pagos y configuración |
| `GastoRepositorio.cs` | Acceso a datos de gastos |
| `CalculadoraIva.cs` | Cálculo del IVA incluido en el precio y de los descuentos |
| `Dinero.cs` | Manejo de montos como enteros en céntimos |
| `Identificacion.cs` | Tipos de identificación de Costa Rica: formato, validación y presentación |
| `Auditoria.cs` | Registro de actividad y número de orden consecutivo por mes |
| `Usuarios.cs` | Usuarios, roles, inicio de sesión y contraseñas temporales |
| `SeguridadServicio.cs` | Código de confirmación para acciones sensibles |
| `Claves.cs` | Hash de contraseñas con PBKDF2-SHA256 y sal aleatoria |
| `ReporteServicio.cs` | Flujo de caja mensual y anual, y exportación a Excel |
| `RespaldoServicio.cs` | Respaldos con `VACUUM INTO` y restauración |

### Alquileres.App

| Archivo | Responsabilidad |
|---|---|
| `FormPrincipal.cs` | Ventana principal con barra lateral (Operación, Finanzas, Administración) |
| `Tema.cs` | Paleta de colores del sistema visual |
| `Ui.cs` | Ayudantes para construir pantallas: tipografía, botones, campos, tarjetas, tablas e indicadores |
| `Componentes.cs` | Controles dibujados a mano: botón redondeado, tarjeta, tabla con etiquetas de estado, selector segmentado, gráfico de barras, avisos, íconos y logo |
| `Controles.cs` | Campo de monto y campo numérico con botones − / + |
| `Pantallas/` | Inicio de sesión, órdenes, productos, clientes, gastos, flujo de caja, respaldos, registro de actividad y usuarios |

---

## Decisiones de diseño

| Decisión | Motivo |
|---|---|
| **Montos guardados como enteros en céntimos** | Evita los errores de redondeo de `double`; las sumas del flujo de caja son exactas |
| **Cada línea guarda su precio e IVA al momento de la orden** | Cambiar un precio después no altera las órdenes ni los reportes anteriores |
| **IVA incluido: IVA = total × 13/113** | Los precios del negocio ya incluyen el 13 %; el IVA no es el 13 % del total |
| **Migraciones de esquema versionadas** | Las bases de versiones anteriores se actualizan solas al abrir el programa, sin perder datos |
| **Contraseñas con PBKDF2-SHA256, sal y comparación en tiempo constante** | Nunca se guarda la contraseña ni el código en texto |
| **Anular ≠ eliminar** | Anular conserva la orden visible con su motivo; eliminar la oculta pero se puede restaurar con su mismo número |
| **Usuarios desactivados, no borrados** | Se conserva el historial del registro de actividad |
| **Registro de actividad con valores antes → después** | Trazabilidad de quién cambió qué y cuándo |
| **SQLite local y `.exe` autocontenido** | El negocio no necesita servidor, internet ni instalar dependencias |

---

## Reglas de negocio principales

- **Total de cada línea** = precio unitario × cantidad × días (si el producto se cobra por alquiler, días = 1).
- **Número de orden** = año + mes + consecutivo del mes (ej. `202612001`), según la fecha de la orden. Nunca se repite.
- **Descuento** de 1 % a 15 %, a toda la orden o a una línea. Se redondea al colón y el IVA se calcula sobre el monto ya descontado.
- **Pagos:** no se puede pagar más que el saldo ni registrar pagos en órdenes anuladas.
- **Clientes:** tipo y número de identificación obligatorios (cédula física, cédula jurídica, DIMEX, NITE o pasaporte), validados según el tipo y sin repetirse.
- **Código de producto:** las 3 primeras letras del nombre sin tildes + consecutivo (*Manteles* → `MAN001`).
- **Utilidad mensual** = ingresos netos (sin IVA) − gastos. Las órdenes anuladas o eliminadas no cuentan.
- **Roles:** el Administrador maneja todo el sistema, incluidos usuarios; el Empleado opera pero no administra usuarios ni el código de confirmación.
- **Acciones con código de confirmación:** eliminar o restaurar órdenes, eliminar pagos, modificar productos e inventario, editar o eliminar clientes y gastos, y administrar usuarios. Tras 3 intentos fallidos la acción se cancela y queda registrada.

---

## Cómo ejecutarlo

**Requisitos:** Visual Studio 2026 con la carga de trabajo *Desarrollo de escritorio de .NET*, o el SDK de .NET 10.

1. Clonar el repositorio y abrir `SistemaAlquileres.sln`.
2. Establecer `Alquileres.App` como proyecto de inicio.
3. Ejecutar con **F5**. La primera vez aparece la configuración inicial para crear el administrador y el código de confirmación.

**Pruebas:**

```
dotnet test
```

**Generar el ejecutable:**

```
dotnet publish src/Alquileres.App -p:PublishProfile=Cliente
```

El perfil `Cliente.pubxml` genera un único `NARRENT.exe` autocontenido (≈ 55 MB) que incluye .NET 10 y SQLite.

**Ubicación de los datos:** `%LOCALAPPDATA%\SistemaAlquileres\datos.db`, con respaldos automáticos en la subcarpeta `Respaldos` (se conservan los últimos 30).

---

## Limitaciones conocidas

- Las contraseñas y el código se guardan con hash, pero el archivo de datos no está cifrado. Es control y trazabilidad entre usuarios, no protección frente a alguien con acceso técnico a la computadora.
- No emite factura electrónica; funciona como control interno. El IVA a pagar ante Hacienda requiere además el IVA de las compras, que el sistema no registra.
- El inventario es la cantidad total por producto, no la disponibilidad por fecha.

## Próximas mejoras

- Disponibilidad de inventario por rango de fechas.
- Flujo de caja por fecha de pago, además del actual por fecha de la orden.
- Comprobante imprimible de cada orden en PDF.
- Tema oscuro (la paleta ya está preparada en `Tema.cs`).

---

La instalación en el cliente, la actualización desde versiones anteriores y la recuperación de contraseñas están en [docs/OPERACION.md](docs/OPERACION.md).

**Autor:** Daniel Mora Víquez
