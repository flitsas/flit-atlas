# deploy · Base de despliegue

Base kustomize/helm que consume `flit-gitops`. Aquí no van secretos, IPs ni rutas de SealedSecrets: los valores por ambiente viven en `flit-gitops` (ATLAS-ADR-0006). Fase **F1**.
