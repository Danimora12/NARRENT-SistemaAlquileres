# NARRENT — Guía de operación

Instrucciones para instalar NARRENT en la computadora del cliente, actualizarlo y resolver problemas de acceso. La descripción general del sistema está en el [README](../README.md).

## Instalación en la computadora del cliente

1. Generar el ejecutable con el perfil `Cliente` (Visual Studio: clic derecho en `Alquileres.App` → **Publicar…** → perfil **Cliente**; o por comando: `dotnet publish src/Alquileres.App -p:PublishProfile=Cliente`).
2. Probar el `.exe` en una computadora que no tenga Visual Studio.
3. Copiar `NARRENT.exe` a la computadora del cliente (por ejemplo a `C:\SistemaAlquileres\`) y crear un acceso directo en el escritorio.
4. Abrir NARRENT y completar la **configuración inicial** con el cliente presente: primer administrador y código de confirmación.
5. En la sección *Respaldos*, configurar una carpeta adicional (recomendado: OneDrive o Google Drive). Si el disco se daña, esa es la única copia fuera del equipo.

## Actualizar a una versión nueva

Basta con reemplazar `NARRENT.exe`. Los datos están en otra carpeta y no se pierden; si la base es de una versión anterior, se actualiza sola al abrir el programa y los cambios quedan en el registro de actividad.

## Dónde quedan los datos

| Qué | Dónde |
|---|---|
| Base de datos | `%LOCALAPPDATA%\SistemaAlquileres\datos.db` |
| Respaldos automáticos | `%LOCALAPPDATA%\SistemaAlquileres\Respaldos\` (se conservan los últimos 30) |
| Respaldo adicional | La carpeta configurada en *Respaldos* |

Cada vez que se cierra el programa se crea un respaldo. Restaurar un respaldo antiguo también restaura los usuarios y contraseñas que había en esa fecha.

## Recuperación de accesos

- **Una persona olvidó su contraseña:** un administrador la restablece en *Usuarios → Restablecer contraseña*. La persona la cambia al ingresar.
- **Todos los administradores olvidaron su contraseña** (solo el técnico): hacer un respaldo, cerrar el programa, abrir `datos.db` con [DB Browser for SQLite](https://sqlitebrowser.org/) y ejecutar `DELETE FROM usuarios;`. Al abrir NARRENT aparece la configuración inicial para crear un administrador nuevo. El registro de actividad conserva los nombres anteriores.
- **Se perdió el código de confirmación** (solo el técnico): con el mismo procedimiento, ejecutar `DELETE FROM config WHERE clave = 'seg_confirmacion';`. La próxima acción protegida pedirá a un administrador definir uno nuevo.

## Notas de migración entre versiones

### De la versión 1 ("Sistema de Alquileres") a NARRENT

1. Cerrar el programa anterior (al cerrar se crea un respaldo automático).
2. Copiar `NARRENT.exe`, cambiar el acceso directo para que apunte a él y borrar `SistemaAlquileres.exe`.
3. Abrir NARRENT: la base se actualiza y las órdenes se renumeran con el formato año-mes-consecutivo. Las órdenes antiguas aparecen sin pago; si ya estaban pagadas, registrar el pago desde el detalle.
4. Completar la configuración inicial y el inventario en *Productos*.

### Esquema v3 → v4: inicio de sesión

Los empleados que tenían código pasan a ser usuarios administradores: usuario = primer nombre + último apellido sin tildes (*María José Pérez* → `maria.perez`) y contraseña inicial = su código de empleado anterior. El sistema les pide cambiarla al ingresar.

### Esquema v4 → v5: clientes, productos y gastos

Cada producto recibe su código; se eliminan las características de inventario (se conserva el total por producto); en los gastos, *Descripción* pasa a *Detalle*, el proveedor se agrega al final del detalle y se elimina el IVA de la factura, de modo que esos gastos restan su monto completo en la utilidad. Los cambios quedan resumidos en el registro de actividad.

## Consideraciones fiscales

- Los precios incluyen el IVA del 13 %. El *Total IVA 13 %* del flujo de caja es el IVA cobrado (débito fiscal). El IVA a pagar en el formulario 150 de TRIBU-CR es ese monto menos el IVA de las compras del negocio (crédito fiscal), que este sistema no registra.
- NARRENT no emite factura electrónica. Si el negocio está inscrito en Hacienda, debe seguir facturando con un facturador autorizado; este programa sirve como control interno y para conciliar.
