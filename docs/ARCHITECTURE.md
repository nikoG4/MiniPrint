# Arquitectura

MiniPrint separa el protocolo de red del código exclusivo de Windows.

## Componentes

| Componente | Responsabilidad |
| --- | --- |
| `MiniPrint.Protocol` | Lectura y escritura binaria de mensajes IPP |
| `IppEndpoint` | Validación, despacho de operaciones y respuestas IPP |
| `WindowsPrinterCatalog` | Enumeración segura de colas locales mediante Winspool |
| `PrintJobStore` | Metadatos, archivos temporales y cola de trabajos |
| `PrintJobProcessor` | Procesamiento secuencial y transición de estados |
| `WindowsPrintBackend` | PDF/JPEG a GDI y RAW opcional a Winspool |
| `MdnsAdvertisementService` | Anuncio `_ipp._tcp` de cada impresora |
| `PrivateNetworkGuard` | Rechazo predeterminado de clientes fuera de redes privadas |

## Decisiones

### PDF como formato principal

El servidor anuncia `application/pdf` y `image/jpeg`. PDF conserva un trabajo multipágina y puede renderizarse en el servidor con PDFium. Esto evita instalar el driver del fabricante en clientes modernos.

### Procesamiento secuencial

PDFium no garantiza renderización concurrente. La primera versión utiliza un único consumidor de cola, lo cual también simplifica el orden y reduce picos de memoria.

### Spool en disco

El cuerpo IPP se valida y se guarda con un nombre generado dentro del directorio de datos. Nunca se utiliza una ruta suministrada por el cliente. El archivo se elimina después de completar, cancelar o abortar el trabajo, salvo configuración contraria.

### Sin controladores propios

El cliente usa el Microsoft IPP Class Driver y el servidor usa la cola/driver local existente. MiniPrint no instala monitores de puerto ni controladores de kernel.

## Límites de confianza

```text
[Cliente LAN]
      | HTTP/IPP, documento no confiable
      v
[Validación MiniPrint] -> [Spool privado] -> [PDFium/GDI] -> [Spooler Windows] -> [Impresora]
```

- El documento y todos los atributos IPP se consideran no confiables.
- El tamaño se limita antes de analizar o escribir el trabajo.
- Los nombres de trabajo/usuario se limpian y acortan.
- Los nombres de impresora proceden exclusivamente del catálogo local.
- RAW requiere habilitación explícita.
