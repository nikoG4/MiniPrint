# MiniPrint

MiniPrint convierte las impresoras instaladas en una PC Windows en impresoras IPP de red. Los equipos cliente las agregan como impresoras normales y pueden usarlas desde Word, Excel, Chrome o cualquier aplicación con `Ctrl+P`, sin compartirlas mediante SMB/RPC ni usar Point and Print.

> Estado: MVP para pruebas controladas. El protocolo, la cola, el descubrimiento y el puente Windows están implementados, pero la compatibilidad final debe validarse con una impresora física y el Microsoft IPP Class Driver antes de usarlo en producción.

## Flujo

```text
Aplicación cliente
    -> cola de impresión de Windows
    -> Microsoft IPP Class Driver
    -> MiniPrint por HTTP/IPP
    -> driver local del servidor
    -> impresora USB
```

## Funciones incluidas

- IPP 1.1/2.0 con `Print-Job`, `Validate-Job`, `Create-Job`, `Send-Document`, `Get-Printer-Attributes`, `Get-Jobs`, `Get-Job-Attributes` y `Cancel-Job`.
- Enumeración automática de impresoras instaladas en Windows.
- Una dirección IPP estable por impresora.
- Trabajos PDF y JPEG.
- Copias, orientación, A4/Carta/Legal, color y dúplex cuando la cola local los admite.
- Cola persistente en disco mientras se procesa cada documento.
- Descubrimiento mDNS/DNS-SD `_ipp._tcp`.
- Servicio de Windows y reglas de firewall para el perfil privado.
- Límite de carga, nombres saneados, acceso limitado a redes privadas y trabajos RAW desactivados por defecto.
- Pruebas unitarias y compilación automatizada en Windows.

## Requisitos del servidor

- Windows 10, Windows 11 o Windows Server x64.
- La impresora instalada localmente y capaz de imprimir una página de prueba.
- PowerShell ejecutado como administrador para instalar el servicio.
- Puerto TCP 631 disponible.

## Compilar

Desde PowerShell con el SDK de .NET 8:

```powershell
.\scripts\build-release.ps1
```

El paquete queda en `artifacts\MiniPrint-win-x64.zip`.

## Instalar el servidor

Descomprima el paquete y, desde PowerShell como administrador:

```powershell
.\scripts\install-server.ps1 -Source .\publish
```

Después abra desde otro equipo:

```text
http://NOMBRE-DEL-SERVIDOR:631/
```

La página devuelve las impresoras detectadas y el `slug` de cada una.

Antes de agregarla al cliente puede comprobar el protocolo:

```powershell
.\scripts\test-ipp.ps1 -Server NOMBRE-DEL-SERVIDOR -PrinterSlug SLUG
```

## Agregar una impresora cliente

En Windows 10/11:

```powershell
.\scripts\add-printer.ps1 `
  -Server NOMBRE-DEL-SERVIDOR `
  -PrinterSlug hp-laserjet-xxxxxxxx `
  -DisplayName "HP Administración - MiniPrint"
```

También puede agregarse manualmente desde **Configuración > Impresoras y escáneres > Agregar dispositivo > Agregar manualmente > Agregar una impresora mediante una dirección IP o nombre de host**, seleccionando **Dispositivo IPP** y usando:

```text
http://NOMBRE-DEL-SERVIDOR:631/ipp/printers/SLUG
```

## Configuración

`src/MiniPrint.Server/appsettings.json` contiene las opciones principales:

| Opción | Predeterminado | Uso |
| --- | ---: | --- |
| `Port` | `631` | Puerto HTTP/IPP |
| `MaxRequestBytes` | `104857600` | Tamaño máximo por solicitud |
| `EnableMdns` | `true` | Anuncia impresoras automáticamente |
| `AllowPrivateNetworksOnly` | `true` | Rechaza direcciones fuera de LAN/loopback |
| `IncludeVirtualPrinters` | `false` | Excluye PDF, XPS, Fax y OneNote |
| `EnableRawPrinting` | `false` | Habilita datos PCL/PostScript ya renderizados |
| `KeepPayloadsAfterPrinting` | `false` | Conserva o elimina documentos procesados |

## Seguridad

MiniPrint evita credenciales de Windows y Point and Print, pero no debe exponerse directamente a Internet. La instalación abre el puerto únicamente para el perfil de red privado y `LocalSubnet`. Los documentos se eliminan al terminar salvo que se cambie la configuración.

El modo RAW permanece apagado porque confía en que el cliente envíe lenguaje de impresora válido. Solo está pensado para una futura compatibilidad controlada con Windows 7 y drivers previamente instalados.

## Limitaciones del MVP

- La salida física aún debe verificarse en Windows con modelos reales de impresora.
- El servidor anuncia PDF/JPEG, no afirma certificación IPP Everywhere ni Mopria.
- No implementa todavía PWG Raster, autenticación, IPPS/TLS ni administración remota.
- Algunas versiones de Windows pueden no descubrir el servicio por mDNS; la URL manual sigue funcionando.
- Windows 7 requerirá su Internet Printing Client y normalmente un driver local compatible. Está planteado como segunda fase.

Consulte [arquitectura](docs/ARCHITECTURE.md), [alcance del protocolo](docs/PROTOCOL.md) y [plan de pruebas en Windows](docs/WINDOWS-TEST-PLAN.md).
