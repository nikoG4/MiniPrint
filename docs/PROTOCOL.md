# Alcance IPP del MVP

La codificación sigue el modelo binario de IPP sobre HTTP. Todas las respuestas IPP usan HTTP 200 y expresan éxito o error en el campo `status-code`, salvo que el cuerpo no pueda interpretarse como IPP.

## Operaciones

| Operación | Código | Estado |
| --- | ---: | --- |
| Print-Job | `0x0002` | Implementada |
| Validate-Job | `0x0004` | Implementada |
| Create-Job | `0x0005` | Implementada |
| Send-Document | `0x0006` | Un documento por trabajo |
| Cancel-Job | `0x0008` | Pendientes o retenidos |
| Get-Job-Attributes | `0x0009` | Implementada |
| Get-Jobs | `0x000A` | `completed`, `not-completed`, `all`, `my-jobs` y `limit` |
| Get-Printer-Attributes | `0x000B` | Implementada |

## Formatos

- `application/pdf`
- `image/jpeg`
- `application/octet-stream`, únicamente si el administrador activa RAW

Si el cliente declara `application/octet-stream`, MiniPrint intenta reconocer PDF y JPEG por su firma antes de tratarlo como RAW.

## Atributos de trabajo aplicados

- `job-name`
- `requesting-user-name`
- `document-format`
- `copies`
- `sides`
- `orientation-requested`
- `print-color-mode`
- `media`
- `last-document`

## Próximas ampliaciones

1. Validación con Microsoft IPP Class Driver y `ipptool`.
2. `media-col` y consulta más completa de capacidades DEVMODE.
3. Decodificador `image/pwg-raster`.
4. Cancelación de un trabajo que ya entró al spooler físico.
5. IPPS y aprobación de clientes.
6. Compatibilidad controlada con Windows 7/PCL/PostScript.
