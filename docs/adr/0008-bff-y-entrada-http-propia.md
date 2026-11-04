---
id: ATLAS-ADR-0008
titulo: BFF en atlas-api y entrada HTTP propia (sin Flit.Gateway)
estado: aceptado
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: ["plan 00 D-14 (API de Atlas detrás del gateway YARP de FLIT bajo /api/v1/atlas/**)"]
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-07, §5.1, §6.3 y §12, plan 00 D-14, plan 03 §12]
---

# ATLAS-ADR-0008 · BFF en atlas-api y entrada HTTP propia (sin Flit.Gateway)

## Contexto y problema

El plan 00 (D-14) exponía Atlas por el gateway YARP de FLIT 2.0 (`Flit.Gateway`), con JWT emitidos por core-identity. Con la identidad propia (ATLAS-ADR-0004) ese diseño deja de encajar: el gateway valida tokens de core-identity, no sesiones de Atlas. Además, en una PWA guardar tokens en `localStorage` expone a robo por XSS, y el gateway corta las peticiones a los 30 s, cuando el streaming del asistente necesita ≥ 120 s (plan 03 §12).

## Factores de decisión

- Ningún token de acceso en el navegador (OWASP ASVS V3, guía de OAuth 2.0 para aplicaciones de navegador).
- Una sola superficie expuesta a internet.
- Servicios internos (Go, Python) sin exposición pública y sin confiar en datos del cliente.
- Tiempos de espera adecuados para SSE y SignalR.
- No acoplar los despliegues de Atlas a los de FLIT 2.0.

## Opciones consideradas

1. **`atlas-api` como BFF y única entrada**, con ingress propio en el host de Atlas y YARP **interno** hacia Go y Python.
2. Detrás de `Flit.Gateway` (D-14) con tokens en el navegador.
3. SPA con tokens en memoria (sin BFF) y API pública aparte.

## Decisión

Opción elegida: **1**.

- **Ingress propio** por host (`dev.atlas.flitsas.online`, `qa.atlas.flitsas.online`, `atlas.flitsas.com`), sin pasar por `Flit.Gateway` salvo que el líder lo pida.
- **Cookie de sesión** `__Host-atlas` con `HttpOnly`, `Secure`, `SameSite=Strict` y `Path=/`, cifrada con Data Protection (llaves compartidas entre réplicas en Redis o en la base). Ningún token en `localStorage` ni en `sessionStorage`.
- **Antifalsificación**: además de `SameSite=Strict`, las mutaciones exigen una cabecera propia (`X-Atlas-Csrf`) validada contra la sesión, y se rechaza todo `Origin` distinto del host.
- **Mismo origen**: la PWA y la API se sirven bajo el mismo host (estáticos en `/` y API en `/api/v1/**`), sin CORS para el navegador.
- **YARP interno**: `atlas-api` enruta hacia `atlas-render`, `atlas-ia` y `atlas-docs` por la red del clúster, con token de servicio firmado por OpenIddict (`client_credentials`) o mTLS. Para `atlas-ia`, `atlas-api` adjunta el **contexto de permisos firmado** (ATLAS-ADR-0011). Python nunca confía en datos del cliente.
- **Tiempos**: `ActivityTimeout` ≥ 120 s en las rutas de streaming del asistente y en SignalR; 30 s por defecto en el resto.
- **Cabeceras**: CSP estricta sin `unsafe-inline` en scripts, HSTS, `X-Content-Type-Options`, `Referrer-Policy` y `Permissions-Policy`. Limitación de tasa por usuario y por organización (más estricta para externos y en los endpoints de identidad).
- **Clientes no navegador** (MCP, CLI, widget, CI): usan tokens bearer de OpenIddict o el OIDC de GitHub en rutas propias (ATLAS-ADR-0004 y 0009), nunca la cookie.

## Consecuencias

### Positivas

- Superficie mínima: un solo servicio expuesto y ningún token en el navegador.
- Atlas controla sus tiempos, su CSP y sus límites de tasa sin pedir cambios en `Flit.Gateway`.

### Negativas

- `atlas-api` concentra carga y responsabilidad: hay que escalarlo horizontalmente y mantenerlo sin estado (sesiones en el servidor, en la base o en Redis).
- No hereda la política de borde de FLIT 2.0 (WAF o reglas comunes **[por verificar qué aplica hoy `Flit.Gateway`]**). Hay que replicarla en el ingress de Atlas.
- YARP (`Yarp.ReverseProxy`, MIT) va en la 2.3.0 del 27/02/2025 y no tiene versiones nuevas desde hace ~20 meses. Declara compatibilidad con net6.0 o superior, así que funciona en .NET 10 por compatibilidad hacia adelante. Como solo se usa como proxy interno, el riesgo es bajo, pero hay que vigilar su mantenimiento. La alternativa sería `HttpClient` con reenvío propio, solo para las rutas internas.
- El widget del asistente embebido en otras apps (F7) no puede usar la cookie (es de otro origen) y requiere el intercambio de tokens de ATLAS-ADR-0004.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Detrás de `Flit.Gateway` (D-14) | Valida tokens de core-identity, no de Atlas; corta a los 30 s; acopla los despliegues |
| SPA con tokens en memoria | Expone tokens a XSS y complica el refresco y el offline de la PWA |

## Más información

- Prompt maestro §6.3 (sesión) y §12 (seguridad).
- Detalle de la sesión y sus sellos de seguridad en ATLAS-ADR-0004.
