# Plan de pruebas en Windows

## Preparación

1. Instalar la impresora USB para todos los usuarios.
2. Imprimir una página de prueba directamente desde el equipo servidor.
3. Confirmar que TCP 631 no esté ocupado.
4. Compilar el paquete x64 e instalar MiniPrint como administrador.
5. Abrir `http://localhost:631/` y anotar el `slug`.

## Prueba del servidor

- Reiniciar Windows y confirmar que el servicio inicia automáticamente.
- Consultar `/health` y `/api/printers`.
- Apagar/desconectar la impresora y verificar `IsOffline`.
- Confirmar que un cliente fuera de subred privada recibe HTTP 403.
- Intentar enviar más de 100 MiB y verificar rechazo.

## Prueba del cliente Windows 11

1. Agregar la URL con el script y confirmar que Windows usa Microsoft IPP Class Driver.
2. Imprimir una página desde Bloc de notas.
3. Imprimir un PDF multipágina desde Edge.
4. Imprimir desde Word y Excel.
5. Probar A4, Carta, vertical y horizontal.
6. Si corresponde, probar monocromo/color y dúplex largo/corto.
7. Enviar dos trabajos seguidos y comprobar el orden.
8. Cancelar un trabajo todavía pendiente.
9. Reiniciar el equipo cliente y volver a imprimir sin reinstalar nada.

## Prueba del cliente Windows 10

Repetir la matriz anterior. Si `Add-Printer -IppURL` no existe, realizar la instalación manual y registrar la versión exacta de Windows.

## Criterio para pasar a producción

- Cero páginas corruptas en 100 trabajos variados.
- Opciones expuestas por Windows coinciden con la impresora.
- Recuperación después de reiniciar servidor y cliente.
- Documentos temporales eliminados después de imprimir.
- Servicio inaccesible desde una interfaz/red no autorizada.
- Registro suficiente para diagnosticar fallos sin conservar el contenido impreso.
