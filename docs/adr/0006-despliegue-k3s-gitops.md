---
id: ATLAS-ADR-0006
titulo: Despliegue en k3s con flit-gitops y Argo CD
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-05 y §14, plan 00 §13]
---

# ATLAS-ADR-0006 · Despliegue en k3s con flit-gitops y Argo CD

## Contexto y problema

FLIT 2.0 ya se despliega por GitOps: el CD de cada repo construye imágenes, las fija por digest y escribe el cambio en `flit-gitops`, y Argo CD sincroniza los clústeres k3s (dev y qa en la VPS 177, pdn en la 169). Atlas necesita su lugar en ese esquema, sin una vía paralela.

## Factores de decisión

- Una sola forma de desplegar en FLIT; operación conocida por el equipo.
- Secretos fuera del repo de Atlas.
- Recordar la tormenta de reinicios de la 169 (septiembre de 2026): sondas holgadas.
- Capacidad limitada de las VPS.

## Opciones consideradas

1. **k3s + `flit-gitops` + Argo CD**, imágenes fijadas por digest y SealedSecrets por ambiente.
2. Despliegue directo desde el CD de `flit-atlas` con `kubectl` o `helm`.
3. Un PaaS externo.

## Decisión

Opción elegida: **1**.

- `deploy/` de `flit-atlas` tiene la **base** (kustomize/helm) sin valores sensibles. Los overlays por ambiente viven en `flit-gitops`.
- El CD de `flit-atlas` construye `atlas-api`, `atlas-web` (estáticos), `atlas-render`, `atlas-ia` y `atlas-docs`, fija los digests y abre el cambio en `flit-gitops`.
- Secretos: SealedSecret por ambiente (cadena de conexión, cliente de Entra ID, certificados de OpenIddict, RabbitMQ, almacenamiento, llave virtual de LiteLLM). **La llave de sealed-secrets de la 177 es distinta de la de la 169.**
- Sondas `/health/live` y `/health/ready` con tiempos holgados; réplicas sin estado.
- **Namespaces:** propuesta `atlas-dev`, `atlas-qa` (177) y `atlas-pdn` (169) **[por verificar con el líder: propios o los `flit2-*` existentes]**. Los puertos y namespaces definitivos salen de F0.3.

## Consecuencias

### Positivas

- Mismo flujo que el resto de FLIT: auditable, reversible por git y con imágenes inmutables.
- Atlas no guarda secretos ni topología en su repo.

### Negativas

- Cada alta (namespace, ingress, SealedSecret) es un PR en `flit-gitops`, otro repo que se coordina con el líder (prompt maestro §1, regla 4c).
- Namespaces propios exigen replicar NetworkPolicies y cuotas; los `flit2-*` mezclan a Atlas con FLIT 2.0.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| CD directo con `kubectl`/`helm` | Rompe el modelo GitOps y deja los despliegues sin rastro en git |
| PaaS externo | Datos fuera de la infraestructura de FLIT; costo; otra operación |

## Más información

- Plan 00 §13 (tabla por ambiente y prerrequisitos). Prompt maestro §14. Los prerrequisitos se listan en F0.4.
