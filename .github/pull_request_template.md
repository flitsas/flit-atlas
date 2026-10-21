## Historia de usuario

- HU: <!-- p. ej. F1 · HU 1.3 -->
- ADR relacionados: <!-- ATLAS-ADR-NNNN, o "ninguno" -->

## Qué cambia

<!-- Resumen corto del cambio y por qué. -->

## Criterio de aceptación

- [ ] <!-- criterio 1, copiado de la HU -->
- [ ] <!-- criterio 2 -->

## Evidencia

### Pruebas y comandos

```text
<!-- salida de dotnet test / go test / pytest / vitest / playwright, o comandos ejecutados -->
```

### Escritorio

<!-- captura o "no aplica" -->

### Móvil

| Perfil | Captura | Notas |
|---|---|---|
| Android (Pixel 7 o real) | <!-- captura --> | |
| iOS (iPhone 14 o real) | <!-- captura --> | |

<!-- Si el cambio no tiene interfaz, escribe "no aplica" y explica por qué. -->

## Lista de chequeo

- [ ] Sin secretos, tokens, IPs internas, rutas de SealedSecrets ni datos personales reales (código, fixtures, commits)
- [ ] Datos de ejemplo ficticios (`ejemplo.com`, `Organismo Demo`)
- [ ] Permisos aplicados en el servidor y en SQL, no solo en la interfaz
- [ ] Textos en español de Colombia (fechas `dd/mm/aaaa`, hora de Bogotá)
- [ ] Una sola HU en este PR
